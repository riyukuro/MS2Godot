using Ms2.Net.Packets;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>PC::vfunc_135 / FUN_141659250 / vfunc_12 (pc.cpp:3038-3089, 822-852) and the cube-mode action map
/// (actionkeyobject.cpp:531-534, 707-717).</summary>
public class CubeCraftStateTests
{
    static readonly CubeItemInfo Item = new() { ItemId = 50100001, InstanceId = 7, Count = 1 };

    static CubeCraftState Loaded()
    {
        var c = new CubeCraftState();
        c.NewPc();
        c.ModelAttached();
        return c;
    }

    [Test]
    public void ACubeModeWithTheModelInMakesTheCraftAndModeZeroDropsIt()
    {
        var c = Loaded();
        c.Set(1, 2, Item, dead: false);
        Assert.That(c.InCubeMode, Is.True);
        Assert.That(c.Current, Is.Not.Null);
        Assert.That(c.Current.Mode, Is.EqualTo(2));
        Assert.That(c.Current.HoldsLift, Is.True);
        Assert.That(c.Current.Item, Is.SameAs(Item));
        Assert.That(c.Pending, Is.Null);
        c.Set(0, 0, null, dead: false);
        Assert.That(c.InCubeMode, Is.False);
        Assert.That(c.Current, Is.Null);
    }

    [Test]
    public void BeforeTheModelTheCraftWaitsAndTheModelMakesIt()
    {
        var c = new CubeCraftState();
        c.NewPc();
        c.Set(1, 1, Item, dead: false);
        Assert.That(c.Current, Is.Null);
        Assert.That(c.Pending, Is.Not.Null);
        c.ModelAttached();
        Assert.That(c.Current, Is.Not.Null);
        Assert.That(c.Current.Mode, Is.EqualTo(1));
        Assert.That(c.Current.HoldsLift, Is.False);
        Assert.That(c.Pending, Is.Null);
    }

    [Test]
    public void LeavingCubeModeBeforeTheModelKeepsTheWaitingCraft()
    {
        var c = new CubeCraftState();
        c.NewPc();
        c.Set(1, 2, Item, dead: false);
        c.Set(0, 0, null, dead: false);
        Assert.That(c.InCubeMode, Is.False);
        Assert.That(c.Pending, Is.Not.Null);
        c.ModelAttached();
        Assert.That(c.Current, Is.Not.Null);
    }

    [Test]
    public void ADeadPcChangesNothing()
    {
        var c = Loaded();
        c.Set(1, 2, Item, dead: true);
        Assert.That(c.InCubeMode, Is.False);
        Assert.That(c.Current, Is.Null);
        c.Set(1, 2, Item, dead: false);
        c.Set(0, 0, null, dead: true);
        Assert.That(c.InCubeMode, Is.True);
        Assert.That(c.Current, Is.Not.Null);
    }

    [Test]
    public void ANewPcStartsOutOfCubeModeWithNoModel()
    {
        var c = Loaded();
        c.Set(1, 2, Item, dead: false);
        c.NewPc();
        Assert.That(c.InCubeMode, Is.False);
        Assert.That(c.Current, Is.Null);
        Assert.That(c.ModelLoaded, Is.False);
        c.Set(1, 2, Item, dead: false);
        Assert.That(c.Pending, Is.Not.Null);
    }

    [Test]
    public void TheCubeModeMapPicksPutDownThenPortalThenTalk()
    {
        Assert.That(ActionKeyPoll.PickCubeMode(true, true, true), Is.EqualTo(ActionKeyTarget.PutDownLiftable));
        Assert.That(ActionKeyPoll.PickCubeMode(false, true, true), Is.EqualTo(ActionKeyTarget.Portal));
        Assert.That(ActionKeyPoll.PickCubeMode(false, false, true), Is.EqualTo(ActionKeyTarget.Talk));
        Assert.That(ActionKeyPoll.PickCubeMode(false, false, false), Is.EqualTo(ActionKeyTarget.None));
    }

    [Test]
    public void AnActionPickedFromTheOtherMapDoesNotRun()
    {
        var k = new ActionKeyPoll { Current = ActionKeyTarget.Interact };
        Assert.That(k.InActiveMap(cubeMode: false), Is.True);
        Assert.That(k.InActiveMap(cubeMode: true), Is.False);
        k.Current = ActionKeyTarget.Lift;
        Assert.That(k.InActiveMap(cubeMode: true), Is.False);
        k.Current = ActionKeyTarget.Portal;
        Assert.That(k.InActiveMap(cubeMode: true), Is.True);
        Assert.That(k.InActiveMap(cubeMode: false), Is.True);
        k.Current = ActionKeyTarget.PutDownLiftable;
        Assert.That(k.InActiveMap(cubeMode: true), Is.True);
        Assert.That(k.InActiveMap(cubeMode: false), Is.False);
    }
}
