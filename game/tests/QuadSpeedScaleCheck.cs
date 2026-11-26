using System;
using System.Collections.Generic;
using Godot;
using Ms2.Formats.Model;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/quad_speed_scale.tscn, no game data): an aligned quad system with a speed scale on U, its node
/// scaled 2.5 and its box emitter at speed 40, once in world space and once in local space. The client keeps the system's
/// scale out of the particle data in both spaces (NiPSParticleSystem::UpdateWorldData, NiPSParticleSystem.cpp:509-520; the
/// emitter only rotates the velocity, NiPSBoxEmitter.cpp:100-103), so the kernel sees speed 40 in both and the quad's U
/// side is SpeedScale(amount, limit, rest, 40) times its V side. Before the fix a world-space system took the spawned
/// velocity times the world scale (speed 100 here). Quits with 0 when both hold, else 1.
/// </summary>
public partial class QuadSpeedScaleCheck : Node
{
    const float Scale = 2.5f, Speed = 40f;

    static ParticleSystemDef System(bool worldSpace) => new()
    {
        Node = 0, Name = worldSpace ? "world" : "local", Aligned = true, WorldSpace = worldSpace, MaxParticles = 8,
        NormalMethod = AlignedQuad.LocalFixed, UpMethod = AlignedQuad.LocalFixed,
        NormalDirection = new Vec3(0, 0, 1), UpDirection = new Vec3(0, 1, 0), HasRotations = false,
        ScaleAmountU = 0.1, ScaleLimitU = 3, ScaleRestU = 1, ScaleAmountV = 0, ScaleLimitV = 1, ScaleRestV = 1,
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
                Kind = EmitterKind.Box, Speed = Speed, Size = 1, Lifespan = 10, Rate = _ => 100,
                RateCtl = new TimeCtl { Stop = 10 },
            },
        },
    };

    public override void _Ready()
    {
        var fails = new List<string>();
        try { Run(fails); }
        catch (Exception e) { fails.Add(e.ToString()); }
        foreach (var f in fails) GD.PrintErr("quad speed scale: " + f);
        GD.Print(fails.Count == 0 ? "quad speed scale: PASS" : $"quad speed scale: FAIL ({fails.Count})");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    void Run(List<string> fails)
    {
        foreach (bool worldSpace in new[] { true, false })
        {
            var sc = new EffectScene { Duration = 10 };
            sc.Nodes.Add(new EffectNode { Name = "system", Local = Xform.Identity with { S = Scale } });
            var d = System(worldSpace);
            sc.Systems.Add(d);
            var fx = new EffectInstance(sc, new ModelBuilder(null, null), 10, 1);
            AddChild(fx);
            fx.FileSpace = Transform3D.Identity;
            fx._Process(0.1);
            MultiMesh mm = null;
            foreach (var c in fx.FindChildren("*", nameof(MultiMeshInstance3D), true, false)) mm ??= ((MultiMeshInstance3D)c).Multimesh;
            if (mm == null || mm.VisibleInstanceCount <= 0) { fails.Add($"{d.Name}: no quads"); continue; }
            var buf = mm.Buffer;
            float amt = AlignedQuad.NormalizedAmount(d.ScaleAmountU, d.ScaleLimitU, d.ScaleRestU);
            float want = AlignedQuad.SpeedScale(amt, (float)d.ScaleLimitU, (float)d.ScaleRestU, Speed);
            for (int i = 0; i < mm.VisibleInstanceCount; i++)
            {
                int o = i * 20;
                float u = new Vector3(buf[o], buf[o + 4], buf[o + 8]).Length(), v = new Vector3(buf[o + 1], buf[o + 5], buf[o + 9]).Length();
                float got = v > 0 ? u / v : float.NaN;
                if (!(MathF.Abs(got - want) < 1e-3f)) { fails.Add($"{d.Name} quad {i}: U over V {got:F4}, want {want:F4} (speed {Speed})"); break; }
                if (i == 0) GD.Print($"quad speed scale: {d.Name} U over V {got:F4}, want {want:F4}");
            }
            fx.QueueFree();
        }
    }
}
