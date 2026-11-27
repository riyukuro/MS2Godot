using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using Ms2.Formats.Model;
using Ms2Godot.Net;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Render check (res://tests/alpha_render.tscn, no game data; needs a GPU, not headless): the opacity a character mesh and
/// the glow pass test and blend, read back as pixels. Each case builds synthetic quads through ModelBuilder.Build (the
/// character path, with its glow twin and its Attached step) or GlowMaterial (the field glow), draws them in a SubViewport
/// of its own over black, and compares the centre pixel with the value the client's opacity gives:
/// the NiAlphaController alpha, the vertex alpha (EMISSIVE + LIGHTING_E, AMB_DIFF + E_A_D), the test on the opacity, ONE / ONE
/// unweighted, the z write of a blended mesh, DRAW_BOTH, the blink's node alpha (NodeFade), MS2AlphaBoostMaterial's saturated output, the glow pass's opacity, the closest-point alpha sort, and an
/// effect mesh's unsaturated colour (EffectInstance). Each view converts through Ms2Bloom.GammaOut(), as the game's views without
/// bloom do.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class AlphaRenderCheck : Node
{
    const int Size = 32;
    readonly List<(string Name, SubViewport Vp, Func<Color, string> Check)> cases = new();
    readonly List<string> fails = new();
    ModelBuilder mb;
    ImageTexture white;

    /// <summary>A quad of half size <paramref name="h"/> at file z <paramref name="z"/>, facing +z (file winding CCW from +z),
    /// or facing away with <paramref name="back"/>.</summary>
    static ModelMesh Quad(string name, float z, float h = 1f, bool back = false, float vertexAlpha = -1f, float[] extra = null)
    {
        var pos = new List<float> { -h, -h, z, h, -h, z, h, h, z, -h, h, z };
        var idx = new List<int> { 0, 1, 2, 0, 2, 3 };
        if (back) idx = new List<int> { 0, 2, 1, 0, 3, 2 };
        if (extra != null)
        {
            // a far tiny triangle off screen: moves the mesh's AABB centre (Godot's own sort key) without drawing anything seen
            int b = pos.Count / 3;
            pos.AddRange(extra);
            idx.AddRange(new[] { b, b + 1, b + 2 });
        }
        int nv = pos.Count / 3;
        var nrm = new float[nv * 3];
        for (int i = 0; i < nv; i++) nrm[i * 3 + 2] = back ? -1 : 1;
        var m = new ModelMesh
        {
            Name = name, FileName = name, Positions = pos.ToArray(), Normals = nrm, Indices = idx.ToArray(),
            Ambient = new float[] { 0, 0, 0 }, Diffuse = new float[] { 0, 0, 0 }, Emissive = new float[] { 1, 1, 1 },
        };
        if (vertexAlpha >= 0)
        {
            m.Colors = new float[nv * 4];
            for (int i = 0; i < nv; i++) { m.Colors[i * 4] = m.Colors[i * 4 + 1] = m.Colors[i * 4 + 2] = 1; m.Colors[i * 4 + 3] = vertexAlpha; }
        }
        return m;
    }

    static ModelMesh Emit(ModelMesh m, float r, float g, float b) { m.Emissive = new[] { r, g, b }; return m; }

    SubViewport View(uint mask)
    {
        var vp = new SubViewport { Size = new Vector2I(Size, Size), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Msaa3D = Viewport.Msaa.Disabled };
        vp.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black } });
        AddChild(vp);
        vp.AddChild(new Camera3D { Name = "Cam", CullMask = mask, Current = true, Fov = 40, Compositor = Ms2Bloom.GammaOut() });   // the game's gamma conversion for views without bloom
        return vp;
    }

    /// <summary>Builds the meshes as one model (ModelBuilder.Build) in a new viewport; the camera sits at file (0,0,10)
    /// looking down file -z.</summary>
    Node3D Model(string name, uint mask, Func<Color, string> check, params ModelMesh[] meshes)
    {
        var md = new ModelData { RootLocal = Xform.Identity };
        md.Meshes.AddRange(meshes);
        var vp = View(mask);
        var root = mb.Build(md, name);
        vp.AddChild(root);
        var model = root.GetNode<Node3D>("Model");
        vp.GetNode<Camera3D>("Cam").GlobalTransform = model.GlobalTransform * new Transform3D(Basis.Identity, new Vector3(0, 0, 10));
        cases.Add((name, vp, check));
        return root;
    }

    /// <summary>The meshes as field statics (BuildMesh with the instance colour: field_lit), one MultiMesh instance each
    /// with no MaterialColor (FieldBuilder.InstanceColor: alpha 0, the material's own ambient and diffuse), in a new
    /// viewport; the camera sits at (0,0,10) looking down -z.</summary>
    void Field(string name, Func<Color, string> check, params ModelMesh[] meshes) => Field(name, 1u, check, meshes);

    void Field(string name, uint mask, Func<Color, string> check, params ModelMesh[] meshes)
    {
        var vp = View(mask);
        foreach (var m in meshes)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mb.BuildMesh(m, skin: false, instanceColor: true), InstanceCount = 1 };
            mm.SetInstanceTransform(0, Transform3D.Identity);
            mm.SetInstanceColor(0, new Color(0, 0, 0, 0));
            vp.AddChild(new MultiMeshInstance3D { Multimesh = mm });
        }
        vp.GetNode<Camera3D>("Cam").Position = new Vector3(0, 0, 10);
        cases.Add((name, vp, check));
    }

    /// <summary>One mesh played by the effect runtime (EffectInstance), blended (0xED) at the material alpha, in a new
    /// viewport; the camera sits at file (0,0,10) looking down file -z.</summary>
    void Effect(string name, Func<Color, string> check, ModelMesh m, float alpha)
    {
        m.AlphaFlags = 0xED; m.MaterialAlpha = alpha;
        var sc = new EffectScene { Duration = 10 };
        sc.Nodes.Add(new EffectNode { Name = "n" });
        sc.Meshes.Add(new EffectMesh { Mesh = m, Node = 0 });
        var vp = View(1u);
        var fx = new EffectInstance(sc, mb, 10, 1) { Name = name };
        vp.AddChild(fx);
        fx.FileSpace = Transform3D.Identity;
        vp.GetNode<Camera3D>("Cam").GlobalTransform = fx.GetChild<Node3D>(0).GlobalTransform * new Transform3D(Basis.Identity, new Vector3(0, 0, 10));
        cases.Add((name, vp, check));
    }

    /// <summary>One still particle at the effect's origin, shaded by <paramref name="m"/> as the system's material (blended
    /// 0xED at the material alpha), in a new viewport; the camera as in <see cref="Effect"/>.</summary>
    void Particle(string name, Func<Color, string> check, ModelMesh m, float alpha)
    {
        m.AlphaFlags = 0xED; m.MaterialAlpha = alpha;
        var sc = new EffectScene { Duration = 10 };
        sc.Nodes.Add(new EffectNode { Name = "n" });
        var sys = new ParticleSystemDef { Node = 0, Name = "p", AlphaFlags = 0xED, MaxParticles = 1, Material = new EffectMesh { Mesh = m, Node = 0 } };
        sys.Emitters.Add(new EmitterDef { Kind = EmitterKind.Box, Name = "e", Node = 0, Size = 20, Lifespan = 100, Rate = _ => 100, RateCtl = new TimeCtl() });
        sc.Systems.Add(sys);
        var vp = View(1u);
        var fx = new EffectInstance(sc, mb, 10, 1) { Name = name };
        vp.AddChild(fx);
        fx.FileSpace = Transform3D.Identity;
        vp.GetNode<Camera3D>("Cam").GlobalTransform = fx.GetChild<Node3D>(0).GlobalTransform * new Transform3D(Basis.Identity, new Vector3(0, 0, 10));
        cases.Add((name, vp, check));
    }

    // the read-back value in the space the blend runs in (calibrated: an opaque emissive 0.5 quad)
    bool srgbReadback;
    float Lin(float v) => srgbReadback ? (v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f)) : v;
    Vector3 L(Color c) => new(Lin(c.R), Lin(c.G), Lin(c.B));

    static string Near(Vector3 got, Vector3 want, string what) =>
        (got - want).Abs() is var d && Mathf.Max(d.X, Mathf.Max(d.Y, d.Z)) <= 0.04f ? null : $"{what}: got {got}, want {want}";

    Func<Color, string> Grey(float want, string what) => c => Near(L(c), new Vector3(want, want, want), what);
    Func<Color, string> Rgb(float r, float g, float b, string what) => c => Near(L(c), new Vector3(r, g, b), what);

    public override async void _Ready()
    {
        mb = new ModelBuilder(null, null);
        var img = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
        img.Fill(Colors.White);
        white = ImageTexture.CreateFromImage(img);
        // ModelBuilder reads textures from the packs; the glow cases preload theirs into its cache (no game data here)
        var cache = (Dictionary<string, Texture2D>)typeof(ModelBuilder).GetField("textures", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(mb);
        cache["glow.dds"] = white;
        GlowPass.Wanted = true;
        RenderingServer.GlobalShaderParameterSet("ms2_hfog", Vector3.Zero);

        const uint main = 1u, glow = GlowPass.Bit;

        // calibration: opaque emissive 0.5 and 1 (the readback's space), and the opaque glow
        Model("calib_half", main, null, Emit(Quad("q", 0), 0.5f, 0.5f, 0.5f));
        Model("calib_full", main, Grey(1, "opaque emissive 1"), Quad("q", 0));

        // BLOCKER 2: the NiAlphaController drives the opacity (constant 0.5 here) over a material alpha of 0
        var anim = Quad("q", 0);
        anim.AlphaFlags = 0xED; anim.MaterialAlpha = 0;
        anim.AlphaAnim = new AlphaAnim { Start = 0, Stop = 1, KeyType = 1, Times = new[] { 0f, 1f }, Values = new[] { 0.5f, 0.5f } };
        Model("anim_alpha", main, Grey(0.5f, "alpha controller 0.5 over material alpha 0"), anim);

        // BLOCKER 3: EMISSIVE + LIGHTING_E (vcf 16), additive SRCALPHA / ONE (0x100D): the vertex alpha 0.25 is the opacity
        var emi = Quad("q", 0, vertexAlpha: 0.25f);
        emi.AlphaFlags = 0x100D; emi.VertexColorFlags = 0x10;
        Model("vc_emissive_e", main, Grey(0.25f, "EMISSIVE + LIGHTING_E vertex alpha 0.25, additive"), emi);

        // BLOCKER 3: AMB_DIFF + E_A_D (vcf 40), SRCALPHA / INVSRCALPHA: the vertex alpha 0.25 is the opacity
        var ad = Quad("q", 0, vertexAlpha: 0.25f);
        ad.AlphaFlags = 0xED; ad.VertexColorFlags = 0x28;
        // a type 1 name keeps it on the character light, which saturates its output before the blend as the client's
        // 8-bit scene target does, so the lit colour over 1 blends as 1; a mesh with no name draws with
        // NiStandardMaterial (ModelData.LightForm), unsaturated, so its lit vertex colour sums above 1
        ad.Material = "MS2CharacterMaterial";
        Model("vc_ambdiff_ead", main, Grey(0.25f, "AMB_DIFF + E_A_D vertex alpha 0.25"), ad);

        // MINOR 2: the test runs on the opacity: material alpha 0.4 is not GREATER than 128/255, so the pixel is clipped
        var cut = Quad("q", 0);
        cut.AlphaFlags = 0x200 | 4 << 10; cut.AlphaThreshold = 128; cut.MaterialAlpha = 0.4f;
        Model("test_opacity", main, Grey(0, "GREATER 128 on material alpha 0.4 clips"), cut);

        // BLOCKER 6: ONE / ONE adds the colour unweighted (emissive 0.5 at material alpha 0.3 adds 0.5, not 0.15)
        var one = Emit(Quad("q", 0), 0.5f, 0.5f, 0.5f);
        one.AlphaFlags = 1; one.ZFlags = 1; one.MaterialAlpha = 0.3f;
        Model("one_one", main, Grey(0.5f, "ONE / ONE emissive 0.5 at alpha 0.3"), one);

        // BLOCKER 4: a blended mesh with the z write bit writes depth. The near half-alpha white quad draws first (its AABB
        // centre is far behind, and no model bound keeps it out of the alpha sort); the far red quad behind it then fails the
        // depth test: half white over black, no red
        var near = Quad("near", 5, extra: new float[] { 30, 30, -45, 30.1f, 30, -45, 30, 30.1f, -45 });
        near.AlphaFlags = 0xED; near.MaterialAlpha = 0.5f;
        var far = Emit(Quad("far", 0), 1, 0, 0);
        far.AlphaFlags = 0xED; far.ZFlags = 1;
        Model("zwrite", main, Rgb(0.5f, 0.5f, 0.5f, "blended z write occludes the later far quad"), near, far);

        // MINOR 1: DRAW_BOTH (draw mode 3) draws a blended mesh's back faces
        var both = Quad("q", 0, back: true);
        both.AlphaFlags = 0xED; both.DrawMode = 3;
        Model("draw_both", main, Grey(1, "DRAW_BOTH back face drawn"), both);

        // the invincible blink (NodeFade, SetNodeAlpha alphaanimation.cpp:478-545): an opaque white quad at factor 0.5 blends
        // half over black; 1 forced restores it opaque; the alpha test is left as it is, so GREATER 128 at 0.4 clips
        NodeFade.Set(Model("blink_half", main, Grey(0.5f, "opaque mesh at node alpha 0.5"), Quad("q", 0)), 0.5f, false);
        var restored = Model("blink_restored", main, Grey(1, "node alpha restored to 1"), Quad("q", 0));
        NodeFade.Set(restored, 0.5f, false);
        NodeFade.Set(restored, 1f, true);
        var fcut = Quad("q", 0);
        fcut.AlphaFlags = 0x200 | 4 << 10; fcut.AlphaThreshold = 128;
        NodeFade.Set(Model("blink_test", main, Grey(0, "GREATER 128 on node alpha 0.4 clips"), fcut), 0.4f, false);

        // BLOCKER 1: the glow pass tests the colour pass's opacity: climbpc's 0x12ED (GREATER 0) at material alpha 0 is clipped
        var gcut = Quad("q", 0);
        gcut.AlphaFlags = 0x12ED; gcut.MaterialAlpha = 0; gcut.Textures["glow"] = "glow.dds";
        Model("glow_test", glow, Grey(0, "glow of a mesh clipped by its opacity"), gcut);

        // BLOCKER 1: and blends with it (SRCALPHA / INVSRCALPHA, material alpha 0.5)
        var gblend = Quad("q", 0);
        gblend.AlphaFlags = 0xED; gblend.MaterialAlpha = 0.5f; gblend.Textures["glow"] = "glow.dds";
        Model("glow_blend", glow, Grey(0.5f, "glow blended at material alpha 0.5"), gblend);

        // BLOCKER 1: a field mesh's glow takes field_lit's opacity: the AMB_DIFF + E_A_D vertex alpha 0.25
        {
            var fm = Quad("q", 0, vertexAlpha: 0.25f);
            fm.AlphaFlags = 0xED; fm.VertexColorFlags = 0x28; fm.Textures["glow"] = "glow.dds";
            var vp = View(main);
            vp.AddChild(new MeshInstance3D { Mesh = mb.BuildMesh(fm, instanceColor: true), MaterialOverride = mb.GlowMaterial(fm, false) });
            vp.GetNode<Camera3D>("Cam").Position = new Vector3(0, 0, 10);
            cases.Add(("field_glow_vc", vp, Grey(0.25f, "field glow at vertex alpha 0.25")));
        }

        // MINOR 5: the field glow runs field_lit's alpha test (MS2GlowMaterial ends in HandleMS2FinalPixelOutputs ->
        // HandleAlphaTest, ms2standardmaterial.cpp:786): GREATER 128 passes material alpha 0.6 (opaque, no blend bit) and clips 0.4
        foreach (var (name, ma, want) in new[] { ("field_glow_test_pass", 0.6f, 1f), ("field_glow_test_clip", 0.4f, 0f) })
        {
            var fm = Quad("q", 0);
            fm.AlphaFlags = 0x200 | 4 << 10; fm.AlphaThreshold = 128; fm.MaterialAlpha = ma; fm.Textures["glow"] = "glow.dds";
            var vp = View(main);
            vp.AddChild(new MeshInstance3D { Mesh = mb.BuildMesh(fm, instanceColor: true), MaterialOverride = mb.GlowMaterial(fm, false) });
            vp.GetNode<Camera3D>("Cam").Position = new Vector3(0, 0, 10);
            cases.Add((name, vp, Grey(want, $"field glow, GREATER 128 at material alpha {ma}")));
        }

        // a static entity's glow holds its alpha controller at the entity's add time, as field_lit does (frozen_time):
        // the keys give 0.25 only in a 0.02 s spike at t = 1 of a 2 s loop, so the scene time reads 1 almost always
        {
            var fm = Quad("q", 0);
            fm.AlphaFlags = 0xED; fm.MaterialAlpha = 0; fm.Textures["glow"] = "glow.dds";
            fm.AlphaAnim = new AlphaAnim { Start = 0, Stop = 2, Frequency = 1, Phase = 0, KeyType = 1,
                Times = new[] { 0f, 0.99f, 1f, 1.01f, 2f }, Values = new[] { 1f, 1f, 0.25f, 1f, 1f } };
            var vp = View(main);
            vp.AddChild(new MeshInstance3D { Mesh = mb.BuildMesh(fm, instanceColor: true), MaterialOverride = mb.GlowMaterial(fm, false, frozenAt: 1f) });
            vp.GetNode<Camera3D>("Cam").Position = new Vector3(0, 0, 10);
            cases.Add(("field_glow_frozen", vp, Grey(0.25f, "field glow alpha held at its frozen time")));
        }

        // BLOCKER 5: the closest-point key (centre . view - radius). The big-bound red quad (bound radius 8 at distance 10:
        // key 2) draws after the small-bound green quad (radius 0.5 at distance 5: key 4.5), so red shows on top; Godot's
        // own key (AABB centre distance 10 against 5) draws green last
        var big = Emit(Quad("big", 0), 1, 0, 0);
        big.AlphaFlags = 0xED; big.ZFlags = 1; big.Bound = new float[] { 0, 0, 0, 8 };
        var small = Emit(Quad("small", 5), 0, 1, 0);
        small.AlphaFlags = 0xED; small.ZFlags = 1; small.Bound = new float[] { 0, 0, 5, 0.5f };
        Model("sort_closest", main, Rgb(1, 0, 0, "closest-point sort draws the big-bound quad last"), big, small);

        // the effect mesh's colour is not saturated before the blend (CompositeFinalRGBAColor into the FP16 target): emissive
        // (1.6, 1, 2) at material alpha 0.5, SRCALPHA / INVSRCALPHA over black, gives (0.8, 0.5, 1), not the clamped 0.5 grey
        Effect("fx_unclamped", Rgb(0.8f, 0.5f, 1f, "effect emissive (1.6, 1, 2) at alpha 0.5"), Emit(Quad("q", 0), 1.6f, 1f, 2f), 0.5f);
        // the effect mesh's coefficient reaches the shader as it is: emissive 0.5 at alpha 1 reads 0.5, as the field mesh's
        // calib_half does (set as a Color, Godot decoded it from sRGB to 0.21)
        Effect("fx_tint_gamma", Grey(0.5f, "effect emissive 0.5 at alpha 1"), Emit(Quad("q", 0), 0.5f, 0.5f, 0.5f), 1f);
        // nor is the opacity: material alpha 1.5 (the crystal ring's controller peak) weighs emissive (0.4, 0.2, 0.3) by 1.5
        // over black, (0.6, 0.3, 0.45), where a saturated opacity gives the emissive as it is
        Effect("fx_opacity_over_1", Rgb(0.6f, 0.3f, 0.45f, "effect emissive (0.4, 0.2, 0.3) at alpha 1.5"), Emit(Quad("q", 0), 0.4f, 0.2f, 0.3f), 1.5f);
        // MINOR 4: the particle quads draw through the same standard material (SoftParticlesMaterial), so neither their colour
        // nor their opacity is saturated either
        Particle("fx_part_unclamped", Rgb(0.8f, 0.5f, 1f, "particle emissive (1.6, 1, 2) at alpha 0.5"), Emit(Quad("q", 0), 1.6f, 1f, 2f), 0.5f);
        Particle("fx_part_opacity_over_1", Rgb(0.6f, 0.3f, 0.45f, "particle emissive (0.4, 0.2, 0.3) at alpha 1.5"), Emit(Quad("q", 0), 0.4f, 0.2f, 0.3f), 1.5f);

        // MS2AlphaBoostMaterial (rgb *= opacity where it is above 1, HandleMS2FinalPixelOutputs ms2standardmaterial.cpp:774-790)
        // into the client's A8R8G8B8 scene target (CreateRenderTargetSurface @0x1416a3cd0): the boosted output saturates
        // before the blend, so an opacity of 1.5 blends as 1 and a boosted colour above 1 stores 1 (captures/02000299_-450_0_1800_f299,
        // the dome glass). Character path (ModelBuilder.Build, character_lit) and field path (BuildMesh instanceColor, field_lit):
        // emissive 0.2 at 1.5 over an opaque white quad replaces it with 0.3 (unsaturated: 0.3 * 1.5 - 0.5 * 1 = 0); emissive 0.8
        // at 1.5 over black stores 1, and a black layer at alpha 0.5 over it leaves 0.5 (unsaturated: 1.8 * 0.5 = 0.9)
        ModelMesh Boosted(string name, float z, float e, float[] bound)
        {
            var m = Emit(Quad(name, z), e, e, e);
            m.AlphaFlags = 0xED; m.ZFlags = 1; m.MaterialAlpha = 1.5f; m.AlphaBoost = true; m.Bound = bound;
            return m;
        }
        ModelMesh Layer()
        {
            var m = Emit(Quad("layer", 1), 0, 0, 0);
            m.AlphaFlags = 0xED; m.ZFlags = 1; m.MaterialAlpha = 0.5f; m.Bound = new float[] { 0, 0, 1, 1.5f };
            return m;
        }
        Model("boost_alpha_sat", main, Grey(0.3f, "boosted opacity 1.5 blends as 1"),
            Quad("bg", 0, 2), Boosted("q", 0.5f, 0.2f, new float[] { 0, 0, 0.5f, 1.5f }));
        Model("boost_rgb_sat", main, Grey(0.5f, "boosted colour 1.2 stores 1 under a half black layer"),
            Boosted("q", 0, 0.8f, new float[] { 0, 0, 0, 1.5f }), Layer());
        Field("field_boost_alpha_sat", Grey(0.3f, "field: boosted opacity 1.5 blends as 1"),
            Quad("bg", 0, 2), Boosted("q", 0.5f, 0.2f, null));
        Field("field_boost_rgb_sat", Grey(0.5f, "field: boosted colour 1.2 stores 1 under a half black layer"),
            Boosted("q", 0, 0.8f, null), Layer());

        // the client's fog-off subtrees (ModelMesh.NoFog, FUN_141567b60 @0x141567b60): the depth fog at its full share (near 0,
        // far 1, the quad 10 away) to black, through a camera that fogs (Field.FogLayer, 1 << 20); a NoFog mesh keeps its
        // emissive 1. Character path and field path. The other cases' cameras carry no fog layer.
        RenderingServer.GlobalShaderParameterSet("ms2_fog", new Vector4(0, 1, 0, 0));
        RenderingServer.GlobalShaderParameterSet("ms2_fog_color", Vector3.Zero);
        const uint fogged = main | 1u << 20;
        static ModelMesh NoFog(ModelMesh m) { m.NoFog = true; return m; }
        Model("fog_on", fogged, Grey(0, "full depth fog to black"), Quad("q", 0));
        Model("fog_off", fogged, Grey(1, "a NoFog mesh draws without the depth fog"), NoFog(Quad("q", 0)));
        Field("field_fog_on", fogged, Grey(0, "field: full depth fog to black"), Quad("q", 0));
        Field("field_fog_off", fogged, Grey(1, "field: a NoFog mesh draws without the depth fog"), NoFog(Quad("q", 0)));
        // fog, then saturate, then blend (PS 0x42a9d9d0's depth-fog mad before the A8R8G8B8 write): a blended type-0 quad
        // 0.5 from the camera (a share of 0.5 under the same fog), emissive 3 at alpha 0.5 over black stores sat(1.5) * 0.5
        // = 0.5; the fog after the saturate, or none, gives 0.75
        var hot = Mesh(Emit(Quad("q", 9.5f, 0.1f), 3, 3, 3), 0xED);
        hot.Material = "MS2StandardMaterial"; hot.MaterialAlpha = 0.5f;
        Model("fog_saturate_blend", fogged, Grey(0.5f, "a blended colour fogged before it saturates"), hot);

        // the shadow of a blended mesh, with or without the z write: it stops casting and a shadow-only twin with the opaque form casts in its place
        var castRoot = Model("caster", main, null, Mesh(Quad("blend_zw", 0), 0xED), Mesh(Quad("blend_nozw", 0, 0.5f), 0xED, 1));
        var skel = castRoot.GetNode("Model/" + ModelBuilder.SkeletonName);
        var zw = skel.GetNode<MeshInstance3D>("blend_zw");
        var tw = zw.GetNodeOrNull<MeshInstance3D>("blend_zw_shadow");
        if (zw.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off) fails.Add("blended z-write mesh still casts from its transparent material");
        if (tw == null || tw.CastShadow != GeometryInstance3D.ShadowCastingSetting.ShadowsOnly || tw.Mesh != zw.Mesh
            || tw.MaterialOverride is not ShaderMaterial tm || tm.Shader.Code.Contains("#define BLEND") || !tm.Shader.Code.Contains("render_mode unshaded, cull_back;"))
            fails.Add("no opaque shadow-only twin of the blended z-write mesh");
        // the shadow write ignores the z-write bit (SetShadowCaster @0x141567330 marks every render object): the mesh without it casts the same way
        var nz = skel.GetNode<MeshInstance3D>("blend_nozw");
        var tn = nz.GetNodeOrNull<MeshInstance3D>("blend_nozw_shadow");
        if (nz.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off || tn == null || tn.CastShadow != GeometryInstance3D.ShadowCastingSetting.ShadowsOnly
            || tn.Mesh != nz.Mesh || tn.MaterialOverride is not ShaderMaterial tnm || tnm.Shader.Code.Contains("#define BLEND"))
            fails.Add("no opaque shadow-only twin of the blended mesh without the z write");

        // ShadowCullingProcess (@0x141753590) drops a blended mesh whose material alpha is not 1: at material alpha 0.3 it
        // casts nothing, no twin
        var faint = Mesh(Quad("blend_faint", 0), 0xED);
        faint.MaterialAlpha = 0.3f;
        var fr = Model("caster_faint", main, null, faint);
        var fmi = fr.GetNode("Model/" + ModelBuilder.SkeletonName).GetNode<MeshInstance3D>("blend_faint");
        if (fmi.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off || fmi.GetNodeOrNull("blend_faint_shadow") != null)
            fails.Add("the blended mesh at material alpha 0.3 still casts");
        // the twin's opacity is the base map's alpha alone (NiDirectionalShadowWriteMaterial.cpp:192-229): a blended mesh at
        // material alpha 1, faint through its alpha controller and vertex alpha, casts a full shadow, cut only by the base map
        var dim = Mesh(Quad("blend_dim", 0, vertexAlpha: 0.3f), 0xED);
        dim.VertexColorFlags = 0x28;
        dim.AlphaAnim = new AlphaAnim { Start = 0, Stop = 1, KeyType = 1, Times = new[] { 0f, 1f }, Values = new[] { 0.3f, 0.3f } };
        var dr = Model("caster_dim", main, null, dim);
        var dmi = dr.GetNode("Model/" + ModelBuilder.SkeletonName).GetNode<MeshInstance3D>("blend_dim");
        var dt = dmi.GetNodeOrNull<MeshInstance3D>("blend_dim_shadow");
        if (dmi.Mesh?.SurfaceGetMaterial(0) is not ShaderMaterial dsm || dsm.GetShaderParameter(ShaderParam.AlphaAnimCount).AsInt32() != 2
            || !dsm.GetShaderParameter(ShaderParam.VcOpacity).AsBool())
            fails.Add("the dim blended mesh's colour pass lacks its alpha controller or vertex alpha");
        if (dt?.MaterialOverride is not ShaderMaterial dtm) fails.Add("no shadow twin of the dim blended mesh");
        else if (dtm.GetShaderParameter(ShaderParam.MatAlpha).AsSingle() != 1f || dtm.GetShaderParameter(ShaderParam.AlphaAnimCount).AsInt32() != 0
                 || dtm.GetShaderParameter(ShaderParam.VcOpacity).AsBool())
            fails.Add($"shadow twin keeps the colour pass's opacity terms (alpha anim keys {dtm.GetShaderParameter(ShaderParam.AlphaAnimCount)})");

        for (int i = 0; i < 4; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Color Px(SubViewport vp) => vp.GetTexture().GetImage().GetPixel(Size / 2, Size / 2);
        float half = Px(cases[0].Vp).R;
        srgbReadback = Mathf.Abs(half - 0.5f) > 0.05f;
        GD.Print($"alpha_render: calibration emissive 0.5 reads {half:F3} ({(srgbReadback ? "sRGB" : "linear")} readback)");
        foreach (var (name, vp, check) in cases)
        {
            if (check == null) continue;
            var c = Px(vp);
            var err = check(c);
            GD.Print($"alpha_render: {name} {L(c)} {(err == null ? "ok" : "FAIL")}");
            if (err != null) fails.Add($"{name}: {err}");
        }
        // each view converts through the game's Ms2Bloom.GammaOut(), not GammaGuard's environment fallback (an error per view)
        foreach (var (name, vp, _) in cases)
        {
            var cam = vp.GetNodeOrNull<Camera3D>("Cam");
            bool own = false;
            if (cam?.Compositor?.CompositorEffects is { } effects)
                foreach (var e in effects) own |= e is Ms2Bloom { Ready: true, Failed: false };
            if (!own) fails.Add($"{name}: the view has no working Ms2Bloom conversion (GammaGuard falls back)");
        }
        foreach (var f in fails) GD.PrintErr("FAIL " + f);
        GD.Print(fails.Count == 0 ? "alpha_render: ALL OK" : $"alpha_render: {fails.Count} FAILED");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    static ModelMesh Mesh(ModelMesh m, int alphaFlags, int zFlags = 15) { m.AlphaFlags = alphaFlags; m.ZFlags = zFlags; return m; }
}
