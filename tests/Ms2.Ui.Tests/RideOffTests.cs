using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>MyPC::SendRequestRideMode1 (mypc.cpp:3566) and the riding table's basic type (FUN_140785190).</summary>
public class RideOffTests
{
    static (RideOff.Outcome, List<IOutPacket>) Run(bool riding, bool data, int type, bool ui, int action, bool allowsBattle)
    {
        var sent = new List<IOutPacket>();
        var o = RideOff.Request(riding, data, type, ui, action, allowsBattle, new RideOffReactRequest(40, "box"), sent.Add);
        return (o, sent);
    }

    [Test]
    public void ADefaultMountSendsTheReactRideOff()
    {
        var (o, sent) = Run(true, true, RideOff.TypeDefault, false, RideOffReactRequest.ActionType, false);
        Assert.That(o, Is.EqualTo(RideOff.Outcome.Sent));
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new RideOffReactRequest(40, "box") }));
    }

    [Test]
    public void NotRidingOrNoRideeDataSendsNothing()
    {
        foreach (var (riding, data) in new[] { (false, true), (true, false) })
        {
            var (o, sent) = Run(riding, data, 0, true, RideOffReactRequest.ActionType, false);
            Assert.That(o, Is.EqualTo(RideOff.Outcome.NotRiding));
            Assert.That(sent, Is.Empty);
        }
    }

    [Test]
    public void BattleAndObjectMountsRefuseTheReactWithTheCantRideNotice()
    {
        foreach (int t in new[] { RideOff.TypeBattle, RideOff.TypeObject })
        {
            var (o, sent) = Run(true, true, t, true, RideOffReactRequest.ActionType, false);
            Assert.That(o, Is.EqualTo(RideOff.Outcome.CantRide));
            Assert.That(sent, Is.Empty);
            Assert.That(Run(true, true, t, true, RideOffReactRequest.ActionType, true).Item1, Is.EqualTo(RideOff.Outcome.Sent), "an action with m_n20 1");
        }
        Assert.That(RideOff.CantRideNoticeId, Is.EqualTo(1621));
    }

    [Test]
    public void TheUiActionNeedsEnableRideOffUi()
    {
        Assert.That(Run(true, true, 0, false, RideOff.ActionTypeUi, false).Item1, Is.EqualTo(RideOff.Outcome.Refused));
        Assert.That(Run(true, true, 0, true, RideOff.ActionTypeUi, false).Item1, Is.EqualTo(RideOff.Outcome.Sent));
    }

    [Test]
    public void BasicTypeNames()
    {
        Assert.That(RideOff.TypeOf("default"), Is.EqualTo(0));
        Assert.That(RideOff.TypeOf("Battle"), Is.EqualTo(1));
        Assert.That(RideOff.TypeOf("OBJECT"), Is.EqualTo(2));
        Assert.That(RideOff.TypeOf("other"), Is.EqualTo(0));
        Assert.That(RideOff.TypeOf(null), Is.EqualTo(0));
    }
}
