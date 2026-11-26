using System;
using System.Collections.Generic;
using Godot;
using Ms2.Formats.Model;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/effect_vis.tscn, no game data): an NiVisController and an NiPSResetOnLoopCtlr in an effect.
/// A node culled in the file with vis keys hidden at 0.2 s and shown at 0.5 s (an assassin web piece) hides the mesh under
/// it until 0.5 s; an animated node keyed shown, hidden at 0.5 s, shown at 1 s (a chest) does the same on its own mesh;
/// a culled node with no keys hides its child's mesh all the time; another mesh stays shown. Before, the meshes under the
/// vis nodes were drawn all the time and a culled parent's child was drawn. A system on a culled node simulates and draws
/// nothing (its quads and a mesh system's clones are hidden). A mesh system whose reset controller loops every 0.5 s loses
/// every particle at the wrap and its clones are reused (before, the particles lived on). The steps allocate 0 B.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class EffectVisCheck : Node
{
    static ModelMesh Quad(string name) => new()
    {
        Name = name, FileName = name, Positions = new float[] { -1, -1, 0, 1, -1, 0, 1, 1, 0, -1, 1, 0 },
        Normals = new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1 }, Indices = new[] { 0, 1, 2, 0, 2, 3 },
        Ambient = new float[] { 0, 0, 0 }, Diffuse = new float[] { 0, 0, 0 }, Emissive = new float[] { 1, 1, 1 },
    };

    static EffectMesh Material() => new()
    {
        Node = 0, Mesh = new ModelMesh
        {
            Name = "material", FileName = "material", Positions = Array.Empty<float>(), Indices = Array.Empty<int>(),
            Ambient = new float[] { 0, 0, 0 }, Diffuse = new float[] { 0, 0, 0 }, Emissive = new float[] { 1, 1, 1 },
        },
    };

    static EmitterDef Emitter() => new()
    {
        Kind = EmitterKind.Box, Speed = 10, Size = 1, Lifespan = 10, Rate = _ => 100, RateCtl = new TimeCtl { Stop = 10 },
    };

    public override void _Ready()
    {
        var fails = new List<string>();
        try { Vis(fails); } catch (Exception e) { fails.Add("vis: " + e); }
        try { CulledSystems(fails); } catch (Exception e) { fails.Add("culled systems: " + e); }
        try { Reset(fails); } catch (Exception e) { fails.Add("reset: " + e); }
        foreach (var f in fails) GD.PrintErr("effect vis: " + f);
        GD.Print(fails.Count == 0 ? "effect vis: PASS" : $"effect vis: FAIL ({fails.Count})");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    EffectInstance Start(EffectScene sc)
    {
        var fx = new EffectInstance(sc, new ModelBuilder(null, null), 10, 1);
        AddChild(fx);
        fx.FileSpace = Transform3D.Identity;
        return fx;
    }

    static Dictionary<string, MeshInstance3D> ByName(EffectInstance fx)
    {
        var d = new Dictionary<string, MeshInstance3D>();
        foreach (var c in fx.GetChild<Node3D>(0).GetChildren()) if (c is MeshInstance3D m) d[m.Name] = m;
        return d;
    }

    void Vis(List<string> fails)
    {
        var sc = new EffectScene { Duration = 10 };
        sc.Nodes.Add(new EffectNode
        {
            Name = "web", Culled = true,
            Vis = new BoolTrack { Times = new[] { 0.2, 0.5 }, Values = new[] { false, true }, KeyType = 5 },
            VisCtl = new TimeCtl { Start = 0.2, Stop = 0.5, Cycle = 2 },
        });
        sc.Nodes.Add(new EffectNode { Name = "piece", Parent = 0 });
        sc.Nodes.Add(new EffectNode
        {
            Name = "chest", Anim = new KfChannel { T = t => new Vec3(t, 0, 0) },
            Vis = new BoolTrack { Times = new[] { 0.0, 0.5, 1.0 }, Values = new[] { true, false, true }, KeyType = 5 },
            VisCtl = new TimeCtl { Start = 0, Stop = 1, Cycle = 2 },
        });
        sc.Nodes.Add(new EffectNode { Name = "culled", Culled = true });
        sc.Nodes.Add(new EffectNode { Name = "under", Parent = 3 });
        sc.Nodes.Add(new EffectNode { Name = "other" });
        foreach (var (name, node) in new[] { ("piece", 1), ("chest", 2), ("under", 4), ("other", 5) })
            sc.Meshes.Add(new EffectMesh { Mesh = Quad(name), Node = node });
        var fx = Start(sc);
        var m = ByName(fx);
        if (m.Count != 4) { fails.Add($"vis: {m.Count} meshes, want 4"); return; }
        double at = 0;
        void Expect(double to, bool piece, bool chest)
        {
            while (at < to - 1e-6) { fx._Process(0.02); at += 0.02; }
            var want = new Dictionary<string, bool> { ["piece"] = piece, ["chest"] = chest, ["under"] = false, ["other"] = true };
            foreach (var (k, v) in want)
                if (m[k].Visible != v) fails.Add($"vis: t {at:F2}: {k} visible {m[k].Visible}, want {v}");
        }
        Expect(0.1, false, true);
        Expect(0.4, false, true);
        Expect(0.6, true, false);
        Expect(0.9, true, false);
        Expect(1.2, true, true);
        GD.Print($"effect vis: vis at {at:F2}: piece {m["piece"].Visible}, chest {m["chest"].Visible}, under {m["under"].Visible}, other {m["other"].Visible}");
        long bytes = CheckAlloc.Least(() => { for (int i = 0; i < 50; i++) fx._Process(0.02); });
        if (bytes != 0) fails.Add($"vis: steps allocated {bytes} B");
        fx.QueueFree();
    }

    void CulledSystems(List<string> fails)
    {
        var sc = new EffectScene { Duration = 10 };
        sc.Nodes.Add(new EffectNode { Name = "hidden", Culled = true });
        sc.Nodes.Add(new EffectNode { Name = "system", Parent = 0 });
        sc.Nodes.Add(new EffectNode { Name = "master" });
        sc.Meshes.Add(new EffectMesh { Mesh = Quad("clone"), Node = 2, Master = true });
        sc.Systems.Add(new ParticleSystemDef { Node = 1, Name = "quads", MaxParticles = 256, Material = Material(), Emitters = { Emitter() } });
        sc.Systems.Add(new ParticleSystemDef
        {
            Node = 1, Name = "mesh", MaxParticles = 256, MeshParticles = true, MasterNodes = new[] { new[] { 2 } }, MasterMeshes = new[] { new[] { 0 } }, Material = Material(),
            NormalMethod = AlignedQuad.LocalFixed, UpMethod = AlignedQuad.LocalFixed,
            NormalDirection = new Vec3(1, 0, 0), UpDirection = new Vec3(0, 0, 1), HasRotations = false, Emitters = { Emitter() },
        });
        var fx = Start(sc);
        for (int f = 0; f < 25; f++) fx._Process(0.02);
        int live = fx.MeshParticleCount(out int cloned), shownClones = 0, quads = 0; bool quadsShown = false;
        foreach (var c in fx.FindChildren("*", nameof(MeshInstance3D), true, false)) if (((MeshInstance3D)c).Visible) shownClones++;
        foreach (var c in fx.FindChildren("*", nameof(MultiMeshInstance3D), true, false))
        {
            var mm = (MultiMeshInstance3D)c;
            quads += mm.Multimesh.VisibleInstanceCount; quadsShown |= mm.Visible;
        }
        GD.Print($"effect vis: culled systems: {live} mesh particles ({cloned} cloned, {shownClones} shown), {quads} quads (shown {quadsShown})");
        if (live < 40 || cloned != live) fails.Add($"culled systems: {live} mesh particles, {cloned} cloned; the system should still simulate");
        if (quads < 40) fails.Add($"culled systems: {quads} quads; the system should still simulate");
        if (shownClones != 0) fails.Add($"culled systems: {shownClones} clones shown under a culled node");
        if (quadsShown) fails.Add("culled systems: the quads are shown under a culled node");
        fx.QueueFree();
    }

    void Reset(List<string> fails)
    {
        var sc = new EffectScene { Duration = 10 };
        sc.Nodes.Add(new EffectNode { Name = "system" });
        sc.Nodes.Add(new EffectNode { Name = "master" });
        sc.Meshes.Add(new EffectMesh { Mesh = Quad("clone"), Node = 1, Master = true });
        sc.Systems.Add(new ParticleSystemDef
        {
            Node = 0, Name = "mesh", MaxParticles = 256, MeshParticles = true, MasterNodes = new[] { new[] { 1 } }, MasterMeshes = new[] { new[] { 0 } }, Material = Material(),
            NormalMethod = AlignedQuad.LocalFixed, UpMethod = AlignedQuad.LocalFixed,
            NormalDirection = new Vec3(1, 0, 0), UpDirection = new Vec3(0, 0, 1), HasRotations = false, Emitters = { Emitter() },
            ResetCtl = new TimeCtl { Start = 0, Stop = 0.5, Cycle = 0 },
        });
        var fx = Start(sc);
        int Shown() { int n = 0; foreach (var c in fx.FindChildren("*", nameof(MeshInstance3D), true, false)) if (((MeshInstance3D)c).Visible) n++; return n; }
        int Made() => fx.FindChildren("*", nameof(MeshInstance3D), true, false).Count;
        for (int f = 0; f < 24; f++) fx._Process(0.02);        // t 0.48
        int before = fx.MeshParticleCount(out _), shownBefore = Shown(), madeBefore = Made();
        for (int f = 0; f < 2; f++) fx._Process(0.02);         // t 0.52: wrapped at 0.5
        int after = fx.MeshParticleCount(out _), shownAfter = Shown();
        for (int f = 0; f < 23; f++) fx._Process(0.02);        // t 0.98
        int again = fx.MeshParticleCount(out _), madeAgain = Made();
        GD.Print($"effect vis: reset: {before} particles ({shownBefore} shown, {madeBefore} clones) at 0.48 s, {after} ({shownAfter} shown) at 0.52 s, {again} at 0.98 s ({madeAgain} clones made)");
        if (before < 40 || shownBefore != before) fails.Add($"reset: {before} particles, {shownBefore} shown before the wrap, want about 48 all shown");
        if (after > 4 || shownAfter != after) fails.Add($"reset: {after} particles, {shownAfter} shown just after the wrap, want only those born since (2-4)");
        if (again < 40) fails.Add($"reset: {again} particles at 0.98 s, want the system emitting again");
        if (madeAgain > madeBefore + 4) fails.Add($"reset: {madeAgain} clones made by 0.98 s after {madeBefore}; the reset's clones are not reused");
        fx.QueueFree();
    }
}
