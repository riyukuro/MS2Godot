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
    /// <summary>ApplyPlanarGravityForce / ApplySphericalGravityForce: the strength is Strength * 1.6 (NiPSForceHelpers.inl:770,
    /// :878); planar pulls along the axis, fading as exp(-decay * |axis . (gravPos - pos)|); spherical pulls toward the
    /// object, fading as exp(-decay * distance).</summary>
    [Test]
    public void GravityForceMath()
    {
        var v = ParticleForces.Gravity(Vector3.Zero, new Vector3(5, 0, 0), Vector3.Zero, -Vector3.UnitZ, 10, 0, 0, 0.5f);
        Assert.That(v, Is.EqualTo(new Vector3(0, 0, -8)));                                       // 10 * 1.6 * 0.5
        v = ParticleForces.Gravity(Vector3.One, new Vector3(0, 0, 3), Vector3.Zero, -Vector3.UnitZ, 10, 0.5f, 0, 1f);
        Assert.That(v.Z, Is.EqualTo(1f - 16f * MathF.Exp(-1.5f)).Within(1e-5)); Assert.That(v.X, Is.EqualTo(1f));
        v = ParticleForces.Gravity(Vector3.Zero, new Vector3(3, 4, 0), Vector3.Zero, Vector3.UnitZ, 10, 0, 1, 1f);
        Assert.That(v.X, Is.EqualTo(-9.6f).Within(1e-5)); Assert.That(v.Y, Is.EqualTo(-12.8f).Within(1e-5));
        v = ParticleForces.Gravity(Vector3.Zero, new Vector3(3, 4, 0), Vector3.Zero, Vector3.UnitZ, 10, 0.2f, 1, 1f);
        Assert.That(v.Length(), Is.EqualTo(16f * MathF.Exp(-1f)).Within(1e-5));
    }

    /// <summary>The forces run in the list's order, each on the velocity the one before left
    /// (NiPSSimulatorForcesKernel.cpp:232-236): gravity then drag is not drag then gravity.</summary>
    [Test]
    public void ForcesApplyInListOrder()
    {
        var g = new ParticleForces.Step { Kind = ForceKind.Gravity, Dir = -Vector3.UnitZ, Strength = 10 };
        var d = new ParticleForces.Step { Kind = ForceKind.Drag, Dir = Vector3.UnitZ, Percentage = 0.5f, Range = float.MaxValue, Falloff = float.MaxValue };
        const float dt = 0.0333333f;
        var gd = ParticleForces.Apply(Vector3.Zero, Vector3.Zero, new() { g, d }, dt);
        var dg = ParticleForces.Apply(Vector3.Zero, Vector3.Zero, new() { d, g }, dt);
        Assert.That(dg.Z, Is.EqualTo(-16f * dt).Within(1e-5));                                 // drag on zero, then gravity
        Assert.That(gd.Z, Is.EqualTo(-8f * dt).Within(1e-4));                                  // gravity, then half of it dragged
    }

    /// <summary>The field snow (eff_cam_snow_soft, 02000260's weather): its gravity, turned by Wind01, gives a flake at rest
    /// Strength * 1.6 of downward speed per second; without the 1.6 the snow fell 1.6 times too slowly.</summary>
    [Test]
    public void SnowGravityGivesStrengthTimes1_6()
    {
        var sc = Load("bg/common/eff_cam_snow_soft.nif");
        var gf = sc.Systems.Single().Forces.Single(f => f.Kind == ForceKind.Gravity);
        var l = sc.Nodes[gf.Node].Local; var a = l.Apply(gf.Axis);
        var dir = Vector3.Normalize(new Vector3((float)(a.X - l.T.X), (float)(a.Y - l.T.Y), (float)(a.Z - l.T.Z)));
        var step = new ParticleForces.Step { Kind = ForceKind.Gravity, Dir = dir, Strength = (float)gf.Strength, Decay = (float)gf.Decay, ForceType = gf.ForceType };
        Assert.That(gf.ForceType, Is.EqualTo(0)); Assert.That(gf.Decay, Is.EqualTo(0));
        var v = ParticleForces.Apply(Vector3.Zero, Vector3.Zero, new() { step }, 1f);
        Assert.That(v.Z, Is.EqualTo(-1.6f * (float)gf.Strength).Within(1e-3));
    }
}
