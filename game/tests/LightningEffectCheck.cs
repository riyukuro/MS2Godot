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
/// - a hidden lightning lays nothing; the points moving move the strip's ends; a shorter strip leaves no width past its rows;
/// - the surface is made once with the client's triangles and rewritten in place (the checks read the laid bytes: the
///   headless renderer keeps no region updates);
/// - a 16 lightning chain: the time per frame;
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
            var mesh = (ArrayMesh)le.Mesh;
            int laid = LightningEffect.Laid;
            le._Process(1 / 60.0);
            Check(LightningEffect.Laid == laid + 1 && mesh.GetSurfaceCount() == 1, $"laid {LightningEffect.Laid - laid}, surfaces {mesh.GetSurfaceCount()}");
            Ends(le, new Vector3(0, 0, 0), new Vector3(300, 0, 400), Check, "first");
            var mat = mesh.SurfaceGetMaterial(0) as ShaderMaterial;
            Check(mat?.Shader?.Code?.Contains("blend_add") == true, "the material does not blend additively");
            Check(mat?.GetShaderParameter(Tex).AsGodotObject() is Texture2D, "the material has no texture");

            points.To = new System.Numerics.Vector3(-200, 100, 50);
            le._Process(1 / 60.0);
            Ends(le, new Vector3(0, 0, 0), new Vector3(-200, 50, -100), Check, "moved");
            // a shorter strip lays fewer rows: the pairs it no longer lays lose their width
            int rows = le.Rows;
            points.To = new System.Numerics.Vector3(40, 0, 0);
            le._Process(1 / 60.0);
            Check(le.Rows < rows, $"the shorter strip has {le.Rows} rows, the long one {rows}");
            Ends(le, new Vector3(0, 0, 0), new Vector3(40, 0, 0), Check, "shorter");
            points.To = new System.Numerics.Vector3(-200, 100, 50);
            le._Process(1 / 60.0);
            Ends(le, new Vector3(0, 0, 0), new Vector3(-200, 50, -100), Check, "longer again");

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
        Chain(skills, actor, Check);
        var shot = System.Environment.GetEnvironmentVariable("MS2_LIGHTNING_SHOT");
        if (!string.IsNullOrEmpty(shot) && DisplayServer.GetName() != "headless") { Drawn(shot, fails); return; }
        Done(fails);
    }

    void Done(List<string> fails)
    {
        foreach (var f in fails) GD.PrintErr("lightning effect: " + f);
        GD.Print(fails.Count == 0 ? "lightning effect: PASS" : $"lightning effect: FAIL ({fails.Count})");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    /// <summary>With a renderer (MS2_LIGHTNING_SHOT, not headless): after 10 drawn frames the first strip lights the frame
    /// (pixels brighter than the clear colour; the strip blends additively). Saves the frame to the path given.</summary>
    async void Drawn(string path, List<string> fails)
    {
        for (int i = 0; i < 10; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var tex = GetViewport().GetTexture();
        using var img = tex.GetImage();
        img.SavePng(path);
        var clear = (Color)ProjectSettings.GetSetting("rendering/environment/defaults/default_clear_color");
        int lit = 0;
        for (int y = 0; y < img.GetHeight(); y++)
            for (int x = 0; x < img.GetWidth(); x++)
            {
                var c = img.GetPixel(x, y);
                if (c.R + c.G + c.B > clear.R + clear.G + clear.B + 0.15f) lit++;
            }
        GD.Print($"lightning effect: drawn, {lit} lit pixels of {img.GetWidth() * img.GetHeight()}");
        if (lit < 200) fails.Add($"the drawn strip lit {lit} pixels");
        Done(fails);
    }

    /// <summary>A 16 target chain: 16 lightnings, each 600 long (its rows), updated 500 frames; the time per frame.</summary>
    static void Chain(RegionSkills skills, Node3D actor, System.Action<bool, string> check)
    {
        var caster = new Node3D { Name = "Chain" };
        actor.AddChild(caster);
        for (int i = 0; i < 16; i++)
            skills.SpawnOn(Ray, caster, 0, points: new LightningPoints { From = new System.Numerics.Vector3(600 * i, 0, 0), To = new System.Numerics.Vector3(600 * i + 600, 0, 0) });
        var list = Lightnings(caster);
        check(list.Count == 16, $"chain: {list.Count} lightnings");
        void Frame() { for (int i = 0; i < list.Count; i++) list[i]._Process(1 / 60.0); }
        for (int i = 0; i < 50; i++) Frame();
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int i = 0; i < 500; i++) Frame();
        var took = System.Diagnostics.Stopwatch.GetElapsedTime(t0);
        long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
        check(bytes == 0, $"chain: 500 frames allocated {bytes} B");
        int rows = list.Count > 0 ? list[0].Rows : 0;
        GD.Print($"lightning effect: chain of {list.Count}, {rows} rows each, {took.TotalMilliseconds * 1000 / 500:F1} us per frame, {bytes} B");
    }

    static List<LightningEffect> Lightnings(Node n)
    {
        var r = new List<LightningEffect>();
        for (int i = 0; i < n.GetChildCount(); i++)
            if (n.GetChild(i) is LightningEffect le) r.Add(le);
        return r;
    }

    /// <summary>The surface's triangles are the client's (pair i is vertices 2i left, 2i + 1 right: (a0, b0, a1), (a1, b0, b1)
    /// for each pair). The laid first pair sits on the first point, the last laid pair (n - 1) on the last: each pair's middle
    /// is the point (godot space), the pair 96 apart (the ray's width) and level (the camera is straight down). The uv is
    /// (j / (n - 1), 0 or 1); the pairs past n are one point each (no area); the box holds the laid pairs.</summary>
    static void Ends(LightningEffect le, Vector3 from, Vector3 to, System.Action<bool, string> check, string what)
    {
        var mesh = (ArrayMesh)le.Mesh;
        int n = le.Rows, pairs = Ms2.Formats.World.LightningPath.MaxRow;
        using (var arrays = mesh.SurfaceGetArrays(0))
        {
            var tri = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            bool ok = tri.Length == (pairs - 1) * 6;
            for (int i = 0; ok && i < pairs - 1; i++)
                ok = tri[6 * i] == 2 * i && tri[6 * i + 1] == 2 * i + 1 && tri[6 * i + 2] == 2 * i + 2
                    && tri[6 * i + 3] == 2 * i + 2 && tri[6 * i + 4] == 2 * i + 1 && tri[6 * i + 5] == 2 * i + 3;
            check(ok, $"{what}: the surface's {tri.Length} indices are not the client's triangles");
            check(arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length == 2 * pairs, $"{what}: not {2 * pairs} vertices");
        }
        if (n < 2) { check(false, $"{what}: {n} rows"); return; }
        for (int j = n; j < pairs; j++)
            if (le.LaidVertex(2 * j) != le.LaidVertex(2 * j + 1)) { check(false, $"{what}: pair {j} past the {n} rows has width"); break; }
        if (n < pairs) check(le.LaidVertex(2 * n) == le.LaidVertex(2 * n - 2), $"{what}: the first pair past the rows is not the last left point");
        for (int j = 0; j < n; j++)
            if (le.LaidUv(2 * j) != new Vector2(j / (n - 1f), 0) || le.LaidUv(2 * j + 1) != new Vector2(j / (n - 1f), 1))
            { check(false, $"{what}: pair {j} uv {le.LaidUv(2 * j)} {le.LaidUv(2 * j + 1)}"); break; }
        var box = mesh.CustomAabb.Grow(0.01f);   // the culling box (the surface box stays the made one)
        for (int j = 0; j < 2 * n; j++)
            if (!box.HasPoint(le.LaidVertex(j))) { check(false, $"{what}: the box {mesh.CustomAabb} misses vertex {j}"); break; }
        var v0 = le.LaidVertex(0); var v1 = le.LaidVertex(1);
        var w0 = le.LaidVertex(2 * n - 2); var w1 = le.LaidVertex(2 * n - 1);
        // the pair's middle is the point, 96 apart, level
        void Pair(Vector3 a, Vector3 b, Vector3 at, string end)
        {
            check(((a + b) / 2).DistanceTo(at) < 0.01f, $"{what} {end}: middle {(a + b) / 2}, want {at}");
            check(Mathf.Abs(a.DistanceTo(b) - 96) < 0.01f, $"{what} {end}: width {a.DistanceTo(b)}");
            check(Mathf.Abs(a.Y - b.Y) < 0.01f, $"{what} {end}: not level ({a.Y}, {b.Y})");
        }
        Pair(v0, v1, from, "start");
        Pair(w0, w1, to, "end");
    }

}
