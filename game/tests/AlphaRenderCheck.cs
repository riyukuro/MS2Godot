using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using Ms2.Formats.Model;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Render check (res://tests/alpha_render.tscn, no game data; needs a GPU, not headless): the opacity a character mesh and
/// the glow pass test and blend, read back as pixels. Each case builds synthetic quads through ModelBuilder.Build (the
/// character path, with its glow twin and its Attached step) or GlowMaterial (the field glow), draws them in a SubViewport
/// of its own over black, and compares the centre pixel with the value the client's opacity gives:
/// the NiAlphaController alpha, the vertex alpha (EMISSIVE + LIGHTING_E, AMB_DIFF + E_A_D), the test on the opacity, ONE / ONE
/// unweighted, the z write of a blended mesh, DRAW_BOTH, the glow pass's opacity, and the closest-point alpha sort.
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
        vp.AddChild(new Camera3D { Name = "Cam", CullMask = mask, Current = true, Fov = 40 });
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

        // BLOCKER 5: the closest-point key (centre . view - radius). The big-bound red quad (bound radius 8 at distance 10:
        // key 2) draws after the small-bound green quad (radius 0.5 at distance 5: key 4.5), so red shows on top; Godot's
        // own key (AABB centre distance 10 against 5) draws green last
        var big = Emit(Quad("big", 0), 1, 0, 0);
        big.AlphaFlags = 0xED; big.ZFlags = 1; big.Bound = new float[] { 0, 0, 0, 8 };
        var small = Emit(Quad("small", 5), 0, 1, 0);
        small.AlphaFlags = 0xED; small.ZFlags = 1; small.Bound = new float[] { 0, 0, 5, 0.5f };
        Model("sort_closest", main, Rgb(1, 0, 0, "closest-point sort draws the big-bound quad last"), big, small);

        // the shadow of a blended z-writing mesh: it stops casting and a shadow-only twin with the opaque form casts in its place
        var castRoot = Model("caster", main, null, Mesh(Quad("blend_zw", 0), 0xED), Mesh(Quad("blend_nozw", 0, 0.5f), 0xED, 1));
        var skel = castRoot.GetNode("Model/" + ModelBuilder.SkeletonName);
        var zw = skel.GetNode<MeshInstance3D>("blend_zw");
        var tw = zw.GetNodeOrNull<MeshInstance3D>("blend_zw_shadow");
        if (zw.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off) fails.Add("blended z-write mesh still casts from its transparent material");
        if (tw == null || tw.CastShadow != GeometryInstance3D.ShadowCastingSetting.ShadowsOnly || tw.Mesh != zw.Mesh
            || tw.MaterialOverride is not ShaderMaterial tm || tm.Shader.Code.Contains("#define BLEND") || !tm.Shader.Code.Contains("render_mode unshaded, cull_back;"))
            fails.Add("no opaque shadow-only twin of the blended z-write mesh");
        if (skel.GetNode<MeshInstance3D>("blend_nozw").GetChildCount() != 0) fails.Add("a twin for a mesh without the z write");

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
        foreach (var f in fails) GD.PrintErr("FAIL " + f);
        GD.Print(fails.Count == 0 ? "alpha_render: ALL OK" : $"alpha_render: {fails.Count} FAILED");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    static ModelMesh Mesh(ModelMesh m, int alphaFlags, int zFlags = 15) { m.AlphaFlags = alphaFlags; m.ZFlags = zFlags; return m; }
}
