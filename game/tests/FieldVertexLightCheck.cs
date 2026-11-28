using System;
using System.Collections.Generic;
using Godot;
using Ms2.Formats.Model;

namespace Ms2Godot.Tests;

/// <summary>
/// Render check (res://tests/field_vertex_light.tscn, no game data; needs a GPU, not headless): where a field mesh's lights
/// are evaluated. The client lights a field mesh (MS2StandardMaterial type 0) in the vertex shader unless a light has a
/// shadow map for it or it has a normal or parallax map with tangents (NiStandardMaterial::GenerateDescriptor,
/// NiStandardMaterial.cpp:648-666); field_lit follows that with ms2_sun_shadow, ms2_receive and pixel_lit. Each case is one
/// field-path quad (ModelBuilder.BuildMesh with the instance colour) with a point light close to one corner and nothing
/// else lit: per vertex the centre (on the shared diagonal) is the mean of the two diagonal corners' light, per pixel it is
/// the light at the centre, about twenty times less. The four pixels around the centre are averaged.
/// <list type="bullet">
/// <item>no sun shadow map (ms2_sun_shadow 0): per vertex;</item>
/// <item>no sun shadow map, a normal map with tangents (pixel_lit): per pixel;</item>
/// <item>a sun shadow map, a receiver: per pixel;</item>
/// <item>a sun shadow map, not a receiver (ms2_receive 0): per vertex.</item>
/// </list>
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class FieldVertexLightCheck : Node
{
    const int Size = 64;
    readonly List<(string Name, SubViewport Vp, float Want)> cases = new();
    readonly List<string> fails = new();
    ModelBuilder mb;

    // the point light (Godot space) 0.3 in front of the quad's (1, 1) corner: diffuse 1, attenuation 1 / max(1, 4 d^2)
    static readonly Vector3 LightPos = new(1, 1, 0.3f);
    const float Quadratic = 4f;

    static float LightAt(Vector3 p)
    {
        var to = LightPos - p;
        float d = to.Length();
        float ndl = Mathf.Max(to.Z / d, 0f);    // the quad's normal is +z
        return ndl / Mathf.Max(1f, Quadratic * d * d);
    }

    /// <summary>A quad of half size 1 in the x-y plane facing +z (BuildMesh keeps the positions as given); its shared
    /// diagonal runs from (-1, -1, 0) to (1, 1, 0).</summary>
    static ModelMesh Quad(float emissive = 0f, bool normalMap = false)
    {
        var m = new ModelMesh
        {
            Name = "q", FileName = "q", Material = "MS2StandardMaterial",
            Positions = new float[] { -1, -1, 0, 1, -1, 0, 1, 1, 0, -1, 1, 0 },
            Normals = new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1 },
            Indices = new[] { 0, 1, 2, 0, 2, 3 },
            Uvs = new float[] { 0, 0, 1, 0, 1, 1, 0, 1 },
            Ambient = new float[] { 0, 0, 0 }, Diffuse = new float[] { 1, 1, 1 }, Emissive = new[] { emissive, emissive, emissive },
        };
        if (normalMap)
        {
            m.Tangents = new float[] { 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0 };
            m.Binormals = new float[] { 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0 };
            m.Textures["normal"] = "flat_n.dds";
        }
        return m;
    }

    SubViewport View(string name, ModelMesh mesh, float want, Action<MeshInstance3D> set = null)
    {
        var vp = new SubViewport { Size = new Vector2I(Size, Size), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Msaa3D = Viewport.Msaa.Disabled };
        vp.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black } });
        AddChild(vp);
        var cam = new Camera3D { Current = true, Fov = 40, Compositor = Ms2Bloom.GammaOut() };
        vp.AddChild(cam);
        var mi = new MeshInstance3D { Mesh = mb.BuildMesh(mesh, skin: false, instanceColor: true) };
        set?.Invoke(mi);
        vp.AddChild(mi);
        cam.LookAtFromPosition(new Vector3(0, 0, 10), Vector3.Zero, Vector3.Up);
        cases.Add((name, vp, want));
        return vp;
    }

    bool srgbReadback;
    float Lin(float v) => srgbReadback ? (v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f)) : v;

    float Centre(SubViewport vp)
    {
        var img = vp.GetTexture().GetImage();
        float s = 0;
        for (int y = Size / 2 - 1; y <= Size / 2; y++)
            for (int x = Size / 2 - 1; x <= Size / 2; x++) s += Lin(img.GetPixel(x, y).G);
        return s / 4f;
    }

    async System.Threading.Tasks.Task Frames()
    {
        for (int i = 0; i < 4; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    public override async void _Ready()
    {
        mb = new ModelBuilder(null, null);
        var cache = (Dictionary<string, Texture2D>)typeof(ModelBuilder).GetField("textures", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(mb);
        var flat = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
        flat.Fill(new Color(0.5f, 0.5f, 1f));
        cache["flat_n.dds"] = ImageTexture.CreateFromImage(flat);

        // nothing lit but the point light: no sun, no sun ambient, no ambient, no fog
        RenderingServer.GlobalShaderParameterSet("ms2_sun_dir", new Vector3(0, 0, -1));
        RenderingServer.GlobalShaderParameterSet("ms2_sun_color", Vector3.Zero);
        RenderingServer.GlobalShaderParameterSet("ms2_sun_ambient", Vector3.Zero);
        RenderingServer.GlobalShaderParameterSet("ms2_sun_specular", Vector3.Zero);
        RenderingServer.GlobalShaderParameterSet("ms2_ambient", Vector3.Zero);
        RenderingServer.GlobalShaderParameterSet("ms2_hfog", Vector3.Zero);
        // ms2_lights.gdshaderinc's layout, one column: position + flags, diffuse + constant, ambient + linear,
        // specular + quadratic, direction + cos outer, cos inner + exponent
        var rows = new float[]
        {
            LightPos.X, LightPos.Y, LightPos.Z, 0,
            1, 1, 1, 0,
            0, 0, 0, 0,
            0, 0, 0, Quadratic,
            0, 0, -1, 0,
            0, 0, 0, 0,
        };
        var bytes = new byte[rows.Length * 4];
        Buffer.BlockCopy(rows, 0, bytes, 0, bytes.Length);
        RenderingServer.GlobalShaderParameterSet("ms2_lights", ImageTexture.CreateFromImage(Image.CreateFromData(1, 6, false, Image.Format.Rgbaf, bytes)));
        RenderingServer.GlobalShaderParameterSet("ms2_light_count", 1f);

        float perVertex = 0.5f * (LightAt(new Vector3(-1, -1, 0)) + LightAt(new Vector3(1, 1, 0)));
        float perPixel = LightAt(Vector3.Zero);
        GD.Print($"field_vertex_light: want per vertex {perVertex:F3}, per pixel {perPixel:F3}");

        // phase 1: no shadow map. The calibration quad masks the light out (ms2_light_off) and shows its emissive 0.5.
        RenderingServer.GlobalShaderParameterSet("ms2_sun_shadow", 0f);
        var calib = View("calib_half", Quad(emissive: 0.5f), 0.5f, mi => mi.SetInstanceShaderParameter("ms2_light_off", 1f));
        View("no_shadow_map", Quad(), perVertex);
        View("no_shadow_map_normal_map", Quad(normalMap: true), perPixel);
        await Frames();
        float half = calib.GetTexture().GetImage().GetPixel(Size / 2, Size / 2).G;
        srgbReadback = Mathf.Abs(half - 0.5f) > 0.05f;
        GD.Print($"field_vertex_light: calibration emissive 0.5 reads {half:F3} ({(srgbReadback ? "sRGB" : "linear")} readback)");
        var got = new Dictionary<string, float>();
        foreach (var (name, vp, _) in cases) got[name] = Centre(vp);
        foreach (var (_, vp, _) in cases) vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        int phase1 = cases.Count;

        // phase 2: the sun casts a shadow map; a receiver and a copy kept out of the receiver list
        RenderingServer.GlobalShaderParameterSet("ms2_sun_shadow", 1f);
        View("shadow_map_receiver", Quad(), perPixel);
        View("shadow_map_not_receiving", Quad(), perVertex, mi => mi.SetInstanceShaderParameter("ms2_receive", 0f));
        await Frames();
        for (int i = phase1; i < cases.Count; i++) got[cases[i].Name] = Centre(cases[i].Vp);

        foreach (var (name, _, want) in cases)
        {
            bool ok = Mathf.Abs(got[name] - want) <= 0.03f;
            GD.Print($"field_vertex_light: {name} {got[name]:F3} want {want:F3} {(ok ? "ok" : "FAIL")}");
            if (!ok) fails.Add($"{name}: {got[name]:F3}, want {want:F3}");
        }
        foreach (var f in fails) GD.PrintErr("FAIL " + f);
        GD.Print(fails.Count == 0 ? "field_vertex_light: ALL OK" : $"field_vertex_light: {fails.Count} FAILED");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
