using System.Collections.Generic;
using Godot;
using Ms2.Formats.Pack;
using Ms2Godot.Net;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/lightning_effect.tscn, game data) of a lightning part on a path segment's point list
/// (LightningEffect, RegionSkills.SpawnOn with the points), on the chain lightning's ray effect (a lightning part only):
/// - without a point list the effect makes nothing (no part counted, handle 0);
/// - with one it makes one lightning under the actor; each update lays one surface whose first and last edge pairs sit
///   around the first and last points, width apart, on the side the camera sees (camera straight down);
/// - the surface's material blends additively (alphaMode add) and has its texture;
/// - a hidden lightning lays nothing; the points moving move the strip's ends;
/// - 1000 updates allocate 0 B.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class LightningEffectCheck : Node3D
{
    const string Ray = "CH/Skill/Wizard/Eff_Wizard_ChainLightning_Ray_01.xml";
    static readonly StringName Tex = "tex";

    public override void _Ready()
    {
        var fails = new List<string>();
        void Check(bool ok, string what) { if (!ok) fails.Add(what); }
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var skills = new RegionSkills(packs, builder);
        AddChild(skills);
        // the camera straight down: its world direction (0, -1, 0), client (0, 0, -1)
        var cam = new Camera3D { Position = new Vector3(0, 2000, 0), RotationDegrees = new Vector3(-90, 0, 0) };
        AddChild(cam);
        cam.MakeCurrent();
        var actor = new Node3D { Name = "Caster" };
        AddChild(actor);

        int made = LightningEffect.Made;
        Check(skills.SpawnOn(Ray, actor, 0) == 0, "the ray effect without a point list was made");
        Check(LightningEffect.Made == made && Lightnings(actor).Count == 0, "a lightning without a point list");

        var points = new LightningPoints { From = new System.Numerics.Vector3(0, 0, 0), To = new System.Numerics.Vector3(300, -400, 0) };
        int h = skills.SpawnOn(Ray, actor, 0, points: points);
        var list = Lightnings(actor);
        Check(h != 0 && LightningEffect.Made == made + 1 && list.Count == 1, $"handle {h}, made {LightningEffect.Made - made}, nodes {list.Count}");
        if (list.Count == 1)
        {
            var le = list[0];
            var mesh = (ImmediateMesh)le.Mesh;
            int laid = LightningEffect.Laid;
            le._Process(1 / 60.0);
            Check(LightningEffect.Laid == laid + 1 && mesh.GetSurfaceCount() == 1, $"laid {LightningEffect.Laid - laid}, surfaces {mesh.GetSurfaceCount()}");
            Ends(mesh, new Vector3(0, 0, 0), new Vector3(300, 0, 400), Check, "first");
            var mat = mesh.SurfaceGetMaterial(0) as ShaderMaterial;
            Check(mat?.Shader?.Code?.Contains("blend_add") == true, "the material does not blend additively");
            Check(mat?.GetShaderParameter(Tex).AsGodotObject() is Texture2D, "the material has no texture");

            points.To = new System.Numerics.Vector3(-200, 100, 50);
            le._Process(1 / 60.0);
            Ends(mesh, new Vector3(0, 0, 0), new Vector3(-200, 50, -100), Check, "moved");

            le.Visible = false;
            laid = LightningEffect.Laid;
            le._Process(1 / 60.0);
            Check(LightningEffect.Laid == laid, "a hidden lightning laid its strip");
            le.Visible = true;

            for (int i = 0; i < 20; i++) le._Process(1 / 60.0);
            laid = LightningEffect.Laid;
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) le._Process(1 / 60.0);
            long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Check(bytes == 0, $"1000 updates allocated {bytes} B");
            Check(LightningEffect.Laid == laid + 1000, $"1000 updates laid {LightningEffect.Laid - laid}");
            GD.Print($"lightning effect: {mesh.GetSurfaceCount()} surface, {bytes} B over 1000 updates");
        }
        foreach (var f in fails) GD.PrintErr("lightning effect: " + f);
        GD.Print(fails.Count == 0 ? "lightning effect: PASS" : $"lightning effect: FAIL ({fails.Count})");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    static List<LightningEffect> Lightnings(Node n)
    {
        var r = new List<LightningEffect>();
        for (int i = 0; i < n.GetChildCount(); i++)
            if (n.GetChild(i) is LightningEffect le) r.Add(le);
        return r;
    }

    /// <summary>The strip's first triangle starts with the first left and right points, its last with ... the last pair: each
    /// pair's middle is the point (godot space), the pair 96 apart (the ray's width) and level (the camera is straight down).</summary>
    static void Ends(ImmediateMesh mesh, Vector3 from, Vector3 to, System.Action<bool, string> check, string what)
    {
        var arrays = mesh.SurfaceGetArrays(0);
        var v = arrays.Count > (int)Mesh.ArrayType.Vertex ? arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array() : System.Array.Empty<Vector3>();
        if (v.Length < 6) { check(false, $"{what}: {v.Length} vertices"); return; }
        // triangles (a0, b0, a1), (a1, b0, b1): the first pair is v[0], v[1]; the last pair v[^1] (b1) and v[^3] (a1)
        void Pair(Vector3 a, Vector3 b, Vector3 at, string end)
        {
            check(((a + b) / 2).DistanceTo(at) < 0.01f, $"{what} {end}: middle {(a + b) / 2}, want {at}");
            check(Mathf.Abs(a.DistanceTo(b) - 96) < 0.01f, $"{what} {end}: width {a.DistanceTo(b)}");
            check(Mathf.Abs(a.Y - b.Y) < 0.01f, $"{what} {end}: not level ({a.Y}, {b.Y})");
        }
        Pair(v[0], v[1], from, "start");
        Pair(v[^3], v[^1], to, "end");
    }
}
