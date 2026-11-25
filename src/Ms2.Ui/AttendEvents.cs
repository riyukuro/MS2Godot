using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// What the attendance dialog (UIAttendGiftDialog) reads: the running GameEventAttendGift events (the game event list,
/// GameEventManagerCli::Dispatch @0x140ceab50, gameeventmanagercli.cpp:77) and the character's started-event times
/// (CMyCharacter m_times, a map of event id to end time, filled by the Attendance packet's kind 6 through FUN_14102ec90 @0x14102ec90 into
/// FUN_140d9f1c0 @0x140d9f1c0 and FUN_140d9fc80 @0x140d9fc80, notifydebugstate.cpp:133-150; emptied by ClearDungeonState @0x140d9f140, :121-129).
/// </summary>
public sealed class AttendEvents
{
    readonly Dictionary<int, AttendGiftEvent> events = new();
    readonly List<int> order = new();
    readonly Dictionary<int, long> times = new();

    /// <summary>The running events, in the order they were added.</summary>
    public IEnumerable<AttendGiftEvent> Events { get { foreach (int id in order) yield return events[id]; } }

    public int Count => events.Count;

    public bool TryGetEvent(int id, out AttendGiftEvent e) => events.TryGetValue(id, out e);

    /// <summary>One GameEvent message (kinds 0 and 1 add: an id already held is not added again; 2 removes; 3 replaces).</summary>
    public void Apply(GameEventMessage m)
    {
        switch (m.Kind)
        {
            case 0:
            case 1:
                foreach (var e in m.AttendGiftEvents)
                    if (events.TryAdd(e.Id, e)) order.Add(e.Id);
                break;
            case 2:
                foreach (int id in m.RemovedIds)
                    if (events.Remove(id)) order.Remove(id);
                break;
            case 3:
                foreach (var e in m.AttendGiftEvents)
                {
                    if (!events.ContainsKey(e.Id)) order.Add(e.Id);
                    events[e.Id] = e;
                }
                break;
        }
    }

    /// <summary>GameEventManagerCli::Clear (@0x140ceac00, :98-107): the events of the old game scene go. The character's times stay
    /// (ClearDungeonState runs on the dungeon's own reset).</summary>
    public void Clear() { events.Clear(); order.Clear(); }

    /// <summary>Kind 6 of the Attendance packet: FUN_140d9fc80 sets each pair in the map (a repeated id replaces, ids not in the packet stay).</summary>
    public void SetTimes(IEnumerable<(int EventId, long EndTime)> pairs)
    {
        foreach (var (id, end) in pairs) times[id] = end;
    }

    /// <summary>CMyCharacter::GetTime(id) for an event: the end time the user's start set, or false.</summary>
    public bool TryGetTime(int id, out long end) => times.TryGetValue(id, out end);

    /// <summary>ClearDungeonState's <c>m_times.RemoveAll()</c> (@0x140d9f140, notifydebugstate.cpp:127).</summary>
    public void ClearTimes() => times.Clear();
}
