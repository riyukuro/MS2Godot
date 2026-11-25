using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// The state UIAttendGiftDialog (0x4eb8, uiattendgiftdialog.cpp) keeps across opens, and the GameEventAttendGiftCli readers it calls
/// (gameeventcli.cpp:437-565). The client's dialog is one persistent object that every started attendance event tells through
/// GameEventAttendGiftCli::OnStart (AddEvent) and vfunc_6 (RemoveEvent), whether the window is open or not, so this lives with the
/// session; the window (<see cref="Hud.AttendGiftDialogController"/>) only reads it and is told through <see cref="IsOpen"/>,
/// <see cref="RefreshAll"/>, <see cref="UpdateEventButtons"/> and <see cref="Close"/> (FUN_141924630 "is loaded", the two updates, vfunc_67).
/// Events are compared by reference as the client compares its MS2Ptr pointers: a Packet3 update replaces the manager's record but the
/// dialog keeps the one it was given (GameEventAttendGiftCli has no vfunc_7 override), so a later removal does not match it by pointer.
/// </summary>
public sealed class AttendGiftState
{
    readonly AttendEvents attend;
    readonly GameEventUserValues values;
    readonly Func<long> serverTime;
    readonly Func<string, string> eventString;
    readonly SortedDictionary<int, AttendGiftEvent> events = new();      // m_events, a std::map by event id
    readonly List<AttendGiftEvent> activeEvents = new();                 // m_activeEvents

    public AttendGiftState(AttendEvents attend, GameEventUserValues values, Func<long> serverTime, Func<string, string> eventString = null)
    {
        this.attend = attend;
        this.values = values;
        this.serverTime = serverTime ?? (() => 0);
        this.eventString = eventString ?? (s => s);
        foreach (var e in attend.Events) AddEvent(e);
        attend.Started += AddEvent;
        attend.Ended += RemoveEvent;
        // The ctor's listeners (uiattendgiftdialog.cpp:201): the attended days (0x66) and the points (0x69) of a user value changing.
        values.RegisterListener(0x66, OnAttendedDaysChanged);
        values.RegisterListener(0x69, OnPointsChanged);
    }

    /// <summary>The window's values (the use-point button reads bool 100, the pay box 0x67).</summary>
    public GameEventUserValues Values => values;

    /// <summary>FUN_1422dc2d0: the movie is loaded; set by the window.</summary>
    public Func<bool> IsLoaded = () => false;
    public Action UpdateUsePointButton = () => { };
    /// <summary>FUN_141924680: open the window (the select-event packet when it is closed); set by the owner.</summary>
    public Action Open = () => { };
    /// <summary>FUN_140d73ff0 (ShowSystemNotice of a common string key, 1, 1); set by the owner.</summary>
    public Action<string> SystemNotice = _ => { };

    /// <summary>FUN_14102d610 (:308): a changed attended-days value refreshes a loaded window.</summary>
    void OnAttendedDaysChanged(GameEventVariable _) { if (IsLoaded()) RefreshAll(); }
    /// <summary>FUN_14102d650 (:310): a changed points value updates the use-point button of a loaded window.</summary>
    void OnPointsChanged(GameEventVariable _) { if (IsLoaded()) UpdateUsePointButton(); }

    /// <summary>The character's own time of an event (CMyCharacter::HasTime).</summary>
    public bool HasTime(int id) => attend.TryGetTime(id, out _);

    /// <summary>
    /// UIAttendGiftDialog::OnAttendancePacket (@0x14102EBB0, :345): kind 5 refreshes (FUN_14102ec60); 6 sets the character's times
    /// (done by the session: FUN_14102ec90); 7 (FUN_14102ecc0) ignores an id no held event has, else SetEventId and a refresh when
    /// open, or the open call when not; 9 (FUN_14102ed90) shows the system notice of the failure (a result the client asserts on
    /// shows none).
    /// </summary>
    public void OnAttendance(AttendanceMessage m)
    {
        switch (m)
        {
            case AttendanceMessage.RefreshAll: RefreshAll(); break;
            case AttendanceMessage.SelectEvent s:
                if (!events.ContainsKey((int)s.EventId)) return;
                SetEventId((int)s.EventId);
                if (IsOpen()) RefreshAll(); else Open();
                break;
            case AttendanceMessage.Error e:
                if (ErrorKey(e.Result) is { } key) SystemNotice(key);
                break;
        }
    }

    /// <summary>FUN_14102ed90 (:353): the common string key of a result byte; null for the ones the client asserts on.</summary>
    public static string ErrorKey(byte result) => result switch
    {
        1 => "s_attendGift_payAttend_result_lackMoney",
        2 => "s_attendGift_payAttend_result_lackMerat",
        3 => "s_attendGift_payAttend_result_hasNotCoupon",
        5 => "s_attendGift_item_attend_already_used",
        6 => "s_attendGift_item_attend_not_found_evnet",
        _ => null,
    };

    /// <summary>m_requested: the event a "select event" packet or an opener asked for; the window shows only it while set.</summary>
    public AttendGiftEvent Requested { get; private set; }
    /// <summary>m_selected: the event the window shows.</summary>
    public AttendGiftEvent Selected { get; private set; }
    /// <summary>m_eventId: the id SetEventId chose (0: none).</summary>
    public int EventId { get; private set; }

    /// <summary>FUN_141924630 (the window is loaded and open); set by the window.</summary>
    public Func<bool> IsOpen = () => false;
    public Action RefreshAll = () => { };
    public Action UpdateEventButtons = () => { };
    /// <summary>vfunc_67: close the window.</summary>
    public Action Close = () => { };

    public IReadOnlyCollection<AttendGiftEvent> Events => events.Values;
    public IReadOnlyList<AttendGiftEvent> ActiveEvents => activeEvents;

    // ---- the dialog's own functions ----

    /// <summary>UIAttendGiftDialog::AddEvent @0x14102C8E0 (:236): a std::map insert (an id held stays), active ones join the list, an open window refreshes.</summary>
    public void AddEvent(AttendGiftEvent e)
    {
        events.TryAdd(e.Id, e);
        if (IsActive(e)) activeEvents.Add(e);
        if (IsOpen()) RefreshAll();
    }

    /// <summary>UIAttendGiftDialog::RemoveEvent @0x14102C9E0 (:364).</summary>
    public void RemoveEvent(AttendGiftEvent e)
    {
        events.Remove(e.Id);
        if (ReferenceEquals(Requested, e)) { Requested = null; Close(); return; }
        RebuildActiveEvents();
        if (!IsOpen()) return;
        if (activeEvents.Count == 0) { Close(); return; }
        if (ReferenceEquals(Selected, e)) { Selected = activeEvents[0]; RefreshAll(); }
        else UpdateEventButtons();
    }

    /// <summary>UIAttendGiftDialog::SelectRequestedEvent @0x14102C7A0 (:309): false (and the not-found box, the caller's) when no held event has the id.</summary>
    public bool SelectRequestedEvent(int id)
    {
        Requested = null;
        foreach (var e in events.Values)
            if (e.Id == id) { Requested = e; return true; }
        return false;
    }

    /// <summary>UIAttendGiftDialog::HasActiveEvents @0x14102CB60.</summary>
    public bool HasActiveEvents => activeEvents.Count > 0;

    /// <summary>UIAttendGiftDialog::FindEventByKind @0x14102CBB0 (:270): the first event in id order of the kind.</summary>
    public AttendGiftEvent FindEventByKind(int kind)
    {
        foreach (var e in events.Values)
            if (e.Kind == kind) return e;
        return null;
    }

    /// <summary>UIAttendGiftDialog::GetTotalUsablePoints @0x14102CCD0 (:221): the points of the events that do not use points up as a title only.</summary>
    public int GetTotalUsablePoints()
    {
        int total = 0;
        foreach (var e in events.Values)
            if (!e.UsePoints) total += UsablePoints(e);
        return total;
    }

    /// <summary>UIAttendGiftDialog::SetEventId @0x14102D5C0 (:67).</summary>
    public void SetEventId(int id)
    {
        EventId = id;
        if (IsOpen()) { UpdateSelectedEvent(); RefreshAll(); }
    }

    /// <summary>UIAttendGiftDialog::RebuildActiveEvents @0x14102E940 (:223).</summary>
    public void RebuildActiveEvents()
    {
        activeEvents.Clear();
        foreach (var e in events.Values)
            if (IsActive(e)) activeEvents.Add(e);
    }

    /// <summary>UIAttendGiftDialog::UpdateSelectedEvent @0x14102EA30 (:369).</summary>
    public void UpdateSelectedEvent()
    {
        Selected = null;
        if (Requested != null) { Selected = Requested; return; }
        if (activeEvents.Count == 0) return;
        if (EventId != 0)
            foreach (var e in activeEvents)
                if (e.Id == EventId) { Selected = e; return; }
        Selected = activeEvents[0];
    }

    /// <summary>UIAttendGiftDialog::SelectEventByIndex @0x14102CDC0: the button's index into the active list; ignored while one event was requested,
    /// past the list, or the one already shown. True when the selection changed (the caller refreshes).</summary>
    public bool SelectEventByIndex(int index)
    {
        if (Requested != null) return false;
        if ((uint)index >= (uint)activeEvents.Count) return false;
        var e = activeEvents[index];
        if (ReferenceEquals(Selected, e)) return false;
        Selected = e;
        return true;
    }

    /// <summary>UIAttendGiftDialog::vfunc_26 @0x14102c610 (:184): the window unloads and forgets its request, id and selection; the events stay.</summary>
    public void Unload()
    {
        Requested = null;
        EventId = 0;
        Selected = null;
    }

    // ---- GameEventAttendGiftCli (gameeventcli.cpp:437-565) ----

    /// <summary>FUN_140ce3510 @0x140ce3510 (:437): disabled → no; kind 1 → the bool user value of ValueId; kind 3 → the user value of ValueId is absent; else yes.</summary>
    public bool IsActive(AttendGiftEvent e)
    {
        if (e.Disabled) return false;
        switch (e.Kind)
        {
            case 1: return values.GetBool(0, e.ValueId, 0, false);
            case 3: return !values.Has(0, e.ValueId);
            default: return true;
        }
    }

    /// <summary>IsCheckedInToday @0x140ce35c0 (:454): user value 0x65 of the event is the time of the last check-in (0: never); the same local day as now or later.</summary>
    public bool IsCheckedInToday(AttendGiftEvent e)
    {
        long end = values.GetInt64(0, 0x65, e.Id);
        if (end == 0) return false;
        return Day(serverTime()) <= Day(end);
    }

    /// <summary>The CMsTime truncation to a local day (the zone offset applied).</summary>
    static long Day(long t) => (long)Math.Floor((t - Net.MsTime.CalcZoneOffset) / 86400.0);

    /// <summary>GetAttendedDays @0x140ce3690 (:471): user value 0x66.</summary>
    public int GetAttendedDays(AttendGiftEvent e) => values.GetInt(0, 0x66, e.Id);

    /// <summary>GetUsablePoints @0x140ce36d0 (:478): user value 0x69.</summary>
    public int UsablePoints(AttendGiftEvent e) => values.GetInt(0, 0x69, e.Id);

    /// <summary>FUN_140ce3920 @0x140ce3920 (:516): user value 0x67, the early participations used.</summary>
    public int UsedPayAttends(AttendGiftEvent e) => values.GetInt(0, 0x67, e.Id);

    /// <summary>The event's name as the server string table gives it (vfunc_10 @0x140ce3480).</summary>
    public string NameOf(AttendGiftEvent e) => eventString(e.Name);

    /// <summary>GetTitleWithPoints @0x140ce37a0 (:493): "{name}({points})" for an event that does not use points up while it has some.</summary>
    public string TitleWithPoints(AttendGiftEvent e)
    {
        int n;
        if (!e.UsePoints && (n = UsablePoints(e)) > 0) return NameOf(e) + "(" + n + ")";
        return NameOf(e);
    }

    /// <summary>GetEndTime @0x140ce3ae0 (:561): kind 2 and the character's own end time of it when set, else the event's end.</summary>
    public long EndTime(AttendGiftEvent e)
    {
        if (e.Kind == 2 && attend.TryGetTime(e.Id, out long t) && t != 0) return t;
        return e.End;
    }
}
