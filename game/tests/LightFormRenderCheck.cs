using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using Ms2.Formats.Model;
using Ms2Godot.Ui;

namespace Ms2Godot.Tests;

/// <summary>
/// Render check (res://tests/light_form_render.tscn, no game data; needs a GPU, not headless): the light form a character
/// mesh draws with (character_lit's light_form, from the mesh's active material), read back as pixels. Each case builds one
/// synthetic quad through ModelBuilder.Build, draws it in a SubViewport of its own over black, and compares the centre pixel
/// with the value the client's form gives under one field light:
/// <list type="bullet">
/// <item>type 0 (MS2StandardMaterial): HandleMS2Light's MS2Light, N.L with the light's ambient by 1 - N.L, not clamped;</item>
/// <item>no material: the renderer's default NiStandardMaterial, the light's ambient into the ambient accumulator times the
/// material ambient, not clamped;</item>
/// <item>types 1-3 (MS2CharacterMaterial): the character light from the camera, clamped;</item>
/// <item>MS2AlphaBoostMaterial (the swap of a blended mesh at an opacity above 1): MS2Light, the colour times the opacity;</item>
/// <item>an own light pair (PortraitView.OwnLights): type 0 takes the pair's direction, not the field's.</item>
/// </list>
/// The quad faces Godot +z (file -y) at the origin; the camera looks at it from +z, so the view vector is the normal.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class LightFormRenderCheck : Node
{
    const int Size = 32;
    readonly List<(string Name, SubViewport Vp, Func<Color, string> Check)> cases = new();
    readonly List<string> fails = new();
    ModelBuilder mb;

    // one field light: the sun at N.L = 0.5 on the quad, diffuse 0.6, ambient 0.4, no scene ambient, no specular, no point lights
    const float SunColor = 0.6f, SunAmbient = 0.4f, Ndl = 0.5f;
    // the character light from a level camera: the camera direction tilted 30 degrees down, N.L = cos 30 on the quad
    static readonly float HalfLambert = Mathf.Pow(0.5f * Mathf.Cos(Mathf.DegToRad(30)) + 0.5f, 2f);

    /// <summary>A quad of half size 1 in the file's x-z plane, facing file -y (Godot +z).</summary>
    static ModelMesh Quad(string material, float ambient = 1f, float emissive = 0f, string tex = null)
    {
        var m = new ModelMesh
        {
            Name = "q", FileName = "q", Material = material,
            Positions = new float[] { -1, 0, -1, 1, 0, -1, 1, 0, 1, -1, 0, 1 },
            Normals = new float[] { 0, -1, 0, 0, -1, 0, 0, -1, 0, 0, -1, 0 },
            Indices = new[] { 0, 1, 2, 0, 2, 3 },
            Uvs = new float[] { 0, 0, 1, 0, 1, 1, 0, 1 },
            Ambient = new[] { ambient, ambient, ambient }, Diffuse = new float[] { 1, 1, 1 }, Emissive = new[] { emissive, emissive, emissive },
        };
        if (tex != null) m.Textures["base"] = tex;
        return m;
    }

    /// <summary>Builds the mesh as one model in a new viewport over black, the camera 10 in front of the quad.</summary>
    Node3D Model(string name, Func<Color, string> check, ModelMesh mesh)
    {
        var md = new ModelData { RootLocal = Xform.Identity };
        md.Meshes.Add(mesh);
        var vp = new SubViewport { Size = new Vector2I(Size, Size), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Msaa3D = Viewport.Msaa.Disabled };
        vp.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black } });
        AddChild(vp);
        var cam = new Camera3D { Name = "Cam", Current = true, Fov = 40, Compositor = Ms2Bloom.GammaOut() };   // the game's gamma conversion for views without bloom
        vp.AddChild(cam);
        var root = mb.Build(md, name);
        vp.AddChild(root);
        cam.LookAtFromPosition(new Vector3(0, 0, 10), Vector3.Zero, Vector3.Up);
        cases.Add((name, vp, check));
        return root;
    }

    // the read-back value in the space the shader writes (calibrated: an emissive 0.5 quad with no light)
    bool srgbReadback;
    float Lin(float v) => srgbReadback ? (v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f)) : v;
    float G(Color c) => Lin(c.G);

    Func<Color, string> Grey(float want, string what) => c =>
        Mathf.Abs(Lin(c.R) - want) <= 0.03f && Mathf.Abs(Lin(c.G) - want) <= 0.03f && Mathf.Abs(Lin(c.B) - want) <= 0.03f
            ? null : $"{what}: got ({Lin(c.R):F3}, {Lin(c.G):F3}, {Lin(c.B):F3}), want {want:F3}";

    public override async void _Ready()
    {
        mb = new ModelBuilder(null, null);
        // ModelBuilder reads textures from the packs; the textured cases preload a 0.5 grey into its cache (no game data here)
        var img = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
        img.Fill(new Color(0.5f, 0.5f, 0.5f));
        var cache = (Dictionary<string, Texture2D>)typeof(ModelBuilder).GetField("textures", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(mb);
        cache["half.dds"] = ImageTexture.CreateFromImage(img);
        float tex = img.GetPixel(0, 0).R;   // 128 / 255

        // the field light travels down and back onto the quad: l = -dir = 0.5 * n + 0.866 * up
        var n = new Vector3(0, 0, 1);
        var sunDir = -(n * Ndl + Vector3.Up * Mathf.Sqrt(1 - Ndl * Ndl));
        RenderingServer.GlobalShaderParameterSet("ms2_sun_dir", sunDir);
        RenderingServer.GlobalShaderParameterSet("ms2_sun_color", new Vector3(SunColor, SunColor, SunColor));
        RenderingServer.GlobalShaderParameterSet("ms2_sun_ambient", new Vector3(SunAmbient, SunAmbient, SunAmbient));
        RenderingServer.GlobalShaderParameterSet("ms2_sun_specular", Vector3.Zero);
        RenderingServer.GlobalShaderParameterSet("ms2_ambient", Vector3.Zero);
        RenderingServer.GlobalShaderParameterSet("ms2_light_count", 0f);
        RenderingServer.GlobalShaderParameterSet("ms2_hfog", Vector3.Zero);

        // calibration: emissive 0.5 with no material ambient or diffuse, so no light term
        var calib = Quad("MS2CharacterMaterial", ambient: 0, emissive: 0.5f);
        calib.Diffuse = new float[] { 0, 0, 0 };
        Model("calib_half", null, calib);

        // MS2Light (type 0): 0.6 * 0.5 + 0.4 * (1 - 0.5)
        float ms2 = SunColor * Ndl + SunAmbient * (1 - Ndl);
        Model("type0_ms2light", Grey(ms2, "type 0 under MS2Light"), Quad("MS2StandardMaterial"));
        // NiStandardMaterial (no material): 0.6 * 0.5 + material ambient 1 * the light's ambient 0.4
        float nis = SunColor * Ndl + SunAmbient;
        Model("none_nistandard", Grey(nis, "a mesh with no material under NiStandardMaterial's light"), Quad(null));
        // the character light (type 1): half-lambert at N.L = cos 30, no rim facing the camera
        float chr = SunColor * HalfLambert + SunAmbient * (1 - HalfLambert);
        Model("type1_character", Grey(chr, "type 1 under the character light"), Quad("MS2CharacterMaterial"));

        // not clamped: emissive 1 over a 0.5 base map; type 0 keeps (1 + 0.5) * 0.5, NiStandardMaterial (1 + 0.7) * 0.5,
        // the character light clamps to 1 * 0.5
        Model("type0_unclamped", Grey((1 + ms2) * tex, "type 0 lit above 1 is not clamped"), Quad("MS2StandardMaterial", emissive: 1, tex: "half.dds"));
        Model("none_unclamped", Grey((1 + nis) * tex, "NiStandardMaterial lit above 1 is not clamped"), Quad(null, emissive: 1, tex: "half.dds"));
        Model("type1_clamped", Grey(tex, "the character light clamps at 1"), Quad("MS2CharacterMaterial", emissive: 1, tex: "half.dds"));

        // MS2AlphaBoostMaterial: a blended MS2CharacterMaterial mesh at material alpha 1.5 draws as type 0 times 1.5 (over
        // black, whatever the blend does with the opacity, the same mesh without the swap gives the ratio)
        ModelMesh Blended(bool boost)
        {
            var m = Quad("MS2CharacterMaterial", tex: "half.dds");
            m.AlphaFlags = 0xED; m.MaterialAlpha = 1.5f; m.AlphaBoost = boost;
            return m;
        }
        Model("boost_off", null, Blended(false));
        Model("boost_on", null, Blended(true));

        // an own light pair: the pair's light straight onto the quad (N.L = 1), diffuse 0.9, ambient 0.9 (material ambient 0
        // here, so only the diffuse shows); type 0 follows the pair's direction, the character light still the camera's
        var ownDir = -n;
        PortraitView.OwnLights(Model("own_type0", Grey(0.9f, "type 0 under an own pair follows its direction"), Quad("MS2StandardMaterial", ambient: 0)), ownDir);
        PortraitView.OwnLights(Model("own_none", Grey(0.9f, "NiStandardMaterial under an own pair follows its direction"), Quad(null, ambient: 0)), ownDir);
        PortraitView.OwnLights(Model("own_type1", Grey(0.9f * HalfLambert, "type 1 under an own pair keeps the camera's light"), Quad("MS2CharacterMaterial", ambient: 0)), ownDir);

        for (int i = 0; i < 4; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var px = new Dictionary<string, Color>();
        foreach (var (name, vp, _) in cases) px[name] = vp.GetTexture().GetImage().GetPixel(Size / 2, Size / 2);
        float half = px["calib_half"].R;
        srgbReadback = Mathf.Abs(half - 0.5f) > 0.05f;
        GD.Print($"light_form_render: calibration emissive 0.5 reads {half:F3} ({(srgbReadback ? "sRGB" : "linear")} readback)");
        if (Mathf.Abs(Lin(half) - 0.5f) > 0.03f) fails.Add($"calibration: emissive 0.5 reads {Lin(half):F3}");
        foreach (var (name, _, check) in cases)
        {
            if (check == null) continue;
            var err = check(px[name]);
            GD.Print($"light_form_render: {name} {G(px[name]):F3} {(err == null ? "ok" : "FAIL")}");
            if (err != null) fails.Add($"{name}: {err}");
        }
        float off = G(px["boost_off"]), on = G(px["boost_on"]);
        // without the swap the mesh is type 1 (the character light, clamped); with it MS2Light times the opacity
        float want = ms2 * tex * 1.5f, wantOff = chr * tex;
        float ratio = on / Mathf.Max(off, 1e-4f), wantRatio = want / wantOff;
        GD.Print($"light_form_render: boost off {off:F3} on {on:F3} ratio {ratio:F3} (want {wantRatio:F3})");
        if (Mathf.Abs(ratio - wantRatio) > 0.08f) fails.Add($"alpha boost: on / off {ratio:F3}, want {wantRatio:F3}");
        foreach (var (name, vp, _) in cases)
        {
            bool own = false;
            if (vp.GetNodeOrNull<Camera3D>("Cam")?.Compositor?.CompositorEffects is { } effects)
                foreach (var e in effects) own |= e is Ms2Bloom { Ready: true, Failed: false };
            if (!own) fails.Add($"{name}: the view has no working Ms2Bloom conversion");
        }
        foreach (var f in fails) GD.PrintErr("FAIL " + f);
        GD.Print(fails.Count == 0 ? "light_form_render: ALL OK" : $"light_form_render: {fails.Count} FAILED");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
