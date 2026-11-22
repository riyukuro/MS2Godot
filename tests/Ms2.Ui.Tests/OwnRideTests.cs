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
}
