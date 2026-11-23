using System.Collections.Generic;
using Godot;
using Ms2.Formats.Model;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/effect_nan.tscn, no game data): an effect node whose scale key turns NaN, as the Wizard's Magic
/// Claw cast effect does (eff_wizard_magicclaw_cast_01_a.nif: sphere scale keys 1 at 0.333 s, NaN at 0.7 s). Its mesh and its
/// child's mesh are hidden while their world transform is not finite and never get a non-finite transform; another node's
/// mesh stays shown. Before the fix both meshes took the NaN transform (the renderer's "!v.is_finite()" error).
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class EffectNanCheck : Node
{
    static ModelMesh Quad(string name) => new()
    {
        Name = name, FileName = name, Positions = new float[] { -1, -1, 0, 1, -1, 0, 1, 1, 0, -1, 1, 0 },
        Normals = new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1 }, Indices = new[] { 0, 1, 2, 0, 2, 3 },
        Ambient = new float[] { 0, 0, 0 }, Diffuse = new float[] { 0, 0, 0 }, Emissive = new float[] { 1, 1, 1 },
    };

    public override void _Ready()
    {
        var fails = new List<string>();
        var sc = new EffectScene { Duration = 2 };
        sc.Nodes.Add(new EffectNode { Name = "sphere", Anim = new KfChannel { S = t => t < 0.333 ? 1 : double.NaN } });
        sc.Nodes.Add(new EffectNode { Name = "child", Parent = 0 });
        sc.Nodes.Add(new EffectNode { Name = "other" });
        for (int i = 0; i < 3; i++) sc.Meshes.Add(new EffectMesh { Mesh = Quad("m" + i), Node = i });
        var fx = new EffectInstance(sc, new ModelBuilder(null, null), 2, 1);
        AddChild(fx);
        fx.FileSpace = Transform3D.Identity;
        var meshes = new List<MeshInstance3D>();
        foreach (var c in fx.GetChild<Node3D>(0).GetChildren()) if (c is MeshInstance3D m) meshes.Add(m);
        if (meshes.Count != 3) fails.Add($"meshes {meshes.Count}, want 3");
        else
        {
            void Expect(double at, bool shown)
            {
                for (int i = 0; i < 3; i++)
                {
                    var m = meshes[i];
                    if (!m.Transform.IsFinite()) fails.Add($"t {at}: mesh {i} transform {m.Transform}");
                    bool want = i == 2 || shown;
                    if (m.Visible != want) fails.Add($"t {at}: mesh {i} visible {m.Visible}, want {want}");
                }
            }
            fx._Process(0.1); Expect(0.1, true);
            fx._Process(0.4); Expect(0.5, false);
            fx._Process(0.5); Expect(1.0, false);
        }
        foreach (var f in fails) GD.PrintErr("effect nan: " + f);
        GD.Print(fails.Count == 0 ? "effect nan: PASS" : $"effect nan: FAIL ({fails.Count})");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
