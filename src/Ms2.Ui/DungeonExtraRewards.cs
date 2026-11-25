using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// The game rule's dungeon extra reward counts (CGameRule m_840, commonfiles/gamerule.cpp: AddDungeonExtraRewardCount @0x14047a420,
/// RemoveDungeonExtraRewardCount @0x14047a4e0, GetDungeonExtraRewardCount @0x14047a5b0) as GameEventManagerCli (gameeventmanagercli.cpp)
/// fills them from GameEventDungeonExtraReward (commonfiles/gameevent.cpp:1860-1928). A room holds one count: OnStart (@0x1403c0d30) does not
/// replace a room another event already holds (the client logs "Duplicate DungeonID"), and vfunc_6 (@0x1403c0ea0) takes back only the
/// rooms still held under its own event id.
/// <para>GameEventDungeonExtraRewardCli (@0x140ce46f0, @0x140ce4730; gameeventcli.cpp:737-752) asks the open dungeon room window to
/// RefreshReward after each OnStart and each vfunc_6: <see cref="Changed"/> is that call, raised once per event started or ended.</para>
/// <para>The manager keys its events by id across every type; this holder only sees this type, so an id it shares with another type's event is
/// not skipped or replaced as in the client (an update that gives this type's id to an event of another type runs the old event's
/// vfunc_6 there; the message keeps only this type's entries, so the counts stay here).</para>
/// </summary>
public sealed class DungeonExtraRewards
{
    readonly Dictionary<int, DungeonExtraRewardEvent> events = new();
    readonly Dictionary<uint, DungeonExtraRewardCount> counts = new();

    /// <summary>Raised after an event's OnStart or vfunc_6 ran (the client's pDlg->RefreshReward() on UI 0x4ebd).</summary>
    public event Action Changed;

    /// <summary>CGameRule::GetDungeonExtraRewardCount(room) (@0x14047a5b0): the count the event asks for, null with no event for the room.</summary>
    public uint? CountOf(uint room) => counts.TryGetValue(room, out var c) ? c.Count : null;

    /// <summary>GameEventManagerCli::Dispatch (:77-95) for one GameEvent message.</summary>
    public void Apply(GameEventMessage m)
    {
        switch (m.Kind)
        {
            case 0:
            case 1:
                // Add (@0x140ceb730, :211-220): an id already held is not added again; else OnStart
                foreach (var e in m.DungeonExtraRewardEvents)
                    if (events.TryAdd(e.Id, e)) Start(e);
                break;
            case 2:
                // Remove (@0x140ceb880, :223-233): a held id's vfunc_6, then RemoveKey
                foreach (int id in m.RemovedIds)
                    if (events.Remove(id, out var held)) End(held);
                break;
            case 3:
                // Update (@0x140ceba50, :236-242): GameEvent::vfunc_7 (@0x1402e2220): the held event's vfunc_6, then OnStart
                foreach (var e in m.DungeonExtraRewardEvents)
                {
                    if (events.TryGetValue(e.Id, out var prev)) End(prev);
                    events[e.Id] = e;
                    Start(e);
                }
                break;
        }
    }

    /// <summary>GameEventManagerCli::Clear (@0x140ceac00, :98-107): every event's vfunc_6, then RemoveAll.</summary>
    public void Clear()
    {
        var held = events.Values.ToList();
        events.Clear();
        foreach (var e in held) End(e);
    }

    /// <summary>OnStart: each room goes in unless a count is already held for it.</summary>
    void Start(DungeonExtraRewardEvent e)
    {
        foreach (var c in e.Counts) counts.TryAdd(c.Key, c);
        Changed?.Invoke();
    }

    /// <summary>vfunc_6: each room's count goes out when it is still this event's (RemoveDungeonExtraRewardCount, @0x14047a4e0).</summary>
    void End(DungeonExtraRewardEvent e)
    {
        foreach (var c in e.Counts)
            if (counts.TryGetValue(c.Key, out var held) && held.EventId == c.EventId) counts.Remove(c.Key);
        Changed?.Invoke();
    }
}
