using System.Text;
using Ms2.Quest;

namespace Ms2.Ui.Items;

/// <summary>Unk_QuestItemLink (questmanager_2.h:20): one quest an item belongs to. Kind 0 = the quest's item, 1 = an
/// equip quest; State 0 = can start, 1 = in progress, 2 = complete (GetQuestItemLinks drops the default 4).</summary>
public readonly record struct QuestItemLink(int QuestId, int State, int Kind);

public sealed partial class ItemTooltip
{
    /// <summary>
    /// FUN_1409d3a40 (astooltip.cpp:2622): "itemQuestInfo", s_msg_tooltip_quest_info with the item's kind 0 quests
    /// (GetQuestNameWithLevel with s_msg_tooltip_quest_name_progress / _name / _name_complete by link state, joined by
    /// ", "), then a line of s_msg_tooltip_equip_quest_info when a kind 1 link is there; with the text, "questBeginAble" or
    /// "questProgress" by FUN_1416ce6a0. Needs a local player. The links come from <see cref="UiModel.QuestItemLinks"/>,
    /// which the host answers with <see cref="QuestItemLinks"/>.
    /// </summary>
    void SetItemQuestInfo(Item item)
    {
        if (!model.HasLocalPlayer) return;
        var links = model.QuestItemLinks?.Invoke(item);
        if (links == null || links.Count == 0) return;
        bool equipQuest = false;
        var quests = new StringBuilder();
        foreach (var l in links)
        {
            if (l.Kind == 0)
            {
                string key = l.State switch
                {
                    1 => "s_msg_tooltip_quest_name_progress",
                    0 => "s_msg_tooltip_quest_name",
                    2 => "s_msg_tooltip_quest_name_complete",
                    _ => "",
                };
                if (quests.Length != 0) quests.Append(", ");
                quests.Append(QuestNameWithLevel(l.QuestId, key));
            }
            else if (l.Kind == 1) equipQuest = true;
        }
        string info = quests.Length != 0 ? CommonF("s_msg_tooltip_quest_info", quests.ToString()) : "";
        if (equipQuest)
        {
            if (info.Length != 0) info += "\n";
            info += Common("s_msg_tooltip_equip_quest_info");
        }
        if (info.Length == 0) return;
        Data["itemQuestInfo"] = info;
        int state = QuestItemState(links);
        if (state == 0) Data["questBeginAble"] = 1;
        else if (state == 1) Data["questProgress"] = 1;
    }

    /// <summary>
    /// CQuestManager::GetQuestItemLinks (questmanager_2.cpp:1304): for kind 0 then 1, each quest of
    /// QuestDataTable::GetQuestIdsByKey(item id, kind) that has data, is not locked (m_30), has its term tag when it has
    /// one (FUN_14074b210: CGameRule::HasTermTag; a tag the context does not know counts as missing) and takes the job
    /// group when it lists jobs; kind 1 skips an item with the stream flag m_d4 (FUN_140529620). The state is 0 with no
    /// record, 1 in progress, 0 for a complete repeat quest; a complete non-repeat quest (and any other state) keeps the
    /// default 4 and is left out. STILL-OPEN, taken as 0 so nothing is skipped: QuestDataTable::FUN_14074e510, the m_2d0
    /// map lookup, whose setter is not found.
    /// </summary>
    public static List<QuestItemLink> QuestItemLinks(QuestTable table, IQuestContext quests, Item item)
    {
        var links = new List<QuestItemLink>();
        if (table == null || quests == null || item == null) return links;
        for (int kind = 0; kind < 2; kind++)
            foreach (uint id in table.QuestIdsByKey((uint)item.ItemId, kind))
            {
                var d = table.Quest(id);
                if (d == null || d.Locking) continue;
                if (d.Schedule.Length != 0 && quests.TermTag(d.Schedule).Tag == null) continue;
                if (d.Require.Job.Length != 0 && (quests.JobGroup is not int job || Array.IndexOf(d.Require.Job, job) < 0)) continue;
                if (kind == 1 && item.Record?.HeaderFlagD4 == true) continue;
                int state = 4;
                var q = quests.Get((int)id);
                if (q == null) state = 0;
                else if (q.State == 2) { if (d.Repeat != 0) state = 0; }
                else if (q.State == 1) state = 1;
                if (state != 4) links.Add(new QuestItemLink((int)id, state, kind));
            }
        return links;
    }

    /// <summary>GetQuestNameWithLevel (questuidata.cpp:256): the key's string with GetQuestName and the quest level, or the
    /// bare name when the quest has no data.</summary>
    string QuestNameWithLevel(int questId, string key)
    {
        var table = model.Quests;
        if (table == null) return "";
        string name = table.QuestName(questId);
        var d = table.Quest(questId);
        return d == null ? name : CommonF(key, name, d.Level);
    }

    /// <summary>CQuestManager::FUN_1416ce6a0 (questmanager_2.cpp:1280): the item's best link (in progress over can start
    /// over complete): 0 can start, 1 in progress, 2 complete, 4 none.</summary>
    public static int QuestItemState(IReadOnlyList<QuestItemLink> links)
    {
        int best = 0;
        foreach (var l in links)
        {
            int rank = l.State switch { 0 => 2, 1 => 3, 2 => 1, _ => 0 };
            if (rank > best) best = rank;
        }
        return best switch { 2 => 0, 3 => 1, 1 => 2, _ => 4 };
    }
}
