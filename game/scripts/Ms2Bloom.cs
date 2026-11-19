using System;
using Godot;
using Godot.Collections;

namespace Ms2Godot;

/// <summary>
/// The client's HDR bloom, as a compositor effect after transparents. The client's chain (BloomRenderStep and the final
/// scene pass) works on its gamma-space HDR scene target:
///  1. average each 4x4 block into a quarter-size target;
///  2. bright pass: x = max(c - threshold, 0), keep x / (x + offset) + glow.r * c * glowscale in an 8-bit target (glow: the
///     GlowPass target; postprocessing.fxo HDRRenderStep_BrightPassFilter: mul r0.xyz, glow.x, scene; mul r0.xyz, r0, gfGlowScale;
///     mad x, 1 / (x + gfBrightPassOffset), r0), so a glow-mapped object blooms in its own colour;
///  3. two 15-tap Gaussian blurs (deviation 3, every weight doubled), horizontal then vertical, point sampled, 8-bit targets; both
///     step by 1 / quarter width, so the vertical step is a fraction of a texel on a wide screen;
///  4. final: scene + bloomscale * bloom.
/// The doubled weights make each blur pass gain about 2, so a lit floor gains ~15-20% overall.
/// The field's colour buffer holds gamma values as the client's target does (field_lit.gdshader), so the passes read it
/// as it is, and the final pass converts the sum to linear once, for Godot's linear -> sRGB output. Threshold, offset and
/// bloomscale come from the field's &lt;hdr&gt; entry (client defaults 0.27, 30, 2).
/// A 3D view without the bloom (UI character views) still needs that conversion: <see cref="GammaOut"/> runs only it.
/// <see cref="GammaGuard"/> catches a view without either, or a failed setup, and converts through the camera's environment instead.
/// </summary>
public partial class Ms2Bloom : CompositorEffect
{
    public GlowPass Glow;
    public float Threshold = 0.27f, Offset = 30f, BloomScale = 2f, GlowScale = 0.1f;
    /// <summary>Only the gamma -> linear conversion, no bloom.</summary>
    public bool ConvertOnly;

    /// <summary>A compositor for a 3D view that draws the gamma-valued shaders without the bloom.</summary>
    public static Compositor GammaOut() => new() { CompositorEffects = new Array<CompositorEffect> { new Ms2Bloom { ConvertOnly = true } } };

    /// <summary>Set on the render thread: 0 until the passes are set up, 1 when they are, -1 when they could not be (GammaGuard then falls back).</summary>
    volatile int setup;
    public bool Ready => setup != 0;
    public bool Failed => setup < 0;

    RenderingDevice rd;
    readonly Rid[] shaders = new Rid[4], pipes = new Rid[4];
    Rid sampler, point, bright, blurH, blurV, black;
    Vector2I quarter;

    const string Code = @"
layout(local_size_x = 8, local_size_y = 8) in;
layout(push_constant, std430) uniform Params { vec2 step_uv; float threshold; float offset; float scale; float glow_scale; float pad1; float pad2; } pc;

vec3 to_linear(vec3 c) { return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c)); }

#if MODE == 0
layout(rgba16f, set = 0, binding = 0) uniform restrict readonly image2D scene;
layout(rgba8, set = 0, binding = 1) uniform restrict writeonly image2D dst;
layout(set = 0, binding = 2) uniform sampler2D glow;
void main() {
	ivec2 q = ivec2(gl_GlobalInvocationID.xy);
	if (any(greaterThanEqual(q, imageSize(dst)))) return;
	ivec2 last = imageSize(scene) - 1;
	vec3 s = vec3(0.0);
	for (int y = 0; y < 4; y++)
		for (int x = 0; x < 4; x++)
			s += imageLoad(scene, min(q * 4 + ivec2(x, y), last)).rgb;
	s /= 16.0;
	vec3 b = max(s - pc.threshold, vec3(0.0));
	// the glow target (GlowPass, full size): its red channel scales the scene colour. Read with Shader0LinearClampSampler at
	// the quarter texel's centre, so the middle 2x2 of each 4x4 block (HDRRenderStep_BrightPassFilter ps)
	float g = texture(glow, (vec2(q) + 0.5) / vec2(imageSize(dst))).r;
	imageStore(dst, q, vec4(min(b / (b + pc.offset) + g * s * pc.glow_scale, vec3(1.0)), 1.0));
}
#elif MODE == 1
layout(set = 0, binding = 0) uniform sampler2D src;
layout(rgba8, set = 0, binding = 1) uniform restrict writeonly image2D dst;
void main() {
	ivec2 q = ivec2(gl_GlobalInvocationID.xy);
	ivec2 size = imageSize(dst);
	if (any(greaterThanEqual(q, size))) return;
	vec2 uv = (vec2(q) + 0.5) / vec2(size);
	const float rho = 3.0;
	vec3 s = vec3(0.0);
	for (int i = -7; i <= 7; i++) {
		float w = 2.0 * exp(-float(i * i) / (2.0 * rho * rho)) / sqrt(2.0 * 3.14159265 * rho * rho);
		s += w * texture(src, uv + float(i) * pc.step_uv).rgb;
	}
	imageStore(dst, q, vec4(s, 1.0));
}
#elif MODE == 2
layout(rgba16f, set = 0, binding = 0) uniform restrict image2D scene;
layout(set = 0, binding = 1) uniform sampler2D bloom;
void main() {
	ivec2 p = ivec2(gl_GlobalInvocationID.xy);
	ivec2 size = imageSize(scene);
	if (any(greaterThanEqual(p, size))) return;
	vec4 c = imageLoad(scene, p);
	vec3 g = c.rgb + pc.scale * texture(bloom, (vec2(p) + 0.5) / vec2(size)).rgb;
	imageStore(scene, p, vec4(to_linear(g), c.a));
}
#else
layout(rgba16f, set = 0, binding = 0) uniform restrict image2D scene;
void main() {
	ivec2 p = ivec2(gl_GlobalInvocationID.xy);
	if (any(greaterThanEqual(p, imageSize(scene)))) return;
	vec4 c = imageLoad(scene, p);
	imageStore(scene, p, vec4(to_linear(c.rgb), c.a));
}
#endif
";

    public Ms2Bloom()
    {
        EffectCallbackType = EffectCallbackTypeEnum.PostTransparent;
        RenderingServer.CallOnRenderThread(Callable.From(Init));
    }

    void Init()
    {
        rd = RenderingServer.GetRenderingDevice();
        if (rd == null)
        {
            GD.PushError("bloom: no RenderingDevice");
            setup = -1;
            return;
        }
        for (int m = 0; m < 4; m++)
        {
            var spirv = rd.ShaderCompileSpirVFromSource(new RDShaderSource
            {
                Language = RenderingDevice.ShaderLanguage.Glsl,
                SourceCompute = $"#version 450\n#define MODE {m}\n" + Code,
            });
            if (!string.IsNullOrEmpty(spirv.CompileErrorCompute))
            {
                GD.PushError("bloom shader: " + spirv.CompileErrorCompute);
                rd = null;
                setup = -1;
                return;
            }
            shaders[m] = rd.ShaderCreateFromSpirV(spirv);
            pipes[m] = rd.ComputePipelineCreate(shaders[m]);
        }
        black = rd.TextureCreate(new RDTextureFormat { Format = RenderingDevice.DataFormat.R8G8B8A8Unorm, Width = 1, Height = 1, UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanUpdateBit }, new RDTextureView(), new Array<byte[]> { new byte[4] });
        sampler = rd.SamplerCreate(new RDSamplerState
        {
            MinFilter = RenderingDevice.SamplerFilter.Linear,
            MagFilter = RenderingDevice.SamplerFilter.Linear,
            RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge,
            RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge,
        });
        // the blur samples with BasePointClampSampler (postprocessing.fxo, HDRRenderStep_Bloom ps): its vertical step of
        // 1 / width is a fraction of a texel, so the client's taps snap to rows
        point = rd.SamplerCreate(new RDSamplerState
        {
            MinFilter = RenderingDevice.SamplerFilter.Nearest,
            MagFilter = RenderingDevice.SamplerFilter.Nearest,
            RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge,
            RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge,
        });
        setup = 1;
    }

    Rid Target(Vector2I size) => rd.TextureCreate(new RDTextureFormat
    {
        Format = RenderingDevice.DataFormat.R8G8B8A8Unorm,
        Width = (uint)size.X,
        Height = (uint)size.Y,
        UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.StorageBit,
    }, new RDTextureView());

    void FreeTargets()
    {
        foreach (var t in new[] { bright, blurH, blurV })
            if (t.IsValid) rd.FreeRid(t);
        bright = blurH = blurV = default;
    }

    static RDUniform Image(int binding, Rid tex)
    {
        var u = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = binding };
        u.AddId(tex);
        return u;
    }

    RDUniform Sampled(int binding, Rid tex, bool nearest = false)
    {
        var u = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = binding };
        u.AddId(nearest ? point : sampler);
        u.AddId(tex);
        return u;
    }

    void Run(int mode, Vector2I size, Vector2 step, params RDUniform[] uniforms)
    {
        var set = UniformSetCacheRD.GetCache(shaders[mode], 0, new Array<RDUniform>(uniforms));
        var pc = new float[] { step.X, step.Y, Threshold, Offset, BloomScale, GlowScale, 0, 0 };
        var bytes = new byte[pc.Length * 4];
        Buffer.BlockCopy(pc, 0, bytes, 0, bytes.Length);
        long list = rd.ComputeListBegin();
        rd.ComputeListBindComputePipeline(list, pipes[mode]);
        rd.ComputeListBindUniformSet(list, set, 0);
        rd.ComputeListSetPushConstant(list, bytes, (uint)bytes.Length);
        rd.ComputeListDispatch(list, (uint)(size.X + 7) / 8, (uint)(size.Y + 7) / 8, 1);
        rd.ComputeListEnd();
    }

    public override void _RenderCallback(int effectCallbackType, RenderData renderData)
    {
        if (rd == null || effectCallbackType != (int)EffectCallbackTypeEnum.PostTransparent) return;
        // disposed here, on the render thread: a wrapper left to the GC finalizer can hold the last reference to a freed
        // viewport's buffers (the character list's capture), and their textures then fail to free off the render thread
        using var sceneBuffers = renderData.GetRenderSceneBuffers();
        if (sceneBuffers is not RenderSceneBuffersRD buffers) return;
        var size = buffers.GetInternalSize();
        if (ConvertOnly)
        {
            for (uint view = 0; view < buffers.GetViewCount(); view++)
                Run(3, size, Vector2.Zero, Image(0, buffers.GetColorLayer(view)));
            return;
        }
        if (size.X < 4 || size.Y < 4) return;
        var q = size / 4;
        if (q != quarter || !bright.IsValid)
        {
            FreeTargets();
            quarter = q;
            bright = Target(q);
            blurH = Target(q);
            blurV = Target(q);
        }
        var texel = 1f / q.X;   // the client steps both blur passes by 1 / width
        for (uint view = 0; view < buffers.GetViewCount(); view++)
        {
            var color = buffers.GetColorLayer(view);
            var glowTex = Glow != null && IsInstanceValid(Glow) ? Glow.Texture() : default;
            Run(0, q, Vector2.Zero, Image(0, color), Image(1, bright), Sampled(2, glowTex.IsValid ? glowTex : black));
            Run(1, q, new Vector2(texel, 0), Sampled(0, bright, true), Image(1, blurH));
            Run(1, q, new Vector2(0, texel), Sampled(0, blurH, true), Image(1, blurV));
            Run(2, size, Vector2.Zero, Image(0, color), Sampled(1, blurV));
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete && rd != null)
        {
            FreeTargets();
            for (int m = 0; m < 4; m++)
                if (shaders[m].IsValid) rd.FreeRid(shaders[m]);   // frees the pipeline too
            if (sampler.IsValid) rd.FreeRid(sampler);
            if (point.IsValid) rd.FreeRid(point);
            if (black.IsValid) rd.FreeRid(black);
        }
    }
}
