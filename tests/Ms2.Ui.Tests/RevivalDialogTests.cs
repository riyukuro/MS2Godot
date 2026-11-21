using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UIRevivalDialog: the buttons' sends (SendOp Revival 0x1F), the paid mode and the costs.</summary>
public class RevivalDialogTests
{
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    sealed class Rig
    {
        public readonly List<string> Sent = new();
        public readonly List<string> Notices = new();
        public readonly List<MessageBoxData> Boxes = new();
        public readonly RevivalDialogController C = new();
        public Rig(RevivalField field = null)
        {
            C.CommonById = id => $"#{id:x}";
            C.CommonByKey = k => k + "|{0}";
            var e = C.Env;
            e.Field = () => field ?? new RevivalField(0, 1, false, false);
            e.FieldId = () => 100;
            e.Send = p => Sent.Add($"{p.Op}:{Convert.ToHexString(Bytes(p))}");
            e.NoticeById = id => Notices.Add($"id {id:x}");
            e.Notice = t => Notices.Add(t);
            e.ShowBox = Boxes.Add;
            e.ReplaceBox = _ => { };
            e.Level = () => 30;
            e.MeratAbilityRate = () => 0f;
            e.MeratRateCap = () => 1f;
            e.MesoRateCap = () => 1f;
            e.Meso = () => 1_000_000;
            e.CanPayMerat = _ => true;
        }
    }

    [Test]
    public void FreeReviveAtTheReturnPointSendsMode0()
    {
        var r = new Rig(new RevivalField(0, 1, false, false));
        r.C.Refresh();
        r.C.OnRevival(1);
        Assert.That(r.Sent, Is.EqualTo(new[] { "Revival:00" }));
        Assert.That(r.Boxes, Is.Empty);
    }

    [Test]
    public void FreeReviveInAnotherFieldAsksFirst()
    {
        var r = new Rig(new RevivalField(200, 1, false, false));
        r.C.Refresh();
        r.C.OnRevival(1);
        Assert.That(r.Sent, Is.Empty, "the box comes first");
        Assert.That(r.Boxes, Has.Count.EqualTo(1));
        r.Boxes[0].Callback(1, r.Boxes[0]);
        Assert.That(r.Sent, Is.Empty, "the other button sends nothing");
        r.Boxes[0].Callback(0, r.Boxes[0]);
        Assert.That(r.Sent, Is.EqualTo(new[] { "Revival:00" }));
    }

    [Test]
    public void MeratReviveAsksThenSendsMode1WithTheCouponByte()
    {
        var r = new Rig();
        r.C.Env.MesoRevivalRemaining = () => 0;
        r.C.Refresh();
        Assert.That(r.C.PaidMode, Is.EqualTo(1));
        Assert.That(r.C.MeratCost, Is.EqualTo(20), "10 * 0 + 20");
        r.C.OnRevival(2);
        Assert.That(r.Boxes, Has.Count.EqualTo(1));
        r.Boxes[0].Callback(0, r.Boxes[0]);
        Assert.That(r.Sent, Is.EqualTo(new[] { "Revival:0100" }));
    }

    [Test]
    public void ACouponSendsMode1AtOnce()
    {
        var r = new Rig();
        r.C.Env.HasCoupon = () => true;
        r.C.Refresh();
        Assert.That(r.C.CouponSelected, Is.True);
        r.C.OnRevival(2);
        Assert.That(r.Sent, Is.EqualTo(new[] { "Revival:0101" }));
        Assert.That(r.Boxes, Is.Empty);
    }

    [Test]
    public void MesoReviveSendsMode2AndTheCostFollowsTheLevel()
    {
        var r = new Rig();
        r.C.Env.MesoRevivalRemaining = () => 2;
        r.C.Refresh();
        Assert.That(r.C.PaidMode, Is.EqualTo(2));
        Assert.That(r.C.MesoCost, Is.EqualTo(10000 + 1000 * 20), "level 30");
        r.C.OnRevival(2);
        Assert.That(r.Sent, Is.EqualTo(new[] { "Revival:0200" }));
    }

    [Test]
    public void MesoReviveWithoutTheMesoOffersMerat()
    {
        var r = new Rig();
        r.C.Env.MesoRevivalRemaining = () => 2;
        r.C.Env.Meso = () => 5;
        r.C.Refresh();
        r.C.OnRevival(2);
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.Boxes, Has.Count.EqualTo(1));
        r.Boxes[0].Callback(0, r.Boxes[0]);
        Assert.That(r.Sent, Is.EqualTo(new[] { "Revival:0100" }));
    }

    [Test]
    public void ABlockedFieldAndASteppedUpPlayerSendNothing()
    {
        var blocked = new Rig(new RevivalField(0, 1, true, false));
        blocked.C.Refresh();
        blocked.C.OnRevival(2);
        Assert.That(blocked.Sent, Is.Empty);
        Assert.That(blocked.Notices, Is.EqualTo(new[] { "id 282" }));

        var alive = new Rig();
        alive.C.Env.IsDead = () => false;
        alive.C.Refresh();
        alive.C.OnRevival(2);
        alive.Boxes[0].Callback(0, alive.Boxes[0]);
        Assert.That(alive.Sent, Is.Empty);
        Assert.That(alive.Notices, Is.EqualTo(new[] { "id 284" }));
    }

    [Test]
    public void ADungeonWithNoRevivalsLeftLeavesInstead()
    {
        var r = new Rig();
        r.C.Env.Room = () => new RevivalRoom(false, false, 3);
        r.C.Env.RoomRevivalsUsed = () => 3;
        r.C.Refresh();
        Assert.That(r.C.FirstButtonLeaves, Is.True);
        r.C.OnRevival(1);
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.Boxes[0].Extra, Is.EqualTo("leaveInField"));
        Assert.That(Bytes(new Ms2.Net.Packets.RequestMoveFieldLeave()), Is.EqualTo(new byte[] { 1 }));
    }

    [Test]
    public void TheLeaveBoxCountdownSendsTheLeaveRequest()
    {
        var r = new Rig();
        ((IMessageBoxTimerListener)r.C).OnTimerComplete(null, "leaveInField");
        Assert.That(r.Sent, Is.EqualTo(new[] { "RequestMoveField:01" }));
    }

    [Test]
    public void AnExhaustedRoomKeepsTheFreeReviveWhenTheCountPassesTheLimit()
    {
        var r = new Rig();
        r.C.Env.Room = () => new RevivalRoom(false, false, 3);
        r.C.Env.RoomRevivalsUsed = () => 5;
        r.C.Refresh();
        Assert.That(r.C.FirstButtonLeaves, Is.False, "the client's unsigned difference wraps, so it never reads as used up");
        r.C.OnRevival(1);
        Assert.That(r.Sent, Is.EqualTo(new[] { "Revival:00" }));
    }

    [Test]
    public void TheCallbackNameOnRevivalPicksTheButton()
    {
        var r = new Rig();
        Assert.That(r.C.OnCallback("onRevival", new object[] { 1 }), Is.True);
        Assert.That(r.Sent, Is.EqualTo(new[] { "Revival:00" }));
        Assert.That(r.C.OnCallback("other", Array.Empty<object>()), Is.False);
    }
    static Ms2.Net.Packets.MyCharacter DeadPlayer()
    {
        var me = new Ms2.Net.Packets.MyCharacter();
        var u = new Ms2.Net.Packets.FieldAddUser { ObjectId = 9, User = new Ms2.Net.Packets.UserBlock { Name = "a" } };
        u.Stats[0].Health = 500; u.Stats[1].Health = 400; u.Stats[2].Health = 480;
        me.Apply(u);
        me.Apply(new Ms2.Net.Packets.PlayerStat(9, new Dictionary<byte, long[]> { [Ms2.Net.Packets.PlayerStat.Health] = new long[] { 500, 400, 0 } }));
        me.Die(9);
        return me;
    }
    /// <summary>The wiring the game scene uses: the field data feeds Field and FieldId, so the free button sends; the paid ones send nothing.</summary>
    [Test]
    public void TheGameWiringSendsTheFreeReviveFromTheFieldData()
    {
        var me = DeadPlayer();
        Assert.That(me.Dead, Is.True);
        var sent = new List<string>(); var notices = new List<string>();
        var c = new RevivalDialogController { CommonById = id => $"#{id:x}", CommonByKey = k => k + "|{0}" };
        c.Env.MeratRateCap = () => 1f; c.Env.MesoRateCap = () => 1f;
        RevivalWiring.Connect(c.Env, me, () => 100, id => new Ms2.Formats.World.FieldData { Id = id, RevivalReturnId = id },
            p => sent.Add($"{p.Op}:{Convert.ToHexString(Bytes(p))}"), notices.Add, id => $"notice {id:x}");
        c.Refresh();
        c.OnRevival(1);
        Assert.That(sent, Is.EqualTo(new[] { "Revival:00" }));
        sent.Clear();
        c.OnRevival(2);
        Assert.That(sent, Is.Empty, "the paid revive needs inputs the game does not have yet");
    }

    [Test]
    public void TheGameWiringSendsNothingWithoutFieldData()
    {
        var me = DeadPlayer();
        var sent = new List<string>();
        var c = new RevivalDialogController { CommonById = id => $"#{id:x}", CommonByKey = k => k + "|{0}" };
        RevivalWiring.Connect(c.Env, me, () => 100, _ => null, p => sent.Add(p.Op.ToString()), _ => { }, id => "");
        c.Refresh();
        c.OnRevival(1);
        Assert.That(sent, Is.Empty);
    }

    [Test]
    public void TheGameWiringNoticesTheCommonStringById()
    {
        var me = DeadPlayer();
        var notices = new List<string>();
        var c = new RevivalDialogController();
        RevivalWiring.Connect(c.Env, me, () => 100, _ => null, _ => { }, notices.Add, id => $"notice {id:x}");
        c.Env.NoticeById(0x281);
        Assert.That(notices, Is.EqualTo(new[] { "notice 281" }));
    }
}

/// <summary>UIAutoReviveCountDownDialog's countdown (vfunc_72, FUN_141284d70).</summary>
public class AutoReviveCountDownTests
{
    [Test]
    public void TheSecondsCountDownAndTheDeadlineCloses()
    {
        var c = new AutoReviveCountDownController();
        c.Arm(10_000, 5_000);
        Assert.That(c.RemainSeconds, Is.EqualTo(6u), "duration / 1000 + 1");
        Assert.That(c.Step(10_000), Is.EqualTo(0), "unchanged at once");
        Assert.That(c.Step(11_500), Is.EqualTo(4), "3500 ms left: 3 + 1");
        Assert.That(c.Step(11_900), Is.EqualTo(0));
        Assert.That(c.Step(14_999), Is.EqualTo(1));
        Assert.That(c.Step(15_000), Is.EqualTo(0), "the deadline tick is still on 1");
        Assert.That(c.Step(15_001), Is.EqualTo(-1), "past the deadline");
    }
}
