using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using Ms2.Formats.Model;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/particle_space.tscn, no game data) of where live particles go when their effect or their
/// system's node moves, for the two values of NiPSParticleSystem's "World Space" (nif.xml NiPSParticleSystem, default true).
/// On, the particles are kept in world coordinates and the system is drawn with the identity rotation and translate
/// (NiPSParticleSystem::UpdateWorldData, NiPSParticleSystem.cpp:509-520): the ones already emitted stay where they are in the
/// world and only new ones start from the moved emitter. Off, they live in the system node's space and are drawn with its
/// world transform: they all move with it. Two moves, a frame each, after ten particles are emitted at rest:
/// - the effect's parent (the actor an attach type 1 part rides) moves and turns: world space stays, local follows;
/// - the system's node, keyed in the file, jumps with the actor still: world space stays, local follows the node.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class ParticleSpaceCheck : Node
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly Vector3 Jump = new(50, 0, 0);
    const float Off = 10;

    static ParticleSystemDef System(bool worldSpace) => new()
    {
        Node = 1, Name = worldSpace ? "world" : "local", WorldSpace = worldSpace, MaxParticles = 16,
        Material = new EffectMesh
        {
            Node = 1, Mesh = new ModelMesh
            {
                Name = "material", FileName = "material", Positions = Array.Empty<float>(), Indices = Array.Empty<int>(),
                Ambient = new float[] { 0, 0, 0 }, Diffuse = new float[] { 0, 0, 0 }, Emissive = new float[] { 1, 1, 1 },
            },
        },
        Emitters =
        {
            new EmitterDef
            {
                Kind = EmitterKind.Box, Speed = 0, Size = 1, Lifespan = 10, Rate = lt => lt < 0.15 ? 100 : 0,
                RateCtl = new TimeCtl { Stop = 10 },
            },
        },
    };

    public override void _Ready()
    {
        var fails = new List<string>();
        try
        {
            foreach (bool worldSpace in new[] { true, false })
            {
                Run(fails, worldSpace, nodeKeyed: false);
                Run(fails, worldSpace, nodeKeyed: true);
            }
        }
        catch (Exception e) { fails.Add(e.ToString()); }
        foreach (var f in fails) GD.PrintErr("particle space: " + f);
        GD.Print(fails.Count == 0 ? "particle space: PASS" : $"particle space: FAIL ({fails.Count})");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    void Run(List<string> fails, bool worldSpace, bool nodeKeyed)
    {
        string what = $"{(worldSpace ? "world space" : "local")} system, {(nodeKeyed ? "its node keyed to jump" : "the actor moving")}";
        var sc = new EffectScene { Duration = 10 };
        sc.Nodes.Add(new EffectNode { Name = "root" });
        // the system's node off the effect's origin, so the actor's turn moves it too
        var node = new EffectNode { Name = "system", Parent = 0, Local = Xform.Identity with { T = new Vec3(Off, 0, 0) } };
        // the node's key: at rest until 0.25 s, then Jump on (KfChannel T, file space)
        if (nodeKeyed) node.Anim = new KfChannel { T = lt => lt < 0.25 ? new Vec3(Off, 0, 0) : new Vec3(Off + Jump.X, Jump.Y, Jump.Z) };
        sc.Nodes.Add(node);
        sc.Systems.Add(System(worldSpace));
        var actor = new Node3D { Name = "actor" };
        AddChild(actor);
        var fx = new EffectInstance(sc, new ModelBuilder(null, null), 10, 1);
        actor.AddChild(fx);
        fx.FileSpace = Transform3D.Identity;
        fx._Process(0.1);                      // t 0.1: ten particles emitted at the node
        fx._Process(0.1);                      // t 0.2: none emitted, nothing moved
        var before = Particles(fx);
        var nodeBefore = fx.NodePoint(1);
        if (before.Count == 0) { fails.Add($"{what}: no particles emitted"); actor.QueueFree(); return; }
        if (!nodeKeyed) actor.Transform = new Transform3D(new Basis(Vector3.Up, Mathf.Pi / 2), new Vector3(5, 0, 3));
        fx._Process(0.1);                      // t 0.3: the move seen (the key past 0.25 s)
        var after = Particles(fx);
        var nodeAfter = fx.NodePoint(1);
        if (after.Count != before.Count) { fails.Add($"{what}: {before.Count} particles became {after.Count}"); actor.QueueFree(); return; }
        if (nodeAfter.DistanceTo(nodeBefore) < 1) fails.Add($"{what}: the system's node did not move ({nodeBefore} -> {nodeAfter})");
        var follow = nodeAfter - nodeBefore;
        float worst = 0;
        for (int i = 0; i < before.Count; i++)
        {
            // world space: the same world place; local: moved as the node's world point moved (all start at the node, at rest)
            var want = worldSpace ? before[i] : before[i] + follow;
            worst = Math.Max(worst, after[i].DistanceTo(want));
        }
        if (worst > 0.01f) fails.Add($"{what}: a live particle is {worst:F3} off ({(worldSpace ? "it should stay where it was" : "it should move with the node")}; node moved by {follow})");
        GD.Print($"particle space: {what}: {before.Count} particles, the node moved by {follow}, the particles by {after[0] - before[0]}");
        actor.QueueFree();
    }

    /// <summary>The live particles' world places (the effect keeps them in its root's space).</summary>
    static List<Vector3> Particles(EffectInstance fx)
    {
        var z = (Node3D)typeof(EffectInstance).GetField("z", Private)!.GetValue(fx);
        var list = new List<Vector3>();
        foreach (var ps in (IEnumerable)typeof(EffectInstance).GetField("systems", Private)!.GetValue(fx))
        {
            var pos = (Vector3[])ps.GetType().GetField("Pos")!.GetValue(ps);
            int n = (int)ps.GetType().GetField("Count")!.GetValue(ps);
            for (int i = 0; i < n; i++) list.Add(z.GlobalTransform * pos[i]);
        }
        return list;
    }
}
