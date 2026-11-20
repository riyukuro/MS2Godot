using System.Numerics;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>Particle bomb and drag forces: their objects, decay and symmetry are read, and the kernel helpers match
/// NiPSForceHelpers::ApplyBombForce / ApplyDragForce (NiPSForceHelpers.inl:29-164).</summary>
public class ParticleForceTests
{
    static EffectScene Load(string entry)
    {
        var (packs, spec) = NifEnv.Open();
        return EffectScene.Load(NifFile.Load(spec, packs.Read("Resource/Model/Effect", entry)));
    }

    /// <summary>The flamethrower's fire and smoke are pushed out by a bomb on PBomb01 and slowed by a drag with its own object;
    /// both must be read with their node, or the step leaves them out and the smoke piles up at the nozzle.</summary>
    [Test]
    public void FlamethrowerBombAndDragHaveTheirObjects()
    {
        var sc = Load("ch/skill/heavygunner/eff_heavygunner_flamethrower_loop_01_a.nif");
        var withBomb = sc.Systems.Where(s => s.Forces.Any(f => f.Kind == ForceKind.Bomb)).ToList();
        Assert.That(withBomb, Has.Count.GreaterThanOrEqualTo(2), "fire and smoke");
        foreach (var s in withBomb)
        {
            var bomb = s.Forces.Single(f => f.Kind == ForceKind.Bomb);
            Assert.That(bomb.Node, Is.GreaterThanOrEqualTo(0), s.Name);
            Assert.That(sc.Nodes[bomb.Node].Name, Is.EqualTo("PBomb01"), s.Name);
            Assert.That(bomb.DeltaV, Is.EqualTo(18000).Within(1e-3), s.Name);
            Assert.That(bomb.SymmType, Is.EqualTo(0), s.Name);
            Assert.That(bomb.DecayType, Is.EqualTo(0), s.Name);
            var drags = s.Forces.Where(f => f.Kind == ForceKind.Drag).ToList();
            Assert.That(drags, Is.Not.Empty, s.Name);
            Assert.That(drags.All(f => f.Node >= 0), s.Name);
            // the drag comes before the bomb in the list, so it slows the velocity the bomb then adds to
            Assert.That(s.Forces.IndexOf(drags[0]), Is.LessThan(s.Forces.IndexOf(bomb)), s.Name);
        }
    }

    /// <summary>The rabbit ball's bomb decays linearly over 50000.</summary>
    [Test]
    public void RabbitBallBombDecaysLinearly()
    {
        var sc = Load("npc/skill/rabbitshortbandage/eff_rabbitshortbandage_attack_02_b_ball.nif");
        var bombs = sc.Systems.SelectMany(s => s.Forces).Where(f => f.Kind == ForceKind.Bomb).ToList();
        Assert.That(bombs, Is.Not.Empty);
        Assert.That(bombs.All(b => b.DecayType == 1 && b.Node >= 0 && Math.Abs(b.Decay - 50000) < 1e-3));
    }

    /// <summary>ApplyBombForce: spherical pushes along pos - bombPos, scaled by decay * deltaV * dt; linear decay fades
    /// with distance and does nothing past Decay; planar pushes along the axis, toward the side the particle is on.</summary>
    [Test]
    public void BombForceMath()
    {
        var p = new Vector3(3, 4, 0);
        var v = ParticleForces.Bomb(Vector3.Zero, p, Vector3.Zero, Vector3.UnitZ, 10, 0, 0, 0, 0.5f);
        Assert.That(v.X, Is.EqualTo(3f).Within(1e-5)); Assert.That(v.Y, Is.EqualTo(4f).Within(1e-5));
        v = ParticleForces.Bomb(Vector3.Zero, p, Vector3.Zero, Vector3.UnitZ, 10, 10, 1, 0, 0.5f);   // (10-5)/10
        Assert.That(v.Length(), Is.EqualTo(2.5f).Within(1e-5));
        Assert.That(ParticleForces.Bomb(Vector3.One, p, Vector3.Zero, Vector3.UnitZ, 10, 4, 1, 0, 0.5f), Is.EqualTo(Vector3.One));
        v = ParticleForces.Bomb(Vector3.Zero, new Vector3(1, 0, -2), Vector3.Zero, Vector3.UnitZ, 10, 0, 0, 2, 1f);
        Assert.That(v, Is.EqualTo(new Vector3(0, 0, -10)));
        v = ParticleForces.Bomb(Vector3.Zero, new Vector3(2, 0, 7), Vector3.Zero, Vector3.UnitZ, 10, 0, 0, 1, 1f); // cylindrical
        Assert.That(v.X, Is.EqualTo(10f).Within(1e-5)); Assert.That(v.Z, Is.EqualTo(0f).Within(1e-5));
    }

    /// <summary>ApplyDragForce: the part along the axis shrinks by Percentage per 1/30 s, clamped to a full stop; the
    /// other parts are kept; past RangeFalloff nothing happens.</summary>
    [Test]
    public void DragForceMath()
    {
        var vel = new Vector3(10, 5, 0);
        var v = ParticleForces.Drag(vel, Vector3.Zero, Vector3.Zero, Vector3.UnitX, 0.5f, float.MaxValue, float.MaxValue, 0.0333333f);
        Assert.That(v.X, Is.EqualTo(5f).Within(1e-4)); Assert.That(v.Y, Is.EqualTo(5f));
        v = ParticleForces.Drag(vel, Vector3.Zero, Vector3.Zero, Vector3.UnitX, 0.7f, float.MaxValue, float.MaxValue, 0.1f);
        Assert.That(v.X, Is.EqualTo(0f).Within(1e-5)); Assert.That(v.Y, Is.EqualTo(5f));
        Assert.That(ParticleForces.Drag(vel, new Vector3(20, 0, 0), Vector3.Zero, Vector3.UnitX, 0.5f, 5, 10, 0.0333333f), Is.EqualTo(vel));
        v = ParticleForces.Drag(vel, new Vector3(7.5f, 0, 0), Vector3.Zero, Vector3.UnitX, 0.5f, 5, 10, 0.0333333f); // half range
        Assert.That(v.X, Is.EqualTo(7.5f).Within(1e-4));
    }
}
