using Godot;
using Ms2.Formats.Model;

namespace Ms2Godot.Net;

/// <summary>The effect shader's parameter names, allocated once: a string literal passed to SetShaderParameter makes a new finalizable StringName on every call.</summary>
static class FxParam
{
    public static readonly StringName HasTex = "has_tex", HeightFog = "height_fog", NoFog = "no_fog", Tex = "tex", HasGlow = "has_glow", GlowTex = "glow_tex", Tint = "tint",
        Frames = "frames", Alpha = "alpha", BaseR0 = "base_r0", BaseR1 = "base_r1", GlowR0 = "glow_r0", GlowR1 = "glow_r1",
        BaseUv2 = "base_uv2", GlowUv2 = "glow_uv2", BaseClamp = "base_clamp", GlowClamp = "glow_clamp", BaseFilter = "base_filter", GlowFilter = "glow_filter", DetailFilter = "detail_filter",
        HasDetail = "has_detail", DetailOpacity = "detail_opacity", DetailTex = "detail_tex", DetailR0 = "detail_r0", DetailR1 = "detail_r1", DetailUv2 = "detail_uv2", DetailClamp = "detail_clamp",
        Ambient = "mat_ambient", MatDiffuse = "mat_diffuse", LightForm = "light_form", LitNormals = "lit_normals", VcEmi = "vc_emi", VcDif = "vc_dif", VcAmb = "vc_amb", VcOpacity = "vc_opacity", MorphW = "morph_w",
        AlphaBoost = "alpha_boost", NormalTex = "normal_tex", NormalR0 = "normal_r0", NormalR1 = "normal_r1", GlossTex = "gloss_tex",
        HoEmi = "ho_emi", HoSpec = "ho_spec", HoPower = "ho_power", AlphaTest = "alpha_test", AlphaRef = "alpha_ref";
}

/// <summary>
/// A playing effect NIF: its node tree with the transform controllers, its static meshes with the alpha, uv-scroll and flipbook controllers, and its
/// particle systems simulated on the CPU (emitters, gravity, drag and bomb forces, size / color keys over the particle's age, grow and shrink ramps)
/// and drawn as camera-facing or aligned quads (AlignedQuad) in one MultiMesh per system, or for a mesh-particle system as a clone of its master
/// particle per particle, turned and scaled by the mesh-align kernel (MeshAlign, DrawClones). All of it lives in MS2 file space (Z up) under one
/// rotated node. A mesh's morph controller moves its vertices (EffectMorph). The planar and spherical colliders bounce or kill the particles
/// (ParticleColliders). Not simulated: the other forces, spawners (death spawns), emitter-radius / color controllers.
/// </summary>
public sealed partial class EffectInstance : Node3D
{
    sealed class MeshState
    {
        public EffectMesh Def; public MeshInstance3D Node; public ShaderMaterial Mat;
        public ShaderMaterial Glow;                              // its glow-mask twin's material (GlowTwin), or null; per-frame parameters go to both
        public Texture2D[] Flip; public Variant[] FlipV; public Texture2D Base; public bool Add;
        public bool NotFinite;                                   // hidden for a non-finite world transform (Place)
        public bool Shown;                                       // Node.Visible as last set (Show)
        public string BaseKey; public TexMap BaseMap, GlowMap, DetailMap, NormalMap;   // the base map's ModelMesh.Maps key ("base" or "shader0") and the drawn maps
        public TexTransform BaseT, GlowT, DetailT, NormalT;       // working transforms the uv controllers write each frame
        public readonly float[] Rows = new float[6];             // their matrix rows, reused every frame (no per-frame allocation)
        public HoTechnique Ho;                                    // drawn through that _HO technique (EffectMesh.Ho: HoCode), else the standard shader
        public readonly double[] Lit = new double[3], Dif = new double[3], Amb = new double[3];   // EffectMesh.Shade's output (Lit the emissive), reused every frame
        public bool Static, Placed;                               // no node from it to the root animates or faces the camera: placed once
        public EffectMorph Morph; public int MorphTargets;        // the drawn morph and its offset targets (CUSTOM0-3), 0 for none
        public Vector4 LastW;                                     // the morph weights last set (morph_w)
        // the last value set on each per-frame shader parameter: a parameter is set again only when its value changes
        public Vector4 LastTint = Vector4.One; public Vector3 LastAmb, LastDif; public float LastAlpha; public Texture2D LastTex;
        // base r0 r1, glow r0 r1, detail r0 r1, normal r0 r1 (the shader defaults)
        public readonly Vector3[] LastRows = { new(1, 0, 0), new(0, 1, 0), new(1, 0, 0), new(0, 1, 0), new(1, 0, 0), new(0, 1, 0), new(1, 0, 0), new(0, 1, 0) };
    }

    sealed class PSys
    {
        public ParticleSystemDef Def; public MultiMeshInstance3D Node; public MultiMesh MM; public ShaderMaterial Mat;
        public Vector3[] Pos, Vel; public float[] Age, Life, Size, Rot, RotSpeed, Variance; public int Count; public AlignedQuad.TexAnim Tex;
        public double[] Accum; public float[] Buf;
        public int Cap, LastCount;                                // the MultiMesh's instance count (live particles fit) and last frame's live count
        public Aabb Box; public bool HasBox;                      // the MultiMesh's CustomAabb: every quad drawn so far, grown only (Step)
        public MeshState Shading;                                 // the system's material (ParticleSystemDef.Material) and its shader parameters
        public ParticleColliders.Collider[] Colliders; public int[] Tried; // the active colliders on this step (Step), and the kernel's scratch
        // a mesh-particle system (ParticleSystemDef.MeshParticles; no MultiMesh, no Shading): each live particle's clone, kept
        // in its slot (Swap) and reused by the next particle there (the client's per-generation pool, InitializeParticle);
        // per particle its rotation axis (null without "Has Rotation Axes"); per generation each master node's parent slot
        // in MasterNodes, each master mesh's node slot, and the material its clones share when nothing animates it
        public Clone[] Clones; public System.Numerics.Vector3[] RotAxis;
        public int[][] ParentSlot, MeshSlot; public ShaderMaterial[][] SharedMat, SharedGlow;
        public bool ClonesOk;                                     // the kernel that ran is ported (SetupClones)
        public bool Refused, Shown = true;                        // the quads' generator refuses the system; Node.Visible as last set
        public double LastScaled = double.NegativeInfinity;      // the reset controller's last scaled time (ParticleSystemDef.ResetCtl)
    }

    /// <summary>One particle's clone of a master particle (NiPSMeshParticleSystem::InitializeParticle,
    /// NiPSMeshParticleSystem.cpp:229-300): its meshes, its nodes' world transforms this frame (MasterNodes order), and the
    /// first and third columns of the rotation last set (PostUpdate reads the particle's current rotation, :408-424).</summary>
    sealed class Clone
    {
        public int Gen; public MeshState[] Meshes; public Transform3D[] World;
        public System.Numerics.Vector3 Prev0, Prev2; public bool Shown;
    }

    readonly EffectScene scene; readonly ModelBuilder mb;
    readonly Node3D z = new() { Name = "Z", Transform = new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2), Vector3.Zero) };
    readonly List<MeshState> meshes = new(); readonly List<PSys> systems = new();
    readonly Transform3D[] world; readonly bool[] done;
    // each node's file transform in Godot form, with its orthonormal basis and X scale (the animated pose's rest parts)
    readonly Transform3D[] rest; readonly Basis[] restBasis; readonly float[] restScale;
    readonly int[] dynNodes;             // the nodes whose world transform changes over time (an animated or billboard node on the way to the root)
    // each node's hidden state on this frame (Hidden) and whether it is worked out yet; visDyn: a node has a vis track, so
    // the states are worked out again each frame (else once)
    readonly bool[] hidden, hidDone; readonly bool visDyn;
    readonly bool needCam;               // a billboard node or a particle system reads the camera
    readonly bool needAxes;              // a system has NiPSAlignedQuadGenerator quads, or is a world-space mesh-particle system
    readonly bool useLighting;           // the part's <attachInfo useLighting> (the constructor's)
    Vector3 worldX = new(1, 0, 0), worldZ = new(0, 0, 1);
    bool inTree, shown = true;           // shown: z's Visible as last set (a new node is visible)
    readonly Random rng;
    readonly float emitUntil, life;
    float t;
    public bool Finished { get; private set; }
    public int ParticlesEmitted;
    public float Delay;                  // seconds before it starts (the effect xml's startDelay)
    /// <summary>Placement in file space (Z up): translate, rotate, scale from the effect xml.</summary>
    public Transform3D FileSpace { set => z.Transform = new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2), Vector3.Zero) * value; }

    static readonly Dictionary<string, Shader> partShaders = new();
    static readonly Dictionary<string, Shader> meshShaders = new();
    // An effect mesh's built ArrayMesh, one per EffectMesh (the scenes are cached per file): every instance of the effect
    // draws the same vertices with its own material (MaterialOverride), and nothing here changes a built mesh, so a
    // footstep or cast effect replayed by each player shares it instead of building the arrays (and a surface material) again.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EffectMesh, ArrayMesh> builtMeshes = new();
    readonly Dictionary<EffectMesh, (Texture2D[] T, Variant[] V)> flips = new();   // each flipbook mesh's frames and their Variants (BuildMeshState)
    // Textures and colours are taken raw (no source_color): the colour buffer holds gamma values (field_lit.gdshader).
    static readonly string MeshCode = @"shader_type spatial;
render_mode unshaded, MESHSTATE, BLEND;
uniform sampler2D tex : repeat_enable, filter_linear_mipmap;
uniform bool has_tex = true;
uniform sampler2D glow_tex : repeat_enable, filter_linear_mipmap;
uniform bool has_glow = false;
uniform sampler2D detail_tex : repeat_enable, filter_linear_mipmap;
uniform bool has_detail = false;
uniform bool detail_opacity = false;
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
uniform int base_filter = 1;
uniform int glow_filter = 1;
uniform int detail_filter = 1;
uniform float alpha = 1.0;
uniform vec4 tint = vec4(1.0);
// the terms the vertex colour (COLOR) stands in for: EffectMesh.VertexColors
uniform bool vc_emi = false;
uniform bool vc_dif = false;
uniform bool vc_amb = false;
uniform bool vc_opacity = false;
uniform bool alpha_boost = false;
FOGDECL
LIGHTDECL
MORPHDECL
void vertex() { MORPHVERT FOGVERT vec3 la, ld; light_vert(lit_normals ? normalize(mat3(MODEL_MATRIX) * NORMAL) : vec3(0.0), la, ld); light_amb = la; light_dif = ld; }
MAPDECL
void fragment() {
    vec4 t = has_tex ? map_tex(tex, map_uv(base_uv2 ? UV2 : UV, base_r0, base_r1), base_clamp, base_filter) : vec4(1.0);
    // the detail map multiplies the base colour by twice its colour, and under an MS2StandardMaterial the opacity by its
    // alpha (detail_opacity: EffectMesh.DetailOpacity; MS2StandardMaterial::HandleDetailMap, ms2standardmaterial.cpp:681-694,
    // its opacity at :342-343); the default NiStandardMaterial takes the colour only (NiStandardMaterial.cpp:4765-4774)
    if (has_detail) {
        vec4 d = map_tex(detail_tex, map_uv(detail_uv2 ? UV2 : UV, detail_r0, detail_r1), detail_clamp, detail_filter);
        t.rgb *= d.rgb * 2.0;
        if (detail_opacity) t.a *= d.a;
    }
    // the texture colour times the diffuse coefficient: emissive (tint) + ambient * the ambient accumulator + diffuse * the
    // diffuse accumulator, the lights' per vertex (LightDecl; EffectMesh.Shade, LightForm)
    // The coefficient is not saturated (EffectMesh.Shade), and the shader passes the colour on as it is
    // (HandleMS2FinalPixelOutputs @0x14274fd00, ms2standardmaterial.cpp:758-792; CompositeFinalRGBColor and CompositeFinalRGBAColor,
    // NiStandardMaterialNodeLibrary.cpp:3057-3106); the target saturates it (SceneTargetOut below).
    // The vertex colour takes the place of the terms EffectMesh.VertexColors names (Shade leaves those out of tint and
    // mat_ambient), and its alpha that of the material alpha (NiStandardMaterial::HandleInitialSpecAmbDiffEmissiveColor, NiStandardMaterial.cpp:4377-4450).
    vec3 coef = tint.rgb + mat_ambient * light_amb + mat_diffuse * light_dif;
    if (vc_emi) coef += COLOR.rgb;
    if (vc_dif) coef += COLOR.rgb * light_dif;
    if (vc_amb) coef += COLOR.rgb * light_amb;
    vec3 col = t.rgb * coef;
    // a glow map adds its colour whatever the material colour is
    if (has_glow) col += map_tex(glow_tex, map_uv(glow_uv2 ? UV2 : UV, glow_r0, glow_r1), glow_clamp, glow_filter).rgb;
    // The opacity: an opacity above 1 (the 02020120 crystals' material alpha 1.2, their ring's controller peak 1.5) only
    // counts through MS2AlphaBoostMaterial; the target saturates the opacity the blend takes
    float a = max(t.a * tint.a * (vc_opacity ? COLOR.a : alpha), 0.0);
    // MS2AlphaBoostMaterial (alpha_boost: EffectMesh.AlphaBoost) multiplies the fogged colour by an opacity above 1
    // (ms2standardmaterial.cpp:774-790; the node, ms2materialnodelibrary.cpp:839-846): the depth fog's colour as well
    float k = alpha_boost && a > 1.0 ? a : 1.0;
    // SceneTargetOut: the client draws the scene into an 8-bit target, CreateRenderTargetSurface (@0x1416a3cd0,
    // postprocessingservice.cpp:1519-1556) hard-codes D3DFMT_A8R8G8B8 for it (CreateOffscreenTarget @0x1416a3bb0; FUN_1416ac3b0,
    // :2056, binds it in LinkEffects); a float format only holds the post steps' copy of it (CopyTargetToTexture @0x1416abb30).
    // So the colour, after the depth fog, and the opacity the blend takes are saturated: a colour above 1 at a low opacity
    // lifts what is behind it evenly instead of tinting it with its hue. This saturates per draw, not per blend: the 8-bit
    // target also saturates each blend's result, so an additive stack above 1 under a later mix layer reads brighter here
    // (STILL-OPEN: an 8-bit or clamped scene target; alpha_render fx_stack_saturated records the gap).
    FOG = vec4(0.0);
    ALBEDO = clamp(mix(ms2_fog_apply(col, CAMERA_VISIBLE_LAYERS) * k, ms2_fog_color * k, fog_share), 0.0, 1.0);
    ALPHA = clamp(a, 0.0, 1.0);
}";
    static readonly string PartCode = @"shader_type spatial;
render_mode unshaded, PARTCULL, depth_draw_never, BLEND;
uniform sampler2D tex : repeat_enable, filter_linear_mipmap;
uniform bool has_tex = true;
uniform vec2 frames = vec2(1.0, 1.0);
uniform vec3 base_r0 = vec3(1.0, 0.0, 0.0);
uniform vec3 base_r1 = vec3(0.0, 1.0, 0.0);
uniform int base_clamp = 0;
uniform int base_filter = 1;
uniform sampler2D detail_tex : repeat_enable, filter_linear_mipmap;
uniform bool has_detail = false;
uniform bool detail_opacity = false;
uniform vec3 detail_r0 = vec3(1.0, 0.0, 0.0);
uniform vec3 detail_r1 = vec3(0.0, 1.0, 0.0);
uniform int detail_clamp = 0;
uniform int detail_filter = 1;
uniform sampler2D glow_tex : repeat_enable, filter_linear_mipmap;
uniform bool has_glow = false;
uniform vec3 glow_r0 = vec3(1.0, 0.0, 0.0);
uniform vec3 glow_r1 = vec3(0.0, 1.0, 0.0);
uniform int glow_clamp = 0;
uniform int glow_filter = 1;
uniform vec3 mat_ambient = vec3(0.0);
global uniform vec3 ms2_ambient;
uniform float alpha = 1.0;
uniform vec4 tint = vec4(1.0);
uniform bool vc_emi = false;
uniform bool vc_dif = false;
uniform bool vc_amb = false;
uniform bool vc_opacity = false;
uniform bool alpha_boost = false;
varying vec2 cell;
FOGDECL
LIGHTDECL
// the quad's normal is its x axis cross its y axis (AlignedQuad.Quad: the aligned normal; a facing quad's camera right
// cross up, the facing generator's normal, NiPSFacingQuadGenerator.cpp:340-341)
void vertex() { cell = INSTANCE_CUSTOM.xy; FOGVERT vec3 la, ld; light_vert(normalize(cross(MODEL_MATRIX[0].xyz, MODEL_MATRIX[1].xyz)), la, ld); light_amb = la; light_dif = ld; }
MAPDECL
void fragment() {
    // the quad's texture coordinate (the flipbook cell's), then each map's own transform, clamp and filter modes as
    // the mesh shader's (MapDecl); the detail map multiplies the colour by twice its colour, and under an MS2StandardMaterial the
    // opacity by its alpha (detail_opacity: ParticleSystemDef.DetailOpacity; MS2StandardMaterial::HandleDetailMap,
    // ms2standardmaterial.cpp:681-694, :342-343; NiStandardMaterial::HandleDetailMap, NiStandardMaterial.cpp:4765-4774,
    // takes the colour only)
    vec2 uv = UV / frames + cell;
    vec4 t = has_tex ? map_tex(tex, map_uv(uv, base_r0, base_r1), base_clamp, base_filter) : vec4(1.0);
    if (has_detail) {
        vec4 d = map_tex(detail_tex, map_uv(uv, detail_r0, detail_r1), detail_clamp, detail_filter);
        t.rgb *= d.rgb * 2.0;
        if (detail_opacity) t.a *= d.a;
    }
    // the mesh shader's colour terms (MeshCode, EffectMesh.Shade): the particle's key colour is its vertex colour
    // (ParticleSystemDef.Material), so the material gives the terms the vertex colour mode leaves (AMB_DIFF: the emissive).
    // The quads have normals (NiPSAlignedQuadGenerator.cpp:181-195, NiPSFacingQuadGenerator.cpp:152-164), so the
    // directional light reaches them (LightDecl)
    vec3 coef = tint.rgb + mat_ambient * light_amb + mat_diffuse * light_dif;
    if (vc_emi) coef += COLOR.rgb;
    if (vc_dif) coef += COLOR.rgb * light_dif;
    if (vc_amb) coef += COLOR.rgb * light_amb;
    // the quads draw through a standard material (ParticleSystemDef.DetailOpacity), whose colour and opacity go out as they
    // are (CompositeFinalRGBAColor, NiStandardMaterialNodeLibrary.cpp:3057-3106) and the target saturates (SceneTargetOut)
    vec3 col = t.rgb * coef;
    // the glow map adds its colour after the lighting (NiStandardMaterial::HandlePostLightTextureApplication,
    // NiStandardMaterial.cpp:5617-5700, HandleGlowMap :4816-4824), as the mesh shader's
    if (has_glow) col += map_tex(glow_tex, map_uv(uv, glow_r0, glow_r1), glow_clamp, glow_filter).rgb;
    float a = max(t.a * tint.a * (vc_opacity ? COLOR.a : alpha), 0.0);
    float k = alpha_boost && a > 1.0 ? a : 1.0;     // MS2AlphaBoostMaterial, as the mesh shader's
    // the 8-bit target saturates the fogged colour and the opacity (SceneTargetOut, in the mesh shader)
    FOG = vec4(0.0);
    ALBEDO = clamp(mix(ms2_fog_apply(col, CAMERA_VISIBLE_LAYERS) * k, ms2_fog_color * k, fog_share), 0.0, 1.0);
    ALPHA = clamp(a, 0.0, 1.0);
}";
    // A block naming an _HO technique (HoTechnique, which gives the formulas and the sampler modes) draws through that
    // effect pass, for a mesh and for a particle system's quads alike: DISSOLVE, GLOW, WRAP (the base map wraps), VC (c0
    // times the vertex colour, COLOR: a particle's key colour), GLOWVC (c1 times it) and PARTICLE (the quad's flipbook cell,
    // as PartCode's) are the technique's defines. ho_emi is c0 (MATERIALEMISSIVE: the emissive and the material alpha),
    // ho_spec c1's colour (MATERIALSPECULAR), ho_power the distortion (MATERIALPOWER, the glossiness). The pass reads only
    // texcoord 0, whatever uv set the map names. There is no lighting (UsesNiLightState 0) and no height fog: the pass
    // writes its colour and the fixed-function depth fog of the render state applies (NiDX9RenderState::UpdateRenderState,
    // NiDX9RenderState.cpp:434-447). The render state also gives the blend and the alpha test (ApplyAlpha, :449-476):
    // WRITE_ALPHA writes ALPHA (blending off draws the pass opaque), and alpha_test is the NiAlphaProperty test function
    // (TEST_ALWAYS 0 .. TEST_NEVER 7, NiAlphaProperty.h:47-57; -1 off) against alpha_ref, its threshold / 255.
    // A map the block lacks reads the texture the effect lends it (EffectMesh.Lent: the one the effect's other blocks of
    // that file carry), else (0, 0, 0, 1), a NULL texture's value (hint_default_black).
    static readonly string HoCode = @"shader_type spatial;
render_mode unshaded, MESHSTATE, BLEND;
DEFINES
#if defined(WRAP)
uniform sampler2D tex : repeat_enable, filter_linear, hint_default_black;
#else
uniform sampler2D tex : repeat_disable, filter_linear, hint_default_black;
#endif
uniform sampler2D detail_tex : repeat_enable, filter_linear, hint_default_black;
uniform vec3 base_r0 = vec3(1.0, 0.0, 0.0);
uniform vec3 base_r1 = vec3(0.0, 1.0, 0.0);
uniform vec3 detail_r0 = vec3(1.0, 0.0, 0.0);
uniform vec3 detail_r1 = vec3(0.0, 1.0, 0.0);
#if defined(DISSOLVE)
uniform sampler2D gloss_tex : repeat_disable, filter_linear, hint_default_black;
#else
uniform sampler2D normal_tex : repeat_enable, filter_linear, hint_default_black;
uniform vec3 normal_r0 = vec3(1.0, 0.0, 0.0);
uniform vec3 normal_r1 = vec3(0.0, 1.0, 0.0);
uniform float ho_power = 0.0;
#endif
#if defined(GLOW)
uniform sampler2D glow_tex : repeat_enable, filter_linear, hint_default_black;
uniform vec3 glow_r0 = vec3(1.0, 0.0, 0.0);
uniform vec3 glow_r1 = vec3(0.0, 1.0, 0.0);
uniform vec3 ho_spec = vec3(0.0);
#endif
uniform vec4 ho_emi = vec4(0.0, 0.0, 0.0, 1.0);
uniform int alpha_test = -1;
uniform float alpha_ref = 0.0;
#if defined(PARTICLE)
uniform vec2 frames = vec2(1.0, 1.0);
varying vec2 cell;
#endif
FOGDECL
MORPHDECL
void vertex() {
#if defined(PARTICLE)
    cell = INSTANCE_CUSTOM.xy;
#endif
    MORPHVERT FOGVERT }
vec2 rows(vec2 uv, vec3 r0, vec3 r1) { return vec2(dot(r0, vec3(uv, 1.0)), dot(r1, vec3(uv, 1.0))); }
bool alpha_pass(float a) {
    a = clamp(a, 0.0, 1.0);
    switch (alpha_test) {
        case 0: return true;
        case 1: return a < alpha_ref;
        case 2: return a == alpha_ref;
        case 3: return a <= alpha_ref;
        case 4: return a > alpha_ref;
        case 5: return a != alpha_ref;
        case 6: return a >= alpha_ref;
        case 7: return false;
    }
    return true;
}
void fragment() {
#if defined(PARTICLE)
    vec2 uv = UV / frames + cell;
#else
    vec2 uv = UV;
#endif
    vec4 c0 = ho_emi;
#if defined(VC)
    c0 *= COLOR;
#endif
#if defined(DISSOLVE)
    float x = 2.0 * c0.a + texture(detail_tex, rows(uv, detail_r0, detail_r1)).r - 1.0;
    vec4 b = texture(tex, rows(uv, base_r0, base_r1));
    vec3 col = b.rgb * c0.rgb + texture(gloss_tex, vec2(1.0 - 2.0 * x, 0.0)).rgb;
    float a = x * b.a;
#else
    float n = texture(normal_tex, rows(uv, normal_r0, normal_r1)).r * ho_power;
    vec4 b = pow(texture(tex, rows(uv, base_r0, base_r1) + n), vec4(1.8));
    vec3 col = b.rgb * c0.rgb;
#if defined(GLOW)
    vec3 c1 = ho_spec;
#if defined(GLOWVC)
    c1 *= COLOR.rgb;
#endif
    col += texture(glow_tex, rows(uv, glow_r0, glow_r1) + n).rgb * c1 * 10.0;
#endif
    float a = clamp(texture(detail_tex, rows(uv, detail_r0, detail_r1)).r * b.a * c0.a * 5.0, 0.0, 1.0);
#endif
    if (!alpha_pass(a)) discard;
    // the depth fog, then the target's saturation (SceneTargetOut)
    FOG = vec4(0.0);
    ALBEDO = clamp(mix(col, ms2_fog_color, fog_share), 0.0, 1.0);
#if defined(WRITE_ALPHA)
    ALPHA = a;
#endif
}";
    static readonly Dictionary<string, Shader> hoShaders = new();

    /// <summary>The shader of an _HO technique (HoCode) for a mesh (render modes as RenderModes) or a particle system
    /// (<paramref name="particle"/>: the quads' modes, as PartCode's), with the block's blend: none (opaque), additive or mix.</summary>
    static Shader HoShader(HoTechnique h, string state, bool particle, bool blendOn, bool add, int morphTargets = 0)
    {
        string key = h.Name + " " + state + (particle ? " particle" : "") + (!blendOn ? " opaque" : add ? " add" : " mix") + (morphTargets > 0 ? " morph" + morphTargets : "");
        if (hoShaders.TryGetValue(key, out var sh)) return sh;
        var defs = new System.Text.StringBuilder();
        void Def(bool on, string name) { if (on) defs.Append("#define ").Append(name).Append('\n'); }
        Def(h.Dissolve, "DISSOLVE"); Def(h.Glow, "GLOW"); Def(h.Wrap, "WRAP"); Def(h.VertexColor, "VC"); Def(h.GlowVertexColor, "GLOWVC");
        Def(particle, "PARTICLE"); Def(blendOn, "WRITE_ALPHA");
        string code = WithFog(HoCode).Replace("DEFINES", defs.ToString()).Replace("MESHSTATE", state).Replace("BLEND", add ? "blend_add" : "blend_mix");
        return hoShaders[key] = new Shader { Code = WithMorph(code, morphTargets), ResourceName = h.Name };   // the name: the technique (tests)
    }

    // the alpha test of an NiAlphaProperty's flags as HoCode's alpha_test (bit 9 on, the function in bits 10-12), -1 off
    static int AlphaTest(int alphaFlags) => alphaFlags >= 0 && (alphaFlags & 0x200) != 0 ? (alphaFlags >> 10) & 7 : -1;

    // The client's fog for an effect's meshes and particle systems. A NIF effect carries no NiFogProperty (none of the
    // effect pack's nifs has one), so its meshes take the default fog property ApplyEffect::SetFog sets for the field
    // (applyeffect.cpp:586-622: NiFogProperty::ms_spDefault), and their materials are the field's: MS2StandardMaterial
    // (CalculateBiasedFog, ms2standardmaterial.cpp:535-584), or a plain NiStandardMaterial (the renderer's default), whose
    // CalculateFog takes the same FogStartEnd and gakFogBias with the same linear-z formula (NiStandardMaterial.cpp:5074-5131,
    // NiStandardMaterialNodeLibrary.cpp:1663-1792). So the depth fog is ms2_fog.gdshaderinc's, per vertex, and ApplyFog
    // lerps to the fog colour by it in every blend mode. But PrepareLoadedNif runs FUN_141567b60 on every effect nif as on a
    // model (nifeffectparam.cpp:2338; unit_141567000.cpp:349-424): a "nofog" tag, flag 0x400 or an own blended dest-ONE
    // alpha property attaches a fog-off property to that subtree, which draws with FOG_NONE (no_fog: ModelMesh.NoFog, a
    // system's Material.Mesh.NoFog). Flag 0x400 is IS_PARTICLE_SYSTEM, which NiPSParticleSystem::LoadBinary sets on every
    // particle system after reading the file's flags (NiPSParticleSystem.cpp:1282, exe 0x141e9afd0), so every particle
    // system draws without the depth fog, whatever its blend (NifModel.ParticleSystemFlag); its mesh particles inherit that
    // property state (NiPSMeshParticleSystem.cpp:270). The pillar capture (02000110, eff_com_recovery.nif) draws all four of
    // its systems, the SRCALPHA / INVSRCALPHA PArray01 and leaf too, without the depth fog (FOGENABLE FALSE, no g_FogColor):
    // the blended two keep the height fog in the pixel shader, the dest-ONE bling ones have none; its meshes have both. The height
    // fog comes first, but not on meshes whose dest blend is ONE (NiStandardMaterial::GenerateDescriptor,
    // NiStandardMaterial.cpp:687-697; the Godot height_fog flag). The glow and shimmer passes have no fog
    // (ms2glowmaterial.cpp:126, ms2shimmermaterial.cpp:52); the ribbon, swoosh, beam and lightning effects turn it off on
    // their own meshes (NiFogProperty SetFog(false)), and are not drawn here.
    const string FogDecl = @"#include ""res://shaders/ms2_fog.gdshaderinc""
global uniform vec3 ms2_hfog_color;
global uniform vec3 ms2_hfog;
uniform bool height_fog = true;
uniform bool no_fog = false;
varying vec3 fog_world;
vec3 ms2_fog_apply(vec3 c, uint layers) {
    if ((layers & FOG_LAYER) != 0u && height_fog) {
        float hf = ms2_hfog.z * (1.0 - clamp((fog_world.y - ms2_hfog.y) / (ms2_hfog.x - ms2_hfog.y), 0.0, 1.0));
        c = mix(c, ms2_hfog_color, hf);
    }
    return c;
}";
    const string FogVert = "fog_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; fog_share = no_fog ? 0.0 : ms2_fog_share(VIEW_MATRIX, fog_world, CAMERA_VISIBLE_LAYERS);";

    // The field's lights on an effect block, per vertex (EffectMesh.LightForm): the ambient and diffuse accumulators the
    // material's ambient and diffuse terms (or the vertex colour in their place) multiply. ms2_ambient is the ambient
    // light (SCM_DEF_AMBIENTLIGHT); the directional light adds sun * N.L to the diffuse and its ambient colour to the
    // ambient (form 1, Gamebryo's Light) or sun * N.L + its ambient colour * (1 - N.L) to the diffuse (form 2, MS2Light).
    // Without normals (lit_normals false) the directional light is dropped; form 0 (LightingE) takes no light.
    const string LightDecl = @"global uniform vec3 ms2_sun_color;
global uniform vec3 ms2_sun_ambient;
global uniform vec3 ms2_sun_dir;
uniform vec3 mat_diffuse = vec3(1.0);
uniform int light_form = 1;
uniform bool lit_normals = true;
varying vec3 light_amb;
varying vec3 light_dif;
void light_vert(vec3 n, out vec3 amb, out vec3 dif) {
    float ndl = max(dot(n, -ms2_sun_dir), 0.0);
    amb = vec3(0.0); dif = vec3(0.0);
    if (light_form == 0) return;
    amb = ms2_ambient;
    if (!lit_normals) return;
    if (light_form == 1) { amb += ms2_sun_ambient; dif = ms2_sun_color * ndl; }
    else dif = ms2_sun_color * ndl + ms2_sun_ambient * (1.0 - ndl);
}";

    // A map's sample (MAPDECL): its texture transform's rows (map_uv), then the clamp mode (bit 0 S, bit 1 T) as CLAMP
    // addressing and the filter mode (TexMap.Filter) as the sampler states the client sets from it
    // (NiDX9FragmentShader::SetupTextures, NiDX9FragmentShader.cpp:487; ms_akD3DFilterMapping, NiDX9Renderer.cpp:2197-2217):
    // NEAREST (0) point and BILERP (1) linear read the top level only, TRILERP (2) blends the two mip levels the footprint
    // falls between. A clamped axis holds the coordinate half a texel of the level read inside the edge, which is what
    // CLAMP addressing reads; a wrapped trilinear map takes the sampler as it is.
    const string MapDecl = @"vec2 map_uv(vec2 uv, vec3 r0, vec3 r1) { return vec2(dot(r0, vec3(uv, 1.0)), dot(r1, vec3(uv, 1.0))); }
vec2 map_hold(vec2 o, int cl, ivec2 size) {
    vec2 h = 0.5 / vec2(max(size, ivec2(1)));
    if ((cl & 1) != 0) o.x = clamp(o.x, h.x, 1.0 - h.x);
    if ((cl & 2) != 0) o.y = clamp(o.y, h.y, 1.0 - h.y);
    return o;
}
vec4 map_tex(sampler2D s, vec2 o, int cl, int fl) {
    if (fl == 0) {
        vec2 n = vec2(max(textureSize(s, 0), ivec2(1)));
        return textureLod(s, (floor(map_hold(o, cl, textureSize(s, 0)) * n) + 0.5) / n, 0.0);
    }
    if (fl == 1) return textureLod(s, map_hold(o, cl, textureSize(s, 0)), 0.0);
    if (cl == 0) return texture(s, o);
    float l = textureQueryLod(s, o).x, l0 = floor(l);
    vec4 c = textureLod(s, map_hold(o, cl, textureSize(s, int(l0))), l0);
    return l > l0 ? mix(c, textureLod(s, map_hold(o, cl, textureSize(s, int(l0) + 1)), l0 + 1.0), l - l0) : c;
}";

    // FogDecl / FogVert in place of the FOGDECL / FOGVERT marks: the height fog goes into the colour, the depth fog
    // through FOG (as character_lit; the colour buffer holds gamma values); LightDecl in place of LIGHTDECL, MapDecl in
    // place of MAPDECL
    static string WithFog(string code) => code.Replace("FOGDECL", FogDecl).Replace("FOGVERT", FogVert).Replace("LIGHTDECL", LightDecl).Replace("MAPDECL", MapDecl);

    // the engine's bAdditive for the height fog: an NiAlphaProperty whose dest blend is ONE, blending on or not
    // (NiStandardMaterial.cpp:690-692)
    static bool DestOne(int alphaFlags) => alphaFlags >= 0 && ((alphaFlags >> 5) & 15) == 0;

    /// <summary>A particle system's shader: its _HO technique's (EffectMesh.Ho on Material), else the standard quad shader,
    /// with the culled side of the system's draw mode (ReadSystem's ModelData.ReadDrawState) under the placement's world
    /// (<paramref name="mirrored"/>, FaceCulls.UnderWorld). The quads never write depth.</summary>
    static Shader PartShader(ParticleSystemDef d, bool mirrored)
    {
        string cull = CullMode(d.Material.Mesh.Cull.UnderWorld(mirrored));
        if (d.Material.Ho is { } ho) return HoShader(ho, cull + ", depth_draw_never", true, BlendOn(d.AlphaFlags), d.Additive);
        string key = cull + (d.Additive ? " add" : " mix");
        if (!partShaders.TryGetValue(key, out var sh))
            partShaders[key] = sh = new Shader { Code = WithFog(PartCode).Replace("PARTCULL", cull).Replace("BLEND", d.Additive ? "blend_add" : "blend_mix") };
        return sh;
    }

    static string CullMode(FaceCull c) => c switch { FaceCull.None => "cull_disabled", FaceCull.Front => "cull_front", _ => "cull_back" };

    /// <summary>A mesh's render modes from its NiZBufferProperty and NiStencilProperty (the nearest up the graph, ModelMesh.ZFlags
    /// / DrawMode). Z write on = depth_draw_always (the portals' outer shells are 15, their glow shells 13: test, no write); z test
    /// off = depth_test_disabled. The test function is TEST_LESSEQUAL in every one checked (the default, NiZBufferProperty.inl:22),
    /// which is Godot's own test. The draw mode maps to D3D culling as NiDX9RenderState does for the right-handed
    /// camera (NiDX9RenderState::ApplyStencil, NiDX9RenderState.cpp:592, mapping :380-388): CCW_OR_BOTH and CCW cull the back faces as the field meshes do (field_lit's
    /// cull_back), CW the front, BOTH none (the portals' NiStencilProperty 19840 is DRAW_BOTH with the stencil test off).
    /// The stencil test itself is not drawn: no effect NIF sets the stencil enable bit (RenderStateTests.NoEffectEnablesStencil).
    /// The field meshes cull the same way (ModelBuilder.CullDefine).
    /// Under a mirrored world (<paramref name="mirrored"/>, a negative determinant: an x-mirrored end effect basis) the culled
    /// side swaps (<see cref="FaceCulls.UnderWorld"/>): D3D9 keeps the render state's cull mode whatever the world matrix,
    /// while Godot flips the culled side of a mirrored instance.</summary>
    internal static string RenderModes(ModelMesh m, bool mirrored = false)
    {
        string cull = CullMode(m.Cull.UnderWorld(mirrored));
        string depth = m.ZWrite ? "depth_draw_always" : "depth_draw_never";
        return m.ZTest ? $"{cull}, {depth}" : $"{cull}, {depth}, depth_test_disabled";
    }

    /// <param name="morphTargets">The offset targets of the mesh's morph (EffectMorph), 0 for none: the vertex adds each
    /// one (CUSTOM0-3) times its weight (morph_w), before the fog reads the vertex.</param>
    static Shader MeshShader(ModelMesh m, bool add, bool mirrored = false, int morphTargets = 0)
    {
        string state = RenderModes(m, mirrored), key = state + (add ? " add" : " mix") + (morphTargets > 0 ? " morph" + morphTargets : "");
        if (!meshShaders.TryGetValue(key, out var sh))
            meshShaders[key] = sh = new Shader { Code = WithMorph(WithFog(MeshCode), morphTargets).Replace("MESHSTATE", state).Replace("BLEND", add ? "blend_add" : "blend_mix") };
        return sh;
    }

    // an effect mesh's shader: its _HO technique's (EffectMesh.Ho), else the standard one
    static Shader ShaderOf(EffectMesh em, bool add, bool mirrored, int morphTargets) => em.Ho is { } h
        ? HoShader(h, RenderModes(em.Mesh, mirrored), false, BlendOn(em.Mesh.AlphaFlags), add, morphTargets)
        : MeshShader(em.Mesh, add, mirrored, morphTargets);

    static bool BlendOn(int alphaFlags) => alphaFlags >= 0 && (alphaFlags & 1) != 0;

    // The glow mask an effect mesh draws into the glow target (GlowPass), the client's MS2GlowMaterial over the same mesh:
    // rgb = the glow map's alpha, times the detail map's alpha with a detail map (HandleGlowColor @0x1427761b0), lerped
    // to the height fog colour by the height fog share (MS2GlowMaterial_ps_3_0_DX9.cache @0x8c / @0x327; the depth fog is
    // off); alpha = the base map's alpha times the material (or vertex) alpha, without the detail alpha the colour pass's
    // MS2StandardMaterial takes. The healing pillar's column and ring masks read so in the client's capture: PS 0x3c9ac3f0
    // `oC0.w = Base.a * v0.w; oC0.rgb = lerp(c0, Detail.a * Glow.a, fog)` and PS 0x3c9dcc30 `oC0.rgb = Glow.a`. The draw
    // keeps the colour pass's blend, z and cull states and samplers (MS2MaterialSwapProcessor swaps the material only).
    // No depth fog and no lights; unshaded, the target keeps the value as written (field_glow).
    static readonly string MaskCode = @"shader_type spatial;
render_mode unshaded, MESHSTATE, MASKBLEND;
uniform sampler2D tex : repeat_enable, filter_linear_mipmap;
uniform bool has_tex = true;
uniform sampler2D glow_tex : repeat_enable, filter_linear_mipmap;
uniform sampler2D detail_tex : repeat_enable, filter_linear_mipmap;
uniform bool has_detail = false;
uniform vec3 detail_r0 = vec3(1.0, 0.0, 0.0);
uniform vec3 detail_r1 = vec3(0.0, 1.0, 0.0);
uniform bool detail_uv2 = false;
uniform int detail_clamp = 0;
uniform vec3 base_r0 = vec3(1.0, 0.0, 0.0);
uniform vec3 base_r1 = vec3(0.0, 1.0, 0.0);
uniform vec3 glow_r0 = vec3(1.0, 0.0, 0.0);
uniform vec3 glow_r1 = vec3(0.0, 1.0, 0.0);
uniform bool base_uv2 = false;
uniform bool glow_uv2 = false;
uniform int base_clamp = 0;
uniform int glow_clamp = 0;
uniform int base_filter = 1;
uniform int glow_filter = 1;
uniform int detail_filter = 1;
uniform float alpha = 1.0;
uniform bool vc_opacity = false;
global uniform vec3 ms2_hfog_color;
global uniform vec3 ms2_hfog;
varying vec3 fog_world;
MORPHDECL
void vertex() { MORPHVERT fog_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
MAPDECL
void fragment() {
    float g = map_tex(glow_tex, map_uv(glow_uv2 ? UV2 : UV, glow_r0, glow_r1), glow_clamp, glow_filter).a;
    if (has_detail) g *= map_tex(detail_tex, map_uv(detail_uv2 ? UV2 : UV, detail_r0, detail_r1), detail_clamp, detail_filter).a;
    float hf = ms2_hfog.z * (1.0 - clamp((fog_world.y - ms2_hfog.y) / (ms2_hfog.x - ms2_hfog.y), 0.0, 1.0));
    ALBEDO = mix(vec3(g), ms2_hfog_color, hf);
    MASKALPHA
}";
    static readonly Dictionary<string, Shader> maskShaders = new();

    // the mask shader for a mesh's render modes: blend_mix with the opacity for a SRCALPHA / INVSRCALPHA mesh, no blend for an opaque one
    static Shader MaskShader(ModelMesh m, bool mirrored, int morphTargets)
    {
        bool blend = BlendOn(m.AlphaFlags);
        string state = RenderModes(m, mirrored), key = state + (blend ? " mix" : " opaque") + (morphTargets > 0 ? " morph" + morphTargets : "");
        if (!maskShaders.TryGetValue(key, out var sh))
            maskShaders[key] = sh = new Shader { Code = WithMorph(MaskCode.Replace("MAPDECL", MapDecl), morphTargets).Replace("MESHSTATE", state)
                .Replace(", MASKBLEND", blend ? ", blend_mix" : "")
                .Replace("MASKALPHA", blend ? "ALPHA = clamp((has_tex ? map_tex(tex, map_uv(base_uv2 ? UV2 : UV, base_r0, base_r1), base_clamp, base_filter).a : 1.0) * (vc_opacity ? COLOR.a : alpha), 0.0, 1.0);" : "") };
        return sh;
    }

    /// <summary>An effect mesh with a glow map joins the glow pass as a field mesh does (ModelBuilder.AddGlowTwin): a twin
    /// under its node on the glow layer, drawing the glow mask (MaskCode) with the colour pass's maps, transforms and opacity.
    /// The meshes are the client's glow-click ones (EffectMesh.GlowMask). The particle systems never join: the client's
    /// NiPSParticleSystem::LoadBinary sets NiAVObject flag 0x400 on every system it streams (exe 0x141e9b64e), and the
    /// glow-click predicate turns 0x400 objects away first (FUN_1414cffc0 through FUN_14098dfc0,
    /// ms2materialswapprocessor.cpp:158-170). A mesh particle's clones do join: the swap runs on each object of the visible
    /// array (MS2MaterialSwapProcessor, ms2materialswapprocessor.cpp:100-115), where the clones stand as their own meshes
    /// (the particle container's OnVisible, NiPSMeshParticleSystem.cpp:459-463), copies of a master (Clone, :223, :263) that
    /// is a plain mesh without flag 0x400. <paramref name="shared"/> is the mask material of a clone sharing its colour
    /// material (SharedMat); the twin then draws through it and sets nothing on it. Returns the twin's material, or null.</summary>
    ShaderMaterial GlowTwin(MeshState ms, ShaderMaterial shared = null)
    {
        var m = ms.Def.Mesh;
        if (!GlowPass.Wanted || !ms.Def.GlowMask || mb.Texture(m.Textures["glow"]) == null) return null;
        var gm = shared;
        if (gm == null)
        {
            gm = new ShaderMaterial { Shader = MaskShader(m, mirrored, ms.MorphTargets), RenderPriority = ms.Mat.RenderPriority };
            foreach (var name in MaskUniforms(gm.Shader))
            {
                var v = ms.Mat.GetShaderParameter(name);
                if (v.VariantType != Variant.Type.Nil) gm.SetShaderParameter(name, v);
            }
        }
        ms.Glow = gm;
        ms.Node.AddChild(new MeshInstance3D { Mesh = ms.Node.Mesh, MaterialOverride = gm, Name = ms.Node.Name + "_glow", Layers = GlowPass.Bit, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        return gm;
    }

    static readonly Dictionary<Shader, StringName[]> maskUniforms = new();

    // a mask shader's uniform names, read once per shader (GetShaderUniformList builds a new array of dictionaries per call)
    static StringName[] MaskUniforms(Shader sh)
    {
        if (!maskUniforms.TryGetValue(sh, out var names))
        {
            var list = sh.GetShaderUniformList();
            names = new StringName[list.Count];
            for (int i = 0; i < names.Length; i++) names[i] = list[i].AsGodotDictionary()["name"].AsStringName();
            maskUniforms[sh] = names;
        }
        return names;
    }

    static readonly string[] MorphTerms = { "morph_w.x * CUSTOM0.xyz", "morph_w.y * CUSTOM1.xyz", "morph_w.z * CUSTOM2.xyz", "morph_w.w * CUSTOM3.xyz" };

    // NiMorphingKernel (NiMorphingKernel.cpp:30-40) for relative targets: target 0 (VERTEX) plus each weighted offset
    static string WithMorph(string code, int targets) => targets == 0
        ? code.Replace("MORPHDECL", "").Replace("MORPHVERT", "")
        : code.Replace("MORPHDECL", "uniform vec4 morph_w = vec4(0.0);")
              .Replace("MORPHVERT", "VERTEX += " + string.Join(" + ", MorphTerms.Take(targets)) + ";");

    static float Rad(float x) => x;

    /// <param name="useLighting">The part's &lt;attachInfo useLighting&gt; (EffectPart.UseLighting): false draws every block
    /// unlit (EffectMesh.PartLightForm).</param>
    public EffectInstance(EffectScene scene, ModelBuilder mb, float duration, int seed, bool useLighting = true)
    {
        this.scene = scene; this.mb = mb; this.useLighting = useLighting; rng = new Random(seed);
        // The client removes an effect once the game time passes start + duration (CEffect::vfunc_9, OnTick), with no tail
        // and no fade: a fade-out runs only when the effect is faded on request. A zero duration takes the longest
        // controller end time of the loaded nif (CNifEffect::vfunc_3).
        emitUntil = duration > 0 ? duration : (float)scene.Duration;
        life = emitUntil;
        world = new Transform3D[scene.Nodes.Count]; done = new bool[scene.Nodes.Count];
        hidden = new bool[scene.Nodes.Count]; hidDone = new bool[scene.Nodes.Count]; visDyn = scene.Nodes.Any(n => n.Vis != null);
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
        needAxes = scene.Systems.Any(d => d.Aligned || (d.MeshParticles && d.WorldSpace));
        AddChild(z);
        foreach (var em in scene.Meshes)
        {
            // a master particle's meshes are drawn only as the particles' clones (EffectMesh.Master, DrawClones)
            if (em.Master) continue;
            var ms = BuildMeshState(em, null);
            ms.Static = !Dyn(em.Node);
            ms.Node.Visible = false;              // shown by the first Place
            z.AddChild(ms.Node);
            GlowTwin(ms);
            meshes.Add(ms);
        }
        foreach (var d in scene.Systems)
        {
            if (d.Emitters.Count == 0) continue;
            var ps = new PSys { Def = d };
            int n = d.MaxParticles;
            ps.Pos = new Vector3[n]; ps.Vel = new Vector3[n]; ps.Age = new float[n]; ps.Life = new float[n]; ps.Size = new float[n]; ps.Rot = new float[n]; ps.RotSpeed = new float[n]; ps.Variance = new float[n]; ps.Tex = new AlignedQuad.TexAnim(d);
            ps.Accum = new double[d.Emitters.Count];
            int nc = 0;
            foreach (var c in d.ColliderList) if (c.Active) nc++;
            if (nc > 0) { ps.Colliders = new ParticleColliders.Collider[nc]; ps.Tried = new int[nc]; }
            if (d.MeshParticles) { SetupClones(ps); systems.Add(ps); continue; }
            // the quads draw through the system's _HO technique (EffectMesh.Ho on Material), else the standard shader
            var ho = d.Material.Ho;
            var mat = new ShaderMaterial { Shader = PartShader(d, mirrored) };
            if (d.Material.Mesh.NoFog) mat.SetShaderParameter(FxParam.NoFog, true);
            var tex = d.Texture != null ? mb.Texture(d.Texture) : null;
            if (ps.Tex.On) mat.SetShaderParameter(FxParam.Frames, new Vector2(d.FramesAcross, d.FramesDown));
            ps.Mat = mat;
            // the base and detail maps' transforms and clamp modes (ParticleSystemDef.Material's Maps; ReadSystem)
            var pm = d.Material.Mesh;
            var baseMap = pm.Maps.GetValueOrDefault("base") ?? pm.Maps.Values.Where(x => x.ShaderIndex >= 0 && x.File == d.Texture).OrderBy(x => x.ShaderIndex).FirstOrDefault();
            if (ho != null)
            {
                ps.Shading = new MeshState { Def = d.Material, Mat = mat, Ho = ho, Base = tex, BaseMap = baseMap };
                HoSetup(ps.Shading, pm);
            }
            else
            {
                mat.SetShaderParameter(FxParam.HasTex, tex != null);
                if (DestOne(d.AlphaFlags)) mat.SetShaderParameter(FxParam.HeightFog, false);
                if (tex != null) mat.SetShaderParameter(FxParam.Tex, tex);
                var detailMap = pm.Maps.GetValueOrDefault("detail");
                var glowMap = pm.Maps.GetValueOrDefault("glow");
                var gtex = glowMap?.File != null ? mb.Texture(glowMap.File) : null;
                if (gtex == null) glowMap = null;
                else { mat.SetShaderParameter(FxParam.HasGlow, true); mat.SetShaderParameter(FxParam.GlowTex, gtex); MapModes(mat, glowMap, FxParam.GlowClamp, FxParam.GlowFilter); }
                var dtex = detailMap?.File != null ? mb.Texture(detailMap.File) : null;
                if (dtex == null) detailMap = null;
                else { mat.SetShaderParameter(FxParam.HasDetail, true); mat.SetShaderParameter(FxParam.DetailTex, dtex); if (d.DetailOpacity) mat.SetShaderParameter(FxParam.DetailOpacity, true); }
                if (baseMap != null) MapModes(mat, baseMap, FxParam.BaseClamp, FxParam.BaseFilter);
                if (detailMap != null) MapModes(mat, detailMap, FxParam.DetailClamp, FxParam.DetailFilter);
                // The quads are drawn through the material the system block names (most name MS2StandardMaterial), or the
                // renderer's default NiStandardMaterial when it names none (NiRenderer::NiRenderer, NiRenderer.cpp:49-54; nothing in
                // the client calls SetDefaultMaterial; ParticleSystemDef.DetailOpacity), or MS2AlphaBoostMaterial in their place
                // (EffectMesh.AlphaBoost): their colour is the standard material's, as a mesh's: the key colour stands in for the terms of the NiVertexColorProperty mode (EffectMesh.VertexColors;
                // NiStandardMaterial::HandleInitialSpecAmbDiffEmissiveColor, NiStandardMaterial.cpp:4377-4450). Nearly every
                // system is AMB_DIFF with E_A_D (flags 40): the emissive is added to the key colour times the light, so a dark key
                // colour over a dark texture still shows the emissive.
                // SoftParticlesManager::RegisterModel takes only objects whose material is already SoftParticlesMaterial
                // (softparticlesmanager.cpp:133-162); three systems name it. STILL-OPEN: their soft-depth opacity is not drawn.
                // STILL-OPEN: the dark and decal maps and texture transform controllers on a system's maps are not either.
                ps.Shading = new MeshState { Def = d.Material, Mat = mat, BaseMap = baseMap, GlowMap = glowMap, DetailMap = detailMap };
                SetRows(ps.Shading, baseMap?.Transform, 0, true);
                SetRows(ps.Shading, glowMap?.Transform, 1, true);
                SetRows(ps.Shading, detailMap?.Transform, 2, true);
                var vcu = d.Material.VertexColors;
                if (vcu.Emissive) mat.SetShaderParameter(FxParam.VcEmi, true);
                if (vcu.Diffuse) mat.SetShaderParameter(FxParam.VcDif, true);
                if (vcu.Ambient) mat.SetShaderParameter(FxParam.VcAmb, true);
                if (vcu.Opacity) mat.SetShaderParameter(FxParam.VcOpacity, true);
                if (d.Material.AlphaBoost) mat.SetShaderParameter(FxParam.AlphaBoost, true);
                mat.SetShaderParameter(FxParam.LightForm, EffectMesh.PartLightForm(d.Material.LightForm, useLighting));
                Shade(ps.Shading, 0, true);
                mat.SetShaderParameter(FxParam.Alpha, ps.Shading.LastAlpha = (float)d.Material.Opacity(0));
            }
            // The quad's corners sit at +-(right +- up) times the particle size (initial size times the size multiplier):
            // NiPSFacingQuadGeneratorKernel.cpp:127-131, NiPSAlignedQuadGeneratorKernelFF.cpp:145-169. So the quad is 2 x 2
            // and the per-particle basis carries the size (and for aligned quads the speed scale and pivot, AlignedQuad).
            ps.MM = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, InstanceCount = 0, VisibleInstanceCount = 0,
                Mesh = new QuadMesh { Size = new Vector2(2, 2), Material = mat } };
            ps.Node = new MultiMeshInstance3D { Multimesh = ps.MM, Name = d.Name ?? "particles", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            ps.Refused = d.Aligned && !AlignedQuad.ScaleValid(d);     // the generator refuses it (AlignedQuad.ScaleValid)
            z.AddChild(ps.Node);
            systems.Add(ps);
        }
        Advance(0);
    }

    /// <summary>A mesh's drawn state: its material with the shader parameters at time 0 and its MeshInstance3D (not yet in the
    /// tree, visible): for a mesh in the effect's graph, and for each clone of a master mesh (DrawClones). With
    /// <paramref name="shared"/> the mesh draws through that material and sets nothing on it (a master mesh no controller
    /// animates, MeshAnimated).</summary>
    MeshState BuildMeshState(EffectMesh em, ShaderMaterial shared)
    {
        var ms = new MeshState { Def = em };
        var m = em.Mesh;
        bool add = m.AlphaFlags >= 0 && (m.AlphaFlags & 1) != 0 && ((m.AlphaFlags >> 5) & 15) == 0;
        if (shared != null)
        {
            // a clone of a master mesh nothing animates draws through the material its first clone made (SharedMat)
            ms.Mat = shared; ms.Add = add; ms.Ho = em.Ho;
            ms.Node = new MeshInstance3D { Mesh = builtMeshes.GetValue(em, e => mb.BuildMesh(e.Mesh, false, uv2: true, vertexColor: e.Ho?.VertexColor ?? e.VertexColors.Any, effectMorph: null)), MaterialOverride = shared, Name = m.Name ?? "mesh", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            return ms;
        }
        // a morph with more offset targets than custom arrays keeps the positions with its fixed weights baked
        if (em.Morph is { } mo && mo.Anim.Deltas.Length <= ModelBuilder.EffectMorphTargets) { ms.Morph = mo; ms.MorphTargets = mo.Anim.Deltas.Length; }
        ms.Ho = em.Ho;
        var mat = new ShaderMaterial { Shader = ShaderOf(em, add, mirrored, ms.MorphTargets) };
        if (m.NoFog) mat.SetShaderParameter(FxParam.NoFog, true);
        // SORTING_OFF meshes are drawn unsorted after the opaque buckets and before the depth-sorted alpha list
        // (MS2RenderListProcessor: RenderNoSortList then RenderAlphaList, ms2renderlistprocessor.cpp:264-266, :368-370): a
        // lower priority puts them ahead of the field's other transparent objects. STILL-OPEN: their order among
        // themselves is the culling (scene) order in the client; Godot sorts them by depth within the priority.
        if (m.NoSort) mat.RenderPriority = -1;
        ms.BaseKey = m.Textures.ContainsKey("base") ? "base" : "shader0";
        ms.Base = m.Textures.TryGetValue(ms.BaseKey, out var bn) ? mb.Texture(bn) : null;
        ms.BaseMap = m.Maps.GetValueOrDefault(ms.BaseKey);
        ms.Mat = mat; ms.Add = add;
        ms.BaseT = new TexTransform(); ms.GlowT = new TexTransform(); ms.DetailT = new TexTransform(); ms.NormalT = new TexTransform();
        if (ms.Ho != null) HoSetup(ms, m);
        else
        {
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
                if (em.DetailOpacity) mat.SetShaderParameter(FxParam.DetailOpacity, true);
                ms.DetailMap = m.Maps.GetValueOrDefault("detail");
            }
            MapSetup(mat, ms.BaseMap, m, FxParam.BaseUv2, FxParam.BaseClamp, FxParam.BaseFilter);
            MapSetup(mat, ms.GlowMap, m, FxParam.GlowUv2, FxParam.GlowClamp, FxParam.GlowFilter);
            MapSetup(mat, ms.DetailMap, m, FxParam.DetailUv2, FxParam.DetailClamp, FxParam.DetailFilter);
            SetRows(ms, ms.BaseMap?.Transform, 0, true);
            SetRows(ms, ms.GlowMap?.Transform, 1, true);
            SetRows(ms, ms.DetailMap?.Transform, 2, true);
            var vcu = em.VertexColors;
            if (vcu.Emissive) mat.SetShaderParameter(FxParam.VcEmi, true);
            if (vcu.Diffuse) mat.SetShaderParameter(FxParam.VcDif, true);
            if (vcu.Ambient) mat.SetShaderParameter(FxParam.VcAmb, true);
            if (vcu.Opacity) mat.SetShaderParameter(FxParam.VcOpacity, true);
            if (em.AlphaBoost) mat.SetShaderParameter(FxParam.AlphaBoost, true);
            mat.SetShaderParameter(FxParam.LightForm, EffectMesh.PartLightForm(em.LightForm, useLighting));
            if (m.Normals == null) mat.SetShaderParameter(FxParam.LitNormals, false);
            Shade(ms, 0, true);
            mat.SetShaderParameter(FxParam.Alpha, ms.LastAlpha = (float)em.Opacity(0));   // the static material alpha (portal meshes are 0.05-0.75)
        }
        ms.LastTex = ms.Base;
        if (em.Flip != null)
        {
            // the frames as Variants made once: converting a Texture2D at each SetShaderParameter allocates (112 B); a mesh
            // particle's clones share their master's (flips)
            if (!flips.TryGetValue(em, out var fl))
            {
                var src = em.Flip.Sources;
                fl = (new Texture2D[src.Length], new Variant[src.Length]);
                for (int k = 0; k < src.Length; k++)
                    if (src[k] != null && (fl.T[k] = mb.Texture(src[k])) != null) fl.V[k] = Variant.From(fl.T[k]);
                flips[em] = fl;
            }
            ms.Flip = fl.T; ms.FlipV = fl.V;
        }
        // No shadow. A skill effect (CNifEffect) roots in EffectNode(useLighting, useShadow) (nifeffectparam.cpp:680), and
        // useShadow defaults to false (:353; an xml attribute, :395). EffectNode passes it on as LightUnaffectedNode's second
        // argument (effectservice.cpp:916); when that is false the node joins every shadow generator's unaffected caster and
        // receiver lists (lightunaffectednode.cpp:43-49), and SetShadowCaster(root, useShadow) clears the caster flag (:804).
        // STILL-OPEN: a field entity's EffectMesh graph is flagged a caster like any entity graph (WorldService::
        // SetShadowEnabled, @0x141bd30f0); whether the client's shadow-write pass draws its alpha-blended meshes is not traced.
        // Every mesh here is blended, and the field's blended meshes cast none either (FieldBuilder: Godot leaves them out).
        if (ms.Morph != null) SetMorphWeights(ms, 0, true);
        var morph = ms.Morph?.Anim;
        ms.Node = new MeshInstance3D { Mesh = builtMeshes.GetValue(em, e => mb.BuildMesh(e.Mesh, false, uv2: true, vertexColor: e.Ho?.VertexColor ?? e.VertexColors.Any, effectMorph: morph)), MaterialOverride = mat, Name = m.Name ?? "mesh", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        return ms;
    }

    /// <summary>The mesh has a controller Animate runs (colour, alpha, uv, morph or flipbook).</summary>
    static bool MeshAnimated(EffectMesh d) => d.Colors.Count > 0 || d.Alpha != null || d.Uv.Count > 0 || d.Morph != null || d.Flip != null;

    /// <summary>A mesh's colour terms at effect time t (EffectMesh.Shade) as the tint (the emissive), mat_diffuse and
    /// mat_ambient parameters, each set only when it changed (always when force). The lights they take are the shader's
    /// (LightDecl, EffectMesh.LightForm).</summary>
    static void Shade(MeshState ms, double t, bool force)
    {
        ms.Def.Shade(t, ms.Lit, ms.Dif, ms.Amb);
        // a Vector4, not a Color: Godot converts a Color set on a vec4 uniform from sRGB to linear, which took 0.5 to 0.21;
        // the colour buffer holds gamma values (field_lit, ModelBuilder's Vector3 colours), so the coefficient goes in as it is
        var c = new Vector4((float)ms.Lit[0], (float)ms.Lit[1], (float)ms.Lit[2], 1f);
        var d = new Vector3((float)ms.Dif[0], (float)ms.Dif[1], (float)ms.Dif[2]);
        var a = new Vector3((float)ms.Amb[0], (float)ms.Amb[1], (float)ms.Amb[2]);
        if (force || c != ms.LastTint) ms.Mat.SetShaderParameter(FxParam.Tint, ms.LastTint = c);
        if (force || d != ms.LastDif) ms.Mat.SetShaderParameter(FxParam.MatDiffuse, ms.LastDif = d);
        if (force || a != ms.LastAmb) ms.Mat.SetShaderParameter(FxParam.Ambient, ms.LastAmb = a);
    }

    /// <summary>An _HO block's maps and constants (HoCode): the base, detail and the technique's other maps with their
    /// transforms, the distortion (MATERIALPOWER: the glossiness, NiMaterialProperty's default 4 without one,
    /// NiMaterialProperty.cpp:31-39), the alpha test, and the colours at time 0 (HoShade).</summary>
    void HoSetup(MeshState ms, ModelMesh m)
    {
        var mat = ms.Mat; var h = ms.Ho;
        if (ms.Base != null) mat.SetShaderParameter(FxParam.Tex, ms.Base);
        // a map the block lacks takes the texture the effect lends it (EffectMesh.Lent), under the identity transform
        Texture2D Map(string slot) => m.Textures.TryGetValue(slot, out var f) || ms.Def.Lent?.TryGetValue(slot, out f) == true ? mb.Texture(f) : null;
        if (Map("detail") is { } dt) { mat.SetShaderParameter(FxParam.DetailTex, dt); ms.DetailMap = m.Maps.GetValueOrDefault("detail"); }
        if (h.Dissolve) { if (Map("gloss") is { } gl) mat.SetShaderParameter(FxParam.GlossTex, gl); }
        else
        {
            if (Map("normal") is { } nt) { mat.SetShaderParameter(FxParam.NormalTex, nt); ms.NormalMap = m.Maps.GetValueOrDefault("normal"); }
            mat.SetShaderParameter(FxParam.HoPower, m.Emissive != null ? m.Glossiness : 4f);
        }
        if (h.Glow && Map("glow") is { } gt) { mat.SetShaderParameter(FxParam.GlowTex, gt); ms.GlowMap = m.Maps.GetValueOrDefault("glow"); }
        int test = AlphaTest(m.AlphaFlags);
        if (test >= 0) { mat.SetShaderParameter(FxParam.AlphaTest, test); mat.SetShaderParameter(FxParam.AlphaRef, m.AlphaThreshold / 255f); }
        SetRows(ms, ms.BaseMap?.Transform, 0, true);
        SetRows(ms, ms.GlowMap?.Transform, 1, true);
        SetRows(ms, ms.DetailMap?.Transform, 2, true);
        SetRows(ms, ms.NormalMap?.Transform, 3, true);
        HoShade(ms, 0, true);
    }

    /// <summary>An _HO block's c0 (the emissive with the opacity: ho_emi) and c1 (the specular: ho_spec, under a glow
    /// technique) at effect time t (EffectMesh.HoColors, Opacity), each set only when it changed (always when force).</summary>
    static void HoShade(MeshState ms, double t, bool force)
    {
        ms.Def.HoColors(t, ms.Lit, ms.Amb);
        var c = new Vector4((float)ms.Lit[0], (float)ms.Lit[1], (float)ms.Lit[2], (float)ms.Def.Opacity(t));
        if (force || c != ms.LastTint) ms.Mat.SetShaderParameter(FxParam.HoEmi, ms.LastTint = c);
        if (!ms.Ho.Glow) return;
        var s = new Vector3((float)ms.Amb[0], (float)ms.Amb[1], (float)ms.Amb[2]);
        if (force || s != ms.LastAmb) ms.Mat.SetShaderParameter(FxParam.HoSpec, ms.LastAmb = s);
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

    public override void _EnterTree()
    {
        inTree = true;
        // the mesh and particle shaders' culled side for the world the effect is placed in (RenderModes): checked on entering the tree,
        // as the placement is set after construction. Only here: every caller sets Transform and FileSpace before AddChild,
        // and the scene's own node keys scale by one float, so the sign cannot change later. A caller that turns a placed
        // effect into a mirrored basis would have to check again
        bool m = GlobalBasis.Determinant() < 0;
        if (m == mirrored) return;
        mirrored = m;
        foreach (var ms in meshes)
            if (ms.Def.Mesh.Cull != FaceCull.None)
            {
                ms.Mat.Shader = ShaderOf(ms.Def, ms.Add, mirrored, ms.MorphTargets);
                if (ms.Glow != null) ms.Glow.Shader = MaskShader(ms.Def.Mesh, mirrored, ms.MorphTargets);
            }
        // and the clones made so far (a later one takes mirrored as it is made, BuildMeshState); a shared material is set
        // once per clone, to the same shader
        foreach (var ps in systems)
            if (ps.Clones != null)
                foreach (var cl in ps.Clones)
                    if (cl != null)
                        foreach (var ms in cl.Meshes)
                            if (ms.Def.Mesh.Cull != FaceCull.None)
                            {
                                ms.Mat.Shader = ShaderOf(ms.Def, ms.Add, mirrored, ms.MorphTargets);
                                if (ms.Glow != null) ms.Glow.Shader = MaskShader(ms.Def.Mesh, mirrored, ms.MorphTargets);
                            }
        // the quads of every other system (a mesh-particle system has no quad material: its clones above keep their mesh's cull)
        foreach (var ps in systems)
            if (!ps.Def.MeshParticles && ps.Def.Material.Mesh.Cull != FaceCull.None) ps.Mat.Shader = PartShader(ps.Def, mirrored);
    }
    bool mirrored;
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

    /// <summary>
    /// A mesh's world transform for this frame. The client's key interpolation (Gamebryo's, not in the decompiled source) passes
    /// a NaN key value on: the Wizard's Magic Claw cast effect (eff_wizard_magicclaw_cast_01_a.nif, KMS and GMS2 alike) has a
    /// NaN scale key at 0.7 s on its sphere, so from its first key (0.333 s) the node's and its children's world transforms are
    /// NaN there and their vertices draw nothing. Here such a mesh is hidden and keeps its last transform, as the renderer
    /// takes no non-finite transform; it shows again when the transform is finite and <paramref name="hide"/> is not set (a
    /// hidden mesh stays hidden: Hidden, CloneHidden).
    /// </summary>
    void Place(MeshState ms, in Transform3D w, bool hide)
    {
        bool finite = w.IsFinite();
        if (finite) ms.Node.Transform = w;
        ms.NotFinite = !finite;
        Show(ms, finite && !hide);
    }

    static void Show(MeshState ms, bool on) { if (on != ms.Shown) { ms.Shown = on; ms.Node.Visible = on; } }

    /// <summary>
    /// Whether node i is hidden on this frame: it or a node above it is culled. A node with an NiVisController takes the
    /// track's value at the controller's time in place of its file flag (SetAppCulled(!bValue), NiVisController::Update,
    /// NiVisController.cpp:42-61), else its APP_CULLED flag; a culled node hides its subtree from drawing. A hidden system
    /// still simulates: the client updates a culled object and only leaves it out of the draw.
    /// </summary>
    bool Hidden(int i)
    {
        if (i < 0) return false;
        if (hidDone[i]) return hidden[i];
        var n = scene.Nodes[i];
        bool h = (n.Vis != null ? !n.Vis.At(n.VisCtl.Local(t)) : n.Culled) || Hidden(n.Parent);
        hidden[i] = h; hidDone[i] = true;
        return h;
    }

    /// <summary>Whether a clone's master node k (a slot in MasterNodes, with its parent slots par) is hidden at the particle's
    /// age: culled by its vis track or its flag, or under such a node up to the master root. The root's own flag is not read:
    /// the clone's root is unculled when its particle is added (NiPSMeshParticleSystem::ResolveAddedParticles,
    /// NiPSMeshParticleSystem.cpp:360-372).</summary>
    bool CloneHidden(int[] nodes, int[] par, int k, float age)
    {
        for (; k >= 0; k = par[k])
        {
            var n = scene.Nodes[nodes[k]];
            if (n.Vis != null ? !n.Vis.At(n.VisCtl.Local(age)) : par[k] >= 0 && n.Culled) return true;
        }
        return false;
    }

    Transform3D World(int i, float t, Vector3 camLocal)
    {
        if (i < 0) return Transform3D.Identity;
        if (done[i]) return world[i];
        var n = scene.Nodes[i];
        var l = Local(i, t);
        var p = n.Parent >= 0 ? World(n.Parent, t, camLocal) : Transform3D.Identity;
        var w = p * l;
        if (n.Billboard) w = Face(i, w, camLocal);
        done[i] = true; world[i] = w;
        return w;
    }

    /// <summary>
    /// NiBillboardNode::RotateToCamera (BillboardFace; NiBillboardNode.cpp:66-205, run from OnVisible :212-216) on billboard node
    /// <paramref name="i"/>'s world transform: the node's world rotation times the face matrix, which is a rotation in the node's
    /// own frame (m_kWorld.m_Rotate * kFaceMat, :199). The camera's position, direction, up and right go into that frame without
    /// the node's scale.
    /// </summary>
    Transform3D Face(int i, in Transform3D w, Vector3 camLocal)
    {
        float s = w.Basis.Column0.Length();
        if (!(s > 0)) return w;
        var inv = w.Basis.Orthonormalized().Transposed();
        var (c0, c1, c2) = BillboardFace.Face(scene.Nodes[i].BillboardMode, N(inv * (camLocal - w.Origin)), N(inv * camDirFx), N(inv * camUpFx),
                                              N(inv * camRightFx), s);
        return new Transform3D(w.Basis * new Basis(G(c0), G(c1), G(c2)), w.Origin);
    }

    /// <summary>The live particles of the mesh-particle systems, and how many of them have their clone (StartClone); read by
    /// the checks.</summary>
    public int MeshParticleCount(out int cloned)
    {
        int n = 0; cloned = 0;
        foreach (var ps in systems)
        {
            if (ps.Clones == null) continue;
            n += ps.Count;
            for (int i = 0; i < ps.Count; i++) if (ps.Clones[i] != null) cloned++;
        }
        return n;
    }

    /// <summary>The index of the scene's node named <paramref name="name"/> (NiAVObject::GetObjectByName: the exact name), -1
    /// when the effect has none.</summary>
    public int NodeNamed(string name)
    {
        for (int i = 0; i < scene.Nodes.Count; i++)
            if (string.Equals(scene.Nodes[i].Name, name, StringComparison.Ordinal)) return i;
        return -1;
    }

    /// <summary>Node <paramref name="i"/>'s world point this frame (a swoosh's trail node: CSwooshEffect::UpdateTrail reads its
    /// world translate), Godot space.</summary>
    public Vector3 NodePoint(int i) => z.GlobalTransform * World(i, t, camLocalFx).Origin;

    Vector3 camLocalFx = new(0, -1000, 300);
    // the camera's direction, up and right in this effect's space for the billboard nodes (set each Advance)
    Vector3 camDirFx = new(0, 1, 0), camUpFx = new(0, 0, 1), camRightFx = new(1, 0, 0);
    static System.Numerics.Vector3 N(Vector3 v) => new(v.X, v.Y, v.Z);
    static Vector3 G(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    float R() => (float)rng.NextDouble();
    float Spread(float v) => v * (2 * R() - 1);

    void Advance(float dt)
    {
        t += dt;
        if (t > life) { Finished = true; QueueFree(); return; }
        long q0 = ProbeOn ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        var cam = needCam ? FrameCamera() : null;
        // the effect's world transform, fetched once for the camera and the aligned quads below
        bool zIn = needAxes && z.IsInsideTree();
        var zg = cam != null || zIn ? z.GlobalTransform : Transform3D.Identity;
        var camLocal = cam != null ? zg.AffineInverse() * cam.GlobalPosition : new Vector3(0, -1000, 300);
        // the camera's right and up in this effect's space (file space), for the facing particle quads
        Vector3 camRight = new(1, 0, 0), camUp = new(0, 0, 1), camDir = new(0, 1, 0);
        if (cam != null)
        {
            var inv = zg.Basis.Inverse();
            camRight = (inv * cam.GlobalBasis.X).Normalized(); camUp = (inv * cam.GlobalBasis.Y).Normalized();
            camDir = (inv * -cam.GlobalBasis.Z).Normalized();   // Godot's camera looks along -Z
        }
        camRightFx = camRight; camUpFx = camUp; camDirFx = camDir; camLocalFx = camLocal;
        // the file's world X and Z axes (Z goes to Godot's up) in this effect's space, for the aligned quads of world-space
        // systems (AlignedQuad: the kernels' UNIT_X / UNIT_Z defaults) and the world frame of a world-space mesh-particle
        // system (DrawClones)
        if (needAxes)
        {
            // (the first step runs in the constructor, before the effect is in the tree: its own transform until then)
            var zb = zIn ? zg.Basis : Transform.Basis * z.Transform.Basis;
            var zi = zb.Inverse();
            worldX = (zi * Vector3.Right).Normalized(); worldZ = (zi * Vector3.Up).Normalized();
        }
        // a static node's world transform depends on neither the time nor the camera: it is kept from the first frame
        foreach (int i in dynNodes) done[i] = false;
        if (visDyn) Array.Clear(hidDone);
        long q1 = ProbeOn ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

        foreach (var ms in meshes)
        {
            bool hide = Hidden(ms.Def.Node);
            if (!ms.Static || !ms.Placed) { Place(ms, World(ms.Def.Node, t, camLocal), hide); ms.Placed = true; }
            else Show(ms, !ms.NotFinite && !hide);
            Animate(ms, t, false);
        }

        long q2 = ProbeOn ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        foreach (var ps in systems)
        {
            var sm = ps.Shading;
            if (sm == null) { }                                       // a mesh-particle system: its clones are shaded (DrawClones)
            else if (sm.Ho != null) { if (sm.Def.Colors.Count > 0 || sm.Def.Alpha != null) HoShade(sm, t, false); }
            else
            {
                if (sm.Def.Colors.Count > 0) Shade(sm, t, false);
                if (sm.Def.Alpha != null) { float a = (float)sm.Def.Opacity(t); if (a != sm.LastAlpha) sm.Mat.SetShaderParameter(FxParam.Alpha, sm.LastAlpha = a); }
            }
            // NiPSResetOnLoopCtlr::Update (NiPSResetOnLoopCtlr.cpp:29-50): when its scaled time goes back (the loop wrapped)
            // the system is reset before it updates: no particle is left (ResetParticleSystem, NiPSParticleSystem.cpp:
            // 217-234) and a mesh system's clones go back to the pool (NiPSMeshParticleSystem.cpp:174-201; here they stay in
            // their slots and DrawClones hides them); its first update only takes the time. STILL-OPEN: the emitters'
            // ResetTransformation has nothing to reset here, as the emitters keep no last transform.
            if (ps.Def.ResetCtl is { } rc)
            {
                double s = rc.Local(t);
                if (ps.LastScaled > s) ps.Count = 0;
                ps.LastScaled = s;
            }
            bool hide = Hidden(ps.Def.Node);
            if (ps.Node != null) { bool on = !hide && !ps.Refused; if (on != ps.Shown) { ps.Shown = on; ps.Node.Visible = on; } }
            Step(ps, dt, hide, camLocal, camRight, camUp);
        }
        if (ProbeOn) { long q3 = System.Diagnostics.Stopwatch.GetTimestamp(); seg[0] += q1 - q0; seg[1] += q2 - q1; seg[2] += q3 - q2; }
    }
    /// <summary>A mesh's controllers at time t: the colours and alpha (or an _HO block's), the uv transforms, the morph weights
    /// and the flipbook frame. started: every uv controller runs (a clone's, AnimateUv).</summary>
    void Animate(MeshState ms, float t, bool started)
    {
        var d = ms.Def;
        if (ms.Ho != null) { if (d.Colors.Count > 0 || d.Alpha != null) HoShade(ms, t, false); }
        else
        {
            if (d.Colors.Count > 0) Shade(ms, t, false);
            if (d.Alpha != null) { float a = (float)d.Opacity(t); if (a != ms.LastAlpha) { ms.Mat.SetShaderParameter(FxParam.Alpha, ms.LastAlpha = a); ms.Glow?.SetShaderParameter(FxParam.Alpha, a); } }
        }
        if (d.Uv.Count > 0) AnimateUv(ms, t, started);
        if (ms.Morph != null) SetMorphWeights(ms, t, false);
        if (d.Flip != null && ms.Flip != null && ms.Flip.Length > 0)
        {
            double lt = d.Flip.Ctl.Local(t);
            int idx = d.Flip.Curve != null ? (int)Math.Floor(d.Flip.Curve(lt) + 0.001)
                                           : (int)Math.Floor((lt - d.Flip.Ctl.Start) / Math.Max(d.Flip.Ctl.Stop - d.Flip.Ctl.Start, 1e-6) * ms.Flip.Length);
            idx = Math.Clamp(idx, 0, ms.Flip.Length - 1);
            var tex = ms.Flip[idx];
            if (tex != null && tex != ms.LastTex) { ms.LastTex = tex; ms.Mat.SetShaderParameter(FxParam.Tex, ms.FlipV[idx]); ms.Glow?.SetShaderParameter(FxParam.Tex, ms.FlipV[idx]); }
        }
    }

    // the MS2_PERF_FX probe's segment counters (a measuring tool, printed by Probe; untouched when the probe is off):
    // ticks in camera, meshes, systems, buffer set, draw loop, and the particle count
    static readonly long[] seg = new long[6];

    // The uv controllers of one mesh at effect time t: each active controller replaces one component of its map's transform
    // (UvOp); a map without a static transform gets the controller's default one (TexTransform.ControllerDefault). Only the
    // base, glow and detail maps are drawn here (and the normal map of an _HO technique), so controllers on the dark and decal
    // maps, and on a standard block's normal map, have nothing to move.
    // The working transform is rebuilt from the static one each frame and the running controllers replayed over it. The
    // client instead keeps whatever UpdateValue last wrote into the map's transform, so the two differ only when one of two
    // controllers on a map stops (its range is over, or it is inactive) while the other runs: here the stopped component
    // snaps back to the static value, in the client it keeps its last value (not surveyed; expected rare).
    // Only Active controllers run (NiTimeController::Update skips inactive ones, and CNifEffect never calls
    // StartAnimations). STILL-OPEN: FUN_141545250 (nifanimationcontroller.cpp:175-190) calls NiTimeController::
    // StartAnimations on an entity's scene graph, which activates controllers the file leaves inactive; a field-entity
    // effect started that way would run them. The alpha, colour and flip controllers here do not check Active yet.
    // A clone of a master particle runs them all: InitializeParticle starts the clone's controllers
    // (NiTimeController::StartAnimations(clone, 0), NiPSMeshParticleSystem.cpp:268), and starting one sets it active.
    static readonly TexTransform NoTransform = TexTransform.ControllerDefault();
    static void AnimateUv(MeshState ms, float t, bool started)
    {
        bool baseOn = false, glowOn = false, detailOn = false, normalOn = false;
        foreach (var op in ms.Def.Uv)
        {
            if (!op.Ctl.Active && !started) continue;
            TexTransform w;
            if (op.Slot == ms.BaseKey && ms.BaseMap != null) { if (!baseOn) { ms.BaseT.CopyFrom(ms.BaseMap.Transform ?? NoTransform); baseOn = true; } w = ms.BaseT; }
            else if (op.Slot == "glow" && ms.GlowMap != null) { if (!glowOn) { ms.GlowT.CopyFrom(ms.GlowMap.Transform ?? NoTransform); glowOn = true; } w = ms.GlowT; }
            else if (op.Slot == "detail" && ms.DetailMap != null) { if (!detailOn) { ms.DetailT.CopyFrom(ms.DetailMap.Transform ?? NoTransform); detailOn = true; } w = ms.DetailT; }
            else if (op.Slot == "normal" && ms.NormalMap != null) { if (!normalOn) { ms.NormalT.CopyFrom(ms.NormalMap.Transform ?? NoTransform); normalOn = true; } w = ms.NormalT; }
            else continue;
            w.Set(op.Operation, (float)op.Curve(op.Ctl.Local(t)));
        }
        if (baseOn) SetRows(ms, ms.BaseT, 0, false);
        if (glowOn) SetRows(ms, ms.GlowT, 1, false);
        if (detailOn) SetRows(ms, ms.DetailT, 2, false);
        if (normalOn) SetRows(ms, ms.NormalT, 3, false);
    }

    // the morph's weights at effect time t as morph_w, set only when they differ from the last ones set (always when force)
    static void SetMorphWeights(MeshState ms, float t, bool force)
    {
        Span<float> w = stackalloc float[ModelBuilder.EffectMorphTargets];
        w.Clear();
        ms.Morph.WeightsAt(t, w[..ms.MorphTargets]);
        var v = new Vector4(w[0], w[1], w[2], w[3]);
        if (force || v != ms.LastW) { ms.Mat.SetShaderParameter(FxParam.MorphW, ms.LastW = v); ms.Glow?.SetShaderParameter(FxParam.MorphW, v); }
    }

    // a map's texture transform as the shader's two rows (identity without one), map 0 base, 1 glow, 2 detail or 3 normal; a row is set only
    // when it differs from the last one set (always when force)
    static void SetRows(MeshState ms, TexTransform tt, int map, bool force)
    {
        Vector3 a = new(1, 0, 0), b = new(0, 1, 0);
        if (tt != null) { var r = ms.Rows; tt.Rows(r); a = new(r[0], r[1], r[2]); b = new(r[3], r[4], r[5]); }
        int k = map * 2;
        if (force || a != ms.LastRows[k])
        {
            var p = map == 0 ? FxParam.BaseR0 : map == 1 ? FxParam.GlowR0 : map == 2 ? FxParam.DetailR0 : FxParam.NormalR0;
            ms.Mat.SetShaderParameter(p, ms.LastRows[k] = a); ms.Glow?.SetShaderParameter(p, a);
        }
        if (force || b != ms.LastRows[k + 1])
        {
            var p = map == 0 ? FxParam.BaseR1 : map == 1 ? FxParam.GlowR1 : map == 2 ? FxParam.DetailR1 : FxParam.NormalR1;
            ms.Mat.SetShaderParameter(p, ms.LastRows[k + 1] = b); ms.Glow?.SetShaderParameter(p, b);
        }
    }

    // a map's uv set and clamp mode. The map reads its own uv set (TEXCOORD_MASK; NiStandardMaterialDescriptor.cpp:1006,
    // the glow map's index from FindUVSetIndexForTextureEnum, ms2standardmaterial.cpp:1544): set 1 is the mesh's
    // TEXCOORD1 as UV2; a mesh without one reads set 0. STILL-OPEN: what D3D reads for a set the mesh does not have.
    static void MapSetup(ShaderMaterial mat, TexMap map, ModelMesh m, StringName uv2, StringName clamp, StringName filter)
    {
        if (map == null) return;
        mat.SetShaderParameter(uv2, map.UvSet == 1 && m.Uvs1 != null);
        MapModes(mat, map, clamp, filter);
    }

    // a map's clamp and filter modes (MapDecl)
    static void MapModes(ShaderMaterial mat, TexMap map, StringName clamp, StringName filter)
    {
        mat.SetShaderParameter(clamp, (map.ClampS ? 1 : 0) | (map.ClampT ? 2 : 0));
        mat.SetShaderParameter(filter, map.Filter);
    }

    /// <summary>The gravity, bomb and drag forces on this step, in the effect's space (see Step).</summary>
    readonly List<ParticleForces.Step> forcesNow = new();

    void Step(PSys ps, float dt, bool hide, Vector3 camLocal, Vector3 camRight, Vector3 camUp)
    {
        var d = ps.Def;
        // The particles live in the system's model space and the system's world transform, its scale too, is applied when
        // they are drawn (NiPSParticleSystem::UpdateWorldData keeps the scale of a world-space system, NiPSParticleSystem.cpp:
        // 510-520); an emitter only rotates the velocity into that space (NiPSBoxEmitter.cpp:100-103). Here the particles
        // are kept in the effect's space, so the system node's scale multiplies their size, emitted speed and the forces.
        var sysWorld = World(d.Node, t, camLocal);
        float psScale = sysWorld.Basis.Scale.X;
        // Forces on at this time. A bomb or drag takes its object's position and its object's rotation times its axis
        // (NiPSBombForce::Update, NiPSBombForce.cpp:59-80; NiPSDragForce::Update, NiPSDragForce.cpp:57-78); without an
        // object, or a drag with Percentage <= 0, it is left out (NiPSSimulatorForcesStep.cpp:186-226). The system's
        // space differs from the effect's by the system's transform, so distances there are the effect's over psScale:
        // the bomb's DeltaV and the drag and decay distances are scaled by psScale here.
        // Gravity's position and axis are taken the same way, and gravity without an object is left out
        // (NiPSGravityForce::Update, NiPSGravityForce.cpp:65-79); its Strength is an acceleration (scaled by psScale) and
        // its Decay is per system-space distance (divided by psScale); an NiPSGravityStrengthCtlr sets the strength
        // (ForceDef.StrengthAt). All three run in the list's order.
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
                    DeltaV = (float)f.DeltaV * psScale, Strength = (float)f.StrengthAt(t) * psScale,
                    Decay = f.Kind == ForceKind.Gravity ? (psScale != 0 ? (float)f.Decay / psScale : 0) : (float)f.Decay * psScale,
                    Range = f.Range >= float.MaxValue ? float.MaxValue : (float)f.Range * psScale,
                    Falloff = f.RangeFalloff >= float.MaxValue ? float.MaxValue : (float)f.RangeFalloff * psScale,
                });
            }
        }
        // Colliders: the step hands the kernel the active ones (NiPSSimulatorCollidersStep.cpp:231-262), each in the
        // system's space from its object's transform (ColliderDef). Here that space is the effect's, so a collider takes
        // its object's world position, rotation and scale; one without an object stays at the system's origin, turned and
        // scaled by the system's transform.
        int nColl = 0;
        if (ps.Colliders != null)
            foreach (var c in d.ColliderList)
            {
                if (!c.Active) continue;
                var w = c.Node >= 0 ? World(c.Node, t, camLocal) : sysWorld;
                var rb = w.Basis.Orthonormalized();
                float sc = w.Basis.Scale.X;
                var o = new System.Numerics.Vector3(w.Origin.X, w.Origin.Y, w.Origin.Z);
                if (c.Sphere) ps.Colliders[nColl++] = ParticleColliders.Collider.Ball(o, sc, (float)c.Radius, (float)c.Bounce, c.DieOnCollide);
                else
                {
                    var axX = rb * new Vector3((float)c.XAxis.X, (float)c.XAxis.Y, (float)c.XAxis.Z);
                    var axY = rb * new Vector3((float)c.YAxis.X, (float)c.YAxis.Y, (float)c.YAxis.Z);
                    ps.Colliders[nColl++] = ParticleColliders.Collider.Plane(o, new(axX.X, axX.Y, axX.Z), new(axY.X, axY.Y, axY.Z), sc,
                        (float)c.Width, (float)c.Height, (float)c.Bounce, c.DieOnCollide);
                }
            }
        // simulate: the forces in their order, each on the velocity the one before left, against the step's start
        // position (ParticleForces), then the move. The forces take the step's dt, where the kernel takes each particle's
        // time since its last update (NiPSSimulatorForcesKernel.cpp:33, NiPSForceHelpers.inl). Every live particle is
        // updated every step here and a new one starts at the step's time, so the two are the same.
        int nForces = forcesNow.Count;
        for (int i = ps.Count - 1; i >= 0; i--)
        {
            var v = ps.Vel[i];
            if (nForces > 0)
            {
                var p0 = ps.Pos[i];
                var pn = new System.Numerics.Vector3(p0.X, p0.Y, p0.Z);
                var vn = new System.Numerics.Vector3(v.X, v.Y, v.Z);
                vn = ParticleForces.Apply(vn, pn, forcesNow, dt);
                v = new Vector3(vn.X, vn.Y, vn.Z);
            }
            // then the colliders (ParticleColliders.Step): a particle that hits one moves to the hit and bounces, and the
            // final step moves it and ages it by the time left after the hit, then kills it past its life
            // (NiPSSimulatorKernelHelpers.inl:19-27, :242-264); one that hits a collider that kills on contact is removed.
            // The angle follows the age: the general kernel draws it as the initial angle plus the speed times the age
            // (UpdateParticleRotation, NiPSSimulatorKernelHelpers.inl:238), so it turns by the same time the age takes.
            float tLast = 0f;
            if (nColl > 0)
            {
                var p0 = ps.Pos[i];
                var pn = new System.Numerics.Vector3(p0.X, p0.Y, p0.Z);
                var vn = new System.Numerics.Vector3(v.X, v.Y, v.Z);
                if (ParticleColliders.Step(new ReadOnlySpan<ParticleColliders.Collider>(ps.Colliders, 0, nColl), ps.Tried, ref pn, ref vn, ref tLast, dt))
                { Swap(ps, i, ps.Count - 1); ps.Count--; continue; }
                ps.Pos[i] = new Vector3(pn.X, pn.Y, pn.Z); v = new Vector3(vn.X, vn.Y, vn.Z);
            }
            ps.Age[i] += dt - tLast;
            if (ps.Age[i] >= ps.Life[i]) { Swap(ps, i, ps.Count - 1); ps.Count--; continue; }
            ps.Vel[i] = v;
            ps.Pos[i] += v * (dt - tLast);
            ps.Rot[i] += ps.RotSpeed[i] * (dt - tLast);
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
                if (ps.Accum[ei] >= 1 && ps.Count < ps.Pos.Length)
                {
                    // the emitter's values at this time, its float controllers' values in place of the stored ones (EmitterDef.At)
                    var ev = e.At(t);
                    while (ps.Accum[ei] >= 1 && ps.Count < ps.Pos.Length) { ps.Accum[ei] -= 1; Emit(ps, e, in ev, camLocal, psScale); }
                }
                if (ps.Count >= ps.Pos.Length) ps.Accum[ei] = 0;
            }
        if (ps.Clones != null) { DrawClones(ps, sysWorld, psScale, hide, camLocal); return; }
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
        bool sizeKeys = d.SizeKeys.Length > 0, colorKeys = d.ColorKeys.Length > 0, aligned = d.Aligned;
        bool grow = d.GrowTime > 0, shrink = d.ShrinkTime > 0;
        float growTime = (float)d.GrowTime, shrinkTime = (float)d.ShrinkTime;
        // Aligned quads (AlignedQuad): the system's fixed normal and up and the kernels' defaults, in this effect's space. The
        // particles are kept here for a world-space system too, so its original world rotation is the system node's rotation
        // here and the world axes are the file's world axes brought here; a local-space system's are its own axes. The
        // client keeps the world scale out of a world-space system's data (NiPSParticleSystem::UpdateWorldData,
        // NiPSParticleSystem.cpp:509-520) and its emitters only rotate the velocity (NiPSBoxEmitter.cpp:100-103), so in both
        // spaces the client's speed is the spawned velocity here over the system's scale (AlignedQuad.ClientSpeed).
        var aN = System.Numerics.Vector3.Zero; var aU = aN; var aX = aN; var aZ = aN; var aO = aN;
        float amtU = 0f, amtV = 0f, limU = 1f, limV = 1f, restU = 1f, restV = 1f, cenU = 0f, cenV = 0f, speedScale = 1f;
        bool scaleSpeed = false, rotations = d.HasRotations, anyPer = false;
        int nMethod = d.NormalMethod, uMethod = d.UpMethod;
        if (aligned)
        {
            var r = sysWorld.Basis.Orthonormalized();
            Vector3 defX = d.WorldSpace ? worldX : r * Vector3.Right, defZ = d.WorldSpace ? worldZ : r * Vector3.Back;
            aX = new(defX.X, defX.Y, defX.Z); aZ = new(defZ.X, defZ.Y, defZ.Z); aO = new(sysWorld.Origin.X, sysWorld.Origin.Y, sysWorld.Origin.Z);
            var camN = camRight.Cross(camUp);
            if (nMethod == AlignedQuad.LocalFixed)
            {
                var v = r * new Vector3((float)d.NormalDirection.X, (float)d.NormalDirection.Y, (float)d.NormalDirection.Z);
                aN = AlignedQuad.Unitize(new(v.X, v.Y, v.Z), aX);
            }
            else if (nMethod == AlignedQuad.Camera) aN = new(camN.X, camN.Y, camN.Z);
            else { var v = r * Vector3.Back; aN = new(v.X, v.Y, v.Z); }          // the constructor's (0,0,1) (NiPSAlignedQuadGenerator.cpp:41-42)
            if (uMethod == AlignedQuad.LocalFixed)
            {
                var v = r * new Vector3((float)d.UpDirection.X, (float)d.UpDirection.Y, (float)d.UpDirection.Z);
                aU = AlignedQuad.Unitize(new(v.X, v.Y, v.Z), aZ);
            }
            else if (uMethod == AlignedQuad.Camera) aU = new(camUp.X, camUp.Y, camUp.Z);
            else { var v = r * Vector3.Up; aU = new(v.X, v.Y, v.Z); }            // the constructor's (0,1,0)
            anyPer = ((nMethod | uMethod) & AlignedQuad.PerParticle) != 0;
            amtU = AlignedQuad.NormalizedAmount(d.ScaleAmountU, d.ScaleLimitU, d.ScaleRestU);
            amtV = AlignedQuad.NormalizedAmount(d.ScaleAmountV, d.ScaleLimitV, d.ScaleRestV);
            limU = (float)d.ScaleLimitU; limV = (float)d.ScaleLimitV; restU = (float)d.ScaleRestU; restV = (float)d.ScaleRestV;
            cenU = (float)d.CenterU; cenV = (float)d.CenterV;
            scaleSpeed = amtU != 0f || amtV != 0f;
            speedScale = psScale;
        }
        var tex = ps.Tex;
        float cx = camLocal.X, cy = camLocal.Y, cz = camLocal.Z;
        float rx = camRight.X, ry = camRight.Y, rz = camRight.Z, ux = camUp.X, uy = camUp.Y, uz = camUp.Z;
        float[] age = ps.Age, life = ps.Life, psize = ps.Size, rot = ps.Rot, variance = ps.Variance;
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
            float ox = 0f, oy = 0f, oz = 0f;                      // the aligned quad's pivot offset
            if (aligned)
            {
                var vel = pvel[i];
                var vn = new System.Numerics.Vector3(vel.X, vel.Y, vel.Z);
                float sU = 1f, sV = 1f;
                if (scaleSpeed)
                {
                    float sp = AlignedQuad.ClientSpeed(vn, speedScale);
                    sU = AlignedQuad.SpeedScale(amtU, limU, restU, sp); sV = AlignedQuad.SpeedScale(amtV, limV, restV, sp);
                }
                var qn = aN; var qu = aU;
                if (anyPer)
                    AlignedQuad.Inputs(nMethod, uMethod, rotations, aN, aU, new System.Numerics.Vector3(pos.X, pos.Y, pos.Z) - aO, vn,
                                       aX, aZ, out qn, out qu);
                AlignedQuad.Quad(qn, qu, size, sU, sV, cenU, cenV, rotations ? rot[i] : 0f, out var qx, out var qy, out var qo);
                xx = qx.X; xy = qx.Y; xz = qx.Z; yx = qy.X; yy = qy.Y; yz = qy.Z; ox = qo.X; oy = qo.Y; oz = qo.Z;
                reach = MathF.Abs(size) * (MathF.Abs(sU) + MathF.Abs(sV) + MathF.Abs(sU * cenU) + MathF.Abs(sV * cenV));
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
            buf[o] = xx; buf[o + 1] = yx; buf[o + 2] = tx; buf[o + 3] = pos.X - ox;
            buf[o + 4] = xy; buf[o + 5] = yy; buf[o + 6] = ty; buf[o + 7] = pos.Y - oy;
            buf[o + 8] = xz; buf[o + 9] = yz; buf[o + 10] = tz; buf[o + 11] = pos.Z - oz;
            buf[o + 12] = cr; buf[o + 13] = cg; buf[o + 14] = cb; buf[o + 15] = ca;
            // the texture cell's top left (AlignedQuad.TexAnim; the whole texture without texture animation)
            float cellU = 0f, cellV = 0f;
            if (tex.On) tex.Cell(age[i], life[i], variance[i], out cellU, out cellV);
            buf[o + 16] = cellU; buf[o + 17] = cellV; buf[o + 18] = 0; buf[o + 19] = 0;
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

    /// <summary>A mesh-particle system's clone tables (PSys.Clones): per generation each master node's parent slot and each
    /// master mesh's node slot in ParticleSystemDef.MasterNodes. Only the fixed-method kernel is ported (MeshAlign), with
    /// rotation axes when the system has rotations (NiPSSimulatorMeshAlignKernelFF asserts them); a system needing another
    /// draws nothing. STILL-OPEN: the other mesh-align kernels (none in either data set).</summary>
    void SetupClones(PSys ps)
    {
        var d = ps.Def;
        int n = ps.Pos.Length, gens = d.MasterNodes.Length;
        ps.Clones = new Clone[n];
        if (d.HasRotationAxes) ps.RotAxis = new System.Numerics.Vector3[n];
        ps.ParentSlot = new int[gens][]; ps.MeshSlot = new int[gens][]; ps.SharedMat = new ShaderMaterial[gens][]; ps.SharedGlow = new ShaderMaterial[gens][];
        for (int g = 0; g < gens; g++)
        {
            var nodes = d.MasterNodes[g]; var mi = d.MasterMeshes[g];
            var par = ps.ParentSlot[g] = new int[nodes.Length];
            for (int k = 0; k < nodes.Length; k++) par[k] = Array.IndexOf(nodes, scene.Nodes[nodes[k]].Parent);
            var slot = ps.MeshSlot[g] = new int[mi.Length];
            for (int j = 0; j < mi.Length; j++) slot[j] = Array.IndexOf(nodes, scene.Meshes[mi[j]].Node);
            ps.SharedMat[g] = new ShaderMaterial[mi.Length]; ps.SharedGlow[g] = new ShaderMaterial[mi.Length];
        }
        ps.ClonesOk = gens > 0 && d.NormalMethod == AlignedQuad.LocalFixed && d.UpMethod == AlignedQuad.LocalFixed
                      && (!d.HasRotations || d.HasRotationAxes);
    }

    /// <summary>NiPSMeshParticleSystem::InitializeParticle (NiPSMeshParticleSystem.cpp:229-300) for the new particle i: its
    /// generation (NiRand() % generations, :240-247; STILL-OPEN: the client's NiRand sequence, every system in both data sets
    /// has one generation), then the clone: the slot's own when it is of that generation (the pool's, :249-262), else a new
    /// clone of the master (:263-266). Its controllers start at 0 (:268, AnimateUv), it takes the system's fog-off property
    /// (UpdatePropertiesDownward, :275; EffectScene.ReadMasters sets NoFog), and its current rotation is the identity (:288).
    /// STILL-OPEN: the system's other inherited properties.</summary>
    void StartClone(PSys ps, int i)
    {
        int gens = ps.Def.MasterNodes.Length;
        if (gens == 0) return;
        int g = gens > 1 ? rng.Next(gens) : 0;
        var cl = ps.Clones[i];
        if (cl == null || cl.Gen != g)
        {
            if (cl != null) foreach (var ms in cl.Meshes) ms.Node.QueueFree();
            cl = ps.Clones[i] = BuildClone(ps, g);
        }
        cl.Prev0 = System.Numerics.Vector3.UnitX; cl.Prev2 = System.Numerics.Vector3.UnitZ;
        cl.Shown = false;
    }

    /// <summary>A new clone of generation g's master: its meshes, hidden until DrawClones places them. A master mesh no
    /// controller animates draws through one material for every clone (SharedMat, made with the first), and so does its glow
    /// twin (GlowTwin, SharedGlow).</summary>
    Clone BuildClone(PSys ps, int g)
    {
        var mi = ps.Def.MasterMeshes[g];
        var cl = new Clone { Gen = g, Meshes = new MeshState[mi.Length], World = new Transform3D[ps.Def.MasterNodes[g].Length] };
        var shared = ps.SharedMat[g]; var sharedGlow = ps.SharedGlow[g];
        for (int j = 0; j < mi.Length; j++)
        {
            var em = scene.Meshes[mi[j]];
            bool anim = MeshAnimated(em);
            var ms = BuildMeshState(em, anim ? null : shared[j]);
            if (!anim && shared[j] == null) shared[j] = ms.Mat;
            var gm = GlowTwin(ms, anim ? null : sharedGlow[j]);
            if (!anim && sharedGlow[j] == null) sharedGlow[j] = gm;
            ms.Node.Visible = false;
            z.AddChild(ms.Node);
            cl.Meshes[j] = ms;
        }
        return cl;
    }

    /// <summary>
    /// The live particles' clones this frame: NiPSMeshParticleSystem::PostUpdate (NiPSMeshParticleSystem.cpp:376-436) sets
    /// each clone's translate to the particle's position, its scale to the step's (MeshAlign.Scale over the particle's radius
    /// and size, the size from the general step's keys, grow and shrink as for the quads) and its rotation to the step's
    /// (MeshAlign.Rotation, repaired by MeshAlign.Repair), then updates it at the particle's age (UpdateDownwardPass(age),
    /// :430). The clones are children of the particle container, which takes the system's world transform
    /// (UpdateWorldData, :502-509): for a world-space system that is only its scale, the rotation and translate being cleared
    /// (NiPSParticleSystem::UpdateWorldData), so the frame there is the world's axes. The step's fixed normal and up are the
    /// system's directions, in world space turned by the system's original world rotation (NiPSSimulatorMeshAlignStep.cpp:
    /// 337-346, :388; in local space as they are, :427-458), and unitized with UNIT_X and UNIT_Z. Positions here are in the
    /// effect's space, the client's times the system's scale, so the scale is multiplied by it. A clone whose particle died is
    /// hidden (the client detaches it, RemoveParticle :326-340, and keeps it pooled).
    /// STILL-OPEN: clones are drawn one node per mesh, not batched. Two paths that occur in neither data set (0 cases in
    /// MeshParticleDataTests' files): a transform controller on a master root would
    /// override the particle transform in the client's UpdateDownwardPass(age), where the particle transform always wins
    /// here; and with an unported kernel (!ClonesOk) StartClone still builds the clones that are never placed.
    /// </summary>
    void DrawClones(PSys ps, in Transform3D sysWorld, float psScale, bool hide, Vector3 camLocal)
    {
        var d = ps.Def;
        if (ps.ClonesOk)
        {
            var r = sysWorld.Basis.Orthonormalized();
            var frame = d.WorldSpace ? new Basis(worldX, worldZ.Cross(worldX), worldZ) : r;
            var nd = new Vector3((float)d.NormalDirection.X, (float)d.NormalDirection.Y, (float)d.NormalDirection.Z);
            var ud = new Vector3((float)d.UpDirection.X, (float)d.UpDirection.Y, (float)d.UpDirection.Z);
            if (d.WorldSpace) { var fi = frame.Inverse(); nd = fi * (r * nd); ud = fi * (r * ud); }
            var nrm = AlignedQuad.Unitize(N(nd), System.Numerics.Vector3.UnitX);
            var up = AlignedQuad.Unitize(N(ud), System.Numerics.Vector3.UnitZ);
            bool sizeKeys = d.SizeKeys.Length > 0, grow = d.GrowTime > 0, shrink = d.ShrinkTime > 0, rotations = d.HasRotations;
            float growTime = (float)d.GrowTime, shrinkTime = (float)d.ShrinkTime;
            for (int i = 0; i < ps.Count; i++)
            {
                var cl = ps.Clones[i];
                if (cl == null) continue;
                float age = ps.Age[i], lf = ps.Life[i];
                float n = lf > 0 ? age / lf : 1f;
                float mul = 1f;
                if (sizeKeys) mul *= (float)Lerp(d.SizeKeys, n);
                if (grow) mul *= Mathf.Clamp(age / growTime, 0f, 1f);
                if (shrink) mul *= Mathf.Clamp((lf - age) / shrinkTime, 0f, 1f);
                float scale = MeshAlign.Scale(ps.Size[i], mul) * psScale;
                MeshAlign.Rotation(nrm, up, rotations, ps.Rot[i], ps.RotAxis != null ? ps.RotAxis[i] : default, out var c0, out var c1, out var c2);
                MeshAlign.Repair(ref c0, ref c1, ref c2, cl.Prev0, cl.Prev2);
                cl.Prev0 = c0; cl.Prev2 = c2;
                var nodes = d.MasterNodes[cl.Gen]; var par = ps.ParentSlot[cl.Gen]; var slot = ps.MeshSlot[cl.Gen];
                var wd = cl.World;
                for (int k = 0; k < nodes.Length; k++)
                {
                    Transform3D w;
                    if (par[k] < 0) w = new Transform3D(frame * new Basis(G(c0) * scale, G(c1) * scale, G(c2) * scale), ps.Pos[i]);
                    else w = wd[par[k]] * Local(nodes[k], age);
                    if (scene.Nodes[nodes[k]].Billboard) w = Face(nodes[k], w, camLocal);
                    wd[k] = w;
                }
                for (int j = 0; j < cl.Meshes.Length; j++)
                {
                    var ms = cl.Meshes[j];
                    int at = slot[j] >= 0 ? slot[j] : 0;
                    Place(ms, wd[at], hide || CloneHidden(nodes, par, at, age));
                    Animate(ms, age, true);
                }
                cl.Shown = true;
            }
        }
        // the clones of the particles that died since the last frame (their slots are past the live count)
        for (int i = ps.Count; i < ps.LastCount; i++)
        {
            var cl = ps.Clones[i];
            if (cl == null || !cl.Shown) continue;
            foreach (var ms in cl.Meshes) Show(ms, false);
            cl.Shown = false;
        }
        ps.LastCount = ps.Count;
    }

    static void Swap(PSys p, int a, int b)
    {
        if (a == b) return;
        (p.Pos[a], p.Pos[b]) = (p.Pos[b], p.Pos[a]); (p.Vel[a], p.Vel[b]) = (p.Vel[b], p.Vel[a]); (p.Age[a], p.Age[b]) = (p.Age[b], p.Age[a]);
        (p.Life[a], p.Life[b]) = (p.Life[b], p.Life[a]); (p.Size[a], p.Size[b]) = (p.Size[b], p.Size[a]); (p.Rot[a], p.Rot[b]) = (p.Rot[b], p.Rot[a]);
        (p.RotSpeed[a], p.RotSpeed[b]) = (p.RotSpeed[b], p.RotSpeed[a]); (p.Variance[a], p.Variance[b]) = (p.Variance[b], p.Variance[a]);
        // a mesh particle's clone moves with it (the client moves the last particle's clone into the removed one's slot,
        // NiPSMeshParticleSystem::RemoveParticle, NiPSMeshParticleSystem.cpp:326-340)
        if (p.Clones != null) (p.Clones[a], p.Clones[b]) = (p.Clones[b], p.Clones[a]);
        if (p.RotAxis != null) (p.RotAxis[a], p.RotAxis[b]) = (p.RotAxis[b], p.RotAxis[a]);
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

    void Emit(PSys ps, EmitterDef e, in EmitterValues v, Vector3 camLocal, float psScale)
    {
        // the lifespan is drawn first; a particle already past it is not created (NiPSEmitter.cpp:139-144, its age at
        // emission is 0 here)
        float lifespan = (float)v.Lifespan + Spread((float)v.LifespanVar / 2);
        if (lifespan < 0) return;
        // a mesh emitter's pick of a null mesh ref creates no particle (NiPSMeshEmitter.cpp:103-125)
        if (e.MeshNullShare > 0 && (e.MeshNullShare >= 1 || R() < e.MeshNullShare)) return;
        int i = ps.Count++;
        ParticlesEmitted++;
        var ew = World(e.Node >= 0 ? e.Node : ps.Def.Node, t, camLocal);
        Vector3 local, normalDir = default;
        bool meshNormal = false;
        switch (e.Kind)
        {
            case EmitterKind.Box: local = new Vector3(Spread((float)e.Width / 2), Spread((float)e.Height / 2), Spread((float)e.Depth / 2)); break;
            case EmitterKind.Sphere: { Vector3 q; do q = new Vector3(Spread(1), Spread(1), Spread(1)); while (q.LengthSquared() > 1); local = q * (float)e.Radius; break; }
            case EmitterKind.Cylinder: { float a = R() * Mathf.Tau, r = Mathf.Sqrt(R()) * (float)e.Radius; local = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, Spread((float)e.Height / 2)); break; }
            case EmitterKind.Mesh when e.MeshPositions != null && e.MeshPositions.Length >= 3:
            {
                // NiPSMeshEmitter: a point on the mesh by its emission type, moved into place by the mesh's world transform
                // (TransformIntoCoordinateSpace, NiPSMeshEmitter.cpp:770-823).
                // STILL-OPEN: a skinned emitter mesh (bind-pose streams only) has no positions here, so its particles
                // start at the emitter's node; the client emits from the skinned mesh (EmitFromSkinned*,
                // NiPSMeshEmitter.cpp:440+). Only the first mesh emitter is used; the
                // client picks one at random per particle (NiPSMeshEmitter.cpp:108-118).
                var mw = e.MeshNode >= 0 ? World(e.MeshNode, t, camLocal) : Transform3D.Identity;
                meshNormal = e.SampleMesh(rng, v.DeclinationVar, v.PlanarAngleVar, out var mp, out var mn);
                ps.Pos[i] = mw * new Vector3((float)mp.X, (float)mp.Y, (float)mp.Z);
                if (meshNormal) normalDir = (mw.Basis.Orthonormalized() * new Vector3((float)mn.X, (float)mn.Y, (float)mn.Z)).Normalized();
                local = Vector3.Zero;
                goto haveMeshPos;
            }
            default: local = Vector3.Zero; break;
        }
        ps.Pos[i] = ew * local;
        haveMeshPos:
        // NiPSEmitter::ComputeDirection; speed and life spread over +-var/2 (ComputeSpeed, NiPSEmitter.cpp:139), the other
        // variances over +-var; then the speed flip (NiPSEmitter.cpp:186-193). A mesh emitter with VELOCITY_USE_NORMALS
        // takes the mesh normal (SampleMesh) and VELOCITY_USE_RANDOM a random unit vector (NiPSMeshEmitter.cpp:796-806).
        Vector3 dir;
        if (meshNormal) dir = normalDir;
        else if (e.Kind == EmitterKind.Mesh && e.MeshVelocityType == MeshVelocity.Random)
        {
            dir = new Vector3(Spread(1), Spread(1), Spread(1));
            dir = dir.LengthSquared() > 0 ? dir.Normalized() : Vector3.Up;
        }
        else
        {
            double dec = v.Declination + Spread((float)v.DeclinationVar), pl = v.PlanarAngle + Spread((float)v.PlanarAngleVar);
            var dl = EmitterDef.Direction(dec, pl, ps.Def.PreRpi);
            dir = (ew.Basis.Orthonormalized() * new Vector3((float)dl.X, (float)dl.Y, (float)dl.Z)).Normalized();
        }
        float speed = (float)v.Speed + Spread((float)v.SpeedVar / 2);
        if (v.SpeedFlipRatio > 0 && R() < v.SpeedFlipRatio) speed = -speed;
        ps.Vel[i] = dir * AlignedQuad.SpawnSpeed(speed, psScale);
        ps.Life[i] = lifespan;
        ps.Size[i] = Mathf.Max(0f, (float)v.Size + Spread((float)v.SizeVar));
        ps.Age[i] = 0;
        ps.Rot[i] = (float)v.RotAngle + Spread((float)v.RotAngleVar);
        float rs = (float)v.RotSpeed + Spread((float)v.RotSpeedVar);
        if (e.RandomRotSpeedSign && R() < 0.5f) rs = -rs;
        ps.RotSpeed[i] = rs;
        if (ps.RotAxis != null)
        {
            // the emitter's axis, or a random unit one (NiPSEmitter.cpp:248-260)
            var ax = new System.Numerics.Vector3((float)e.RotAxis.X, (float)e.RotAxis.Y, (float)e.RotAxis.Z);
            if (e.RandomRotAxis)
            {
                float phi = R() * MathF.PI, zc = MathF.Cos(phi), h = MathF.Sqrt(1f - zc * zc), th = R() * 2f * MathF.PI;
                ax = new System.Numerics.Vector3(h * MathF.Cos(th), h * MathF.Sin(th), zc);
            }
            ps.RotAxis[i] = ax;
        }
        ps.Variance[i] = Spread(1f);        // NiSymmetricRandom (NiPSParticleSystem.cpp:262-265)
        if (ps.Clones != null) StartClone(ps, i);
    }
}
