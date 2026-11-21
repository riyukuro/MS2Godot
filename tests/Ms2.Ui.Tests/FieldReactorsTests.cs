using System.Numerics;
using System.Xml.Linq;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CReactor (reactor.cpp), WorldService's reactor map and ReactorAction's checks; CGameRule's reactor context.</summary>
public class FieldReactorsTests
{
    sealed class User : IReactorUser
    {
        public bool CanReact { get; set; } = true;
        public long CharacterId { get; set; } = 77;
        public Dictionary<(int, int), int> Items = new();
        public Dictionary<int, int> Ranks = new();
        public int ItemCount(int code, int rank) => Items.GetValueOrDefault((code, rank));
        public int ItemRank(int code) => Ranks.GetValueOrDefault(code, 1);
    }

    // states: 1 plain (start/idle), 2 connect, 3 sleep, 4 reward, 5 needs 3 of item 900 (consume 2, checkCount 3),
    // 6 needs item 901 at its own rank (rank 0); reactor 10: states [1, 2, 3, 4], 20: [5, 6], 30: [1] with gameEventId 900051
    static ReactorTable Table() => new(
        XDocument.Parse(@"<ms2>
            <reactorState id='1' startAni='Opened_A' idleAni='Idle_A' kfmName='box' actionStringKey='s_open'/>
            <reactorState id='2' type='connect' idleAni='Idle_B'/>
            <reactorState id='3' type='sleep'/>
            <reactorState id='4' type='Reward' startAni='' idleAni='Idle_D'/>
            <reactorState id='5'><condItem code='900' consume='2' checkCount='3'/></reactorState>
            <reactorState id='6'><condItem code='901' rank='0' checkCount='1'/></reactorState>
        </ms2>"),
        XDocument.Parse(@"<ms2>
            <reactor id='10' reactorStateList='1,2,3,4' reactDistance='150' reactTimeMSec='0'/>
            <reactor id='20' reactorStateList='5,6' reactDistance='150'/>
            <reactor id='30' reactorStateList='1' reactDistance='150' gameEventId='900051'/>
        </ms2>"));

    static ReactorState St(int idx, params long[] players) => new(idx, 0, players, 0);
    static ReactorAdd Add(int obj, string name, int reactorId, Vector3 pos, ReactorState s) =>
        new(obj, name, reactorId, pos, Vector3.Zero, s);

    [Test]
    public void AddQueuesIdleAndFailsForUnknownDataOrState()
    {
        var f = new FieldReactors(Table());
        var r = f.Add(Add(1, "a", 10, Vector3.Zero, St(0)), out _);
        Assert.That(r.Animations, Is.EqualTo(new[] { "Idle_A" }), "FUN_140b79490 queues only the idle animation");
        Assert.That(f.Add(Add(2, "b", 99, Vector3.Zero, St(0)), out _), Is.Null, "unknown reactor id");
        Assert.That(f.Add(Add(3, "c", 10, Vector3.Zero, St(4)), out _), Is.Null, "state index past the list");
        Assert.That(f.Count, Is.EqualTo(1));
    }

    [Test]
    public void UpdateQueuesStartThenIdleSkippingEmptyAndKeepsQueueWithoutState()
    {
        var f = new FieldReactors(Table());
        var r = f.Add(Add(1, "a", 10, Vector3.Zero, St(0)), out _);
        f.Update(1, St(0, 5, 6));
        Assert.That(r.Animations, Is.EqualTo(new[] { "Opened_A", "Idle_A" }));
        Assert.That(r.Players, Is.EqualTo(new[] { 5L, 6L }));
        f.Update(1, St(3));
        Assert.That(r.Animations, Is.EqualTo(new[] { "Idle_D" }), "AddAnimation skips the empty startAni");
        f.Update(1, St(9));
        Assert.That(r.Animations, Is.EqualTo(new[] { "Idle_D" }), "no state at the index: the queue is left");
        Assert.That(r.StateIndex, Is.EqualTo(9));
        Assert.That(f.Update(2, St(0)), Is.Null);
    }

    [Test]
    public void EndOfSequencePopsOnlyTheHead()
    {
        var f = new FieldReactors(Table());
        var r = f.Add(Add(1, "a", 10, Vector3.Zero, St(0)), out _);
        f.Update(1, St(0));
        Assert.That(r.EndOfSequence("Idle_A"), Is.False);
        Assert.That(r.EndOfSequence("Opened_A"), Is.True);
        Assert.That(r.Head, Is.EqualTo("Idle_A"));
    }

    [Test]
    public void StateTypesGateTheUse()
    {
        var f = new FieldReactors(Table());
        var u = new User();
        var r = f.Add(Add(1, "a", 10, Vector3.Zero, St(1)), out _);
        Assert.That(r.CanUse(u), Is.True, "connect: not yet in the set");
        f.Update(1, St(1, 77));
        Assert.That(r.CanUse(u), Is.False, "connect: already in the set");
        f.Update(1, St(2, 77));
        Assert.That(r.CanUse(u), Is.False, "sleep refuses everyone");
        f.Update(1, St(3));
        Assert.That(r.CanUse(u), Is.False, "reward: not in the set");
        f.Update(1, St(3, 77));
        Assert.That(r.CanUse(u), Is.True, "reward: in the set");
        u.CanReact = false;
        Assert.That(r.CanUse(u), Is.False, "FUN_140b7a5e0");
    }

    [Test]
    public void ItemConditionNeedsTheLargerCountAtItsRank()
    {
        var f = new FieldReactors(Table());
        var u = new User();
        var r = f.Add(Add(1, "a", 20, Vector3.Zero, St(0)), out _);
        u.Items[(900, 1)] = 2;
        Assert.That(r.CanUse(u), Is.False, "max(checkCount 3, consume 2) = 3");
        u.Items[(900, 1)] = 3;
        Assert.That(r.CanUse(u), Is.True);
        f.Update(1, St(1));
        u.Items[(901, 1)] = 1;
        u.Ranks[901] = 4;
        Assert.That(r.CanUse(u), Is.False, "rank 0 = the item's own rank (4)");
        u.Items[(901, 4)] = 1;
        Assert.That(r.CanUse(u), Is.True);
    }

    [Test]
    public void NearestWalksNamesAndALaterTieWins()
    {
        var f = new FieldReactors(Table());
        var u = new User();
        f.Add(Add(1, "b", 10, new Vector3(100, 0, 0), St(0)), out _);
        f.Add(Add(2, "a", 10, new Vector3(-100, 0, 0), St(0)), out _);
        f.Add(Add(3, "c", 10, new Vector3(0, 160, 0), St(0)), out _);
        f.Add(Add(4, "d", 10, new Vector3(0, 50, 0), St(2)), out _);
        Assert.That(f.ByName.Select(r => r.Name), Is.EqualTo(new[] { "a", "b", "c", "d" }));
        Assert.That(f.Nearest(Vector3.Zero, u).ObjectId, Is.EqualTo(1), "a and b tie: b comes later by name");
        f.Update(1, St(2));
        Assert.That(f.Nearest(Vector3.Zero, u).ObjectId, Is.EqualTo(2), "b sleeps; c is past reactDistance; d sleeps");
        f.Remove(2);
        Assert.That(f.Nearest(Vector3.Zero, u), Is.Null);
    }

    [Test]
    public void SameNameLeavesThePollButStaysAnObject()
    {
        var f = new FieldReactors(Table());
        var u = new User();
        f.Add(Add(1, "a", 10, new Vector3(10, 0, 0), St(0)), out _);
        f.Add(Add(2, "a", 10, new Vector3(200, 0, 0), St(0)), out var replaced);
        Assert.That(replaced, Is.Null);
        Assert.That(f.Get(1), Is.Not.Null, "still found by id");
        Assert.That(f.Nearest(Vector3.Zero, u), Is.Null, "m_7c8 holds only object 2, out of reach");
        f.Remove(1);
        Assert.That(f.ByName, Is.Empty, "RemoveKey by name takes object 2 from the name map");
        Assert.That(f.Get(2), Is.Not.Null);
    }

    [Test]
    public void GameEventCheckCodes()
    {
        var t = Table();
        var plain = t.Get(10);
        var ev = t.Get(30);
        var rule = new ReactorEvent(1, 900051, 2, 0, new uint[] { 2000062 });
        Assert.That(FieldReactors.CheckGameEvent(null, rule, 2000062), Is.EqualTo(1));
        Assert.That(FieldReactors.CheckGameEvent(plain, null, 0), Is.EqualTo(0), "gameEventId 0");
        Assert.That(FieldReactors.CheckGameEvent(ev, null, 2000062), Is.EqualTo(4));
        Assert.That(FieldReactors.CheckGameEvent(ev, rule with { EventId = 5 }, 2000062), Is.EqualTo(4));
        Assert.That(FieldReactors.CheckGameEvent(ev, rule, 2000063), Is.EqualTo(5));
        Assert.That(FieldReactors.CheckGameEvent(ev, rule, 2000062), Is.EqualTo(0));
        Assert.That(FieldReactors.CheckGameEvent(ev, rule, 2000062, 2), Is.EqualTo(6));
    }

    [Test]
    public void UseOutcomes()
    {
        var t = Table();
        var instant = t.Get(10);
        var timed = t.Get(20);
        Assert.That(FieldReactors.Use(timed, 6, 1, false), Is.EqualTo(FieldReactors.UseOutcome.FailedRewardCount));
        Assert.That(FieldReactors.Use(timed, 4, 1, false), Is.EqualTo(FieldReactors.UseOutcome.Failed));
        Assert.That(FieldReactors.Use(timed, 0, 1, false), Is.EqualTo(FieldReactors.UseOutcome.Cast));
        Assert.That(FieldReactors.Use(timed, 0, 0x3a, true), Is.EqualTo(FieldReactors.UseOutcome.RideOff));
        Assert.That(FieldReactors.Use(timed, 0, 2, false), Is.EqualTo(FieldReactors.UseOutcome.None), "state 2 may not enter 0x3d");
        Assert.That(FieldReactors.Use(instant, 0, 2, false), Is.EqualTo(FieldReactors.UseOutcome.Send));
        Assert.That(FieldReactors.Use(instant, 0, 0x10, false), Is.EqualTo(FieldReactors.UseOutcome.None));
    }

    [Test]
    public void NearestAllocatesNothing()
    {
        var f = new FieldReactors(Table());
        var u = new User();
        for (int i = 0; i < 20; i++) f.Add(Add(i, "r" + i, 10, new Vector3(i * 10, 0, 0), St(0)), out _);
        f.Nearest(Vector3.Zero, u);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) f.Nearest(Vector3.Zero, u);
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0));
    }

    static GameEventMessage Msg(byte kind, params ReactorEvent[] e) =>
        new(kind, Array.Empty<ShutdownInteractObjectEvent>(), Array.Empty<int>(), false) { ReactorEvents = e };

    [Test]
    public void RuleFollowsTheManager()
    {
        var rule = new ReactorRule();
        var a = new ReactorEvent(1, 900051, 2, 0, new uint[] { 1 });
        var b = new ReactorEvent(2, 900052, 2, 0, new uint[] { 1 });
        rule.Apply(Msg(0, a));
        Assert.That(rule.Current, Is.SameAs(a));
        rule.Apply(Msg(1, a with { MaxCount = 9 }));
        Assert.That(rule.Current, Is.SameAs(a), "an id already held is not added again");
        rule.Apply(Msg(1, b));
        Assert.That(rule.Current, Is.SameAs(b));
        rule.Apply(new GameEventMessage(2, Array.Empty<ShutdownInteractObjectEvent>(), new[] { 1 }, false));
        Assert.That(rule.Current, Is.Null, "vfunc_6 sets null whichever event held it");
        var a2 = a with { MaxCount = 5 };
        rule.Apply(Msg(3, a2));
        Assert.That(rule.Current, Is.SameAs(a2));
        rule.Clear();
        Assert.That(rule.Current, Is.Null);
    }
}
