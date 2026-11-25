using System.Collections.Generic;
using Godot;
using Ms2.Formats.Model;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/effect_morph.tscn, no game data) of an effect mesh's morph in EffectInstance: a quad whose one
/// offset target lifts it by 2 along z, its weight keyed 0 at 0 s to 1 at 1 s (CLAMP), as an NiMorphWeightsController drives
/// it (EffectScene, EffectMorph).
/// - the mesh carries the offsets in CUSTOM0 and bounds that hold the lifted quad; its shader adds morph_w.x * CUSTOM0;
/// - morph_w follows the key (0.25 at 0.25 s, 1 past the end);
/// - a morph with five offset targets (more than the custom arrays) draws its positions without a morph shader;
/// - the frame update with the morph allocates 0 B.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class EffectMorphCheck : Node
{
    static ModelMesh Quad(string name) => new()
    {
        Name = name, FileName = name, Positions = new float[] { -1, -1, 0, 1, -1, 0, 1, 1, 0, -1, 1, 0 },
        Normals = new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1 }, Indices = new[] { 0, 1, 2, 0, 2, 3 },
        Ambient = new float[] { 0, 0, 0 }, Diffuse = new float[] { 0, 0, 0 }, Emissive = new float[] { 1, 1, 1 },
    };

    static EffectMorph Lift(ModelMesh m, int targets)
    {
        var deltas = new float[targets][];
        for (int t = 0; t < targets; t++) deltas[t] = new float[] { 0, 0, 2, 0, 0, 2, 0, 0, 2, 0, 0, 2 };
        var anim = new MorphAnim { Deltas = deltas, Static = new float[targets], Weights = new FloatAnim[targets], Stop = 1, Clamp = true };
        // target 1's weight keyed 0 at 0 s to 1 at 1 s (linear), as ReadMorph reads the keys
        anim.Weights[0] = new FloatAnim { KeyType = 1, Times = new float[] { 0, 1 }, Values = new float[] { 0, 1 } };
        var w = new System.Func<double, double>[targets];
        w[0] = t => t;
        m.AnyMorph = anim;
        return new EffectMorph { Anim = anim, Weights = w, Ctl = new TimeCtl { Start = 0, Stop = 1, Cycle = 2 } };
    }

    public override void _Ready()
    {
        var fails = new List<string>();
        var sc = new EffectScene { Duration = 100 };
        sc.Nodes.Add(new EffectNode { Name = "root" });
        var lifted = Quad("lifted");
        var five = Quad("five");
        sc.Meshes.Add(new EffectMesh { Mesh = lifted, Node = 0, Morph = Lift(lifted, 1) });
        sc.Meshes.Add(new EffectMesh { Mesh = five, Node = 0, Morph = Lift(five, 5) });
        var fx = new EffectInstance(sc, new ModelBuilder(null, null), 100, 1);
        AddChild(fx);
        fx.FileSpace = Transform3D.Identity;
        var meshes = new List<MeshInstance3D>();
        var root = fx.GetChild<Node3D>(0);
        for (int i = 0; i < root.GetChildCount(); i++) if (root.GetChild(i) is MeshInstance3D mi) meshes.Add(mi);
        if (meshes.Count != 2) fails.Add($"meshes {meshes.Count}, want 2");
        else
        {
            var mat = (ShaderMaterial)meshes[0].MaterialOverride;
            var am = (ArrayMesh)meshes[0].Mesh;
            if (!mat.Shader.Code.Contains("VERTEX += morph_w.x * CUSTOM0.xyz;")) fails.Add("the morph mesh's shader does not add morph_w.x * CUSTOM0");
            if ((am.SurfaceGetFormat(0) & Mesh.ArrayFormat.FormatCustom0) == 0) fails.Add("the morph mesh has no CUSTOM0 array");
            var box = am.CustomAabb;
            if (box.Position.Z > 0.001f || box.End.Z < 1.999f) fails.Add($"the morph mesh's bounds {box} do not hold the lifted quad (z 0..2)");
            float W() => ((Vector4)mat.GetShaderParameter("morph_w")).X;
            fx._Process(0.25);
            if (Mathf.Abs(W() - 0.25f) > 1e-3f) fails.Add($"morph_w.x {W()} at 0.25 s, want 0.25");
            fx._Process(1.0);
            if (Mathf.Abs(W() - 1f) > 1e-3f) fails.Add($"morph_w.x {W()} past the key's end, want 1");
            var mat5 = (ShaderMaterial)meshes[1].MaterialOverride;
            if (mat5.Shader.Code.Contains("morph_w")) fails.Add("the five-target morph got a morph shader");
            fx._Process(0.001);
            long bytes = CheckAlloc.Least(() => { for (int i = 0; i < 1000; i++) fx._Process(0.001); });
            if (bytes != 0) fails.Add($"1000 frames of the morphing effect allocated {bytes} B");
            GD.Print($"effect morph: morph_w.x {W():F3} past the key's end; bytes over 1000 frames {bytes}");
        }
        foreach (var f in fails) GD.PrintErr("effect morph: FAIL " + f);
        GD.Print(fails.Count == 0 ? "effect morph: ok" : $"effect morph: {fails.Count} FAILED");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
