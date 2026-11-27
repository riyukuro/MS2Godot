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
/// <item>types 1-3 (MS2CharacterMaterial): the character light from the camera (SetSunDirection's light and rim directions,
/// also from a camera looking down), ColorBoost on the whole composite, not clamped;</item>
/// <item>MS2AlphaBoostMaterial (the swap of a blended mesh at an opacity above 1): MS2Light, the colour times the opacity;</item>
/// <item>an own light pair (PortraitView.OwnLights): type 0 takes the pair's direction, not the field's.</item>
/// </list>
/// The quad faces Godot +z (file -y) at the origin; the camera looks at it from +z, so the view vector is the normal.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class LightFormRenderCheck : Node
{
    const int Size = 32;
    const float Fov = 40;
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
    Node3D Model(string name, Func<Color, string> check, ModelMesh mesh, Vector3? camPos = null)
    {
        var md = new ModelData { RootLocal = Xform.Identity };
        md.Meshes.Add(mesh);
        var vp = new SubViewport { Size = new Vector2I(Size, Size), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Msaa3D = Viewport.Msaa.Disabled };
        vp.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black } });
        AddChild(vp);
        var cam = new Camera3D { Name = "Cam", Current = true, Fov = Fov, Compositor = Ms2Bloom.GammaOut() };   // the game's gamma conversion for views without bloom
        vp.AddChild(cam);
        var root = mb.Build(md, name);
        vp.AddChild(root);
        cam.LookAtFromPosition(camPos ?? new Vector3(0, 0, 10), Vector3.Zero, Vector3.Up);
        cases.Add((name, vp, check));
        return root;
    }

    /// <summary>
    /// SetSunDirection @0x14107b050 (unit_14107ac30_tcreatecharinfo.cpp:262-281) step by step, for a camera direction
    /// <paramref name="dir"/> in Godot's frame: in Gamebryo's (x, y, z) = Godot's (x, -z, y), the flat direction and the
    /// direction both turned by Matrix3::MakeRotation(-30 degrees, (0, 0, 1) x dir) (Foundation/efd/Win32/Matrix3_Win32.cpp:127-152,
    /// the axis not unitised), times the point as rows; yaw 0. Returns the character light and the rim direction in Godot's frame.
    /// </summary>
    static (Vector3 Light, Vector3 Rim) SunDirection(Vector3 dir)
    {
        var d = new Vector3(dir.X, -dir.Z, dir.Y);
        var flat = new Vector3(d.X, d.Y, 0).Normalized();
        var axis = new Vector3(0, 0, 1).Cross(d);
        float angle = -Mathf.DegToRad(30), sn = Mathf.Sin(angle), cs = Mathf.Cos(angle), omcs = 1 - cs;
        float x = axis.X, y = axis.Y, z = axis.Z;
        var r0 = new Vector3(x * x * omcs + cs, x * y * omcs + z * sn, x * z * omcs - y * sn);
        var r1 = new Vector3(x * y * omcs - z * sn, y * y * omcs + cs, y * z * omcs + x * sn);
        var r2 = new Vector3(x * z * omcs + y * sn, y * z * omcs - x * sn, z * z * omcs + cs);
        Vector3 Rot(Vector3 p) => new(r0.Dot(p), r1.Dot(p), r2.Dot(p));
        Vector3 G(Vector3 p) => new(p.X, p.Z, -p.Y);
        return (G(Rot(flat)), G(Rot(d)));
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

        // not clamped: emissive 1 over a 0.5 base map; type 0 keeps (1 + 0.5) * 0.5, NiStandardMaterial (1 + 0.7) * 0.5, the
        // character light (1 + its light) * 0.5: nothing is saturated before the 8-bit write (captures/02000110_450_1650_4350_char)
        Model("type0_unclamped", Grey((1 + ms2) * tex, "type 0 lit above 1 is not clamped"), Quad("MS2StandardMaterial", emissive: 1, tex: "half.dds"));
        Model("none_unclamped", Grey((1 + nis) * tex, "NiStandardMaterial lit above 1 is not clamped"), Quad(null, emissive: 1, tex: "half.dds"));
        Model("type1_unclamped", Grey((1 + chr) * tex, "the character light lit above 1 is not clamped"), Quad("MS2CharacterMaterial", emissive: 1, tex: "half.dds"));
        // ColorBoost scales the emissive and ambient too, not only the diffuse (PS 0x42a9d9d0: c18.x * col * albedo): emissive
        // 0.25 with no material diffuse or ambient at ColorBoost 2
        var boosted = Quad("MS2CharacterMaterial", ambient: 0, emissive: 0.25f);
        boosted.Diffuse = new float[] { 0, 0, 0 };
        boosted.ExtraFloats["ColorBoost"] = 2;
        Model("type1_color_boost", Grey(0.5f, "ColorBoost scales the whole composite"), boosted);

        // MS2AlphaBoostMaterial: a blended MS2CharacterMaterial mesh at material alpha 1.5 draws as type 0 times 1.5, saturated
        // into the 8-bit target with its opacity (CreateRenderTargetSurface @0x1416a3cd0), so over black it reads type 0 * 1.5
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

        // a camera looking 60 degrees down (h = 0.5): SetSunDirection's light is not unit length and its down part is
        // sin 30 * h, and the rim direction is the camera direction turned down (not the flat direction turned by pitch + 30).
        // Under an own pair (diffuse 0.9, no material ambient) the quad reads 0.9 * (half-lambert + rim).
        var camPos = new Vector3(0, Mathf.Sin(Mathf.DegToRad(60)), Mathf.Cos(Mathf.DegToRad(60))) * 10;
        var (charL, rimL) = SunDirection(-camPos.Normalized());
        float Hl(Vector3 nn) => Mathf.Pow(0.5f * (-charL).Dot(nn) + 0.5f, 2f);
        // the quad (facing Godot +z) turned 60 degrees about x to face the camera: N.V = 1 at the centre, so no rim
        var nFace = camPos.Normalized();
        var faced = Model("type1_pitched", Grey(0.9f * Hl(nFace), "the character light from a camera looking down"),
            Quad("MS2CharacterMaterial", ambient: 0), camPos);
        faced.RotationDegrees = new Vector3(-60, 0, 0);
        PortraitView.OwnLights(faced, ownDir);
        // the quad turned 90 degrees to face up: N.V about cos 30, the rim at FresnelBoost 5, FresnelExponent 1. V is taken
        // through the sampled pixel's centre (Size / 2 + 0.5, half a pixel off the axis), where N.V is sin 60.65, not sin 60.
        var rimQuad = Quad("MS2CharacterMaterial", ambient: 0);
        rimQuad.ExtraFloats["FresnelBoost"] = 5;
        rimQuad.ExtraFloats["FresnelExponent"] = 1;
        var fwd = -camPos.Normalized();
        var right = fwd.Cross(Vector3.Up).Normalized();
        var camUp = right.Cross(fwd);
        float t = Mathf.Tan(Mathf.DegToRad(Fov / 2)) / Size;
        var view = -(fwd + right * t - camUp * t).Normalized();
        float rim = Mathf.Max(0, -rimL.Dot(Vector3.Up)) * 5 * (1 - view.Dot(Vector3.Up));
        var up = Model("type1_pitched_rim", Grey(0.9f * (Hl(Vector3.Up) + rim), "the rim from a camera looking down"), rimQuad, camPos);
        up.RotationDegrees = new Vector3(-90, 0, 0);
        PortraitView.OwnLights(up, ownDir);

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
        // without the swap the mesh is type 1 (the character light); with it MS2Light times the opacity, at an
        // opacity saturated to 1 (an unsaturated 1.5 would weigh it by 1.5 again)
        float want = Mathf.Min(ms2 * tex * 1.5f, 1f);
        GD.Print($"light_form_render: boost off {off:F3} on {on:F3} (want {want:F3})");
        if (Mathf.Abs(on - want) > 0.03f) fails.Add($"alpha boost: on {on:F3}, want {want:F3}");
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
