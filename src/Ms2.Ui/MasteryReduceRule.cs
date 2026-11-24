using Ms2.Net.Packets;
using Ms2.Quest;

namespace Ms2.Ui;

/// <summary>
/// CGameEventValueManager's MasteryReduce value (CGameEventValueMasteryReduce, commonfiles/gameeventvalue.cpp:640-662), set by the
/// GameEventMasteryReduce events of the game event list (GameEventManagerCli, gameeventmanagercli.cpp): one rate in 1/10000, the
/// last started event's. It shortens the gathering and the home made cast times and adds the event's line to the gathering
/// caption (CInteractGatheringObject::vfunc_6, interactobject.cpp:1495-1501).
/// STILL-OPEN: an update (kind 3) that gives a MasteryReduce event's id to an event of another type runs the old event's
/// vfunc_6 (SetRate 0) in the client; the message keeps only the MasteryReduce entries, so the rate stays here.
/// </summary>
public sealed class MasteryReduceRule
{
    readonly HashSet<int> ids = new();

    /// <summary>m_rate (1/10000; 0: no event).</summary>
    public int Rate { get; private set; }

    /// <summary>GameEventManagerCli::Dispatch for one GameEvent message.</summary>
    public void Apply(GameEventMessage m)
    {
        switch (m.Kind)
        {
            case 0:
            case 1:
                // Add (@0x140ceb730, :211-220): an id already held is not added again; else OnStart (SetRate m_d0, gameevent.cpp:3594)
                foreach (var e in m.MasteryReduceEvents)
                    if (ids.Add(e.Id)) Rate = e.Rate;
                break;
            case 2:
                // Packet2 (@0x140ceb2a0): a held id's Remove (:223-233), vfunc_6 (SetRate 0, gameevent.cpp:3598)
                foreach (int id in m.RemovedIds)
                    if (ids.Remove(id)) Rate = 0;
                break;
            case 3:
                // Update (@0x140ceba50, :236-242): GameEvent::vfunc_7, the held event's vfunc_6, then OnStart
                foreach (var e in m.MasteryReduceEvents)
                {
                    if (!ids.Add(e.Id)) Rate = 0;
                    Rate = e.Rate;
                }
                break;
        }
    }

    /// <summary>GameEventManagerCli::Clear (@0x140ceac00, :98-107): every event's vfunc_6.</summary>
    public void Clear()
    {
        if (ids.Count > 0) Rate = 0;
        ids.Clear();
    }

    /// <summary>CGameEventValueMasteryReduce::Apply (@0x1404240e0, gameeventvalue.cpp:644-650): the value divided by the rate
    /// over 10000, in float, cut to an unsigned int; the value as it is with no event.</summary>
    public uint Apply(uint value) => Rate == 0 ? value : (uint)((float)value / ((float)(uint)Rate / 10000.0f));

    /// <summary>CGameEventValueMasteryReduce::GetText (@0x140424150, :655-661): "" with no event, else the common string
    /// s_mastery_reduce_event formatted with the rate over 10000 (FUN_14028dfa0, a float).</summary>
    public string Text(Func<string, string> commonByKey) =>
        Rate == 0 ? "" : ClientFormat.Format(commonByKey("s_mastery_reduce_event") ?? "", (float)(uint)Rate / 10000.0f);
}
