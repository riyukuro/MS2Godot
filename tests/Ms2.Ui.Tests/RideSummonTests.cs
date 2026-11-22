using System.Collections.Generic;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>MyPC::UseRideeItem (mypc.cpp:4627) and StateSummon for a ride item (statesummon.cpp:180-402).</summary>
public class RideSummonTests
{
    static RideItemFacts Ready() => new()
    {
        RideeKnown = true, ItemRideeId = 5, CanUseItem = true, SkillUsable = true, StateAllows = true, MapAllows = true,
    };

    [Test]
    public void AUsableRideItemSummonsWhenNotRiding()
    {
        var f = Ready();
        Assert.That(RideItemUse.Decide(f, out int id, out string key), Is.EqualTo(RideItemUse.Outcome.Summon));
        Assert.That(id, Is.Zero);
        Assert.That(key, Is.Null);
    }

    [Test]
    public void TheSameRideeRidesOffAndAnUnknownRowDoesNothing()
    {
        var f = Ready();
        f.Riding = true;
        f.CurrentRideeId = 5;
        Assert.That(RideItemUse.Decide(f, out _, out _), Is.EqualTo(RideItemUse.Outcome.RideOff));
        f.RideeKnown = false;
        Assert.That(RideItemUse.Decide(f, out _, out _), Is.EqualTo(RideItemUse.Outcome.None));
    }

    [Test]
    public void AMountActionOfAnotherTypeRefusesFirst()
    {
        var f = Ready();
        f.HasMountAction = true;
        f.MountActionType = 2;
        f.RideeKnown = false;
        Assert.That(RideItemUse.Decide(f, out int id, out _), Is.EqualTo(RideItemUse.Outcome.Notice));
        Assert.That(id, Is.EqualTo(0x655));
        f.MountActionType = 1;
        f.RideeKnown = true;
        Assert.That(RideItemUse.Decide(f, out _, out _), Is.EqualTo(RideItemUse.Outcome.Summon));
    }

    [Test]
    public void RefusalNoticesComeInTheClientsOrder()
    {
        var f = Ready();
        f.MapAllows = false;
        f.Expired = f.PcBangRefused = f.VipRefused = f.WeddingRefused = f.BindRefused = true;
        Assert.That(RideItemUse.Decide(f, out int id, out _), Is.EqualTo(RideItemUse.Outcome.Notice));
        Assert.That(id, Is.EqualTo(0xe1));
        f.Expired = false;
        RideItemUse.Decide(f, out id, out _);
        Assert.That(id, Is.EqualTo(0xe2));
        f.PcBangRefused = false;
        RideItemUse.Decide(f, out id, out string key);
        Assert.That((id, key), Is.EqualTo((0, "s_item_err_puton_vip")));
        f.VipRefused = false;
        RideItemUse.Decide(f, out id, out key);
        Assert.That((id, key), Is.EqualTo((0, "s_item_err_puton_wedding")));
        f.WeddingRefused = false;
        RideItemUse.Decide(f, out id, out _);
        Assert.That(id, Is.EqualTo(0xfe));
        f.BindRefused = false;
        RideItemUse.Decide(f, out id, out _);
        Assert.That(id, Is.EqualTo(0x655));
    }

    [Test]
    public void RidingAnotherMountAsksModeTwoOnlyFromItsStateOne()
    {
        var f = Ready();
        f.Riding = true;
        f.CurrentRideeId = 9;
        f.MountState = 1;
        f.StateAllows = false;                 // FUN_141527160 is not asked while riding
        Assert.That(RideItemUse.Decide(f, out _, out _), Is.EqualTo(RideItemUse.Outcome.RideAlong));
        f.MountState = 2;
        f.BindRefused = true;                  // no owner-bind notice on this side
        Assert.That(RideItemUse.Decide(f, out int id, out _), Is.EqualTo(RideItemUse.Outcome.Notice));
        Assert.That(id, Is.EqualTo(0x655));
    }

    [Test]
    public void TheCastTimeIsTheRowsSecondsOrTheConstant()
    {
        Assert.That(RideItemUse.CastMs(1.5f), Is.EqualTo(1500));
        Assert.That(RideItemUse.CastMs(null), Is.EqualTo(5000));
        Assert.That(RideItemUse.CastMs(null, 3000), Is.EqualTo(3000));
    }

    sealed class Host : IRideSummonHost
    {
        public bool Bar = true, Row = true;
        public long Now;
        public readonly List<IOutPacket> Sent = new();
        public string Text, Anim;
        public int Ms, Cancels;
        public ICastingEventListener Listener;
        public bool HasPlayer { get; set; } = true;
        public void Send(IOutPacket packet) => Sent.Add(packet);
        public bool StartCasting(ICastingEventListener listener, string text, int durationMs)
        {
            Listener = listener; Text = text; Ms = durationMs;
            return Bar;
        }
        public void CancelCasting() => Cancels++;
        public long NowMs => Now;
        public void PlayPlayerAnim(string animName, bool loop) => Anim = animName;
        public void StopPlayerInteractAnim() => Anim = null;
        public bool RideeRowOf(long itemUid, out int rideeId) { rideeId = Row ? 77 : 0; return Row; }
        public bool RideOption => true;
    }

    [Test]
    public void AFinishedCastSendsModeZeroWithTheItem()
    {
        var h = new Host();
        var s = new RideSummonCast(h);
        s.Start(0x1234, 1500, "Summoning", null);
        Assert.That((h.Text, h.Ms, h.Anim), Is.EqualTo(("Summoning", 1500, "Object_React_A")));
        Assert.That(s.ItemUid, Is.EqualTo(0x1234));
        Assert.That(s.Tick(false), Is.False);
        h.Listener.OnCastingFinished();
        Assert.That(s.Tick(true), Is.True);      // the finish wins over the move input
        Assert.That(h.Sent, Has.Count.EqualTo(1));
        Assert.That(h.Sent[0], Is.EqualTo(new RideOnItemRequest(77, 0x1234, true)));
        Assert.That(s.IsCasting, Is.False);
        Assert.That(h.Anim, Is.Null);
        Assert.That(h.Cancels, Is.Zero);
    }

    [Test]
    public void MoveInputCancelsTheBarAndAGoneRowSendsNothing()
    {
        var h = new Host();
        var s = new RideSummonCast(h);
        s.Start(1, 1000, "", null);
        Assert.That(s.Tick(true), Is.True);
        Assert.That(h.Cancels, Is.EqualTo(1));
        Assert.That(h.Sent, Is.Empty);

        h.Bar = false;
        h.Row = false;
        s.Start(1, 1000, "", null);
        h.Now = 999;
        Assert.That(s.Tick(false), Is.False);
        h.Now = 1000;
        Assert.That(s.Tick(false), Is.True);     // no bar: the time runs on the clock
        Assert.That(h.Sent, Is.Empty);
        Assert.That(s.IsCasting, Is.False);
    }

    [Test]
    public void ADeathMidCastSendsNoRideRequest()
    {
        // OnDeadUser: MyPC::vfunc_59(0xc) leaves StateSummon through vfunc_5, which cancels the bar and sends nothing
        var me = new MyCharacter();
        var u = new FieldAddUser { ObjectId = 9, User = new UserBlock { Name = "a" } };
        u.Stats[0].Health = 500; u.Stats[2].Health = 500;
        me.Apply(u);
        me.Apply(new PlayerStat(9, new Dictionary<byte, long[]> { [PlayerStat.Health] = new long[] { 500, 500, 0 } }));
        var h = new Host();
        var s = new RideSummonCast(h);
        me.StateEntered += st => InteractKey.LeaveForServerState(st, null, null, null, s);
        s.Start(0x1234, 1500, "", null);
        Assert.That(me.Die(9), Is.True);
        Assert.That(s.IsCasting, Is.False);
        Assert.That(s.ItemUid, Is.Zero);
        Assert.That(h.Cancels, Is.EqualTo(1));
        h.Listener.OnCastingFinished();
        Assert.That(s.Tick(false), Is.False);
        Assert.That(h.Sent, Is.Empty);
    }

    [Test]
    public void AServerSummonStateKeepsTheCast()
    {
        // StateSummon keeps State::vfunc_25 (true): the same state again does not leave it
        var h = new Host();
        var s = new RideSummonCast(h);
        s.Start(1, 1000, "", null);
        InteractKey.LeaveForServerState(InteractKey.StateSummon, null, null, null, s);
        Assert.That(s.IsCasting, Is.True);
        InteractKey.LeaveForServerState(1, null, null, null, s);
        Assert.That(s.IsCasting, Is.False);
    }

    [Test]
    public void TheInteractKeyMidCastDoesNothing()
    {
        // the state reads 0x26, which MyPC::FUN_141527160 refuses for the object and the npc interaction alike
        var h = new Host();
        var s = new RideSummonCast(h);
        LiveInteractObjects objs = null;
        LiveInteractNpc npcs = null;
        var oh = new LiveInteractObjectsTests.Host();
        var nh = new LiveInteractNpcTests.Host();
        oh.StateFn = () => InteractKey.StateId(objs, npcs, null, s, 1);
        nh.StateFn = () => InteractKey.StateId(objs, npcs, null, s, 1);
        objs = LiveInteractObjectsTests.LeverOn(oh, 1500);
        npcs = new LiveInteractNpc(nh);
        npcs.Add(7, LiveInteractNpcTests.Row(), talkable: true, new Ms2.Formats.Model.Vec3(100, 0, 0));
        var at = new Ms2.Formats.Model.Vec3(10, 0, 0);

        s.Start(1, 1000, "", null);
        Assert.That(InteractKey.StateId(objs, npcs, null, s, 1), Is.EqualTo(InteractKey.StateSummon));
        Assert.That(LiveInteractObjects.StateAllowsInteraction(InteractKey.StateId(objs, npcs, null, s, 1)), Is.False);
        Assert.That(InteractKey.Use(objs, npcs, false, at), Is.False);
        npcs.Poll(new Ms2.Formats.Model.Vec3(0, 0, 0));
        Assert.That(InteractKey.Use(objs, npcs, true, at), Is.False);
        Assert.That(oh.Sent, Is.Empty);
        Assert.That(objs.IsCasting || npcs.IsCasting, Is.False);
        Assert.That(s.IsCasting, Is.True);
        Assert.That(h.Cancels, Is.Zero);

        s.Cancel();
        Assert.That(InteractKey.StateId(objs, npcs, null, s, 1), Is.EqualTo(1));
        Assert.That(InteractKey.Use(objs, npcs, false, at), Is.True);
    }

    [Test]
    public void TheOwnPcGoneMidCastEndsItWithoutTouchingTheNewBar()
    {
        // SetMyPC(0) of a field change frees MyPC and its state: nothing sent, the new field's bar left alone
        var h = new Host();
        var s = new RideSummonCast(h);
        s.Start(0x55, 1000, "", null);
        h.HasPlayer = false;
        Assert.That(s.Tick(false), Is.True);
        Assert.That(s.IsCasting, Is.False);
        Assert.That(s.ItemUid, Is.Zero);
        h.HasPlayer = true;
        h.Listener.OnCastingFinished();
        Assert.That(s.Tick(true), Is.False);
        Assert.That(h.Sent, Is.Empty);
        Assert.That(h.Cancels, Is.Zero);
    }

    [Test]
    public void ABarCancelEndsTheStateWithoutAnotherCancel()
    {
        var h = new Host();
        var s = new RideSummonCast(h);
        s.Start(1, 1000, "", null);
        h.Listener.OnCastingCancelled();
        Assert.That(s.IsCasting, Is.False);
        Assert.That(h.Cancels, Is.Zero);
        Assert.That(s.Tick(true), Is.False);
    }
}
