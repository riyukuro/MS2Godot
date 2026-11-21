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
    internal static Ms2.Net.Packets.MyCharacter DeadPlayer()
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
            p => sent.Add($"{p.Op}:{Convert.ToHexString(Bytes(p))}"), notices.Add, id => $"notice {id:x}", id => $"map {id}");
        c.Refresh();
        c.OnRevival(1);
        Assert.That(sent, Is.EqualTo(new[] { "Revival:00" }));
        sent.Clear();
        c.OnRevival(2);
        Assert.That(sent, Is.Empty, "the paid revive needs RevivalPaidInputs");
    }

    /// <summary>The game wiring without RevivalPaidInputs refuses the paid revive even when every other input would let it send
    /// (the meso revive here): its send needs the buffs' disable check, the meso revivals left and the platform's merat check.
    /// So the dialog takes the client's one-button state, mode 0 on the "btn1" frame, not a paid button that does nothing.</summary>
    [Test]
    public void TheGameWiringRefusesAPaidReviveTheOtherInputsWouldAllow()
    {
        var me = DeadPlayer();
        var sent = new List<string>();
        var c = new RevivalDialogController { CommonById = id => $"#{id:x}", CommonByKey = k => k + "|{0}" };
        RevivalWiring.Connect(c.Env, me, () => 100, id => new Ms2.Formats.World.FieldData { Id = id, RevivalReturnId = id },
            p => sent.Add($"{p.Op}:{Convert.ToHexString(Bytes(p))}"), _ => { }, id => "", id => $"map {id}");
        c.Env.MeratRateCap = () => 1f; c.Env.MesoRateCap = () => 1f;
        c.Env.MesoRevivalRemaining = () => 2; c.Env.Meso = () => 1_000_000;
        c.Refresh();
        Assert.That(c.PaidMode, Is.EqualTo(0), "no paid button without the paid inputs");
        c.OnRevival(2);
        Assert.That(sent, Is.Empty);
    }

    sealed class PaidRig
    {
        public readonly Ms2.Net.Packets.MyCharacter Me = DeadPlayer();
        public readonly List<string> Sent = new(), Notices = new();
        public readonly List<MessageBoxData> Boxes = new();
        public readonly Ms2.Ui.Skills.AdditionalBuffList Buffs;
        public readonly RevivalDialogController C = new() { CommonById = id => $"#{id:x}", CommonByKey = k => k + "|{0}" };
        public long Merat;
        public int Short;
        public static readonly Ms2.Ui.Skills.AdditionalLevelData Plain = new() { Level = 1 }, Disable = new() { Level = 1, DisableRevivalHere = true };

        public PaidRig(params (string, string)[] constants)
        {
            Buffs = new Ms2.Ui.Skills.AdditionalBuffList((id, lv) => id == 7 ? Disable : Plain);
            var k = new Ms2.Formats.World.ClientConstants(constants.Select(p => new KeyValuePair<string, string>(p.Item1, p.Item2)));
            RevivalWiring.Connect(C.Env, Me, () => 100, id => new Ms2.Formats.World.FieldData { Id = id, RevivalReturnId = id, InfiniteMeratRevival = false },
                p => Sent.Add($"{p.Op}:{Convert.ToHexString(Bytes(p))}"), Notices.Add, id => $"notice {id:x}", id => $"map {id}",
                new RevivalPaidInputs(() => Merat, Buffs, k));
            C.Env.ShowBox = Boxes.Add;
            C.Env.PaymentShort = () => Short++;
        }

        public void Buff(int key, uint id, int overlap, byte enabled) =>
            Buffs.Apply(new Ms2.Net.Packets.BuffPacket(Ms2.Net.Packets.BuffPacket.Add, 9, key, 9, 0, new Ms2.Net.Packets.BuffState(0, 1000, id, 1, overlap, enabled), 0));
    }

    /// <summary>With RevivalPaidInputs the meso revive sends while a daily count is left (max(3 - used, 0), mypc.cpp:2169), then
    /// the merat revive takes over: its cost is CalcRevivalMerat at the debuff's overlap (Constants +0x51d8) scaled by the
    /// reduction ceiling (+0x8cc0, default 0.5, against an ability rate of 0), checked against the wallet sum (vfunc_40).</summary>
    [Test]
    public void ThePaidInputsGiveTheMesoThenTheMeratRevive()
    {
        var r = new PaidRig();
        r.Me.Meso = 1_000_000;
        r.Me.MesoRevivalsUsed = 2;
        r.C.Refresh();
        Assert.That((r.C.PaidMode, r.C.Env.MesoRevivalRemaining()), Is.EqualTo((2, 1)));
        r.C.OnRevival(2);
        Assert.That(r.Sent, Has.Count.EqualTo(1));
        Assert.That(r.Sent[0], Does.StartWith("Revival:02"));
        r.Sent.Clear();

        r.Me.MesoRevivalsUsed = 5;
        r.Buff(20, 100000001, 3, 0);                    // GetOverlapCount does not test the enabled flag
        r.C.Refresh();
        Assert.That((r.C.PaidMode, r.C.Env.MesoRevivalRemaining(), r.C.Env.RevivalOverlapCount()), Is.EqualTo((1, 0, 3)));
        r.C.OnRevival(2);
        Assert.That((r.Short, r.Boxes.Count), Is.EqualTo((1, 0)), "an empty wallet is short");
        Assert.That(r.C.MeratCost, Is.EqualTo(10 * 3 + 20), "min(0.5, ability rate 0): no reduction");
        r.Merat = 50;
        r.C.OnRevival(2);
        Assert.That((r.Short, r.Boxes.Count), Is.EqualTo((1, 1)));
        Assert.That(r.Sent, Is.Empty, "the merat revive asks first");
    }

    /// <summary>The paid inputs' buff checks: an enabled effect of the debuff code stops a paid revive (0x281, FUN_140970020 with
    /// compare 1), an enabled effect whose level has disableRevivalHere refuses it and blocks the revive (PC::IsRevivalBlocked),
    /// and LimitMeratRevival (Constants +0xa278, true when the text holds one of "1oOtT") switches the merat revive off.</summary>
    [Test]
    public void ThePaidInputsReadTheBuffsAndTheConstants()
    {
        var parsed = Ms2.Ui.Skills.AdditionalLevelData.Load(System.Xml.Linq.XElement.Parse("<level level=\"1\"><Basic disableRevivalHere=\"1\"/></level>"), null);
        Assert.That(parsed.DisableRevivalHere, Is.True);
        var r = new PaidRig();
        r.Me.Meso = 1_000_000;
        r.Buff(20, 100000001, 1, 1);
        r.C.Refresh();
        r.C.OnRevival(2);
        Assert.That((r.Sent.Count, r.Notices.LastOrDefault()), Is.EqualTo((0, "notice 281")));
        r.Buffs.Apply(new Ms2.Net.Packets.BuffPacket(Ms2.Net.Packets.BuffPacket.Remove, 9, 20, 9, 0, null, null));
        Assert.That(r.C.Env.RevivalBlocked(), Is.False);
        r.Buff(21, 7, 1, 1);
        Assert.That((r.C.Env.RevivalBlocked(), r.C.Env.BuffsDisableRevival()), Is.EqualTo((true, true)));
        r.C.OnRevival(2);
        Assert.That((r.Sent.Count, r.Notices.LastOrDefault()), Is.EqualTo((0, "s_msg_revival_merat_cannot_here_debuff|{0}")));

        var off = new PaidRig(("LimitMeratRevival", "TRUE"), ("RoomExitWaitSecond", "4"));
        off.Me.MesoRevivalsUsed = 3;
        off.C.Refresh();
        Assert.That((off.C.PaidMode, off.C.Env.LeaveBoxSeconds()), Is.EqualTo((0, 4)));
        var on = new PaidRig(("LimitMeratRevival", "false"));
        on.Me.MesoRevivalsUsed = 3;
        on.C.Refresh();
        Assert.That((on.C.PaidMode, on.C.Env.LeaveBoxSeconds(), on.C.Env.MeratRateCap()), Is.EqualTo((1, 10, 0.5f)));
    }

    /// <summary>The game wiring names the return field by its map name (CStringTable table 3), in the death text's 0x27d line
    /// and in the 0x287 box (uirevivaldialog.cpp:363, :418), not by its id.</summary>
    [Test]
    public void TheGameWiringNamesTheReturnFieldByItsMapName()
    {
        var me = DeadPlayer();
        var boxes = new List<MessageBoxData>();
        var c = new RevivalDialogController { CommonById = id => $"#{id:x}<{{0}}>", CommonByKey = k => k + "|{0}" };
        RevivalWiring.Connect(c.Env, me, () => 100, id => new Ms2.Formats.World.FieldData { Id = id, RevivalReturnId = 200 },
            _ => { }, _ => { }, id => "", id => $"map {id}");
        c.Env.ShowBox = boxes.Add;
        c.Refresh();
        Assert.That(c.DeathMessage, Does.EndWith("\n#27d<map 200>"));
        c.OnRevival(1);
        Assert.That(boxes, Has.Count.EqualTo(1));
        Assert.That(boxes[0].Msg, Is.EqualTo("#287<map 200>"));
    }

    [Test]
    public void TheGameWiringSendsNothingWithoutFieldData()
    {
        var me = DeadPlayer();
        var sent = new List<string>();
        var c = new RevivalDialogController { CommonById = id => $"#{id:x}", CommonByKey = k => k + "|{0}" };
        RevivalWiring.Connect(c.Env, me, () => 100, _ => null, p => sent.Add(p.Op.ToString()), _ => { }, id => "", id => $"map {id}");
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
        RevivalWiring.Connect(c.Env, me, () => 100, _ => null, _ => { }, notices.Add, id => $"notice {id:x}", id => $"map {id}");
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
