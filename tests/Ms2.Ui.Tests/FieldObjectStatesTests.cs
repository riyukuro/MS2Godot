using System.Collections.Generic;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

public class FieldObjectStatesTests
{
    [Test]
    public void BreakableMarkerTakesStateAndSyncValues()
    {
        // OnBreakable (scenestategame.cpp:3682-3686): SetState(state, 0), FUN_140adeaa0(a, b, c)
        var b = new BreakableObjects();
        b.AddMarker("m1");
        var set = new List<(string, int)>();
        b.StateSet = (n, s) => set.Add((n, s));
        b.Apply(new BreakableUpdate(1, new[] { new BreakableEntry("m1", 3, 1, 20, 30) }));
        var m = b.Find("m1");
        Assert.That((m.State, m.SyncA, m.SyncB, m.SyncC), Is.EqualTo((3, 1, 20, 30)));
        Assert.That(set, Is.EqualTo(new[] { ("m1", 3) }));
        Assert.That(b.CubeStates, Is.Empty);
    }

    [Test]
    public void BreakableWithoutAMarkerGoesToTheCubeStatesAndTheLastWins()
    {
        // :3688-3692 -> CubeMapInformationService::FUN_1410caa60 (cubemapinformationservice_3.cpp:1298-1303)
        var b = new BreakableObjects();
        b.Apply(new BreakableUpdate(0, new[] { new BreakableEntry("c", 2, 0, 0, 0), new BreakableEntry("c", 4, 0, 0, 0) }));
        Assert.That(b.CubeStates["c"], Is.EqualTo(4));
        Assert.That(b.Find("c"), Is.Null);
    }

    static LiftableUpdate List(params LiftableEntry[] e) => new(LiftableUpdate.ModeList, e);

    [Test]
    public void FieldLiftIsDisabledAtStateZeroUnderItsLowercasedId()
    {
        // Init(entity, false, kind) (liftableobject.cpp:167-175), BindEntity's ToLower (:186-191)
        var l = new LiftableObjects();
        var lift = l.AddFieldLift("ABCdef", 7);
        Assert.That((lift.Name, lift.Kind, lift.Enabled, lift.State, lift.Active), Is.EqualTo(("abcdef", 7, false, 0, true)));
    }

    [Test]
    public void ListSetsTheNamedLiftsKindFromTheInt32AndStateFromTheByte()
    {
        // :11964-11971: SetKind(i32), SetLiftEnabled, FUN_141447aa0, SetState(u8); state 1 SetActive(0)
        var l = new LiftableObjects();
        l.AddFieldLift("a", 0);
        l.Apply(List(new LiftableEntry("a", true, 900, 1, "q1", "q2", "q3", "q4", true)));
        var a = l.Find("a");
        Assert.That((a.Kind, a.State, a.Enabled, a.Flag, a.Active), Is.EqualTo((900, 1, true, true, false)));
        Assert.That((a.QuestA, a.QuestB, a.QuestC, a.QuestD), Is.EqualTo(("q1", "q2", "q3", "q4")));
    }

    [Test]
    public void ListRemovesTheFieldLiftsItDoesNotName()
    {
        // :11973-11991: a field lift entity whose name is not in the list is removed; a lift without an entity is not
        var l = new LiftableObjects();
        l.AddFieldLift("keep", 0);
        l.AddFieldLift("drop", 0);
        l.Apply(new LiftableUpdate(LiftableUpdate.ModeAdd, new[] { new LiftableEntry("loose", false, 2, 0) }));
        var removed = new List<string>();
        l.Removed = removed.Add;
        l.Apply(List(new LiftableEntry("keep", true, 1, 0)));
        Assert.That(removed, Is.EqualTo(new[] { "drop" }));
        Assert.That(l.Find("drop"), Is.Null);
        Assert.That(l.Find("keep"), Is.Not.Null);
        Assert.That(l.Find("loose"), Is.Not.Null);
    }

    [Test]
    public void UpdateLeavesQuestStringsAndFlag()
    {
        // case 2 (:11999-12001): SetKind, SetLiftEnabled, SetState only
        var l = new LiftableObjects();
        l.AddFieldLift("a", 0);
        l.Apply(List(new LiftableEntry("a", false, 1, 0, "q1", "q2", "q3", "q4", true)));
        l.Apply(new LiftableUpdate(LiftableUpdate.ModeUpdate, new[] { new LiftableEntry("a", true, 5, 2) }));
        var a = l.Find("a");
        Assert.That((a.Kind, a.Enabled, a.State, a.Flag, a.QuestA), Is.EqualTo((5, true, 2, true, "q1")));
    }

    [Test]
    public void AddedLiftHasNoEntityAndReplacesTheName()
    {
        // FUN_141bc5420 (worldservice_1.cpp:4429-4447): Init(0, true, kind), SetAt(name)
        var l = new LiftableObjects();
        l.AddFieldLift("n", 1);
        l.Apply(new LiftableUpdate(LiftableUpdate.ModeAdd, new[] { new LiftableEntry("n", false, 6, 0, "a", "b", "c", "d", true) }));
        var n = l.Find("n");
        Assert.That((n.HasEntity, n.Kind, n.Enabled, n.State, n.Flag, n.QuestD), Is.EqualTo((false, 6, false, 0, true, "d")));
        Assert.That(l.Count, Is.EqualTo(1));
    }

    [Test]
    public void LeavingTheFieldDropsTheFieldLiftsOnly()
    {
        // the entity removal handler drops its lift (worldservice_1.cpp:3526-3533); a mode 3 lift has no entity
        var l = new LiftableObjects();
        l.AddFieldLift("f", 0);
        l.Apply(new LiftableUpdate(LiftableUpdate.ModeAdd, new[] { new LiftableEntry("x", false, 0, 0) }));
        l.FieldLeft();
        Assert.That(l.Find("f"), Is.Null);
        Assert.That(l.Find("x"), Is.Not.Null);
    }

    [Test]
    public void RemoveTakesAFieldLiftOnly()
    {
        // case 4 -> DestroyLiftByName (worldservice_1.cpp:4451-4456); the list entry goes with the entity (:3526-3533)
        var l = new LiftableObjects();
        l.AddFieldLift("f", 0);
        l.Apply(new LiftableUpdate(LiftableUpdate.ModeAdd, new[] { new LiftableEntry("x", false, 0, 0) }));
        l.Apply(new LiftableUpdate(LiftableUpdate.ModeRemove, new LiftableEntry[0], "f"));
        l.Apply(new LiftableUpdate(LiftableUpdate.ModeRemove, new LiftableEntry[0], "x"));
        Assert.That(l.Find("f"), Is.Null);
        Assert.That(l.Find("x"), Is.Not.Null);
    }
}
