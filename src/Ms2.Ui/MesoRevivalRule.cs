using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// The game rule's meso revival events (CGameRule m_2f0, commonfiles/gamerule.cpp: AddMesoRevivalEvent @0x140477bd0,
/// RemoveMesoRevivalEvent @0x140477c90) as GameEventManagerCli (gameeventmanagercli.cpp) fills them from GameEventMesoRevival
/// (gameevent.cpp:869-913), and the two flags FUN_14047fc60 (@0x14047fc60) keeps over them: m_368 when an event's m_18 is 0,
/// m_36c when it is not. <see cref="Rule"/> is FUN_140477d10, the revival dialog's flat meso revive rule.
/// <para>The manager keys its events by id across every type; this holder only sees this type, so an id it shares with another
/// type's event is not skipped or replaced as in the client.</para>
/// </summary>
public sealed class MesoRevivalRule
{
    readonly Dictionary<int, bool> events = new();
    bool any, anyPcBang;

    /// <summary>GameEventManagerCli::Dispatch (:77-95) for one GameEvent message.</summary>
    public void Apply(GameEventMessage m)
    {
        switch (m.Kind)
        {
            case 0:
            case 1:
                // Add (:211-220): an id already held is not added again; else OnStart
                foreach (var e in m.MesoRevivalEvents)
                    if (!events.ContainsKey(e.Id)) events[e.Id] = e.Flag;
                break;
            case 2:
                // Remove (:223-233): vfunc_6 (RemoveMesoRevivalEvent), then RemoveKey
                foreach (int id in m.RemovedIds) events.Remove(id);
                break;
            case 3:
                // Update (:236-242): the previous event's vfunc_6, then OnStart
                foreach (var e in m.MesoRevivalEvents) events[e.Id] = e.Flag;
                break;
            default:
                return;
        }
        Recompute();
    }

    /// <summary>GameEventManagerCli::Clear (:98-107): every event's vfunc_6, then RemoveAll.</summary>
    public void Clear()
    {
        events.Clear();
        Recompute();
    }

    /// <summary>FUN_14047fc60: m_368 and m_36c over the held contexts.</summary>
    void Recompute()
    {
        any = anyPcBang = false;
        foreach (bool flag in events.Values)
        {
            if (flag) anyPcBang = true;
            else any = true;
        }
    }

    /// <summary>FUN_140477d10(bAll) (gamerule.cpp:945-955): 1 when an event with m_18 0 is held, or when
    /// <paramref name="pcBang"/> (PC::FUN_141658f30, the PC bang grade above 0) and an event with m_18 set is; else 0.</summary>
    public int Rule(bool pcBang) => any || (pcBang && anyPcBang) ? 1 : 0;
}
