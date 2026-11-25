using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Quest;
using QuestRecord = Ms2.Net.Packets.QuestRecord;

namespace Ms2.Ui;

/// <summary>
/// A quest's standing as the mask check reads it (FUN_140764480, questmasktable.cpp:1198): the record the client keeps
/// (CQuestManager vfunc_o10_3) gives absent with no record, rewarded at state 2, and at state 1 complete when
/// CQuest::IsComplete holds (every condition's count at its goal, <see cref="QuestRules.IsComplete"/>), else taken.
/// The mask's stage 2 ("complete") starts there, and a range that ends on stage 1 ("taken") closes there.
/// </summary>
public static class QuestMaskStatus
{
    /// <param name="record">The character's record of the quest, null when it has none.</param>
    /// <param name="data">The client's quest data (QuestDataTable). A quest the data lacks is never kept: CQuest::Init fails and
    /// OnQuest drops it (questmanager_2.cpp:970, :1010). Null when the table could not load: a taken quest then counts as
    /// taken, never complete.</param>
    public static QuestMaskTable.QuestStatus Of(QuestRecord record, Func<int, QuestData> data)
    {
        if (record == null) return QuestMaskTable.QuestStatus.Absent;
        QuestData d = null;
        if (data != null && (d = data(record.Id)) == null) return QuestMaskTable.QuestStatus.Absent;
        return record.State switch
        {
            1 => d != null && QuestRules.IsComplete(d, 1, record.Progress) ? QuestMaskTable.QuestStatus.Completed : QuestMaskTable.QuestStatus.Active,
            2 => QuestMaskTable.QuestStatus.Rewarded,
            // FUN_140764480 sets none of its flags for another state, which every mode reads as not reached, as absent
            _ => QuestMaskTable.QuestStatus.Absent,
        };
    }
}
