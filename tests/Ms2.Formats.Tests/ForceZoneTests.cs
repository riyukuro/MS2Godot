using System.Numerics;
using Ms2.Formats.Model;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The force zones of WorldService::FUN_141bc8420 (worldservice_1.cpp:5503-5608), from the client's code.</summary>
public class ForceZoneTests {
    static RegionSkillObjects Regions(params (Vector3 At, PushCylinder C)[] zones) {
        var r = new RegionSkillObjects();
        int id = 1;
        foreach (var (at, c) in zones)
            r.Add(new RegionSkillObject { Id = id++, Points = new[] { new Vec3(at.X, at.Y, at.Z) }, Attr = new SkillMotion { PushCylinder = c } });
        return r;
    }

    static (bool Changed, Vector3 V, bool Lifted) Step(Vector3 pos, RegionSkillObjects r, float dt = 0.1f, bool dead = false, IReadOnlyList<ForceZone.Source> npcs = null) {
        bool changed = ForceZone.Step(pos, dead, dt, r, npcs, out var v, out bool lifted);
        return (changed, v, lifted);
    }

    [Test] public void APushingZoneMovesAwayFromTheCentreByItsSpeed() {
        var r = Step(new Vector3(500, 0, 0), Regions((Vector3.Zero, new PushCylinder(300, 0, 3000, 300, 350))));
        Assert.That(r.Changed, Is.True);
        Assert.That(r.V.X, Is.EqualTo(30f).Within(1e-4));
        Assert.That(r.V.Y, Is.EqualTo(0f)); Assert.That(r.V.Z, Is.EqualTo(0f)); Assert.That(r.Lifted, Is.False);
    }

    [Test] public void APushStopsAtTheOuterRadius() {
        var r = Step(new Vector3(0, 2990, 0), Regions((Vector3.Zero, new PushCylinder(300, 0, 3000, 300, 350))));
        Assert.That(r.V.Y, Is.EqualTo(10f).Within(1e-3));
    }

    [Test] public void APullingZoneMovesTowardTheCentreByItsSpeedOnlyPastTheInnerRadius() {
        var zones = Regions((Vector3.Zero, new PushCylinder(-280, 0, 1200, 175, 450)));
        Assert.That(Step(new Vector3(600, 0, 0), zones).V.X, Is.EqualTo(-28f).Within(1e-4));
        // the client's min(push * dt, best - inner) keeps the negative push: at 176 it still moves the whole step
        Assert.That(Step(new Vector3(176, 0, 0), zones).V.X, Is.EqualTo(-28f).Within(1e-4));
        Assert.That(Step(new Vector3(170, 0, 0), zones).Changed, Is.False);
    }

    [Test] public void TheHeightAndThe105BelowBoundTheCylinder() {
        var c = new PushCylinder(300, 0, 3000, 0, 350);
        Assert.That(Step(new Vector3(500, 0, 351), Regions((Vector3.Zero, c))).Changed, Is.False);
        Assert.That(Step(new Vector3(500, 0, 350), Regions((Vector3.Zero, c))).Changed, Is.True);
        Assert.That(Step(new Vector3(500, 0, 0), Regions((new Vector3(0, 0, 106), c))).Changed, Is.False);
        Assert.That(Step(new Vector3(500, 0, 0), Regions((new Vector3(0, 0, 105), c))).Changed, Is.True);
    }

    [Test] public void ARisingZoneLiftsToItsTopAndSetsNoVelocity() {
        var zones = Regions((Vector3.Zero, new PushCylinder(0, 200, 500, 0, 300)));
        var r = Step(new Vector3(100, 0, 0), zones);
        Assert.That((r.Changed, r.Lifted), Is.EqualTo((true, true)));
        Assert.That(r.V.Z, Is.EqualTo(20f).Within(1e-4)); Assert.That(r.V.X, Is.EqualTo(0f));
        Assert.That(Step(new Vector3(100, 0, 295), zones).V.Z, Is.EqualTo(5f).Within(1e-3));
        var top = Step(new Vector3(100, 0, 300), zones);
        Assert.That((top.Changed, top.Lifted, top.V.Z), Is.EqualTo((true, true, 0f)));
    }

    [Test] public void AFallingZoneMovesDownWithoutNoVelocity() {
        var r = Step(new Vector3(100, 0, 50), Regions((Vector3.Zero, new PushCylinder(0, -100, 500, 0, 300))));
        Assert.That((r.Changed, r.Lifted), Is.EqualTo((true, false)));
        Assert.That(r.V.Z, Is.EqualTo(-10f).Within(1e-4));
    }

    [Test] public void ZonesWithoutSpeedOuterOrHeightAndDeadActorsAreSkipped() {
        var pos = new Vector3(500, 0, 0);
        Assert.That(Step(pos, Regions((Vector3.Zero, new PushCylinder(0, 0, 3000, 0, 350)))).Changed, Is.False);
        Assert.That(Step(pos, Regions((Vector3.Zero, new PushCylinder(300, 0, 0, 0, 350)))).Changed, Is.False);
        Assert.That(Step(pos, Regions((Vector3.Zero, new PushCylinder(300, 0, 3000, 0, 0)))).Changed, Is.False);
        Assert.That(Step(pos, Regions((Vector3.Zero, new PushCylinder(300, 0, 3000, 0, 350))), dead: true).Changed, Is.False);
    }

    [Test] public void TheNearestZoneWinsAndTheNpcsComeAfterTheRegions() {
        var regions = Regions((new Vector3(1000, 0, 0), new PushCylinder(300, 0, 3000, 0, 350)), (new Vector3(-200, 0, 0), new PushCylinder(-100, 0, 3000, 0, 350)));
        // the second region (200 away) is nearer than the first (1000): its pull, toward -x
        Assert.That(Step(Vector3.Zero, regions).V.X, Is.EqualTo(-10f).Within(1e-4));
        // an npc as near as the nearest region does not replace it; a nearer one does
        var npcs = new List<ForceZone.Source> { new(new Vector3(200, 0, 0), new PushCylinder(500, 0, 3000, 0, 350)) };
        Assert.That(Step(Vector3.Zero, regions, npcs: npcs).V.X, Is.EqualTo(-10f).Within(1e-4));
        npcs[0] = new(new Vector3(100, 0, 0), new PushCylinder(500, 0, 3000, 0, 350));
        Assert.That(Step(Vector3.Zero, regions, npcs: npcs).V.X, Is.EqualTo(-50f).Within(1e-4));
    }

    [Test] public void ARegionWithoutPointsCentresOnTheOrigin() {
        var r = new RegionSkillObjects();
        r.Add(new RegionSkillObject { Id = 1, Points = System.Array.Empty<Vec3>(), Attr = new SkillMotion { PushCylinder = new PushCylinder(300, 0, 3000, 0, 350) } });
        Assert.That(Step(new Vector3(0, 400, 0), r).V.Y, Is.EqualTo(30f).Within(1e-4));
    }
}
