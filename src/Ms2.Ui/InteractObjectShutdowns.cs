using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// The client's interact object shutdown list: GameEventManagerCli's GameEventShutdownInteractObjectId events
/// (gameeventmanagercli.cpp) and CShutdownEventInteractObject (shopeventmanager.cpp:254-292), which counts each
/// (field id, object id) pair once per event that holds it.
/// </summary>
public sealed class InteractObjectShutdowns
{
    readonly Dictionary<int, (uint FieldId, int ObjectId)[]> events = new();
    readonly Dictionary<(uint FieldId, int ObjectId), int> counts = new();

    /// <summary>GameEventManagerCli::Dispatch (:77-95) for one GameEvent message.</summary>
    public void Apply(GameEventMessage m)
    {
        switch (m.Kind)
        {
            case 0:
            case 1:
                // Packet0 / Packet1 -> Add (:211-220): an id already held is not added again; else OnStart
                foreach (var e in m.Shutdowns)
                {
                    if (events.ContainsKey(e.Id)) continue;
                    var pairs = Distinct(e);
                    events[e.Id] = pairs;
                    Start(pairs);
                }
                break;
            case 2:
                // Packet2 -> Find, Remove (:171-185, :223-233): vfunc_6 takes the event's pairs off
                foreach (int id in m.RemovedIds)
                {
                    if (!events.TryGetValue(id, out var pairs)) continue;
                    Stop(pairs);
                    events.Remove(id);
                }
                break;
            case 3:
                // Packet3 -> Update (:236-242): the new event replaces the old; vfunc_7 (gameeventbase.cpp:202-207)
                // runs the old one's vfunc_6, then OnStart
                foreach (var e in m.Shutdowns)
                {
                    if (events.TryGetValue(e.Id, out var prev)) Stop(prev);
                    var pairs = Distinct(e);
                    events[e.Id] = pairs;
                    Start(pairs);
                }
                break;
        }
    }

    /// <summary>GameEventManagerCli::Clear (:98-107), run when the game scene is left (scenestategame.cpp:1314).</summary>
    public void Clear()
    {
        events.Clear();
        counts.Clear();
    }

    /// <summary>CShutdownEventInteractObject::IsShutdown (shopeventmanager.cpp:282-292).</summary>
    public bool IsShutdown(int fieldId, int objectId) =>
        counts.Count != 0 && counts.ContainsKey(((uint)fieldId, objectId));

    // GameEventShutdownInteractObjectId keeps its pairs in a std::set (gameevent.cpp:4309-4316)
    static (uint, int)[] Distinct(ShutdownInteractObjectEvent e)
    {
        var set = new HashSet<(uint, int)>(e.Objects);
        var a = new (uint, int)[set.Count];
        set.CopyTo(a);
        return a;
    }

    // OnStart -> AddShutdown (gameevent.cpp:4321-4326; shopeventmanager.cpp:259-265)
    void Start((uint, int)[] pairs)
    {
        foreach (var k in pairs)
            counts[k] = counts.TryGetValue(k, out int c) ? c + 1 : 1;
    }

    // vfunc_6 -> RemoveShutdown (gameevent.cpp:4329-4334; shopeventmanager.cpp:268-279)
    void Stop((uint, int)[] pairs)
    {
        foreach (var k in pairs)
        {
            if (!counts.TryGetValue(k, out int c)) continue;
            if (c < 2) counts.Remove(k);
            else counts[k] = c - 1;
        }
    }
}
