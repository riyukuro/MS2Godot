using Ms2.Net.Packets;

namespace Ms2.Ui.Guide;

/// <summary>
/// The guide's occurrences and triggers of one quest packet, CQuestManager::OnQuest's raises
/// (questmanager_2.cpp:942-1075): the manager calls the client's handler makes as it applies the packet, over the
/// quest book the packet was applied to (the book after the apply for the progress reads, <see cref="KnownBefore"/>
/// for the remove gates). The book itself is the caller's; nothing here changes it.
/// </summary>
public static class GuideQuests
{
    /// <summary>Whether the packet names a quest the book has, asked before the packet is applied (RemoveQuest
    /// returns early on an unknown quest, questmanager_2.cpp:863-864).</summary>
    public static bool KnownBefore(QuestBook quests, QuestMessage m) => m switch
    {
        QuestMessage.Abandon a => quests.Get(a.Id) != null,
        QuestMessage.Expired e => e.Ids.Any(id => quests.Get(id) != null),
        _ => false,
    };

    /// <summary>
    /// The raises of the packet, with the field's portal lookup for the summon portal (the client's handler keeps
    /// WorldService's registry: FUN_141bc2800, worldservice_1.cpp:4003).
    /// </summary>
    public static void OnQuest(GuideManager g, QuestMessage m, QuestBook quests, bool knownBefore, Func<int, bool> targetPortal)
    {
        switch (m)
        {
            case QuestMessage.Load:                              // OnQuest 0x16 (questmanager_2.cpp:984)
                g.QueueOccurrence(GuideOccurrence.QuestProgress, 0);
                g.QueueOccurrence(GuideOccurrence.QuestComplete, 0);
                break;
            case QuestMessage.Start s:                           // mode 2 (:1021)
                g.QueueOccurrence(GuideOccurrence.QuestProgress, s.Id);
                g.AddTrigger(GuideTrigger.QuestAccept, s.Id);
                break;
            case QuestMessage.Update u when quests.Get(u.Id) is { } r:   // mode 3 (:1032): GetProgress(0) after DecodeProgress
                g.AddTrigger(GuideTrigger.QuestProgress, u.Id, r.Progress.Length > 0 ? (int)r.Progress[0] : 0);
                g.QueueOccurrence(GuideOccurrence.QuestProgress, u.Id);
                break;
            case QuestMessage.Complete c when quests.Get(c.Id) != null:  // mode 4 (:1048)
                g.AddTrigger(GuideTrigger.QuestComplete, c.Id);
                g.QueueOccurrence(GuideOccurrence.QuestComplete, c.Id);
                break;
            case QuestMessage.Result:                            // mode 5 (:1055); RemoveQuest (:865) queues the same
                g.QueueOccurrence(GuideOccurrence.QuestProgress, 0);
                g.QueueOccurrence(GuideOccurrence.QuestComplete, 0);
                break;
            // Modes 6 and 7: RemoveQuest of a known quest, per id in mode 7 (:1072) — and every call queues
            // occurrences 7 and 8 (:865-866), which QueueOccurrence collapses into its sets when the sub value is 0
            // (std::set m_setPlain.insert, cguidemanager.h:112, guideentity_1b.cpp:793-798), so one raise is every
            // call's.
            case QuestMessage.Abandon or QuestMessage.Expired when knownBefore:
                g.QueueOccurrence(GuideOccurrence.QuestProgress, 0);
                g.QueueOccurrence(GuideOccurrence.QuestComplete, 0);
                break;
            // Mode 0x12 (:1125-1141): the client raises the trigger only after the target portal is found
            // (WorldService::FUN_141bc2800, :1129-1130), not for every packet.
            case QuestMessage.SummonPortal p when targetPortal(p.TargetObject):
                g.AddTrigger(GuideTrigger.QuestSummonPortal, 0);
                break;
        }
    }
}
