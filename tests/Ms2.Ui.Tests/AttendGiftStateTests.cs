using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>What UIAttendGiftDialog (uiattendgiftdialog.cpp) keeps across opens and GameEventAttendGiftCli's readers
/// (gameeventcli.cpp:437-565): the held events, the request and selection, the active rules per kind, the user values.</summary>
public class AttendGiftStateTests
{
    const long Now = 1_700_000_000;

    static AttendGiftEvent Event(int id, byte kind = 0, int valueId = 0, bool disabled = false, bool usePoints = true, string name = "n",
        long begin = 100, long end = 200, byte payType = 0, int maxPay = 0, IReadOnlyList<GuildStorageItem> gifts = null) =>
        new(id, begin, end, name, "", disabled, usePoints, 0, kind, valueId, payType, maxPay, 0, 0, gifts ?? Array.Empty<GuildStorageItem>());

    static GameEventMessage Msg(byte kind, params AttendGiftEvent[] e) =>
        new(kind, Array.Empty<ShutdownInteractObjectEvent>(), Array.Empty<int>(), false) { AttendGiftEvents = e };

    static GameEventMessage Removed(params int[] ids) =>
        new(2, Array.Empty<ShutdownInteractObjectEvent>(), ids, false);

    static GameEventUserValueMessage Value(int list, params GameEventVariable[] v) => new(1, list, v);

    static (AttendGiftState, AttendEvents, GameEventUserValues, Log) Make(long now = Now)
    {
        var attend = new AttendEvents();
        var values = new GameEventUserValues(() => now);
        var log = new Log();
        var s = new AttendGiftState(attend, values, () => now, k => "<" + k + ">");
        s.IsOpen = () => log.Open;
        s.IsLoaded = () => log.Loaded;
        s.RefreshAll = () => log.Add("refresh");
        s.UpdateEventButtons = () => log.Add("buttons");
        s.UpdateUsePointButton = () => log.Add("usePoint");
        s.Close = () => log.Add("close");
        s.Open = () => log.Add("open");
        s.SystemNotice = k => log.Add("notice " + k);
        return (s, attend, values, log);
    }

    sealed class Log : List<string> { public bool Open, Loaded; }

    [Test]
    public void AStartedEventIsHeldAndOnlyAnActiveOneJoinsTheList()
    {
        var (s, attend, values, _) = Make();
        attend.Apply(Msg(0, Event(1), Event(2, disabled: true), Event(3, kind: 1, valueId: 9), Event(4, kind: 3, valueId: 9)));
        Assert.That(s.Events.Select(e => e.Id), Is.EqualTo(new[] { 1, 2, 3, 4 }), "the map holds every event, in id order");
        Assert.That(s.ActiveEvents.Select(e => e.Id), Is.EqualTo(new[] { 1, 4 }), "disabled no; kind 1 needs its bool; kind 3 needs the value absent");
        values.Apply(Value(0, new GameEventVariable(9, 0, "1", Now + 100)));
        s.RebuildActiveEvents();
        Assert.That(s.ActiveEvents.Select(e => e.Id), Is.EqualTo(new[] { 1, 3 }), "kind 1 is on and kind 3 is off once the value exists");
    }

    [Test]
    public void TheEventsAHeldBeforeTheDialogExistedAreAddedAndLaterOnesAreTold()
    {
        var attend = new AttendEvents();
        attend.Apply(Msg(0, Event(5)));
        var s = new AttendGiftState(attend, new GameEventUserValues(() => Now), () => Now);
        Assert.That(s.HasActiveEvents, Is.True);
        attend.Apply(Msg(1, Event(6)));
        Assert.That(s.Events.Count, Is.EqualTo(2));
        attend.Apply(Removed(5, 6));
        Assert.That(s.Events, Is.Empty);
        Assert.That(s.HasActiveEvents, Is.False, "RemoveEvent rebuilds the active list");
    }

    [Test]
    public void AddingAnIdHeldKeepsTheFirstAndAnOpenWindowRefreshes()
    {
        var (s, _, _, log) = Make();
        var first = Event(1, name: "a");
        s.AddEvent(first);
        s.AddEvent(Event(1, name: "b"));
        Assert.That(s.Events.Single(), Is.SameAs(first), "a std::map insert of a held id keeps the old");
        Assert.That(log, Is.Empty, "a closed window is not refreshed");
        log.Open = true;
        s.AddEvent(Event(2));
        Assert.That(log, Is.EqualTo(new[] { "refresh" }));
    }

    [Test]
    public void ARemovalMatchesTheEventByReferenceAsTheClientComparesItsPointers()
    {
        var (s, attend, _, log) = Make();
        var given = Event(1, name: "old");
        attend.Apply(Msg(0, given));
        attend.Apply(Msg(3, Event(1, name: "new")));         // Packet3 replaces the manager's record, the dialog keeps the one it has
        log.Open = true;
        s.UpdateSelectedEvent();
        Assert.That(s.Selected, Is.SameAs(given));
        log.Clear();
        attend.Apply(Removed(1));                              // Ended gets the manager's new record: not the one the dialog selected
        Assert.That(s.Events, Is.Empty);
        Assert.That(s.Selected, Is.SameAs(given), "no match by pointer: the selection stays");
        Assert.That(log, Is.EqualTo(new[] { "close" }), "an open window with no active event closes");
    }

    [Test]
    public void RemovingTheSelectedEventSelectsTheFirstActiveAndOtherRemovalsOnlyUpdateTheButtons()
    {
        var (s, attend, _, log) = Make();
        var a = Event(1); var b = Event(2); var c = Event(3);
        attend.Apply(Msg(0, a, b, c));
        log.Open = true;
        s.UpdateSelectedEvent();
        Assert.That(s.Selected, Is.SameAs(a));
        log.Clear();
        attend.Apply(Removed(3));
        Assert.That(log, Is.EqualTo(new[] { "buttons" }));
        log.Clear();
        attend.Apply(Removed(1));
        Assert.That(s.Selected, Is.SameAs(b));
        Assert.That(log, Is.EqualTo(new[] { "refresh" }));
    }

    [Test]
    public void ARemovedRequestedEventClosesTheWindowAndClearsTheRequest()
    {
        var (s, attend, _, log) = Make();
        attend.Apply(Msg(0, Event(1), Event(2)));
        Assert.That(s.SelectRequestedEvent(2), Is.True);
        Assert.That(s.Requested.Id, Is.EqualTo(2));
        log.Open = true;
        attend.Apply(Removed(2));
        Assert.That(s.Requested, Is.Null);
        Assert.That(log, Is.EqualTo(new[] { "close" }));
        Assert.That(s.SelectRequestedEvent(99), Is.False, "no held event of the id");
        Assert.That(s.Requested, Is.Null);
    }

    [Test]
    public void TheShownEventIsTheRequestedOneThenTheChosenIdThenTheFirstActive()
    {
        var (s, attend, _, _) = Make();
        attend.Apply(Msg(0, Event(1), Event(2), Event(3, disabled: true)));
        s.UpdateSelectedEvent();
        Assert.That(s.Selected.Id, Is.EqualTo(1));
        s.SetEventId(2);
        s.UpdateSelectedEvent();
        Assert.That(s.Selected.Id, Is.EqualTo(2));
        s.SetEventId(3);                                      // not an active event
        s.UpdateSelectedEvent();
        Assert.That(s.Selected.Id, Is.EqualTo(1), "an id that is not active falls back to the first");
        s.SelectRequestedEvent(3);
        s.UpdateSelectedEvent();
        Assert.That(s.Selected.Id, Is.EqualTo(3), "a request wins, even over a disabled event");
        Assert.That(s.SelectEventByIndex(0), Is.False, "a request pins the event");
        s.Unload();
        Assert.That((s.Requested, s.Selected), Is.EqualTo(((AttendGiftEvent)null, (AttendGiftEvent)null)), "the unload forgets the request and the selection");
        Assert.That(s.EventId, Is.Zero, "and the id");
        Assert.That(s.Events.Count, Is.EqualTo(3), "the events stay");
    }

    [Test]
    public void AButtonSelectsByIndexIntoTheActiveList()
    {
        var (s, attend, _, _) = Make();
        attend.Apply(Msg(0, Event(1), Event(2)));
        s.UpdateSelectedEvent();
        Assert.That(s.SelectEventByIndex(0), Is.False, "the event already shown");
        Assert.That(s.SelectEventByIndex(2), Is.False, "past the list");
        Assert.That(s.SelectEventByIndex(-1), Is.False);
        Assert.That(s.SelectEventByIndex(1), Is.True);
        Assert.That(s.Selected.Id, Is.EqualTo(2));
    }

    [Test]
    public void FindEventByKindGivesTheFirstInIdOrder()
    {
        var (s, attend, _, _) = Make();
        attend.Apply(Msg(0, Event(7, kind: 2), Event(3, kind: 2), Event(5, kind: 1)));
        Assert.That(s.FindEventByKind(2).Id, Is.EqualTo(3));
        Assert.That(s.FindEventByKind(1).Id, Is.EqualTo(5));
        Assert.That(s.FindEventByKind(9), Is.Null);
    }

    [Test]
    public void TheUserValuesGiveTheDaysPointsPaysAndTheTitle()
    {
        var (s, _, values, _) = Make();
        var e = Event(4, usePoints: false, name: "title");
        values.Apply(Value(0, new GameEventVariable(0x66, 4, "12", Now + 9), new GameEventVariable(0x67, 4, "2", Now + 9),
            new GameEventVariable(0x69, 4, "30", Now + 9), new GameEventVariable(0x69, 5, "7", Now + 9)));
        Assert.That((s.GetAttendedDays(e), s.UsedPayAttends(e), s.UsablePoints(e)), Is.EqualTo((12, 2, 30)));
        Assert.That(s.NameOf(e), Is.EqualTo("<title>"), "the name goes through the server string table");
        Assert.That(s.TitleWithPoints(e), Is.EqualTo("<title>(30)"));
        Assert.That(s.TitleWithPoints(Event(4, usePoints: true, name: "title")), Is.EqualTo("<title>"), "an event that uses points up has no count");
        Assert.That(s.TitleWithPoints(Event(8, usePoints: false, name: "t")), Is.EqualTo("<t>"), "no points: no count");
        Assert.That(s.GetTotalUsablePoints(), Is.Zero, "the dialog holds no events yet");
    }

    [Test]
    public void TheTotalPointsAreThoseOfTheHeldEventsThatDoNotUsePointsUp()
    {
        var (s, attend, values, _) = Make();
        attend.Apply(Msg(0, Event(1, usePoints: false), Event(2, usePoints: true), Event(3, usePoints: false)));
        values.Apply(Value(0, new GameEventVariable(0x69, 1, "5", Now + 9), new GameEventVariable(0x69, 2, "100", Now + 9),
            new GameEventVariable(0x69, 3, "8", Now + 9)));
        Assert.That(s.GetTotalUsablePoints(), Is.EqualTo(13));
    }

    [Test]
    public void CheckedInTodayComparesTheLocalDayOfTheLastCheckIn()
    {
        var (s, _, values, _) = Make();
        var e = Event(4);
        Assert.That(s.IsCheckedInToday(e), Is.False, "never");
        values.Apply(Value(0, new GameEventVariable(0x65, 4, Now.ToString(), Now + 9)));
        Assert.That(s.IsCheckedInToday(e), Is.True, "the same moment");
        values.Apply(Value(0, new GameEventVariable(0x65, 4, (Now - 2 * 86400).ToString(), Now + 9)));
        Assert.That(s.IsCheckedInToday(e), Is.False, "two days ago");
        values.Apply(Value(0, new GameEventVariable(0x65, 4, (Now + 2 * 86400).ToString(), Now + 9)));
        Assert.That(s.IsCheckedInToday(e), Is.True, "a later day counts (the check is day(now) <= day(last))");
    }

    [Test]
    public void TheEndTimeIsTheCharactersOwnOnlyForAKindTwoEventWithOne()
    {
        var (s, attend, _, _) = Make();
        var two = Event(1, kind: 2, end: 500);
        var one = Event(2, kind: 1, end: 600);
        Assert.That(s.EndTime(two), Is.EqualTo(500), "no time of its own");
        attend.SetTimes(new[] { (1, 900L), (2, 950L) });
        Assert.That(s.EndTime(two), Is.EqualTo(900));
        Assert.That(s.EndTime(one), Is.EqualTo(600), "a kind 1 event ignores it");
        Assert.That(s.HasTime(1), Is.True);
        attend.SetTimes(new[] { (1, 0L) });
        Assert.That(s.EndTime(two), Is.EqualTo(500), "a time of 0 is none");
        attend.ClearTimes();
        Assert.That(s.HasTime(1), Is.False);
    }

    [Test]
    public void ThePacketKindsRefreshSelectAndNotify()
    {
        var (s, attend, _, log) = Make();
        attend.Apply(Msg(0, Event(1), Event(2)));
        s.OnAttendance(new AttendanceMessage.RefreshAll());
        Assert.That(log, Is.EqualTo(new[] { "refresh" }), "kind 5 refreshes even a closed window (the window itself checks it is loaded)");
        log.Clear();
        s.OnAttendance(new AttendanceMessage.SelectEvent(99));
        Assert.That(log, Is.Empty, "an id no held event has is ignored");
        Assert.That(s.EventId, Is.Zero);
        s.OnAttendance(new AttendanceMessage.SelectEvent(2));
        Assert.That((s.EventId, log.ToArray()), Is.EqualTo((2, new[] { "open" })), "closed: the open call");
        log.Clear();
        log.Open = true;
        s.UpdateSelectedEvent();
        s.OnAttendance(new AttendanceMessage.SelectEvent(1));
        Assert.That(s.Selected.Id, Is.EqualTo(1), "open: SetEventId selected it");
        Assert.That(log.Where(l => l == "open"), Is.Empty);
        Assert.That(log.Contains("refresh"), Is.True);
        log.Clear();
        s.OnAttendance(new AttendanceMessage.Error(2));
        s.OnAttendance(new AttendanceMessage.Error(4));
        s.OnAttendance(new AttendanceMessage.Other(0x33));
        s.OnAttendance(new AttendanceMessage.StartedEvents(new[] { (1, 5L) }));
        Assert.That(log, Is.EqualTo(new[] { "notice s_attendGift_payAttend_result_lackMerat" }), "a result the client asserts on shows none; 6 is the session's");
    }

    [Test]
    public void EveryResultByteTheClientHandlesHasItsStringAndTheRestNone()
    {
        var keys = Enumerable.Range(0, 256).Select(i => (i, AttendGiftState.ErrorKey((byte)i))).Where(p => p.Item2 != null).ToArray();
        Assert.That(keys, Is.EqualTo(new[]
        {
            (1, "s_attendGift_payAttend_result_lackMoney"), (2, "s_attendGift_payAttend_result_lackMerat"),
            (3, "s_attendGift_payAttend_result_hasNotCoupon"), (5, "s_attendGift_item_attend_already_used"),
            (6, "s_attendGift_item_attend_not_found_evnet"),
        }));
    }

    [Test]
    public void ADaysValueRefreshesALoadedWindowAndAPointsValueUpdatesItsButton()
    {
        var (s, _, values, log) = Make();
        values.Apply(Value(0, new GameEventVariable(0x66, 1, "3", Now + 9), new GameEventVariable(0x69, 1, "3", Now + 9)));
        Assert.That(log, Is.Empty, "not loaded");
        log.Loaded = true;
        values.Apply(Value(0, new GameEventVariable(0x66, 1, "4", Now + 9)));
        values.Apply(Value(0, new GameEventVariable(0x69, 1, "4", Now + 9)));
        values.Apply(Value(0, new GameEventVariable(0x6a, 1, "4", Now + 9)));
        Assert.That(log, Is.EqualTo(new[] { "refresh", "usePoint" }), "0x66 refreshes, 0x69 updates the use-point button, others nothing");
    }
}
