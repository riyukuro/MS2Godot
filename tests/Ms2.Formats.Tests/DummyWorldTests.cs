using System.Numerics;
using Ms2.Formats.Model;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>EffectNode::UpdateWorldData as DummyWorld does it, on made-up rows.</summary>
public class DummyWorldTests
{
    const double Tol = 1e-6;

    static EffectDummy Row(string name, string parent, bool world, bool localScale, Vector3 t, Vector3 rot = default) =>
        new() { Name = name, ParentNode = parent, ApplyWorldTransform = world, ApplyLocalScale = localScale, Translate = t, Rotation = rot };

    static void Near(Vec3 a, Vec3 b) => Assert.That(new[] { a.X, a.Y, a.Z }, Is.EqualTo(new[] { b.X, b.Y, b.Z }).Within(Tol));

    // a parent turned 90 degrees about z and tipped 30 degrees about x, scaled 2, at (10, 20, 30)
    static readonly double[] Tilt = DummyWorld.EulerXyz(30, 0, 90);
    static readonly Xform Parent = new(new Vec3(10, 20, 30), Tilt, 2);

    [Test]
    public void EulerIsGamebryosClockwiseOrder()
    {
        var z = DummyWorld.EulerXyz(0, 0, 90);
        Near((Xform.Identity with { R = z }).Apply(new Vec3(1, 0, 0)), new Vec3(0, -1, 0));
        var x = DummyWorld.EulerXyz(90, 0, 0);
        Near((Xform.Identity with { R = x }).Apply(new Vec3(0, 1, 0)), new Vec3(0, 0, -1));
    }

    /// <summary>World rows go straight up the world from the parent, whatever its rotation; its scale only with applyLocalScale.</summary>
    [Test]
    public void WorldRowsGoStraightUp()
    {
        var w = DummyWorld.Resolve(Row("UI", "Root", true, false, new Vector3(0, 0, 170)), Parent, null);
        Near(w.T, new Vec3(10, 20, 200));
        Assert.That(w.R, Is.EqualTo(Xform.Identity.R).Within(Tol));
        Assert.That(w.S, Is.EqualTo(1).Within(Tol));
        var s = DummyWorld.Resolve(Row("UI", "Root", true, true, new Vector3(0, 0, 170)), Parent, null);
        Near(s.T, new Vec3(10, 20, 370));
    }

    /// <summary>The target's rotation turns the row's x and y; only the result's x and y are added, and it turns the row's rotation.</summary>
    [Test]
    public void TargetTurnsThePlaneOffset()
    {
        var w = DummyWorld.Resolve(Row("UI", "Root", true, false, new Vector3(5, 0, 100), new Vector3(0, 0, 90)), Parent, Tilt);
        var off = Xform.Identity with { R = Tilt };
        var turned = off.Apply(new Vec3(5, 0, 0));
        Assert.That(Math.Abs(turned.Z), Is.GreaterThan(0.1), "the made-up tilt gives the offset a z part");
        Near(w.T, new Vec3(10 + turned.X, 20 + turned.Y, 130));
        var expectR = new Xform(new Vec3(0, 0, 0), Tilt, 1).Compose(Xform.Identity with { R = DummyWorld.EulerXyz(0, 0, 90) }).R;
        Assert.That(w.R, Is.EqualTo(expectR).Within(Tol));
    }

    /// <summary>Other rows are ordinary children of the parent (its scale dropped without applyLocalScale).</summary>
    [Test]
    public void LocalRowsAreChildren()
    {
        var row = Row("Head", "Root", false, true, new Vector3(80, 0, 0), new Vector3(0, 0, 90));
        var w = DummyWorld.Resolve(row, Parent, Tilt);
        var expect = Parent.Compose(new Xform(new Vec3(80, 0, 0), DummyWorld.EulerXyz(0, 0, 90), 1));
        Near(w.T, expect.T);
        Assert.That(w.R, Is.EqualTo(expect.R).Within(Tol));
        Assert.That(w.S, Is.EqualTo(2).Within(Tol));
        var unscaled = DummyWorld.Resolve(Row("Head", "Root", false, false, new Vector3(80, 0, 0)), Parent, Tilt);
        Near(unscaled.T, (Parent with { S = 1 }).Apply(new Vec3(80, 0, 0)));
    }

    /// <summary>A row under another row uses that row's world transform; a missing dummy or parent gives nothing.</summary>
    [Test]
    public void FindChainsRowsAndNeedsTheParent()
    {
        var rows = new List<EffectDummy>
        {
            Row("Hit", "UI", true, false, new Vector3(0, 0, 10)),
            Row("UI", "Root", true, false, new Vector3(0, 0, 170)),
            Row("Hand", "Bone", false, true, new Vector3(1, 0, 0)),
        };
        Near(DummyWorld.Find(rows, "UI", Parent, "Root")!.Value.T, new Vec3(10, 20, 200));
        Near(DummyWorld.Find(rows, "Hit", Parent, "Root")!.Value.T, new Vec3(10, 20, 210));
        Assert.That(DummyWorld.Find(rows, "Nope", Parent, "Root"), Is.Null);
        Assert.That(DummyWorld.Find(rows, "Hand", Parent, "Root"), Is.Null);
        var bone = new Xform(new Vec3(0, 0, 50), Xform.Identity.R, 1);
        Near(DummyWorld.Find(rows, "Hand", Parent, "Root", n => n == "Bone" ? bone : null)!.Value.T, new Vec3(1, 0, 50));
    }

    [Test]
    public void ARowParentedToItselfGivesNothing()
    {
        var rows = new List<EffectDummy> { Row("A", "B", true, false, default), Row("B", "A", true, false, default) };
        Assert.That(DummyWorld.Find(rows, "A", Parent, "Root"), Is.Null);
    }
}
