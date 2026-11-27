using System;
using System.Collections.Generic;
using Godot;

namespace Ms2Godot.Tests;

/// <summary>
/// Depth fog check (res://tests/fog_share.tscn, needs a display): the field shaders' fogged share (ms2_fog.gdshaderinc
/// ms2_fog_share) against the client's vertex fog, 1 - saturate((far + bias - z) / (far - near)) (CalculateBiasedFog,
/// ms2materialnodelibrary.cpp:957-1075), for the mapxblock ranges the data has: far above near (02000110_bf), near equal to
/// far (52000104_qd and three more: a step at far + bias), near above far (63000041_cs: full fog up to far + bias, clear by
/// near + bias), and the fog off (SetFogRange(false)). Each case sets ms2_fog and draws quads at set view depths through a
/// fog-layer camera; a quad's lower half draws the expected share as a constant, so both halves pass through the same
/// output and are compared as drawn. Quits with 0 when every quad's halves agree within 2/255, else 1.
/// </summary>
public partial class FogShareCheck : Node
{
    const int Cell = 16;
    const uint FogLayer = 1u << 20;   // Field.FogLayer

    record Case(string Name, float Near, float Far, float Bias, bool Off, float[] Depths);

    static readonly Case[] Cases =
    {
        new("02000110_bf", 3190, 10000, 1100, false, new[] { 2000f, 4290, 7000, 11000, 12000 }),
        new("near == far", 3590, 3590, 1100, false, new[] { 4000f, 4689, 4691, 6000 }),
        new("63000041_cs", 11000, 10000, 1100, false, new[] { 5000f, 11000, 11350, 11600, 11900, 13000 }),
        new("off", 3190, 10000, 1100, true, new[] { 2000f, 7000, 12000 }),
    };

    /// <summary>The client's fogged share at view depth z (D3D9 float division; 0/0 at the step taken as no fog).</summary>
    static float Expected(Case c, float z)
    {
        if (c.Off) return 0f;
        float f = (c.Far + c.Bias - z) / (c.Far - c.Near);
        if (float.IsNaN(f)) f = 1f;
        return 1f - Math.Clamp(f, 0f, 1f);
    }

    SubViewport vp;
    Node3D quads;
    ShaderMaterial probe;
    int index = -1, wait;
    readonly List<string> fails = new();

    public override void _Ready()
    {
        int cols = 0;
        foreach (var c in Cases) cols = Math.Max(cols, c.Depths.Length);
        vp = new SubViewport { Size = new Vector2I(cols * Cell, Cell), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(vp);
        vp.AddChild(new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal, Size = Cell, KeepAspect = Camera3D.KeepAspectEnum.Height,
            Near = 1, Far = 50000, CullMask = 0xFFFFF | FogLayer, Position = new Vector3(cols * Cell / 2f, 0, 0), Current = true,
            Compositor = Ms2Bloom.GammaOut(),   // the game's gamma conversion for views without bloom
        });
        var shader = new Shader
        {
            Code = @"shader_type spatial;
render_mode unshaded, cull_disabled, fog_disabled;
#include ""res://shaders/ms2_fog.gdshaderinc""
uniform float expected;
void vertex() {
	fog_share = ms2_fog_share(VIEW_MATRIX, (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz, CAMERA_VISIBLE_LAYERS);
}
void fragment() {
	ALBEDO = vec3(UV.y > 0.5 ? expected : fog_share);
}"
        };
        probe = new ShaderMaterial { Shader = shader };
        quads = new Node3D();
        vp.AddChild(quads);
        Next();
    }

    void Next()
    {
        index++;
        foreach (var n in quads.GetChildren()) { quads.RemoveChild(n); n.QueueFree(); }
        if (index >= Cases.Length)
        {
            GD.Print(fails.Count == 0 ? "fog share: ok" : "fog share: FAIL " + string.Join("; ", fails));
            GetTree().Quit(fails.Count == 0 ? 0 : 1);
            return;
        }
        var c = Cases[index];
        RenderingServer.GlobalShaderParameterSet("ms2_fog", new Vector4(c.Near, c.Far, c.Bias, c.Off ? 1f : 0f));
        for (int i = 0; i < c.Depths.Length; i++)
        {
            var m = (ShaderMaterial)probe.Duplicate();
            m.SetShaderParameter("expected", Expected(c, c.Depths[i]));
            quads.AddChild(new MeshInstance3D
            {
                Mesh = new QuadMesh { Size = new Vector2(Cell, Cell) }, MaterialOverride = m,
                Position = new Vector3(i * Cell + Cell / 2f, 0, -c.Depths[i]),
            });
        }
        wait = 4;
    }

    public override void _Process(double delta)
    {
        if (index >= Cases.Length || --wait > 0) return;
        var c = Cases[index];
        var img = vp.GetTexture().GetImage();
        for (int i = 0; i < c.Depths.Length; i++)
        {
            int x = i * Cell + Cell / 2;
            float top = img.GetPixel(x, Cell / 4).R, bottom = img.GetPixel(x, Cell * 3 / 4).R;
            GD.Print($"fog share {c.Name} z {c.Depths[i]}: drawn {top:0.000} expected {bottom:0.000} ({Expected(c, c.Depths[i]):0.000})");
            if (MathF.Abs(top - bottom) > 2f / 255f) fails.Add($"{c.Name} z {c.Depths[i]}: {top:0.000} not {bottom:0.000}");
        }
        Next();
    }
}
