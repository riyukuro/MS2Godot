using System.Reflection;
using Ms2.Net.Packets;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The field lifts' removal and state signal. A mode-3 lift has no entity; today
/// LiftableObjects.Destroy returns early for it (FieldObjectStates.cs:558), so a mode-4 removal leaves the record and Removed
/// never fires. The state change (liftableobject.cpp:292-310: SetState 0/2 active, 1 inactive) has no signal today.
/// Expected API: LiftableObjects.StateChanged, an Action&lt;LiftableObjects.Lift, int&gt; raised by Apply's state paths.
/// </summary>
public class RcLiftStateTests
{
    [Ignore("a mode-3 lift is not destroyed by mode 4")]
    [Test]
    public void ModeThreeLiftIsDestroyedByModeFour()
    {
        var lifts = new LiftableObjects();
        var removed = new List<string>();
        lifts.Removed = removed.Add;
        lifts.Apply(new LiftableUpdate(LiftableUpdate.ModeAdd,
            new[] { new LiftableEntry("lift_a", false, 30001, 0, "qa", "qb", "qc", "qd", false) }));
        Assert.That(lifts.Count, Is.EqualTo(1));
        Assert.That(lifts.Find("lift_a").HasEntity, Is.False, "a mode-3 lift has no entity");
        lifts.Apply(new LiftableUpdate(LiftableUpdate.ModeRemove, Array.Empty<LiftableEntry>(), "lift_a"));
        Assert.That(lifts.Count, Is.EqualTo(0), "the record outlives its removal");
        Assert.That(removed, Is.EqualTo(new[] { "lift_a" }), "Removed fired once");
    }

    sealed class Sink
    {
        public readonly List<int> States = new();
        public void On(LiftableObjects.Lift lift, int state) => States.Add(state);
    }

    [Ignore("there is no lift state signal")]
    [Test]
    public void StateChangeRaisesTheSignal()
    {
        var lifts = new LiftableObjects();
        var lift = lifts.AddFieldLift("Lift_A", 30001);
        var ev = typeof(LiftableObjects).GetEvent("StateChanged", BindingFlags.Public | BindingFlags.Instance);
        Assert.That(ev, Is.Not.Null, "LiftableObjects.StateChanged is not implemented at origin/main");
        var sink = new Sink();
        ev.AddEventHandler(lifts, Delegate.CreateDelegate(ev.EventHandlerType, sink, typeof(Sink).GetMethod(nameof(Sink.On))));
        lifts.Apply(new LiftableUpdate(LiftableUpdate.ModeUpdate,
            new[] { new LiftableEntry("lift_a", true, 30001, 2) }));
        Assert.That(lift.Active, Is.True, "state 2 is active");
        lifts.Apply(new LiftableUpdate(LiftableUpdate.ModeUpdate,
            new[] { new LiftableEntry("lift_a", true, 30001, 1) }));
        Assert.That(lift.Active, Is.False, "state 1 is inactive");
        lifts.Apply(new LiftableUpdate(LiftableUpdate.ModeUpdate,
            new[] { new LiftableEntry("lift_a", true, 30001, 0) }));
        Assert.That(lift.Active, Is.True, "state 0 is active again");
        Assert.That(sink.States, Is.EqualTo(new[] { 2, 1, 0 }));
    }
}
