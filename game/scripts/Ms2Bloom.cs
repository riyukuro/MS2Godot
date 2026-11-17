using System;
using Godot;
using Godot.Collections;

namespace Ms2Godot;

/// <summary>
/// The client's HDR bloom, as a compositor effect after transparents. The client's chain (BloomRenderStep and the final
/// scene pass) works on its gamma-space HDR scene target:
///  1. average each 4x4 block into a quarter-size target;
///  2. bright pass: x = max(c - threshold, 0), keep x / (x + offset) in an 8-bit target (glow target added after the compression: GlowPass);
///  3. two 15-tap Gaussian blurs (deviation 3, every weight doubled), horizontal then vertical, 8-bit targets; both
///     step by 1 / quarter width, so the vertical step is a fraction of a texel on a wide screen;
///  4. final: scene + bloomscale * bloom.
/// The doubled weights make each blur pass gain about 2, so a lit floor gains ~15-20% overall.
/// Godot's buffer is linear, so the passes convert to gamma and back. Threshold, offset and bloomscale come from the
/// field's &lt;hdr&gt; entry (client defaults 0.27, 30, 2).
/// </summary>
public partial class Ms2Bloom : CompositorEffect
{
    public GlowPass Glow;
    public float Threshold = 0.27f, Offset = 30f, BloomScale = 2f;

    RenderingDevice rd;
    readonly Rid[] shaders = new Rid[3], pipes = new Rid[3];
    Rid sampler, bright, blurH, blurV, black;
    Vector2I quarter;

    const string Code = @"
layout(local_size_x = 8, local_size_y = 8) in;
layout(push_constant, std430) uniform Params { vec2 step_uv; float threshold; float offset; float scale; float pad0; float pad1; float pad2; } pc;

vec3 to_gamma(vec3 c) { return mix(c * 12.92, 1.055 * pow(max(c, vec3(0.0)), vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), c)); }
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
			s += to_gamma(imageLoad(scene, min(q * 4 + ivec2(x, y), last)).rgb);
	vec3 b = max(s / 16.0 - pc.threshold, vec3(0.0));
	vec3 g = texture(glow, (vec2(q) + 0.5) / vec2(imageSize(dst))).rgb;   // the glow target (GlowPass), added after the compression
	imageStore(dst, q, vec4(min(b / (b + pc.offset) + g, vec3(1.0)), 1.0));
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
#else
layout(rgba16f, set = 0, binding = 0) uniform restrict image2D scene;
layout(set = 0, binding = 1) uniform sampler2D bloom;
void main() {
	ivec2 p = ivec2(gl_GlobalInvocationID.xy);
	ivec2 size = imageSize(scene);
	if (any(greaterThanEqual(p, size))) return;
	vec4 c = imageLoad(scene, p);
	vec3 g = to_gamma(c.rgb) + pc.scale * texture(bloom, (vec2(p) + 0.5) / vec2(size)).rgb;
	imageStore(scene, p, vec4(to_linear(g), c.a));
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
        if (rd == null) return;
        for (int m = 0; m < 3; m++)
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

    RDUniform Sampled(int binding, Rid tex)
    {
        var u = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = binding };
        u.AddId(sampler);
        u.AddId(tex);
        return u;
    }

    void Run(int mode, Vector2I size, Vector2 step, params RDUniform[] uniforms)
    {
        var set = UniformSetCacheRD.GetCache(shaders[mode], 0, new Array<RDUniform>(uniforms));
        var pc = new float[] { step.X, step.Y, Threshold, Offset, BloomScale, 0, 0, 0 };
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
        if (renderData.GetRenderSceneBuffers() is not RenderSceneBuffersRD buffers) return;
        var size = buffers.GetInternalSize();
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
            Run(1, q, new Vector2(texel, 0), Sampled(0, bright), Image(1, blurH));
            Run(1, q, new Vector2(0, texel), Sampled(0, blurH), Image(1, blurV));
            Run(2, size, Vector2.Zero, Image(0, color), Sampled(1, blurV));
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete && rd != null)
        {
            FreeTargets();
            for (int m = 0; m < 3; m++)
                if (shaders[m].IsValid) rd.FreeRid(shaders[m]);   // frees the pipeline too
            if (sampler.IsValid) rd.FreeRid(sampler);
            if (black.IsValid) rd.FreeRid(black);
        }
    }
}
