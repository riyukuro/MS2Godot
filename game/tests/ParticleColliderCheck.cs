using System;
using System.Collections.Generic;
using Godot;
using Ms2.Formats.Model;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/particle_collider.tscn, no game data): a box emitter at the origin shoots particles up +z at
/// speed 40 toward a 20 x 20 planar collider whose object sits at z = 10 (NiPSPlanarCollider, run by
/// NiPSSimulatorCollidersKernel after the forces). With bounce 1 no particle passes z = 10 and the ones that hit come back
/// down past the emitter; with die on collide the ones that reach the plane are removed. A particle that bounced ages
/// in that step by the time after the hit only (UpdateParticleAgeDeath takes now minus its last update, which the hit set),
/// so a short-lived one outlives the step it would die in when aged by the whole step. Before the colliders every particle
/// flew through the plane. Also: steps with a collider allocate 0 B.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class ParticleColliderCheck : Node
{
    const float Speed = 40f, PlaneZ = 10f;

    static EffectScene Scene(bool die, float lifespan, float speed, float rotSpeed = 0)
    {
        var sc = new EffectScene { Duration = 10 };
        sc.Nodes.Add(new EffectNode { Name = "system" });
        sc.Nodes.Add(new EffectNode { Name = "deflector", Local = Xform.Identity with { T = new Vec3(0, 0, PlaneZ) } });
        var d = new ParticleSystemDef
        {
            Node = 0, Name = die ? "die" : "bounce", MaxParticles = 256, Colliders = 1,
            Material = new EffectMesh
            {
                Node = 0, Mesh = new ModelMesh
                {
                    Name = "material", FileName = "material", Positions = Array.Empty<float>(), Indices = Array.Empty<int>(),
                    Ambient = new float[] { 0, 0, 0 }, Diffuse = new float[] { 0, 0, 0 }, Emissive = new float[] { 1, 1, 1 },
                },
            },
            Emitters =
            {
                new EmitterDef
                {
                    Kind = EmitterKind.Box, Speed = speed, Size = 1, Lifespan = lifespan, Rate = _ => 100, RotSpeed = rotSpeed,
                    RateCtl = new TimeCtl { Stop = 10 },
                },
            },
        };
        d.ColliderList.Add(new ColliderDef { Type = "NiPSPlanarCollider", Node = 1, Width = 20, Height = 20, Bounce = die ? 0 : 1, DieOnCollide = die });
        sc.Systems.Add(d);
        return sc;
    }

    public override void _Ready()
    {
        var fails = new List<string>();
        try { Run(fails); }
        catch (Exception e) { fails.Add(e.ToString()); }
        foreach (var f in fails) GD.PrintErr("particle collider: " + f);
        GD.Print(fails.Count == 0 ? "particle collider: PASS" : $"particle collider: FAIL ({fails.Count})");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    /// <summary>The live particles' positions (the MultiMesh origins; the file space is the identity).</summary>
    static List<Vector3> Positions(EffectInstance fx)
    {
        var list = new List<Vector3>();
        foreach (var c in fx.FindChildren("*", nameof(MultiMeshInstance3D), true, false))
        {
            var mm = ((MultiMeshInstance3D)c).Multimesh;
            if (mm == null) continue;
            var buf = mm.Buffer;
            for (int i = 0; i < mm.VisibleInstanceCount; i++) list.Add(new Vector3(buf[i * 20 + 3], buf[i * 20 + 7], buf[i * 20 + 11]));
        }
        return list;
    }

    /// <summary>The live particles' positions and quad x axes (the MultiMesh rows' first column).</summary>
    static List<(Vector3 P, Vector3 X)> Quads(EffectInstance fx)
    {
        var list = new List<(Vector3, Vector3)>();
        foreach (var c in fx.FindChildren("*", nameof(MultiMeshInstance3D), true, false))
        {
            var mm = ((MultiMeshInstance3D)c).Multimesh;
            if (mm == null) continue;
            var buf = mm.Buffer;
            for (int i = 0; i < mm.VisibleInstanceCount; i++)
                list.Add((new Vector3(buf[i * 20 + 3], buf[i * 20 + 7], buf[i * 20 + 11]), new Vector3(buf[i * 20], buf[i * 20 + 4], buf[i * 20 + 8])));
        }
        return list;
    }

    EffectInstance Start(bool die, float lifespan, float speed = Speed, float rotSpeed = 0)
    {
        var fx = new EffectInstance(Scene(die, lifespan, speed, rotSpeed), new ModelBuilder(null, null), 10, 1);
        AddChild(fx);
        fx.FileSpace = Transform3D.Identity;
        return fx;
    }

    void Run(List<string> fails)
    {
        // bounce 1: after 1 s the first particles hit at 0.25 s and are back below the emitter
        var fx = Start(false, 10);
        float top = float.MinValue; int below = 0, n = 0;
        for (int f = 0; f < 50; f++)
        {
            fx._Process(0.02);
            foreach (var p in Positions(fx)) { top = MathF.Max(top, p.Z); if (f == 49 && p.Z < -1) below++; if (f == 49) n++; }
        }
        GD.Print($"particle collider: bounce: {n} live, highest z {top:F3}, {below} back below the emitter");
        if (!(top <= PlaneZ + 1e-3f)) fails.Add($"bounce: a particle reached z {top:F3}, past the plane at {PlaneZ}");
        if (below < 10) fails.Add($"bounce: {below} particles came back below the emitter, want at least 10");
        // the step allocates nothing with a collider
        long bytes = CheckAlloc.Least(() => { for (int i = 0; i < 50; i++) fx._Process(0.02); });
        GD.Print($"particle collider: 250 steps with a collider: {bytes} B");
        if (bytes != 0) fails.Add($"steps with a collider allocated {bytes} B");
        fx.QueueFree();

        // die on collide: nothing passes or sits on the plane, and only the particles born in the last 0.25 s live
        fx = Start(true, 10);
        top = float.MinValue; n = 0;
        for (int f = 0; f < 50; f++)
        {
            fx._Process(0.02);
            var ps = Positions(fx);
            foreach (var p in ps) top = MathF.Max(top, p.Z);
            n = ps.Count;
        }
        GD.Print($"particle collider: die: {n} live, highest z {top:F3}");
        if (!(top < PlaneZ)) fails.Add($"die: a particle reached z {top:F3}, the plane at {PlaneZ} kills it");
        if (n > 30) fails.Add($"die: {n} particles live, want those born in the last 0.25 s (about 25)");
        fx.QueueFree();

        // a bounced particle ages by the time after the hit only: at speed 30 with 0.1 s steps a particle born at a step's
        // end is at z 3, 6, 9, then hits the plane 1/30 s into its fourth step and ends it at z 8 with age 0.3 + 0.0667.
        // With a life of 0.38 it lives there; aged by the whole step (0.4) it would die. No unbounced particle is at z 8.
        // The angle follows the age too (initial angle + speed x age, UpdateParticleRotation): at 3 rad/s the bounced
        // particle (age 0.3667) is 0.2 rad past one at z 9 (age 0.3), not 0.3 as when it turned by the whole step.
        fx = Start(false, 0.38f, 30, 3);
        int at8 = 0;
        for (int f = 0; f < 10; f++) fx._Process(0.1);
        Vector3 x8 = default, x9 = default;
        foreach (var (p, x) in Quads(fx))
        {
            if (MathF.Abs(p.Z - 8) < 0.01f) { at8++; x8 = x; }
            if (MathF.Abs(p.Z - 9) < 0.01f) x9 = x;
        }
        GD.Print($"particle collider: short life: {at8} bounced particles alive at z 8");
        if (at8 == 0) fails.Add("short life: no bounced particle lives at z 8; the hit's time was counted in its age");
        else if (x9 == default) fails.Add("short life: no particle at z 9 to compare the angle with");
        else
        {
            float turn = MathF.Acos(Math.Clamp(x8.Dot(x9) / (x8.Length() * x9.Length()), -1f, 1f));
            GD.Print($"particle collider: bounced angle {turn:F4} rad past the z 9 particle");
            if (MathF.Abs(turn - 0.2f) > 0.005f) fails.Add($"short life: the bounced particle turned {turn:F4} rad past the z 9 one, want 0.2 (its age's)");
        }
        fx.QueueFree();
    }
}
