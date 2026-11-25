using Ms2.Formats.World;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The GameEventUserValue tables (GameEventUserValue @0x14041f7c0..0x14041fc20, GameEventUserValueManager @0x140da5390) and
/// the attend gift events with the character's times (UIAttendGiftDialog's data).</summary>
public class AttendEventsTests
{
    static GameEventUserValueMessage Whole(int list, params GameEventVariable[] v) => new(0, list, v);
    static GameEventUserValueMessage One(int list, GameEventVariable v) => new(1, list, new[] { v });

    static AttendGiftEvent Event(int id, string name = "n") =>
        new(id, 1, 2, name, "", false, true, 0, 0, 0, 0, 0, 0, 0, Array.Empty<GuildStorageItem>());

    static GameEventMessage Events(byte kind, params AttendGiftEvent[] e) =>
        new(kind, Array.Empty<ShutdownInteractObjectEvent>(), Array.Empty<int>(), false) { AttendGiftEvents = e };

    [Test]
    public void ATableReplacesTheListAndAValueExpiresWithTheServerTime()
    {
        long now = 100;
        var t = new GameEventUserValues(() => now);
        t.Apply(Whole(0, new GameEventVariable(0x66, 0, "12", 200), new GameEventVariable(0x69, 0, "7", 50)));
        Assert.That(t.GetInt(0, 0x66, 0), Is.EqualTo(12));
        Assert.That(t.GetInt(0, 0x69, 0, -1), Is.EqualTo(-1), "an expiry not later than the server time: the default");
        Assert.That(t.Has(1, 0x66), Is.False, "the other list");
        now = 200;
        Assert.That(t.Has(0, 0x66), Is.False, "expiry == server time: gone (Get @0x14041fc20)");
        now = 100;
        t.Apply(Whole(0, new GameEventVariable(0x6a, 0, "9", 300)));
        Assert.That((t.Has(0, 0x66), t.GetInt(0, 0x6a, 0)), Is.EqualTo((false, 9)), "kind 0 clears the list first");
    }

    [Test]
    public void OneValueReplacesItsKeyAndTellsTheListenersOfItsType()
    {
        var t = new GameEventUserValues(() => 0);
        var seen = new List<string>();
        Action<GameEventVariable> a = v => seen.Add("a" + v.Value), b = v => seen.Add("b" + v.Value);
        t.RegisterListener(0x66, a);
        t.RegisterListener(0x66, b);
        t.RegisterListener(0x69, v => seen.Add("p" + v.Value));
        t.Apply(One(0, new GameEventVariable(0x66, 0, "1", 10)));
        t.Apply(One(0, new GameEventVariable(0x66, 0, "2", 10)));
        Assert.That(seen, Is.EqualTo(new[] { "a1", "b1", "a2", "b2" }), "in the order registered, only that type");
        Assert.That(t.GetInt(0, 0x66, 0), Is.EqualTo(2));
        Assert.That(t.Count(0), Is.EqualTo(1));
        t.UnregisterListener(0x66, a);
        t.Apply(One(0, new GameEventVariable(0x66, 0, "3", 10)));
        Assert.That(seen.Last(), Is.EqualTo("b3"));
        t.Apply(Whole(0, new GameEventVariable(0x66, 0, "4", 10)));
        Assert.That(seen.Count, Is.EqualTo(5), "a whole table does not dispatch");
    }

    [Test]
    public void AMessageTheReaderMarkedUnreadChangesNothing()
    {
        var t = new GameEventUserValues(() => 0);
        t.Apply(Whole(1, new GameEventVariable(1, 0, "x", 10)));
        t.Apply(new GameEventUserValueMessage(0, -1, Array.Empty<GameEventVariable>()));
        t.Apply(new GameEventUserValueMessage(2, -1, Array.Empty<GameEventVariable>()));
        Assert.That(t.Count(1), Is.EqualTo(1));
    }

    [TestCase("1", true)] [TestCase("true", true)] [TestCase("T", true)] [TestCase("On", true)] [TestCase("no", true)]
    [TestCase("", false)] [TestCase("0", false)] [TestCase("false", false)] [TestCase("x", false)]
    public void ABoolValueIsParseBoolW(string text, bool expected)
    {
        // ParseBoolW (packetlogger.cpp:183): any of "1oOtT" makes it true, an empty text is false
        var t = new GameEventUserValues(() => 0);
        t.Apply(One(0, new GameEventVariable(100, 0, text, 10)));
        Assert.That(t.GetBool(0, 100, 0), Is.EqualTo(expected));
    }

    [Test]
    public void IntValuesAreTheClientsAtoi()
    {
        var t = new GameEventUserValues(() => 0);
        t.Apply(Whole(0, new GameEventVariable(0x66, 0, "12abc", 10), new GameEventVariable(0x65, 0, "abc", 10), new GameEventVariable(0x6a, 0, "4000000000", 10)));
        Assert.That(t.GetInt(0, 0x66, 0), Is.EqualTo(12), "_wtoi stops at the first non digit");
        Assert.That(t.GetInt(0, 0x65, 0, 5), Is.EqualTo(0), "text that is no number is 0, not the default");
        Assert.That(t.GetInt64(0, 0x6a, 0), Is.EqualTo(4000000000L));
    }

    [Test]
    public void AttendEventsFollowTheGameEventKinds()
    {
        var a = new AttendEvents();
        a.Apply(Events(0, Event(1), Event(2)));
        a.Apply(Events(1, Event(2, "again"), Event(3)));
        Assert.That(a.Events.Select(e => (e.Id, e.Name)), Is.EqualTo(new[] { (1, "n"), (2, "n"), (3, "n") }), "an id already held is not added again");
        a.Apply(Events(3, Event(2, "new")));
        Assert.That(a.TryGetEvent(2, out var e2) && e2.Name == "new", Is.True, "update replaces");
        a.Apply(new GameEventMessage(2, Array.Empty<ShutdownInteractObjectEvent>(), new[] { 1, 99 }, false));
        Assert.That(a.Events.Select(e => e.Id), Is.EqualTo(new[] { 2, 3 }));
        a.Clear();
        Assert.That(a.Count, Is.Zero);
    }

    [Test]
    public void TheCharacterTimesMergeAndOutliveTheScene()
    {
        var a = new AttendEvents();
        a.SetTimes(new[] { (171008, 10L), (171009, 20L) });
        a.SetTimes(new[] { (171009, 30L), (5, 1L) });
        Assert.That((a.TryGetTime(171008, out var t0), t0), Is.EqualTo((true, 10L)), "ids not in the packet stay");
        Assert.That((a.TryGetTime(171009, out var t1), t1), Is.EqualTo((true, 30L)), "a repeated id replaces");
        a.Clear();
        Assert.That(a.TryGetTime(5, out _), Is.True, "the game event list's Clear does not touch the character");
        a.ClearTimes();
        Assert.That(a.TryGetTime(5, out _), Is.False);
    }

    [Test]
    public void TheDialogIsToldOfAStartedEventAndOfTheOneTheManagerHeldWhenItEnds()
    {
        var a = new AttendEvents();
        var log = new List<string>();
        a.Started += e => log.Add("start " + e.Id + e.Name);
        a.Ended += e => log.Add("end " + e.Id + e.Name);
        a.Apply(Events(0, Event(1, "a"), Event(2, "b")));
        a.Apply(Events(1, Event(2, "again")));
        a.Apply(Events(3, Event(2, "new")));
        Assert.That(log, Is.EqualTo(new[] { "start 1a", "start 2b" }), "OnStart only for an event that was new; an update tells nobody");
        log.Clear();
        a.Apply(new GameEventMessage(2, Array.Empty<ShutdownInteractObjectEvent>(), new[] { 2, 99 }, false));
        Assert.That(log, Is.EqualTo(new[] { "end 2new" }), "vfunc_6 runs on the record the manager held (the update), not the one OnStart saw");
        log.Clear();
        a.Clear();
        Assert.That(log, Is.EqualTo(new[] { "end 1a" }), "Clear ends every event it drops");
    }
}
