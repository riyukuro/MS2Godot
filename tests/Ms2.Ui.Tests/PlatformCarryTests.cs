using Ms2.Formats.Model;
using Ms2.Formats.World;
using Ms2Godot.Tests;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The marker carry every PCBase runs (PCBase::vfunc_68, pcbase.cpp:1797-1817), the markers' once-a-frame update
/// before the actors (worldservice_1.cpp:9648-9655, gameobject.cpp:307-312), and which breakables keep their NIF PhysX
/// meshes (PropService.cpp:455-458).</summary>
public class PlatformCarryTests
{
    static (BreakableObjects, BreakableObjects.Marker) OnePlatform()
    {
        var b = new BreakableObjects();
        b.AttachField(new[] { FieldObjectStatesTests.Platform("p") });
        return (b, b.Find("p"));
    }

    [Test]
    public void TheFirstFrameOnAMarkerOnlyRemembersItsPosition()
    {
        var (b, _) = OnePlatform();
        var c = new PlatformCarry();
        b.Update(1500, 1500);
        Assert.That(c.Step(b, "p", out var d), Is.False);
        Assert.That(d, Is.EqualTo(default(Vec3)));
    }

    [Test]
    public void TheMarkersChangeSinceTheLastFrameIsTheCarry()
    {
        // 150 up in 1 s after a 1 s wait: 1500 -> z 375, 2000 -> z 450; the end wait holds it; 7500 -> back at 375
        var (b, _) = OnePlatform();
        var c = new PlatformCarry();
        b.Update(1500, 1500);
        c.Step(b, "p", out _);
        b.Update(2000, 2000);
        Assert.That(c.Step(b, "p", out var up), Is.True);
        Assert.That(up, Is.EqualTo(new Vec3(0, 0, 75)));
        Assert.That(PlatformCarry.SetsAtOnce(up), Is.True, "an upward change sets the position at once");
        b.Update(3000, 3000);
        Assert.That(c.Step(b, "p", out var still), Is.False, "a marker in its wait pushes nothing");
        Assert.That(still, Is.EqualTo(default(Vec3)));
        b.Update(7500, 7500);
        Assert.That(c.Step(b, "p", out var down), Is.True);
        Assert.That(down, Is.EqualTo(new Vec3(0, 0, -75)));
        Assert.That(PlatformCarry.SetsAtOnce(down), Is.False, "a downward change joins the controller move");
        Assert.That(PlatformCarry.SetsAtOnce(new Vec3(10, -5, 0)), Is.False, "a flat change joins the controller move");
    }

    [Test]
    public void LeavingTheMarkerOrStandingOnAnotherStartsOver()
    {
        var b = new BreakableObjects();
        b.AttachField(new[] { FieldObjectStatesTests.Platform("p"), FieldObjectStatesTests.Platform("q", dist: "300") });
        var c = new PlatformCarry();
        b.Update(1500, 1500);
        c.Step(b, "p", out _);
        b.Update(1600, 1600);
        Assert.That(c.Step(b, "q", out _), Is.False, "another marker: its first frame");
        b.Update(1700, 1700);
        Assert.That(c.Step(b, null, out _), Is.False, "off every marker");
        b.Update(1800, 1800);
        Assert.That(c.Step(b, "q", out _), Is.False, "back on q after a frame off it: a first frame again");
        Assert.That(c.Step(b, "nothere", out _), Is.False, "a name with no marker");
        b.Update(1900, 1900);
        Assert.That(c.Step(b, "q", out var d), Is.False, "after a name with no marker: a first frame again");
        Assert.That(d, Is.EqualTo(default(Vec3)));
        Assert.That(c.Step(null, "q", out _), Is.False, "no markers at all");
    }

    [Test]
    public void TheMarkersMoveOncePerFrameWhoeverAsksFirst()
    {
        // the field, the remote layer and the own PC's frame all reach the same markers: only the first call in a frame steps them
        var (b, m) = OnePlatform();
        b.UpdateOnce(7, 1500, 1500);
        Assert.That(m.Position.Z, Is.EqualTo(375.0));
        b.UpdateOnce(7, 2000, 2000);
        Assert.That(m.Position.Z, Is.EqualTo(375.0), "the same frame again");
        b.UpdateOnce(8, 2000, 2000);
        Assert.That(m.Position.Z, Is.EqualTo(450.0), "the next frame");
    }

    [Test]
    public void ACarryStepAllocatesNothing()
    {
        var (b, _) = OnePlatform();
        var c = new PlatformCarry();
        b.UpdateOnce(0, 1500, 1500);
        c.Step(b, "p", out _);
        ulong f = 1;
        long bytes = Alloc.Least(() =>
        {
            for (int i = 0; i < 200; i++) { b.UpdateOnce(f, 1500 + (long)f, 1500 + (int)f); c.Step(b, "p", out _); f++; }
        });
        Assert.That(bytes, Is.EqualTo(0));
    }

    [Test]
    public void OnlyABreakableWithoutPhysXPropLosesItsNifMeshes()
    {
        static FieldEntity Of(params string[] mixins)
        {
            var flat = new FlatModel { Name = "m" };
            foreach (var x in mixins) flat.Mixins.Add(x);
            return new FieldEntity { Raw = new XEntity { Id = "e", ModelName = "m", Name = "e" }, Flat = flat };
        }
        Assert.That(BreakableObjects.NifPhysX(Of("MS2Breakable")), Is.False);
        Assert.That(BreakableObjects.NifPhysX(Of("MS2Breakable", "PhysXProp")), Is.True);
        Assert.That(BreakableObjects.NifPhysX(Of("PhysXProp")), Is.True);
        Assert.That(BreakableObjects.NifPhysX(Of()), Is.True);
    }
}
