using Godot;
using Ms2.Formats.Model;

namespace Ms2Godot.Net;

/// <summary>The effect shader's parameter names, allocated once: a string literal passed to SetShaderParameter makes a new finalizable StringName on every call.</summary>
static class FxParam
{
    public static readonly StringName HasTex = "has_tex", HeightFog = "height_fog", Tex = "tex", HasGlow = "has_glow", GlowTex = "glow_tex", Tint = "tint",
        Frames = "frames", Alpha = "alpha", BaseR0 = "base_r0", BaseR1 = "base_r1", GlowR0 = "glow_r0", GlowR1 = "glow_r1",
        BaseUv2 = "base_uv2", GlowUv2 = "glow_uv2", BaseClamp = "base_clamp", GlowClamp = "glow_clamp",
        HasDetail = "has_detail", DetailTex = "detail_tex", DetailR0 = "detail_r0", DetailR1 = "detail_r1", DetailUv2 = "detail_uv2", DetailClamp = "detail_clamp",
        Ambient = "mat_ambient", VcEmi = "vc_emi", VcDif = "vc_dif", VcAmb = "vc_amb", VcOpacity = "vc_opacity";
}

/// <summary>
/// A playing effect NIF: its node tree with the transform controllers, its static meshes with the alpha, uv-scroll and flipbook controllers, and its
/// particle systems simulated on the CPU (emitters, gravity and drag forces, size / color keys over the particle's age, grow and shrink ramps)
/// and drawn as camera-facing or velocity-aligned quads in one MultiMesh per system. All of it lives in MS2 file space (Z up) under one rotated node.
/// Not simulated: bomb and other forces, colliders, spawners (death spawns), emitter-radius / color / morph controllers, mesh particles (drawn as quads).
/// </summary>
public sealed partial class EffectInstance : Node3D
{
    sealed class MeshState
    {
        public EffectMesh Def; public MeshInstance3D Node; public ShaderMaterial Mat; public Texture2D[] Flip; public Variant[] FlipV; public Texture2D Base; public bool Add;
        public string BaseKey; public TexMap BaseMap, GlowMap, DetailMap;   // the base map's ModelMesh.Maps key ("base" or "shader0") and the drawn maps
        public TexTransform BaseT, GlowT, DetailT;                // working transforms the uv controllers write each frame
        public readonly float[] Rows = new float[6];             // their matrix rows, reused every frame (no per-frame allocation)
        public readonly double[] Lit = new double[3], Amb = new double[3];   // EffectMesh.Shade's output, reused every frame
        public bool Static, Placed;                               // no node from it to the root animates or faces the camera: placed once
        // the last value set on each per-frame shader parameter: a parameter is set again only when its value changes
        public Vector4 LastTint = Vector4.One; public Vector3 LastAmb; public float LastAlpha; public Texture2D LastTex;
        // base r0 r1, glow r0 r1, detail r0 r1 (the shader defaults)
        public readonly Vector3[] LastRows = { new(1, 0, 0), new(0, 1, 0), new(1, 0, 0), new(0, 1, 0), new(1, 0, 0), new(0, 1, 0) };
    }

    sealed class PSys
    {
        public ParticleSystemDef Def; public MultiMeshInstance3D Node; public MultiMesh MM; public ShaderMaterial Mat;
        public Vector3[] Pos, Vel; public float[] Age, Life, Size, Rot, RotSpeed, Frame0; public int Count;
        public double[] Accum; public float[] Buf;
        public int Cap, LastCount;                                // the MultiMesh's instance count (live particles fit) and last frame's live count
        public Aabb Box; public bool HasBox;                      // the MultiMesh's CustomAabb: every quad drawn so far, grown only (Step)
        public MeshState Shading;                                 // the system's material (ParticleSystemDef.Material) and its shader parameters
    }

    readonly EffectScene scene; readonly ModelBuilder mb;
    readonly Node3D z = new() { Name = "Z", Transform = new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2), Vector3.Zero) };
    readonly List<MeshState> meshes = new(); readonly List<PSys> systems = new();
    readonly Transform3D[] world; readonly bool[] done;
    // each node's file transform in Godot form, with its orthonormal basis and X scale (the animated pose's rest parts)
    readonly Transform3D[] rest; readonly Basis[] restBasis; readonly float[] restScale;
    readonly int[] dynNodes;             // the nodes whose world transform changes over time (an animated or billboard node on the way to the root)
    readonly bool needCam;               // a billboard node or a particle system reads the camera
    bool inTree, shown = true;           // shown: z's Visible as last set (a new node is visible)
    readonly Random rng;
    readonly float emitUntil, life;
    float t;
    public bool Finished { get; private set; }
    public int ParticlesEmitted;
    public float Delay;                  // seconds before it starts (the effect xml's startDelay)
    /// <summary>Placement in file space (Z up): translate, rotate, scale from the effect xml.</summary>
    public Transform3D FileSpace { set => z.Transform = new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2), Vector3.Zero) * value; }

    static Shader partAdd, partMix;
    static readonly Dictionary<string, Shader> meshShaders = new();
    // Textures and colours are taken raw (no source_color): the colour buffer holds gamma values (field_lit.gdshader).
    static readonly string MeshCode = @"shader_type spatial;
render_mode unshaded, MESHSTATE, BLEND;
uniform sampler2D tex : repeat_enable, filter_linear;
uniform bool has_tex = true;
uniform sampler2D glow_tex : repeat_enable, filter_linear;
uniform bool has_glow = false;
uniform sampler2D detail_tex : repeat_enable, filter_linear;
uniform bool has_detail = false;
uniform vec3 detail_r0 = vec3(1.0, 0.0, 0.0);
uniform vec3 detail_r1 = vec3(0.0, 1.0, 0.0);
uniform bool detail_uv2 = false;
uniform int detail_clamp = 0;
uniform vec3 mat_ambient = vec3(0.0);
global uniform vec3 ms2_ambient;       // the scene's ambient light colour * dimmer (Field.cs)
uniform vec3 base_r0 = vec3(1.0, 0.0, 0.0);
uniform vec3 base_r1 = vec3(0.0, 1.0, 0.0);
uniform vec3 glow_r0 = vec3(1.0, 0.0, 0.0);
uniform vec3 glow_r1 = vec3(0.0, 1.0, 0.0);
uniform bool base_uv2 = false;
uniform bool glow_uv2 = false;
uniform int base_clamp = 0;
uniform int glow_clamp = 0;
uniform float alpha = 1.0;
uniform vec4 tint = vec4(1.0);
// the terms the vertex colour (COLOR) stands in for: EffectMesh.VertexColors
uniform bool vc_emi = false;
uniform bool vc_dif = false;
uniform bool vc_amb = false;
uniform bool vc_opacity = false;
FOGDECL
void vertex() { FOGVERT }
// a map's uv: its texture transform's rows, then the clamp mode (bit 0 S, bit 1 T) as CLAMP addressing: the samplers
// have no mipmaps, so holding the coordinate half a texel inside the edge is what clamped addressing reads
vec2 map_uv(vec2 uv, vec3 r0, vec3 r1, int cl, ivec2 size) {
    vec2 o = vec2(dot(r0, vec3(uv, 1.0)), dot(r1, vec3(uv, 1.0)));
    vec2 h = 0.5 / vec2(max(size, ivec2(1)));
    if ((cl & 1) != 0) o.x = clamp(o.x, h.x, 1.0 - h.x);
    if ((cl & 2) != 0) o.y = clamp(o.y, h.y, 1.0 - h.y);
    return o;
}
void fragment() {
    vec4 t = has_tex ? texture(tex, map_uv(base_uv2 ? UV2 : UV, base_r0, base_r1, base_clamp, textureSize(tex, 0))) : vec4(1.0);
    // the detail map multiplies the base colour by twice its colour and the opacity by its alpha (MS2StandardMaterial::
    // HandleDetailMap, ms2standardmaterial.cpp:681-694, its opacity at :342-343)
    if (has_detail) {
        vec4 d = texture(detail_tex, map_uv(detail_uv2 ? UV2 : UV, detail_r0, detail_r1, detail_clamp, textureSize(detail_tex, 0)));
        t = vec4(t.rgb * d.rgb * 2.0, t.a * d.a);
    }
    // the texture colour times the diffuse coefficient, emissive + diffuse (tint) + ambient * the scene ambient (EffectMesh.Shade)
    // The coefficient is not saturated (EffectMesh.Shade), and neither is the colour the pixel shader writes:
    // HandleMS2FinalPixelOutputs (@0x14274fd00, ms2standardmaterial.cpp:758-792) ends in CompositeFinalRGBColor and CompositeFinalRGBAColor,
    // which pass the colour on as it is (NiStandardMaterialNodeLibrary.cpp:3057-3106). The client blends it into its FP16
    // scene target, which PostProcessingService::OnInit (@0x1416a4860, postprocessingservice.cpp:1750-1751) turns on whenever
    // the device can convert FP16 to RGBA32 (@0x1416a69a0, :1946-1991; SetFloatTargetFormat @0x1416a36f0, :1400-1411), the
    // HDR option aside. So a colour above 1 at a low opacity keeps its hue and weight, and only the display clamps the sum.
    // Godot's 3D buffer is RGBA16F too, so the colour goes out unclamped.
    // The vertex colour takes the place of the terms EffectMesh.VertexColors names (Shade leaves those out of tint and
    // mat_ambient), and its alpha that of the material alpha (NiStandardMaterial::HandleInitialSpecAmbDiffEmissiveColor, NiStandardMaterial.cpp:4377-4450).
    vec3 coef = tint.rgb + mat_ambient * ms2_ambient;
    if (vc_emi) coef += COLOR.rgb;
    if (vc_dif) coef += COLOR.rgb;
    if (vc_amb) coef += COLOR.rgb * ms2_ambient;
    vec3 col = t.rgb * coef;
    // a glow map adds its colour whatever the material colour is
    if (has_glow) col += texture(glow_tex, map_uv(glow_uv2 ? UV2 : UV, glow_r0, glow_r1, glow_clamp, textureSize(glow_tex, 0))).rgb;
    // The opacity is not saturated either: CompositeFinalRGBAColor writes it as it is, and the FP16 target blends with it
    // unclamped, so an opacity above 1 (the 02020120 crystals' material alpha 1.2, their ring's controller peak 1.5) weighs
    // the colour by more than 1 (SRCALPHA) and an additive mesh adds more than its colour.
    // STILL-OPEN: MS2AlphaBoostMaterial (ms2materialnodelibrary.cpp:839-846) also scales the colour by an opacity above 1;
    // the particle shader (PartCode) still clamps its colour and opacity.
    FOG = vec4(ms2_fog_color, fog_share);
    ALBEDO = ms2_fog_apply(col, CAMERA_VISIBLE_LAYERS);
    ALPHA = max(t.a * tint.a * (vc_opacity ? COLOR.a : alpha), 0.0);
}";
    static readonly string PartCode = @"shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, BLEND;
uniform sampler2D tex : filter_linear;
uniform bool has_tex = true;
uniform vec2 frames = vec2(1.0, 1.0);
uniform vec3 mat_ambient = vec3(0.0);
global uniform vec3 ms2_ambient;
uniform float alpha = 1.0;
uniform vec4 tint = vec4(1.0);
uniform bool vc_emi = false;
uniform bool vc_dif = false;
uniform bool vc_amb = false;
uniform bool vc_opacity = false;
varying float frame;
FOGDECL
void vertex() { frame = INSTANCE_CUSTOM.x; FOGVERT }
void fragment() {
    float fi = floor(frame + 0.5);
    vec2 cell = vec2(mod(fi, frames.x), floor(fi / frames.x));
    vec4 t = has_tex ? texture(tex, (UV + cell) / frames) : vec4(1.0);
    // the mesh shader's colour terms (MeshCode, EffectMesh.Shade): the particle's key colour is its vertex colour
    // (ParticleSystemDef.Material), so the material gives the terms the vertex colour mode leaves (AMB_DIFF: the emissive)
    vec3 coef = tint.rgb + mat_ambient * ms2_ambient;
    if (vc_emi) coef += COLOR.rgb;
    if (vc_dif) coef += COLOR.rgb;
    if (vc_amb) coef += COLOR.rgb * ms2_ambient;
    // neither the colour nor the opacity is saturated, as in the mesh shader (MeshCode): the quads draw through
    // SoftParticlesMaterial, a standard material whose outputs go to the FP16 target as they are
    // (CompositeFinalRGBAColor, NiStandardMaterialNodeLibrary.cpp:3057-3106; postprocessingservice.cpp:1750-1751)
    FOG = vec4(ms2_fog_color, fog_share);
    ALBEDO = ms2_fog_apply(t.rgb * coef, CAMERA_VISIBLE_LAYERS);
    ALPHA = max(t.a * tint.a * (vc_opacity ? COLOR.a : alpha), 0.0);
}";
    // The client's fog for an effect's meshes and particle systems. A NIF effect carries no NiFogProperty (none of the
    // effect pack's nifs has one), so its meshes take the default fog property ApplyEffect::SetFog sets for the field
    // (applyeffect.cpp:586-622: NiFogProperty::ms_spDefault), and their materials are the field's: MS2StandardMaterial
    // (CalculateBiasedFog, ms2standardmaterial.cpp:535-584), or a plain NiStandardMaterial (SoftParticlesMaterial), whose
    // CalculateFog takes the same FogStartEnd and gakFogBias with the same linear-z formula (NiStandardMaterial.cpp:5074-5131,
    // NiStandardMaterialNodeLibrary.cpp:1663-1792). So the depth fog is ms2_fog.gdshaderinc's, per vertex, and ApplyFog
    // lerps to the fog colour by it in every blend mode (an additive effect adds the fog colour at a distance). The height
    // fog comes first, but not on meshes whose dest blend is ONE (NiStandardMaterial::GenerateDescriptor,
    // NiStandardMaterial.cpp:687-697; the Godot height_fog flag). The glow and shimmer passes have no fog
    // (ms2glowmaterial.cpp:126, ms2shimmermaterial.cpp:52); the ribbon, swoosh, beam and lightning effects turn it off on
    // their own meshes (NiFogProperty SetFog(false)), and are not drawn here.
    const string FogDecl = @"#include ""res://shaders/ms2_fog.gdshaderinc""
global uniform vec3 ms2_hfog_color;
global uniform vec3 ms2_hfog;
uniform bool height_fog = true;
varying vec3 fog_world;
vec3 ms2_fog_apply(vec3 c, uint layers) {
    if ((layers & FOG_LAYER) != 0u && height_fog) {
        float hf = ms2_hfog.z * (1.0 - clamp((fog_world.y - ms2_hfog.y) / (ms2_hfog.x - ms2_hfog.y), 0.0, 1.0));
        c = mix(c, ms2_hfog_color, hf);
    }
    return c;
}";
    const string FogVert = "fog_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; fog_share = ms2_fog_share(VIEW_MATRIX, fog_world, CAMERA_VISIBLE_LAYERS);";

    // FogDecl / FogVert in place of the FOGDECL / FOGVERT marks: the height fog goes into the colour, the depth fog
    // through FOG (as character_lit; the colour buffer holds gamma values)
    static string WithFog(string code) => code.Replace("FOGDECL", FogDecl).Replace("FOGVERT", FogVert);

    // the engine's bAdditive for the height fog: an NiAlphaProperty whose dest blend is ONE, blending on or not
    // (NiStandardMaterial.cpp:690-692)
    static bool DestOne(int alphaFlags) => alphaFlags >= 0 && ((alphaFlags >> 5) & 15) == 0;

    static Shader Sh(ref Shader cache, string code, string blend) => cache ??= new Shader { Code = WithFog(code).Replace("BLEND", blend) };

    /// <summary>A mesh's render modes from its NiZBufferProperty and NiStencilProperty (the nearest up the graph, ModelMesh.ZFlags
    /// / DrawMode). Z write on = depth_draw_always (the portals' outer shells are 15, their glow shells 13: test, no write); z test
    /// off = depth_test_disabled. The test function is TEST_LESSEQUAL in every one checked (the default, NiZBufferProperty.inl:22),
    /// which is Godot's own test. The draw mode maps to D3D culling as NiDX9RenderState does for the right-handed
    /// camera (NiDX9RenderState::ApplyStencil, NiDX9RenderState.cpp:592, mapping :380-388): CCW_OR_BOTH and CCW cull the back faces as the field meshes do (field_lit's
    /// cull_back), CW the front, BOTH none (the portals' NiStencilProperty 19840 is DRAW_BOTH with the stencil test off).
    /// The stencil test itself is not drawn: no effect NIF sets the stencil enable bit (RenderStateTests.NoEffectEnablesStencil).
    /// The field meshes cull the same way (ModelBuilder.CullDefine).
    /// STILL-OPEN: Godot flips the culled side for a node whose transform has a negative determinant (a mirrored effect);
    /// D3D9 keeps the render state's cull mode whatever the world matrix.</summary>
    internal static string RenderModes(ModelMesh m)
    {
        string cull = m.Cull switch { FaceCull.None => "cull_disabled", FaceCull.Front => "cull_front", _ => "cull_back" };
        string depth = m.ZWrite ? "depth_draw_always" : "depth_draw_never";
        return m.ZTest ? $"{cull}, {depth}" : $"{cull}, {depth}, depth_test_disabled";
    }

    static Shader MeshShader(ModelMesh m, bool add)
    {
        string state = RenderModes(m), key = state + (add ? " add" : " mix");
        if (!meshShaders.TryGetValue(key, out var sh))
            meshShaders[key] = sh = new Shader { Code = WithFog(MeshCode).Replace("MESHSTATE", state).Replace("BLEND", add ? "blend_add" : "blend_mix") };
        return sh;
    }

    static float Rad(float x) => x;

    public EffectInstance(EffectScene scene, ModelBuilder mb, float duration, int seed)
    {
        this.scene = scene; this.mb = mb; rng = new Random(seed);
        // The client removes an effect once the game time passes start + duration (CEffect::vfunc_9, OnTick), with no tail
        // and no fade: a fade-out runs only when the effect is faded on request. A zero duration takes the longest
        // controller end time of the loaded nif (CNifEffect::vfunc_3).
        emitUntil = duration > 0 ? duration : (float)scene.Duration;
        life = emitUntil;
        world = new Transform3D[scene.Nodes.Count]; done = new bool[scene.Nodes.Count];
        rest = new Transform3D[scene.Nodes.Count]; restBasis = new Basis[scene.Nodes.Count]; restScale = new float[scene.Nodes.Count];
        for (int i = 0; i < scene.Nodes.Count; i++)
        {
            rest[i] = ModelBuilder.ToGodot(scene.Nodes[i].Local);
            restBasis[i] = rest[i].Basis.Orthonormalized(); restScale[i] = rest[i].Basis.Scale.X;
        }
        var dyn = new bool?[scene.Nodes.Count];
        bool Dyn(int i) => i >= 0 && (dyn[i] ??= scene.Nodes[i].Anim != null || scene.Nodes[i].Billboard || Dyn(scene.Nodes[i].Parent));
        dynNodes = Enumerable.Range(0, scene.Nodes.Count).Where(Dyn).ToArray();
        needCam = scene.Nodes.Any(n => n.Billboard) || scene.Systems.Any(d => d.Emitters.Count > 0);
        AddChild(z);
        foreach (var em in scene.Meshes)
        {
            if (scene.Nodes[em.Node].Culled && false) continue;
            var ms = new MeshState { Def = em };
            var m = em.Mesh;
            bool add = m.AlphaFlags >= 0 && (m.AlphaFlags & 1) != 0 && ((m.AlphaFlags >> 5) & 15) == 0;
            var mat = new ShaderMaterial { Shader = MeshShader(m, add) };
            // SORTING_OFF meshes are drawn unsorted after the opaque buckets and before the depth-sorted alpha list
            // (MS2RenderListProcessor: RenderNoSortList then RenderAlphaList, ms2renderlistprocessor.cpp:264-266, :368-370): a
            // lower priority puts them ahead of the field's other transparent objects. STILL-OPEN: their order among
            // themselves is the culling (scene) order in the client; Godot sorts them by depth within the priority.
            if (m.NoSort) mat.RenderPriority = -1;
            ms.BaseKey = m.Textures.ContainsKey("base") ? "base" : "shader0";
            ms.Base = m.Textures.TryGetValue(ms.BaseKey, out var bn) ? mb.Texture(bn) : null;
            ms.BaseMap = m.Maps.GetValueOrDefault(ms.BaseKey);
            mat.SetShaderParameter(FxParam.HasTex, ms.Base != null);
            if (DestOne(m.AlphaFlags)) mat.SetShaderParameter(FxParam.HeightFog, false);
            if (ms.Base != null) mat.SetShaderParameter(FxParam.Tex, ms.Base);
            if (m.Textures.TryGetValue("glow", out var gn) && mb.Texture(gn) is { } gt)
            {
                mat.SetShaderParameter(FxParam.HasGlow, true); mat.SetShaderParameter(FxParam.GlowTex, gt);
                ms.GlowMap = m.Maps.GetValueOrDefault("glow");
            }
            if (m.Textures.TryGetValue("detail", out var dn) && mb.Texture(dn) is { } dt)
            {
                mat.SetShaderParameter(FxParam.HasDetail, true); mat.SetShaderParameter(FxParam.DetailTex, dt);
                ms.DetailMap = m.Maps.GetValueOrDefault("detail");
            }
            MapSetup(mat, ms.BaseMap, m, FxParam.BaseUv2, FxParam.BaseClamp);
            MapSetup(mat, ms.GlowMap, m, FxParam.GlowUv2, FxParam.GlowClamp);
            MapSetup(mat, ms.DetailMap, m, FxParam.DetailUv2, FxParam.DetailClamp);
            ms.Mat = mat;
            SetRows(ms, ms.BaseMap?.Transform, 0, true);
            SetRows(ms, ms.GlowMap?.Transform, 1, true);
            SetRows(ms, ms.DetailMap?.Transform, 2, true);
            ms.BaseT = new TexTransform(); ms.GlowT = new TexTransform(); ms.DetailT = new TexTransform();
            ms.Add = add;
            var vcu = em.VertexColors;
            if (vcu.Emissive) mat.SetShaderParameter(FxParam.VcEmi, true);
            if (vcu.Diffuse) mat.SetShaderParameter(FxParam.VcDif, true);
            if (vcu.Ambient) mat.SetShaderParameter(FxParam.VcAmb, true);
            if (vcu.Opacity) mat.SetShaderParameter(FxParam.VcOpacity, true);
            Shade(ms, 0, true);
            mat.SetShaderParameter(FxParam.Alpha, ms.LastAlpha = (float)em.Opacity(0));   // the static material alpha (portal meshes are 0.05-0.75)
            ms.LastTex = ms.Base;
            if (em.Flip != null)
            {
                ms.Flip = em.Flip.Sources.Select(f => f == null ? null : mb.Texture(f)).ToArray();
                // the frames as Variants made once: converting a Texture2D at each SetShaderParameter allocates (112 B)
                ms.FlipV = ms.Flip.Select(x => x == null ? default : Variant.From(x)).ToArray();
            }
            ms.Static = !Dyn(em.Node);
            // No shadow. A skill effect (CNifEffect) roots in EffectNode(useLighting, useShadow) (nifeffectparam.cpp:680), and
            // useShadow defaults to false (:353; an xml attribute, :395). EffectNode passes it on as LightUnaffectedNode's second
            // argument (effectservice.cpp:916); when that is false the node joins every shadow generator's unaffected caster and
            // receiver lists (lightunaffectednode.cpp:43-49), and SetShadowCaster(root, useShadow) clears the caster flag (:804).
            // STILL-OPEN: a field entity's EffectMesh graph is flagged a caster like any entity graph (WorldService::
            // SetShadowEnabled, @0x141bd30f0); whether the client's shadow-write pass draws its alpha-blended meshes is not traced.
            // Every mesh here is blended, and the field's blended meshes cast none either (FieldBuilder: Godot leaves them out).
            ms.Node = new MeshInstance3D { Mesh = mb.BuildMesh(m, false, uv2: true, vertexColor: vcu.Any), MaterialOverride = mat, Name = m.Name ?? "mesh", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = !scene.Nodes[em.Node].Culled };
            z.AddChild(ms.Node);
            meshes.Add(ms);
        }
        foreach (var d in scene.Systems)
        {
            if (d.Emitters.Count == 0) continue;
            var ps = new PSys { Def = d };
            int n = d.MaxParticles;
            ps.Pos = new Vector3[n]; ps.Vel = new Vector3[n]; ps.Age = new float[n]; ps.Life = new float[n]; ps.Size = new float[n]; ps.Rot = new float[n]; ps.RotSpeed = new float[n]; ps.Frame0 = new float[n];
            ps.Accum = new double[d.Emitters.Count];
            var mat = new ShaderMaterial { Shader = d.Additive ? Sh(ref partAdd, PartCode, "blend_add") : Sh(ref partMix, PartCode, "blend_mix") };
            var tex = d.Texture != null ? mb.Texture(d.Texture) : null;
            mat.SetShaderParameter(FxParam.HasTex, tex != null);
            if (DestOne(d.AlphaFlags)) mat.SetShaderParameter(FxParam.HeightFog, false);
            if (tex != null) mat.SetShaderParameter(FxParam.Tex, tex);
            mat.SetShaderParameter(FxParam.Frames, new Vector2(d.FramesAcross, d.FramesDown));
            ps.Mat = mat;
            // The quads are drawn through SoftParticlesMaterial, an NiStandardMaterial that adds only the soft-depth opacity
            // (softparticlesmaterial.cpp:210-279; ms2materiallibrary.cpp:481-482), so their colour is the standard material's,
            // as a mesh's: the key colour stands in for the terms of the NiVertexColorProperty mode (EffectMesh.VertexColors;
            // NiStandardMaterial::HandleInitialSpecAmbDiffEmissiveColor, NiStandardMaterial.cpp:4377-4450). Nearly every
            // system is AMB_DIFF with E_A_D (flags 40): the emissive is added to the key colour times the light, so a dark key
            // colour over a dark texture still shows the emissive.
            // STILL-OPEN: the soft-depth opacity (GetParticleOpacity, softparticlesmaterial.cpp:241-279) is not drawn.
            ps.Shading = new MeshState { Def = d.Material, Mat = mat };
            var vcu = d.Material.VertexColors;
            if (vcu.Emissive) mat.SetShaderParameter(FxParam.VcEmi, true);
            if (vcu.Diffuse) mat.SetShaderParameter(FxParam.VcDif, true);
            if (vcu.Ambient) mat.SetShaderParameter(FxParam.VcAmb, true);
            if (vcu.Opacity) mat.SetShaderParameter(FxParam.VcOpacity, true);
            Shade(ps.Shading, 0, true);
            mat.SetShaderParameter(FxParam.Alpha, ps.Shading.LastAlpha = (float)d.Material.Opacity(0));
            // The quad's corners sit at +-(right +- up) times the particle size (initial size times the size multiplier):
            // NiPSFacingQuadGeneratorKernel.cpp:127-131, NiPSAlignedQuadGeneratorKernelVV.cpp:103-129. So the quad is 2 x 2
            // and the per-particle basis carries the size. STILL-OPEN: the aligned quads' velocity scale is
            // (amount * speed * limit + rest) / (amount * speed + 1) per axis there (NiPSAlignedQuadGeneratorKernelVV.cpp:108-118).
            ps.MM = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, InstanceCount = 0, VisibleInstanceCount = 0,
                Mesh = new QuadMesh { Size = new Vector2(2, 2), Material = mat } };
            ps.Node = new MultiMeshInstance3D { Multimesh = ps.MM, Name = d.Name ?? "particles", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            z.AddChild(ps.Node);
            systems.Add(ps);
        }
        Advance(0);
    }

    /// <summary>A mesh's colour terms at effect time t (EffectMesh.Shade) as the tint and mat_ambient parameters, each set
    /// only when it changed (always when force).
    /// STILL-OPEN, the lights: the client lights effect meshes. A skill effect's EffectNode passes its parent's dynamic effects
    /// (the lights) down only when useLighting, which defaults to true (LightUnaffectedNode::UpdateEffectsDownward,
    /// lightunaffectednode.cpp:75; nifeffectparam.cpp:353). Which of the field's lights reach it there, and which reach a field
    /// entity's EffectMesh graph, is not traced, so the mesh is drawn unshaded: diffuse at full light plus emissive, plus the
    /// ambient colour times the field's ambient light.</summary>
    static void Shade(MeshState ms, double t, bool force)
    {
        ms.Def.Shade(t, ms.Lit, ms.Amb);
        // a Vector4, not a Color: Godot converts a Color set on a vec4 uniform from sRGB to linear, which took 0.5 to 0.21;
        // the colour buffer holds gamma values (field_lit, ModelBuilder's Vector3 colours), so the coefficient goes in as it is
        var c = new Vector4((float)ms.Lit[0], (float)ms.Lit[1], (float)ms.Lit[2], 1f);
        var a = new Vector3((float)ms.Amb[0], (float)ms.Amb[1], (float)ms.Amb[2]);
        if (force || c != ms.LastTint) ms.Mat.SetShaderParameter(FxParam.Tint, ms.LastTint = c);
        if (force || a != ms.LastAmb) ms.Mat.SetShaderParameter(FxParam.Ambient, ms.LastAmb = a);
    }

    // MS2_PERF_FX=1: every effect's _Process timed (wall time and managed bytes allocated); from 600 frames after the first
    // effect ticks, 1200 frames are summed and printed once: instances per frame, us per instance, us and bytes per frame.
    static readonly bool ProbeOn = System.Environment.GetEnvironmentVariable("MS2_PERF_FX") == "1";
    static ulong probeStart = ulong.MaxValue, probeLast; static int probeFrames, probeInst; static long probeTicks, probeBytes, probeFrameTicks;
    static readonly List<double> probeFrameUs = new(1300);
    static void Probe(long ticks, long bytes)
    {
        ulong f = Engine.GetProcessFrames();
        if (probeStart == ulong.MaxValue) probeStart = f + 600;
        if (f < probeStart || probeFrames > 1200) return;
        if (f != probeLast)
        {
            if (probeLast >= probeStart) probeFrameUs.Add(probeFrameTicks * 1e6 / System.Diagnostics.Stopwatch.Frequency);
            probeLast = f; probeFrameTicks = 0;
            if (++probeFrames > 1200)
            {
                probeFrameUs.Sort();
                double us = probeTicks * 1e6 / System.Diagnostics.Stopwatch.Frequency;
                GD.Print($"fx probe: {probeFrameUs.Count} frames, {(double)probeInst / probeFrameUs.Count:0.0} effects per frame, {us / probeInst:0.0} us per effect, "
                    + $"{probeFrameUs[probeFrameUs.Count / 2]:0} us per frame (median; mean {us / probeFrameUs.Count:0}), {(double)probeBytes / probeFrameUs.Count:0} B allocated per frame; segments us per frame (whole run): camera {seg[0] * 1e6 / System.Diagnostics.Stopwatch.Frequency / probeLast:0.0}, meshes {seg[1] * 1e6 / System.Diagnostics.Stopwatch.Frequency / probeLast:0.0}, systems {seg[2] * 1e6 / System.Diagnostics.Stopwatch.Frequency / probeLast:0.0} (draw loop {seg[4] * 1e6 / System.Diagnostics.Stopwatch.Frequency / probeLast:0.0}, buffer set {seg[3] * 1e6 / System.Diagnostics.Stopwatch.Frequency / probeLast:0.0}, {(double)seg[5] / probeLast:0} particles)");
                return;
            }
        }
        probeInst++; probeTicks += ticks; probeBytes += bytes; probeFrameTicks += ticks;
    }

    /// <summary>The perf gate (HudScene MS2_PERF_GATE): while set, every effect's _Process adds its managed bytes, wall
    /// ticks and one instance to the totals below.</summary>
    public static bool Count;
    public static long CountBytes, CountTicks, CountInstances;

    public override void _Process(double delta)
    {
        if (!ProbeOn && !Count) { Tick(delta); return; }
        long b0 = GC.GetAllocatedBytesForCurrentThread(), s0 = System.Diagnostics.Stopwatch.GetTimestamp();
        Tick(delta);
        long ticks = System.Diagnostics.Stopwatch.GetTimestamp() - s0, bytes = GC.GetAllocatedBytesForCurrentThread() - b0;
        if (Count) { CountBytes += bytes; CountTicks += ticks; CountInstances++; }
        if (ProbeOn) Probe(ticks, bytes);
    }

    void Tick(double delta)
    {
        if (Finished) return;
        if (Delay > 0) { Delay -= (float)delta; if (shown) { z.Visible = false; shown = false; } return; }
        if (!shown) { z.Visible = true; shown = true; }
        Advance((float)delta);
    }

    public override void _EnterTree() => inTree = true;
    public override void _ExitTree() => inTree = false;

    // the frame's camera, fetched once per frame for all effects (one viewport holds them); its position is still read
    // per effect, as the camera may move between two effects' _Process
    static ulong camFrame = ulong.MaxValue; static Camera3D camThisFrame;
    Camera3D FrameCamera()
    {
        if (!inTree) return null;
        ulong f = Engine.GetProcessFrames();
        if (f != camFrame || camThisFrame == null || !IsInstanceValid(camThisFrame)) { camFrame = f; camThisFrame = GetViewport()?.GetCamera3D(); }
        return camThisFrame;
    }

    Transform3D Local(int i, float t)
    {
        var n = scene.Nodes[i];
        if (n.Anim == null) return rest[i];
        double lt = n.AnimCtl != null ? n.AnimCtl.Local(t) : t;
        var basis = restBasis[i]; var origin = rest[i].Origin;
        float scale = restScale[i];
        if (n.Anim.T != null) { var v = n.Anim.T(lt); origin = new Vector3((float)v.X, (float)v.Y, (float)v.Z); }
        if (n.Anim.S != null) scale = (float)n.Anim.S(lt);
        if (n.Anim.R != null) { var q = n.Anim.R(lt); basis = new Basis(new Quaternion((float)q.X, (float)q.Y, (float)q.Z, (float)q.W).Normalized()); }
        return new Transform3D(basis.Scaled(new Vector3(scale, scale, scale)), origin);
    }

    Transform3D World(int i, float t, Vector3 camLocal)
    {
        if (i < 0) return Transform3D.Identity;
        if (done[i]) return world[i];
        var n = scene.Nodes[i];
        var l = Local(i, t);
        var p = n.Parent >= 0 ? World(n.Parent, t, camLocal) : Transform3D.Identity;
        var w = p * l;
        if (n.Billboard)
        {
            // always face the camera: the node's X axis toward the camera, Z kept up (Gamebryo cameras look along their X axis)
            var to = camLocal - w.Origin;
            if (to.LengthSquared() > 1e-6f)
            {
                var x = to.Normalized();
                var up = Vector3.Back;       // +Z in file space
                var y = up.Cross(x);
                if (y.LengthSquared() < 1e-6f) y = Vector3.Up;
                y = y.Normalized();
                var zz = x.Cross(y).Normalized();
                float s = w.Basis.Scale.X;
                w = new Transform3D(new Basis(x, y, zz).Scaled(new Vector3(s, s, s)), w.Origin);
            }
        }
        done[i] = true; world[i] = w;
        return w;
    }

    float R() => (float)rng.NextDouble();
    float Spread(float v) => v * (2 * R() - 1);

    void Advance(float dt)
    {
        t += dt;
        if (t > life) { Finished = true; QueueFree(); return; }
        long q0 = ProbeOn ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        var cam = needCam ? FrameCamera() : null;
        var camLocal = cam != null ? z.GlobalTransform.AffineInverse() * cam.GlobalPosition : new Vector3(0, -1000, 300);
        // the camera's right and up in this effect's space (file space), for the facing particle quads
        Vector3 camRight = new(1, 0, 0), camUp = new(0, 0, 1);
        if (cam != null)
        {
            var inv = z.GlobalTransform.Basis.Inverse();
            camRight = (inv * cam.GlobalBasis.X).Normalized(); camUp = (inv * cam.GlobalBasis.Y).Normalized();
        }
        // a static node's world transform depends on neither the time nor the camera: it is kept from the first frame
        foreach (int i in dynNodes) done[i] = false;
        long q1 = ProbeOn ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

        foreach (var ms in meshes)
        {
            var d = ms.Def;
            if (!ms.Static || !ms.Placed) { ms.Node.Transform = World(d.Node, t, camLocal); ms.Placed = true; }
            if (d.Colors.Count > 0) Shade(ms, t, false);
            if (d.Alpha != null) { float a = (float)d.Opacity(t); if (a != ms.LastAlpha) ms.Mat.SetShaderParameter(FxParam.Alpha, ms.LastAlpha = a); }
            if (d.Uv.Count > 0) AnimateUv(ms, t);
            if (d.Flip != null && ms.Flip != null && ms.Flip.Length > 0)
            {
                double lt = d.Flip.Ctl.Local(t);
                int idx = d.Flip.Curve != null ? (int)Math.Floor(d.Flip.Curve(lt) + 0.001)
                                               : (int)Math.Floor((lt - d.Flip.Ctl.Start) / Math.Max(d.Flip.Ctl.Stop - d.Flip.Ctl.Start, 1e-6) * ms.Flip.Length);
                idx = Math.Clamp(idx, 0, ms.Flip.Length - 1);
                var tex = ms.Flip[idx];
                if (tex != null && tex != ms.LastTex) { ms.LastTex = tex; ms.Mat.SetShaderParameter(FxParam.Tex, ms.FlipV[idx]); }
            }
        }

        long q2 = ProbeOn ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        foreach (var ps in systems)
        {
            var sm = ps.Shading;
            if (sm.Def.Colors.Count > 0) Shade(sm, t, false);
            if (sm.Def.Alpha != null) { float a = (float)sm.Def.Opacity(t); if (a != sm.LastAlpha) sm.Mat.SetShaderParameter(FxParam.Alpha, sm.LastAlpha = a); }
            Step(ps, dt, camLocal, camRight, camUp);
        }
        if (ProbeOn) { long q3 = System.Diagnostics.Stopwatch.GetTimestamp(); seg[0] += q1 - q0; seg[1] += q2 - q1; seg[2] += q3 - q2; }
    }
    // the MS2_PERF_FX probe's segment counters (a measuring tool, printed by Probe; untouched when the probe is off):
    // ticks in camera, meshes, systems, buffer set, draw loop, and the particle count
    static readonly long[] seg = new long[6];

    // The uv controllers of one mesh at effect time t: each active controller replaces one component of its map's transform
    // (UvOp); a map without a static transform gets the controller's default one (TexTransform.ControllerDefault). Only the
    // base, glow and detail maps are drawn here, so controllers on the dark, normal and decal maps have nothing to move.
    // The working transform is rebuilt from the static one each frame and the running controllers replayed over it. The
    // client instead keeps whatever UpdateValue last wrote into the map's transform, so the two differ only when one of two
    // controllers on a map stops (its range is over, or it is inactive) while the other runs: here the stopped component
    // snaps back to the static value, in the client it keeps its last value (not surveyed; expected rare).
    // Only Active controllers run (NiTimeController::Update skips inactive ones, and CNifEffect never calls
    // StartAnimations). STILL-OPEN: FUN_141545250 (nifanimationcontroller.cpp:175-190) calls NiTimeController::
    // StartAnimations on an entity's scene graph, which activates controllers the file leaves inactive; a field-entity
    // effect started that way would run them. The alpha, colour and flip controllers here do not check Active yet.
    static readonly TexTransform NoTransform = TexTransform.ControllerDefault();
    static void AnimateUv(MeshState ms, float t)
    {
        bool baseOn = false, glowOn = false, detailOn = false;
        foreach (var op in ms.Def.Uv)
        {
            if (!op.Ctl.Active) continue;
            TexTransform w;
            if (op.Slot == ms.BaseKey && ms.BaseMap != null) { if (!baseOn) { ms.BaseT.CopyFrom(ms.BaseMap.Transform ?? NoTransform); baseOn = true; } w = ms.BaseT; }
            else if (op.Slot == "glow" && ms.GlowMap != null) { if (!glowOn) { ms.GlowT.CopyFrom(ms.GlowMap.Transform ?? NoTransform); glowOn = true; } w = ms.GlowT; }
            else if (op.Slot == "detail" && ms.DetailMap != null) { if (!detailOn) { ms.DetailT.CopyFrom(ms.DetailMap.Transform ?? NoTransform); detailOn = true; } w = ms.DetailT; }
            else continue;
            w.Set(op.Operation, (float)op.Curve(op.Ctl.Local(t)));
        }
        if (baseOn) SetRows(ms, ms.BaseT, 0, false);
        if (glowOn) SetRows(ms, ms.GlowT, 1, false);
        if (detailOn) SetRows(ms, ms.DetailT, 2, false);
    }

    // a map's texture transform as the shader's two rows (identity without one), map 0 base, 1 glow or 2 detail; a row is set only
    // when it differs from the last one set (always when force)
    static void SetRows(MeshState ms, TexTransform tt, int map, bool force)
    {
        Vector3 a = new(1, 0, 0), b = new(0, 1, 0);
        if (tt != null) { var r = ms.Rows; tt.Rows(r); a = new(r[0], r[1], r[2]); b = new(r[3], r[4], r[5]); }
        int k = map * 2;
        if (force || a != ms.LastRows[k]) ms.Mat.SetShaderParameter(map == 0 ? FxParam.BaseR0 : map == 1 ? FxParam.GlowR0 : FxParam.DetailR0, ms.LastRows[k] = a);
        if (force || b != ms.LastRows[k + 1]) ms.Mat.SetShaderParameter(map == 0 ? FxParam.BaseR1 : map == 1 ? FxParam.GlowR1 : FxParam.DetailR1, ms.LastRows[k + 1] = b);
    }

    // a map's uv set and clamp mode. The map reads its own uv set (TEXCOORD_MASK; NiStandardMaterialDescriptor.cpp:1006,
    // the glow map's index from FindUVSetIndexForTextureEnum, ms2standardmaterial.cpp:1544): set 1 is the mesh's
    // TEXCOORD1 as UV2; a mesh without one reads set 0. STILL-OPEN: what D3D reads for a set the mesh does not have.
    static void MapSetup(ShaderMaterial mat, TexMap map, ModelMesh m, StringName uv2, StringName clamp)
    {
        if (map == null) return;
        mat.SetShaderParameter(uv2, map.UvSet == 1 && m.Uvs1 != null);
        mat.SetShaderParameter(clamp, (map.ClampS ? 1 : 0) | (map.ClampT ? 2 : 0));
    }

    /// <summary>The gravity, bomb and drag forces on this step, in the effect's space (see Step).</summary>
    readonly List<ParticleForces.Step> forcesNow = new();

    void Step(PSys ps, float dt, Vector3 camLocal, Vector3 camRight, Vector3 camUp)
    {
        var d = ps.Def;
        // The particles live in the system's model space and the system's world transform, its scale too, is applied when
        // they are drawn (NiPSParticleSystem::UpdateWorldData keeps the scale of a world-space system, NiPSParticleSystem.cpp:
        // 510-520); an emitter only rotates the velocity into that space (NiPSBoxEmitter.cpp:100-103). Here the particles
        // are kept in the effect's space, so the system node's scale multiplies their size, emitted speed and the forces.
        float psScale = World(d.Node, t, camLocal).Basis.Scale.X;
        // Forces on at this time. A bomb or drag takes its object's position and its object's rotation times its axis
        // (NiPSBombForce::Update, NiPSBombForce.cpp:59-80; NiPSDragForce::Update, NiPSDragForce.cpp:57-78); without an
        // object, or a drag with Percentage <= 0, it is left out (NiPSSimulatorForcesStep.cpp:186-226). The system's
        // space differs from the effect's by the system's transform, so distances there are the effect's over psScale:
        // the bomb's DeltaV and the drag and decay distances are scaled by psScale here.
        // Gravity's position and axis are taken the same way, and gravity without an object is left out
        // (NiPSGravityForce::Update, NiPSGravityForce.cpp:65-79); its Strength is an acceleration (scaled by psScale) and
        // its Decay is per system-space distance (divided by psScale). All three run in the list's order.
        // STILL-OPEN: gravity turbulence (random, NiPSForceHelpers.inl:786-800) is not read or applied.
        forcesNow.Clear();
        foreach (var f in d.Forces)
        {
            bool on = f.Active;
            if (f.On != null) on = f.On(f.OnCtl.Local(t)) > 0.5;
            if (!on) continue;
            if (f.Kind == ForceKind.Drag || f.Kind == ForceKind.Bomb || f.Kind == ForceKind.Gravity)
            {
                if (f.Node < 0 || (f.Kind == ForceKind.Drag && f.Percentage <= 0)) continue;
                var w = World(f.Node, t, camLocal);
                var dir = (w.Basis.Orthonormalized() * new Vector3((float)f.Axis.X, (float)f.Axis.Y, (float)f.Axis.Z)).Normalized();
                forcesNow.Add(new ParticleForces.Step
                {
                    Kind = f.Kind, Pos = new(w.Origin.X, w.Origin.Y, w.Origin.Z), Dir = new(dir.X, dir.Y, dir.Z),
                    DecayType = f.DecayType, SymmType = f.SymmType, ForceType = f.ForceType, Percentage = (float)f.Percentage,
                    DeltaV = (float)f.DeltaV * psScale, Strength = (float)f.Strength * psScale,
                    Decay = f.Kind == ForceKind.Gravity ? (psScale != 0 ? (float)f.Decay / psScale : 0) : (float)f.Decay * psScale,
                    Range = f.Range >= float.MaxValue ? float.MaxValue : (float)f.Range * psScale,
                    Falloff = f.RangeFalloff >= float.MaxValue ? float.MaxValue : (float)f.RangeFalloff * psScale,
                });
            }
        }
        // simulate: the forces in their order, each on the velocity the one before left, against the step's start
        // position (ParticleForces), then the move. The forces take the step's dt, where the kernel takes each particle's
        // time since its last update (NiPSSimulatorForcesKernel.cpp:33, NiPSForceHelpers.inl). Every live particle is
        // updated every step here and a new one starts at the step's time, so the two are the same.
        int nForces = forcesNow.Count;
        for (int i = ps.Count - 1; i >= 0; i--)
        {
            ps.Age[i] += dt;
            if (ps.Age[i] >= ps.Life[i]) { Swap(ps, i, ps.Count - 1); ps.Count--; continue; }
            var v = ps.Vel[i];
            if (nForces > 0)
            {
                var p0 = ps.Pos[i];
                var pn = new System.Numerics.Vector3(p0.X, p0.Y, p0.Z);
                var vn = new System.Numerics.Vector3(v.X, v.Y, v.Z);
                vn = ParticleForces.Apply(vn, pn, forcesNow, dt);
                v = new Vector3(vn.X, vn.Y, vn.Z);
            }
            ps.Vel[i] = v;
            ps.Pos[i] += v * dt;
            ps.Rot[i] += ps.RotSpeed[i] * dt;
        }
        // emit
        if (t <= emitUntil)
            for (int ei = 0; ei < d.Emitters.Count; ei++)
            {
                var e = d.Emitters[ei];
                double rate = e.Rate != null ? e.Rate(e.RateCtl.Local(t)) : 0;
                bool on = e.On == null || e.On(e.OnCtl.Local(t)) > 0.5;
                if (!on || rate <= 0) continue;
                ps.Accum[ei] += rate * dt;
                while (ps.Accum[ei] >= 1 && ps.Count < ps.Pos.Length) { ps.Accum[ei] -= 1; Emit(ps, e, camLocal, psScale); }
                if (ps.Count >= ps.Pos.Length) ps.Accum[ei] = 0;
            }
        long qd = ProbeOn ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        // draw
        // The MultiMesh holds Cap instances, not the system's full capacity: multimesh_set_buffer takes exactly Cap * 20 floats
        // and copies them all (twice: the managed array to a packed array, then into the storage) and uploads them, so a
        // sparse system would copy its whole capacity every frame. Cap grows by doubling when the live count passes it and
        // shrinks when the count falls under a quarter, so the buffer is reallocated rarely, never per frame.
        int cap = ps.Cap;
        if (ps.Count > cap) cap = Math.Min(ps.Pos.Length, Math.Max(16, Math.Max(ps.Count, cap * 2)));
        else if (cap > 16 && ps.Count * 4 < cap) cap = Math.Max(16, cap / 2);
        bool resized = cap != ps.Cap;
        if (resized) { ps.Cap = cap; ps.MM.InstanceCount = cap; ps.Buf = new float[cap * 20]; }
        var buf = ps.Buf;
        // The system's constants are read once and the per-particle math is written out in floats: the game assembly is built
        // without the optimizer, so each Vector3/Basis operator would be a call. The operations and their order are Godot's
        // (Cross, Normalized, Length, vector * scalar), so the buffer holds the same floats as with the Godot types.
        bool sizeKeys = d.SizeKeys.Length > 0, colorKeys = d.ColorKeys.Length > 0, aligned = d.Aligned, flips = d.NumFrames > 1;
        bool grow = d.GrowTime > 0, shrink = d.ShrinkTime > 0;
        float growTime = (float)d.GrowTime, shrinkTime = (float)d.ShrinkTime;
        float stretchLo = (float)Math.Min(d.ScaleRestV, d.ScaleLimitV), stretchHi = (float)Math.Max(d.ScaleRestV, d.ScaleLimitV);
        float initialTime = (float)d.InitialTime, flipSpan = Mathf.Max((float)(d.FinalTime - d.InitialTime), 1e-4f);
        float flipFactor = d.PingPong ? 1 : 0.999f, numFrames = d.NumFrames, frameMod = Mathf.Max(d.FramesAcross * d.FramesDown, 1);
        float cx = camLocal.X, cy = camLocal.Y, cz = camLocal.Z;
        float rx = camRight.X, ry = camRight.Y, rz = camRight.Z, ux = camUp.X, uy = camUp.Y, uz = camUp.Z;
        float[] age = ps.Age, life = ps.Life, psize = ps.Size, rot = ps.Rot, frame0 = ps.Frame0;
        Vector3[] ppos = ps.Pos, pvel = ps.Vel;
        // the bounds of this frame's quads (a quad reaches |x axis| + |y axis| from its centre), for the culling box below
        float bx0 = float.MaxValue, by0 = float.MaxValue, bz0 = float.MaxValue, bx1 = float.MinValue, by1 = float.MinValue, bz1 = float.MinValue;
        for (int i = 0; i < ps.Count; i++)
        {
            float n = life[i] > 0 ? age[i] / life[i] : 1f;     // a zero lifespan is drawn once at its end
            float size = psize[i] * psScale;
            if (sizeKeys) size *= (float)Lerp(d.SizeKeys, n);
            if (grow) size *= Mathf.Clamp(age[i] / growTime, 0f, 1f);
            if (shrink) size *= Mathf.Clamp((life[i] - age[i]) / shrinkTime, 0f, 1f);
            float cr = 1f, cg = 1f, cb = 1f, ca = 1f;
            if (colorKeys) ColorAt(d.ColorKeys, n, out cr, out cg, out cb, out ca);
            var pos = ppos[i];
            // toCam: camera direction from the particle (Vector3.Back when on it)
            float tx = cx - pos.X, ty = cy - pos.Y, tz = cz - pos.Z, l2 = tx * tx + ty * ty + tz * tz;
            if (l2 > 1e-6f) { float l = MathF.Sqrt(l2); tx /= l; ty /= l; tz /= l; } else { tx = 0f; ty = 0f; tz = 1f; }
            float xx, xy, xz, yx, yy, yz, reach;                  // the basis X and Y columns (Z is toCam); the quad's reach
            if (aligned)
            {
                var vel = pvel[i];
                float sp = MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y + vel.Z * vel.Z);
                float ax = 1f, ay = 0f, az = 0f;                  // along: the velocity direction (Vector3.Right when still)
                if (sp > 1e-3f) { ax = vel.X / sp; ay = vel.Y / sp; az = vel.Z / sp; }
                float stretch = Mathf.Clamp((float)(d.ScaleRestV + d.ScaleAmountV * sp * 0.01), stretchLo, stretchHi);
                float sx = ay * tz - az * ty, sy = az * tx - ax * tz, sz = ax * ty - ay * tx, s2 = sx * sx + sy * sy + sz * sz;
                if (s2 > 1e-6f) { float l = MathF.Sqrt(s2); sx /= l; sy /= l; sz /= l; }
                else
                {
                    // up (Vector3.Back) cross along, normalized
                    sx = 0f * az - 1f * ay; sy = 1f * ax - 0f * az; sz = 0f * ay - 0f * ax; s2 = sx * sx + sy * sy + sz * sz;
                    if (s2 == 0f) { sx = 0f; sy = 0f; sz = 0f; } else { float l = MathF.Sqrt(s2); sx /= l; sy /= l; sz /= l; }
                }
                xx = ax * size * stretch; xy = ay * size * stretch; xz = az * size * stretch;
                yx = sx * size; yy = sy * size; yz = sz * size;
                reach = MathF.Abs(size) * (MathF.Abs(stretch) + 1f);
            }
            else
            {
                // Screen-aligned: every quad spans the camera's right and up (NiPSFacingQuadGenerator.cpp:337-345), its corners
                // at +-(right +- up) turned by the particle's rotation (NiPSFacingQuadGeneratorKernel.cpp:87-99): top right
                // (c + s) right + (c - s) up, so the quad's x axis is c right - s up and its y axis c up + s right.
                float c = MathF.Cos(rot[i]), s = MathF.Sin(rot[i]);
                xx = (rx * c - ux * s) * size; xy = (ry * c - uy * s) * size; xz = (rz * c - uz * s) * size;
                yx = (ux * c + rx * s) * size; yy = (uy * c + ry * s) * size; yz = (uz * c + rz * s) * size;
                reach = MathF.Abs(size) * 2f;
            }
            if (pos.X - reach < bx0) bx0 = pos.X - reach; if (pos.X + reach > bx1) bx1 = pos.X + reach;
            if (pos.Y - reach < by0) by0 = pos.Y - reach; if (pos.Y + reach > by1) by1 = pos.Y + reach;
            if (pos.Z - reach < bz0) bz0 = pos.Z - reach; if (pos.Z + reach > bz1) bz1 = pos.Z + reach;
            // one buffer write per system instead of three engine calls per particle: 12 transform floats (rows), colour, custom data
            int o = i * 20;
            buf[o] = xx; buf[o + 1] = yx; buf[o + 2] = tx; buf[o + 3] = pos.X;
            buf[o + 4] = xy; buf[o + 5] = yy; buf[o + 6] = ty; buf[o + 7] = pos.Y;
            buf[o + 8] = xz; buf[o + 9] = yz; buf[o + 10] = tz; buf[o + 11] = pos.Z;
            buf[o + 12] = cr; buf[o + 13] = cg; buf[o + 14] = cb; buf[o + 15] = ca;
            float frame = frame0[i];
            if (flips)
            {
                float u = Mathf.Clamp((n - initialTime) / flipSpan, 0f, 1f);
                frame = (frame0[i] + Mathf.Floor(u * numFrames * flipFactor)) % frameMod;
            }
            buf[o + 16] = frame; buf[o + 17] = 0; buf[o + 18] = 0; buf[o + 19] = 0;
        }
        if (ProbeOn) seg[4] += System.Diagnostics.Stopwatch.GetTimestamp() - qd;
        long qb = ProbeOn ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        if (ps.Count > 0) ps.MM.Buffer = buf;
        // The culling box: the quads drawn so far, grown only, by a quarter of its size past the new quads when they leave it,
        // so it is set a few times per system and not per frame. A fixed 10 km box kept every system of a field drawn
        // wherever the camera was (165 field-model systems on 02020120). Without it Godot would recompute the box from the
        // buffer on every set. The particles are drawn in this node's space, so the box is in it too.
        if (ps.Count > 0 && (!ps.HasBox || bx0 < ps.Box.Position.X || by0 < ps.Box.Position.Y || bz0 < ps.Box.Position.Z
            || bx1 > ps.Box.End.X || by1 > ps.Box.End.Y || bz1 > ps.Box.End.Z))
        {
            if (ps.HasBox)
            {
                bx0 = Math.Min(bx0, ps.Box.Position.X); by0 = Math.Min(by0, ps.Box.Position.Y); bz0 = Math.Min(bz0, ps.Box.Position.Z);
                bx1 = Math.Max(bx1, ps.Box.End.X); by1 = Math.Max(by1, ps.Box.End.Y); bz1 = Math.Max(bz1, ps.Box.End.Z);
            }
            float mx = (bx1 - bx0) * 0.25f, my = (by1 - by0) * 0.25f, mz = (bz1 - bz0) * 0.25f;
            ps.Box = new Aabb(new Vector3(bx0 - mx, by0 - my, bz0 - mz), new Vector3(bx1 - bx0 + 2 * mx, by1 - by0 + 2 * my, bz1 - bz0 + 2 * mz));
            ps.HasBox = true;
            ps.MM.CustomAabb = ps.Box;
        }
        if (ProbeOn) { seg[3] += System.Diagnostics.Stopwatch.GetTimestamp() - qb; seg[5] += ps.Count; }
        if (ps.Count != ps.LastCount || resized) { ps.MM.VisibleInstanceCount = ps.Count; ps.LastCount = ps.Count; }
    }

    static void Swap(PSys p, int a, int b)
    {
        if (a == b) return;
        (p.Pos[a], p.Pos[b]) = (p.Pos[b], p.Pos[a]); (p.Vel[a], p.Vel[b]) = (p.Vel[b], p.Vel[a]); (p.Age[a], p.Age[b]) = (p.Age[b], p.Age[a]);
        (p.Life[a], p.Life[b]) = (p.Life[b], p.Life[a]); (p.Size[a], p.Size[b]) = (p.Size[b], p.Size[a]); (p.Rot[a], p.Rot[b]) = (p.Rot[b], p.Rot[a]);
        (p.RotSpeed[a], p.RotSpeed[b]) = (p.RotSpeed[b], p.RotSpeed[a]); (p.Frame0[a], p.Frame0[b]) = (p.Frame0[b], p.Frame0[a]);
    }

    static double Lerp((double t, double v)[] k, float n)
    {
        if (n <= k[0].t) return k[0].v;
        if (n >= k[^1].t) return k[^1].v;
        for (int i = 0; i + 1 < k.Length; i++)
            if (n >= k[i].t && n <= k[i + 1].t) { double u = (n - k[i].t) / Math.Max(k[i + 1].t - k[i].t, 1e-9); return k[i].v * (1 - u) + k[i + 1].v * u; }
        return k[^1].v;
    }

    static void ColorAt((double t, byte r, byte g, byte b, byte a)[] k, float n, out float cr, out float cg, out float cb, out float ca)
    {
        int i0 = k.Length - 1, i1 = i0; float u = 0;                // the keys around n and the weight (one key at the ends)
        if (n <= k[0].t) i0 = i1 = 0;
        else if (n < k[^1].t)
            for (int i = 0; i + 1 < k.Length; i++)
                if (n >= k[i].t && n <= k[i + 1].t) { i0 = i; i1 = i + 1; u = (float)((n - k[i].t) / Math.Max(k[i + 1].t - k[i].t, 1e-9)); break; }
        cr = k[i0].r / 255f; cg = k[i0].g / 255f; cb = k[i0].b / 255f; ca = k[i0].a / 255f;
        if (i0 == i1) return;
        // Color.Lerp: from + (to - from) * weight per channel
        cr += (k[i1].r / 255f - cr) * u; cg += (k[i1].g / 255f - cg) * u; cb += (k[i1].b / 255f - cb) * u; ca += (k[i1].a / 255f - ca) * u;
    }

    void Emit(PSys ps, EmitterDef e, Vector3 camLocal, float psScale)
    {
        // the lifespan is drawn first; a particle already past it is not created (NiPSEmitter.cpp:139-144, its age at
        // emission is 0 here)
        float lifespan = (float)e.Lifespan + Spread((float)e.LifespanVar / 2);
        if (lifespan < 0) return;
        int i = ps.Count++;
        ParticlesEmitted++;
        var ew = World(e.Node >= 0 ? e.Node : ps.Def.Node, t, camLocal);
        Vector3 local;
        switch (e.Kind)
        {
            case EmitterKind.Box: local = new Vector3(Spread((float)e.Width / 2), Spread((float)e.Height / 2), Spread((float)e.Depth / 2)); break;
            case EmitterKind.Sphere: { Vector3 v; do v = new Vector3(Spread(1), Spread(1), Spread(1)); while (v.LengthSquared() > 1); local = v * (float)e.Radius; break; }
            case EmitterKind.Cylinder: { float a = R() * Mathf.Tau, r = Mathf.Sqrt(R()) * (float)e.Radius; local = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, Spread((float)e.Height / 2)); break; }
            case EmitterKind.Mesh when e.MeshPositions != null && e.MeshPositions.Length >= 3:
            {
                int vi = rng.Next(e.MeshPositions.Length / 3) * 3;
                var v = new Vector3(e.MeshPositions[vi], e.MeshPositions[vi + 1], e.MeshPositions[vi + 2]);
                var mw = e.MeshNode >= 0 ? World(e.MeshNode, t, camLocal) : Transform3D.Identity;
                // the emitter's own transform positions the mesh points relative to the emitter object
                ps.Pos[i] = mw * v;
                local = Vector3.Zero;
                goto haveMeshPos;
            }
            default: local = Vector3.Zero; break;
        }
        ps.Pos[i] = ew * local;
        haveMeshPos:
        // NiPSEmitter::ComputeDirection; speed and life spread over +-var/2 (ComputeSpeed, NiPSEmitter.cpp:139), the other
        // variances over +-var; then the speed flip (NiPSEmitter.cpp:186-193). STILL-OPEN: a mesh emitter's
        // VELOCITY_USE_NORMALS / VELOCITY_USE_RANDOM types (NiPSMeshEmitter.cpp:789-818); every emitter uses the direction here.
        double dec = e.Declination + Spread((float)e.DeclinationVar), pl = e.PlanarAngle + Spread((float)e.PlanarAngleVar);
        var dl = EmitterDef.Direction(dec, pl, ps.Def.PreRpi);
        var dir = (ew.Basis.Orthonormalized() * new Vector3((float)dl.X, (float)dl.Y, (float)dl.Z)).Normalized();
        float speed = (float)e.Speed + Spread((float)e.SpeedVar / 2);
        if (e.SpeedFlipRatio > 0 && R() < e.SpeedFlipRatio) speed = -speed;
        ps.Vel[i] = dir * (speed * psScale);
        ps.Life[i] = lifespan;
        ps.Size[i] = Mathf.Max(0f, (float)e.Size + Spread((float)e.SizeVar));
        ps.Age[i] = 0;
        ps.Rot[i] = (float)e.RotAngle + Spread((float)e.RotAngleVar);
        float rs = (float)e.RotSpeed + Spread((float)e.RotSpeedVar);
        if (e.RandomRotSpeedSign && R() < 0.5f) rs = -rs;
        ps.RotSpeed[i] = rs;
        ps.Frame0[i] = ps.Def.InitialFrame + (ps.Def.InitialFrameVar > 0 ? rng.Next(ps.Def.InitialFrameVar + 1) : 0);
    }
}
