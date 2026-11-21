using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// The game rule's construction sets (CGameRule m_ca0, m_cb0, m_cf0, commonfiles/gamerule.cpp:2428-2541) as
/// GameEventManagerCli (gameeventmanagercli.cpp) fills them from GameEventMassiveConstructionEvent
/// (gameevent.cpp:2926-2967) and GameEventIgnoreConstructionLockEvent (gameevent.cpp:4939-4961).
/// <para>Both events' vfunc_6 clear their whole set(s), not only their own entries: removing or replacing one
/// MassiveConstructionEvent empties the (field, channel) and account sets of every other one as well.</para>
/// <para>The manager keys its events by id across every type; this holder only sees these two types, so an id one of them
/// shares with another type's event is not skipped or replaced as in the client.</para>
/// </summary>
public sealed class ConstructionEvents
{
    readonly Dictionary<int, object> events = new();
    // m_ca0: (field id, channel) keys (Unk_ConstructionKey)
    readonly HashSet<(uint FieldId, int Channel)> fields = new();
    // m_cb0: the accounts a massive construction event is limited to
    readonly HashSet<long> accounts = new();
    // m_cf0: the ignored construction lock ids
    readonly HashSet<long> ignored = new();

    /// <summary>GameEventManagerCli::Dispatch (:77-95) for one GameEvent message.</summary>
    public void Apply(GameEventMessage m)
    {
        switch (m.Kind)
        {
            case 0:
            case 1:
                // Packet0 / Packet1 -> Add (:211-220): an id already held is not added again; else OnStart
                foreach (var e in m.ConstructionEvents)
                {
                    int id = IdOf(e);
                    if (events.ContainsKey(id)) continue;
                    Start(e);
                    events[id] = e;
                }
                break;
            case 2:
                // Packet2 -> Remove (:223-233): vfunc_6, then RemoveKey
                foreach (int id in m.RemovedIds)
                {
                    if (!events.TryGetValue(id, out var e)) continue;
                    Stop(e);
                    events.Remove(id);
                }
                break;
            case 3:
                // Packet3 -> Update (:236-242): SetAt, then vfunc_7 (gameeventbase.cpp:202-208): the previous event's vfunc_6,
                // then OnStart
                foreach (var e in m.ConstructionEvents)
                {
                    int id = IdOf(e);
                    if (events.TryGetValue(id, out var prev)) Stop(prev);
                    events[id] = e;
                    Start(e);
                }
                break;
        }
    }

    /// <summary>GameEventManagerCli::Clear (:98-107), run when the game scene is left: every event's vfunc_6, then RemoveAll.</summary>
    public void Clear()
    {
        foreach (var e in events.Values) Stop(e);
        events.Clear();
    }

    /// <summary>CGameRule::FUN_14047ddd0 (@0x14047ddd0, gamerule.cpp:2462-2475): with an account list, an account not on it is
    /// false; else whether (field, channel) is held.</summary>
    public bool IsMassiveConstruction(uint fieldId, int channel, long accountId)
    {
        if (accounts.Count != 0 && !accounts.Contains(accountId)) return false;
        return fields.Contains((fieldId, channel));
    }

    /// <summary>CGameRule::FUN_14047e150 (@0x14047e150, gamerule.cpp:2511-2519): the id is on the ignored construction lock list.</summary>
    public bool IsIgnoredConstructionLock(long id) => ignored.Count != 0 && ignored.Contains(id);

    static int IdOf(object e) => e switch
    {
        MassiveConstructionEvent m => m.Id,
        IgnoreConstructionLockEvent i => i.Id,
        _ => 0,
    };

    void Start(object e)
    {
        switch (e)
        {
            // OnStart (gameevent.cpp:2946-2958): FUN_14047dba0(field, m_f0) per field, FUN_14047df50 per account
            case MassiveConstructionEvent m:
                foreach (uint f in m.FieldIds) fields.Add((f, m.Channel));
                foreach (long a in m.AccountIds) accounts.Add(a);
                break;
            // OnStart (:4952-4958): AddIgnoredConstructionLockId per id
            case IgnoreConstructionLockEvent i:
                foreach (long a in i.AccountIds) ignored.Add(a);
                break;
        }
    }

    void Stop(object e)
    {
        switch (e)
        {
            // vfunc_6 (:2961-2965): FUN_14047dd60 and FUN_14047e0e0 clear both sets
            case MassiveConstructionEvent:
                fields.Clear();
                accounts.Clear();
                break;
            // vfunc_6 (:4961): ClearIgnoredConstructionLockIds
            case IgnoreConstructionLockEvent:
                ignored.Clear();
                break;
        }
    }
}
