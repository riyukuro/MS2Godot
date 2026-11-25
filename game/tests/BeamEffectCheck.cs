using System.Collections.Generic;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2Godot.Net;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/beam_effect.tscn, game data) of the beam parts on a path segment's point list (BeamEffect,
/// RegionSkills.SpawnOn with the points), on the three ray effects that lay beams only (handy lightning, plasma chain, harmony
/// of soul):
/// - without a point list the effect makes nothing (no part counted, handle 0);
/// - with one it makes one beam per beam part under the holder node; each update lays the strip the client's formulas give
///   (<see cref="BeamStrip"/> on the node's rotation as client columns, the camera straight down): every laid pair is its
///   left and right row point, the first pair's middle the first point; the uv and triangles are the client's;
/// - the node turned a quarter about the up axis lays the strip of the client rotation facing +x (a directionCurve bends
///   with it);
/// - the material has the picked texture and the pick is one of the param's;
/// - a hidden beam lays nothing; the points moving move the strip;
/// - a 16 beam chain: the time per frame;
/// - 1000 updates allocate 0 B.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class BeamEffectCheck : Node3D
{
    static readonly string[] Rays =
    {
        "CH/Skill/Wizard/Eff_Wizard_HandyLightning_Ray_01.xml",
        "CH/Skill/HeavyGunner/Eff_HeavyGunner_PlasmaChain_Ray_01.xml",
        "CH/Skill/SoulBinder/Eff_SoulBinder_HarmonyofSoul2_Ray_01.xml",
    };
    static readonly StringName Tex = "tex";
    // the camera straight down, client space
    static readonly System.Numerics.Vector3 Down = new(0, 0, -1);

    public override void _Ready()
    {
        var fails = new List<string>();
        void Check(bool ok, string what) { if (!ok) fails.Add(what); }
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var skills = new RegionSkills(packs, builder);
        AddChild(skills);
        var effects = new EffectTable(packs);
        var cam = new Camera3D { Position = new Vector3(0, 2000, 0), RotationDegrees = new Vector3(-90, 0, 0) };
        AddChild(cam);
        cam.MakeCurrent();

        foreach (var ray in Rays)
        {
            var all = effects.Get(ray);
            var parts = new List<EffectStripParam>();
            if (all != null)
                foreach (var p in all)
                    if (p.TypeId == EffectSkipTable.TypeBeam && p.Strip != null && EffectRandom.ParamMade(all, p)) parts.Add(p.Strip);
            Check(parts.Count >= 2, $"{ray}: {parts.Count} beam parts");
            var node = new Node3D { Name = "Holder" };
            AddChild(node);
            int made = BeamEffect.Made;
            Check(skills.SpawnOn(ray, node, 0) == 0, $"{ray}: made without a point list");
            Check(BeamEffect.Made == made && Beams(node).Count == 0, $"{ray}: a beam without a point list");

            var points = new LightningPoints { From = new System.Numerics.Vector3(0, 0, 0), To = new System.Numerics.Vector3(300, -400, 0) };
            int h = skills.SpawnOn(ray, node, 0, points: points);
            var list = Beams(node);
            Check(h != 0 && BeamEffect.Made == made + parts.Count && list.Count == parts.Count,
                $"{ray}: handle {h}, made {BeamEffect.Made - made}, nodes {list.Count} of {parts.Count}");
            if (list.Count != parts.Count) { node.QueueFree(); continue; }
            for (int k = 0; k < list.Count; k++)
            {
                var be = list[k];
                var p = parts[k];
                var what = $"{System.IO.Path.GetFileNameWithoutExtension(ray)} beam {k}";
                int laid = BeamEffect.Laid;
                be._Process(1 / 60.0);
                Check(BeamEffect.Laid == laid + 1, $"{what}: laid {BeamEffect.Laid - laid}");
                // the holder at the identity: the client columns are the unit axes
                Laid(be, p, points, System.Numerics.Vector3.UnitX, System.Numerics.Vector3.UnitY, System.Numerics.Vector3.UnitZ, Check, what);
                var mat = ((ArrayMesh)be.Mesh).SurfaceGetMaterial(0) as ShaderMaterial;
                Check(be.Picked >= 0 && be.Picked < p.TexturesA.Length, $"{what}: picked {be.Picked} of {p.TexturesA.Length}");
                Check(mat?.GetShaderParameter(Tex).AsGodotObject() is Texture2D, $"{what}: the material has no texture");

                // a quarter turn about up (godot y): the client rotation facing +x (columns right (0, 1, 0), -direction (-1, 0, 0), up)
                node.RotationDegrees = new Vector3(0, 90, 0);
                be._Process(1 / 60.0);
                Laid(be, p, points, new(0, 1, 0), new(-1, 0, 0), System.Numerics.Vector3.UnitZ, Check, what + " turned");
                node.RotationDegrees = Vector3.Zero;

                points.To = new System.Numerics.Vector3(-200, 100, 50);
                be._Process(1 / 60.0);
                Laid(be, p, points, System.Numerics.Vector3.UnitX, System.Numerics.Vector3.UnitY, System.Numerics.Vector3.UnitZ, Check, what + " moved");
                points.To = new System.Numerics.Vector3(300, -400, 0);

                be.Visible = false;
                laid = BeamEffect.Laid;
                be._Process(1 / 60.0);
                Check(BeamEffect.Laid == laid, $"{what}: a hidden beam laid its strip");
                be.Visible = true;

                for (int i = 0; i < 20; i++) be._Process(1 / 60.0);
                laid = BeamEffect.Laid;
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 1000; i++) be._Process(1 / 60.0);
                long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
                Check(bytes == 0, $"{what}: 1000 updates allocated {bytes} B");
                Check(BeamEffect.Laid == laid + 1000, $"{what}: 1000 updates laid {BeamEffect.Laid - laid}");
                GD.Print($"beam effect: {what}: curve {p.BeamCurve}, {be.Pairs} pairs, width {p.Width}, {p.TexturesA.Length} textures, {bytes} B over 1000 updates");
            }
            if (ray != Rays[0]) node.QueueFree();
            else Shown = node;
        }
        Chain(skills, Check);
        var shot = System.Environment.GetEnvironmentVariable("MS2_BEAM_SHOT");
        if (!string.IsNullOrEmpty(shot) && DisplayServer.GetName() != "headless") { Drawn(shot, fails); return; }
        Done(fails);
    }

    Node3D Shown;

    void Done(List<string> fails)
    {
        foreach (var f in fails) GD.PrintErr("beam effect: " + f);
        GD.Print(fails.Count == 0 ? "beam effect: PASS" : $"beam effect: FAIL ({fails.Count})");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    /// <summary>With a renderer (MS2_BEAM_SHOT, not headless): after 10 drawn frames the handy lightning's beams light the
    /// frame. Saves the frame to the path given.</summary>
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
        GD.Print($"beam effect: drawn, {lit} lit pixels of {img.GetWidth() * img.GetHeight()}");
        if (lit < 200) fails.Add($"the drawn beams lit {lit} pixels");
        Done(fails);
    }

    /// <summary>The laid pairs are the client strip's rows for the node's client columns: pair j is (left j, right j) in godot
    /// space, the first pair's middle the first point, the uv (j / (n - 1), 0 or 1), the triangles the client's.</summary>
    static void Laid(BeamEffect be, EffectStripParam p, LightningPoints points, System.Numerics.Vector3 x, System.Numerics.Vector3 y,
        System.Numerics.Vector3 z, System.Action<bool, string> check, string what)
    {
        var want = new BeamStrip(p);
        check(want.Rebuild(points.From, points.To, x, y, z, Down), $"{what}: the client strip makes no curve");
        int n = want.Count;
        check(be.Pairs == n && n == BeamStrip.Samples(p), $"{what}: {be.Pairs} pairs, the strip {n}");
        if (be.Pairs != n || n < 2) return;
        for (int j = 0; j < n; j++)
        {
            if (be.LaidVertex(2 * j).DistanceTo(G(want.Left[j])) > 0.01f || be.LaidVertex(2 * j + 1).DistanceTo(G(want.Right[j])) > 0.01f)
            { check(false, $"{what}: pair {j} {be.LaidVertex(2 * j)} {be.LaidVertex(2 * j + 1)}, want {G(want.Left[j])} {G(want.Right[j])}"); break; }
        }
        var mid = (be.LaidVertex(0) + be.LaidVertex(1)) / 2;
        check(mid.DistanceTo(G(points.From)) < 0.01f, $"{what}: the first pair's middle {mid}");
        var mesh = (ArrayMesh)be.Mesh;
        using (var arrays = mesh.SurfaceGetArrays(0))
        {
            var tri = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            var uv = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            bool ok = tri.Length == (n - 1) * 6 && uv.Length == 2 * n;
            for (int i = 0; ok && i < n - 1; i++)
                ok = tri[6 * i] == 2 * i && tri[6 * i + 1] == 2 * i + 1 && tri[6 * i + 2] == 2 * i + 2
                    && tri[6 * i + 3] == 2 * i + 2 && tri[6 * i + 4] == 2 * i + 1 && tri[6 * i + 5] == 2 * i + 3;
            for (int j = 0; ok && j < n; j++)
                ok = uv[2 * j] == new Vector2(j / (n - 1f), 0) && uv[2 * j + 1] == new Vector2(j / (n - 1f), 1);
            check(ok, $"{what}: the surface's triangles or uv are not the client's");
        }
        var box = mesh.CustomAabb.Grow(0.01f);
        for (int j = 0; j < 2 * n; j++)
            if (!box.HasPoint(be.LaidVertex(j))) { check(false, $"{what}: the box {mesh.CustomAabb} misses vertex {j}"); break; }
    }

    /// <summary>A 16 target chain of the handy lightning (its beams on 16 segments 600 long), updated 500 frames.</summary>
    static void Chain(RegionSkills skills, System.Action<bool, string> check)
    {
        var holder = new Node3D { Name = "Chain" };
        skills.AddChild(holder);
        for (int i = 0; i < 16; i++)
            skills.SpawnOn(Rays[0], holder, 0, points: new LightningPoints { From = new System.Numerics.Vector3(600 * i, 0, 0), To = new System.Numerics.Vector3(600 * i + 600, 0, 0) });
        var list = Beams(holder);
        check(list.Count >= 32, $"chain: {list.Count} beams");
        void Frame() { for (int i = 0; i < list.Count; i++) list[i]._Process(1 / 60.0); }
        for (int i = 0; i < 50; i++) Frame();
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int i = 0; i < 500; i++) Frame();
        var took = System.Diagnostics.Stopwatch.GetElapsedTime(t0);
        long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
        check(bytes == 0, $"chain: 500 frames allocated {bytes} B");
        GD.Print($"beam effect: chain of 16 segments, {list.Count} beams, {took.TotalMilliseconds * 1000 / 500:F1} us per frame, {bytes} B");
        holder.QueueFree();
    }

    static List<BeamEffect> Beams(Node n)
    {
        var r = new List<BeamEffect>();
        for (int i = 0; i < n.GetChildCount(); i++)
            if (n.GetChild(i) is BeamEffect be) r.Add(be);
        return r;
    }

    static Vector3 G(System.Numerics.Vector3 v) => new(v.X, v.Z, -v.Y);
}
