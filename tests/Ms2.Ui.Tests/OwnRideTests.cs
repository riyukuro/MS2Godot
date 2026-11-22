using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>OnResponseRide (scenestategame.cpp:12026) modes 0-2 for the own pc: PCBase/MyPC vfunc_120 and vfunc_121.</summary>
public class OwnRideTests
{
    const int Self = 1001;
    static readonly RideOnAction Action = new(0, 30000001, 0, false, 0, 0, 0, 0);

    static ResponseRide Ride(byte mode, int id, RideOnAction on = null, RideOffAction off = null) =>
        new(mode, id, on, off, 0, 0, 0, 0, false);

    [Test]
    public void ModeZeroRidesOnlyTheOwnPcWithAnActionAndNoMount()
    {
        Assert.That(OwnRide.For(Ride(0, Self, Action), Self, false), Is.EqualTo(OwnRide.Step.RideOn));
        // vfunc_120 returns for a null action (a ride-on type above 3) and with a ridee already set
        Assert.That(OwnRide.For(Ride(0, Self), Self, false), Is.EqualTo(OwnRide.Step.None));
        Assert.That(OwnRide.For(Ride(0, Self, Action), Self, true), Is.EqualTo(OwnRide.Step.None));
        // another pc, or no own object yet
        Assert.That(OwnRide.For(Ride(0, 7, Action), Self, false), Is.EqualTo(OwnRide.Step.None));
        Assert.That(OwnRide.For(Ride(0, Self, Action), -1, false), Is.EqualTo(OwnRide.Step.None));
    }

    [Test]
    public void ModesOneAndTwoRideOffWithOrWithoutAnAction()
    {
        // a react ride-off reads null (no case 20): still the plain ride off
        Assert.That(OwnRide.For(Ride(1, Self), Self, true), Is.EqualTo(OwnRide.Step.RideOff));
        Assert.That(OwnRide.For(Ride(1, Self, off: new RideOffAction(23, 0)), Self, true), Is.EqualTo(OwnRide.Step.RideOff));
        Assert.That(OwnRide.For(Ride(2, Self), Self, false), Is.EqualTo(OwnRide.Step.RideOff));
        Assert.That(OwnRide.For(Ride(1, 7), Self, true), Is.EqualTo(OwnRide.Step.None));
        Assert.That(OwnRide.SummonsAfterRideOff(Ride(2, Self), Self), Is.True);
        Assert.That(OwnRide.SummonsAfterRideOff(Ride(1, Self), Self), Is.False);
        Assert.That(OwnRide.SummonsAfterRideOff(Ride(2, 7), Self), Is.False);
    }

    [Test]
    public void AnyActorRidesOnAndOffByTheSameModes()
    {
        Assert.That(OwnRide.ForActor(Ride(0, 7, Action), false), Is.EqualTo(OwnRide.Step.RideOn));
        Assert.That(OwnRide.ForActor(Ride(0, 7, Action), true), Is.EqualTo(OwnRide.Step.None));
        Assert.That(OwnRide.ForActor(Ride(0, 7), false), Is.EqualTo(OwnRide.Step.None));
        Assert.That(OwnRide.ForActor(Ride(1, 7), true), Is.EqualTo(OwnRide.Step.RideOff));
        Assert.That(OwnRide.ForActor(Ride(2, 7), false), Is.EqualTo(OwnRide.Step.RideOff));
        foreach (byte mode in new byte[] { 3, 4, 5, 6 })
            Assert.That(OwnRide.ForActor(Ride(mode, 7, Action), false), Is.EqualTo(OwnRide.Step.None), $"mode {mode}");
        Assert.That(OwnRide.ForActor(null, false), Is.EqualTo(OwnRide.Step.None));
    }

    [Test]
    public void PassengerAndFlagModesAreNotTheOwnRide()
    {
        foreach (byte mode in new byte[] { 3, 4, 5, 6 })
            Assert.That(OwnRide.For(Ride(mode, Self, Action), Self, false), Is.EqualTo(OwnRide.Step.None), $"mode {mode}");
    }

    static ResponseRide Pass(byte mode, int owner, int rider = 0, byte seat = 0) =>
        new(mode, owner, null, null, 0, 0, rider, seat, false);

    [Test]
    public void PassengerModesSetRemoveAndClearTheOwnersMap()
    {
        // mode 3 FUN_141676670 (SetAt rider -> seat), mode 4 RemovePassenger, mode 5 ClearPassengers
        var map = new Dictionary<int, int>();
        Assert.That(RidePassengers.Apply(Pass(3, 7, 8, 1), map), Is.True);
        Assert.That(RidePassengers.Apply(Pass(3, 7, 9, 0), map), Is.True);
        Assert.That(map, Is.EquivalentTo(new Dictionary<int, int> { [8] = 1, [9] = 0 }));
        Assert.That(RidePassengers.Apply(Pass(4, 7, 8), map), Is.True);
        Assert.That(map.Keys, Is.EquivalentTo(new[] { 9 }));
        Assert.That(RidePassengers.Apply(Pass(5, 7), map), Is.True);
        Assert.That(map, Is.Empty);
        Assert.That(RidePassengers.Apply(Pass(6, 7), map), Is.False);
        Assert.That(RidePassengers.Apply(Pass(0, 7), map), Is.False);
    }

    [Test]
    public void OnlyModeThreeWithTheOwnPcAsRiderBoardsSelf()
    {
        Assert.That(RidePassengers.BoardsSelf(Pass(3, 7, Self, 1), Self), Is.True);
        Assert.That(RidePassengers.BoardsSelf(Pass(3, 7, 8, 1), Self), Is.False);
        Assert.That(RidePassengers.BoardsSelf(Pass(4, 7, Self), Self), Is.False);
        Assert.That(RidePassengers.BoardsSelf(Pass(3, 7, Self, 1), -1), Is.False);
    }

    [Test]
    public void FieldAddUserReplacesTheMap()
    {
        // scenestategame.cpp:11201-11212: ClearPassengers, then SetAt per entry
        var map = new Dictionary<int, int> { [5] = 0 };
        RidePassengers.Load(new List<(int, byte)> { (8, 1), (9, 0) }, map);
        Assert.That(map, Is.EquivalentTo(new Dictionary<int, int> { [8] = 1, [9] = 0 }));
    }
}
