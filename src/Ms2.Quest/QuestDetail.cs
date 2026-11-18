using System.Globalization;

namespace Ms2.Quest;

/// <summary>
/// An object UIQuestDialog hands the movie (an ASObj): its members in the order the client sets them. A value is a
/// string, a bool, an int, a uint, a long (an int64 member, a Number in the movie), a <see cref="QuestInfoObject"/> or an
/// <see cref="IReadOnlyList{T}"/> of values (an ASArray).
/// </summary>
public sealed class QuestInfoObject
{
    public readonly List<(string Name, object Value)> Members = new();
    public QuestInfoObject Set(string name, object value) { Members.Add((name, value)); return this; }
    public object this[string name] => Members.LastOrDefault(m => m.Name == name).Value;
}

/// <summary>
/// What the quest detail reads from the live game besides the quest state: the local player's field and guild, the
/// server's time zone, the button rules (CQuestCommon) and the reward icon slots.
/// </summary>
public sealed class QuestDetailContext
{
    /// <summary>WorldService::GetFieldId: the field the local player is in.</summary>
    public int FieldId;
    /// <summary>The guild's npc level for the reward readers (Unk_QuestRewardCalc); 0 without a guild.</summary>
    public int GuildNpcLevel;
    /// <summary>The local player's job group (PC::GetJobGroupId), for the choice rewards (FUN_140748050).</summary>
    public int JobGroup;
    /// <summary>CMsTime::GetLocalTm of a Unix time: the calendar time in the server's zone.</summary>
    public Func<long, DateTime> LocalTm = t => DateTimeOffset.FromUnixTimeSeconds(t).UtcDateTime;
    /// <summary>CQuestNavigationMap (NearestMap), null when the map graph is not loaded.</summary>
    public QuestMapGraph Map;
    /// <summary>
    /// AddButtons (uiquestdialog.cpp:1483, <see cref="QuestCommon.Buttons"/>): the "gotoButton" names in the client's
    /// order; a null result (or no function) sets no "gotoButton" member, as AddButtons' early returns do.
    /// </summary>
    public Func<QuestData, QuestRecord, IReadOnlyList<string>> Buttons;
    /// <summary>
    /// CQuestCommon::GetEndTime of a quest's record (QuestProgressStory, <see cref="QuestCommon.EndTime"/>). A null result
    /// (the server time or the term tag is not known) leaves the end time line out.
    /// </summary>
    public Func<QuestData, QuestRecord, long?> EndTime;
}

/// <summary>
/// UIQuestDialog's detail pane (uiquestdialog.cpp): the object it sends m_questInfo's setData for a selected quest
/// (SelectQuest, :777) and for an empty pane (RefreshDetail, :1228), with the texts it builds for them (QuestStory,
/// QuestProgressStory, QuestDate, AddConditions, AddRewardInfo).
/// </summary>
public static class QuestDetail
{
    const int MaxExpRateLevel = 0x32;     // AddRewardInfo: the exp rate is shown below level 50

    /// <summary>
    /// RefreshDetail: id 0, empty title, level, date and story, the give-up button disabled, main on the main tab
    /// (GetTab() == 0).
    /// </summary>
    public static QuestInfoObject Empty(int tab) => new QuestInfoObject()
        .Set("id", 0).Set("title", "").Set("level", "").Set("date", "").Set("story", "")
        .Set("disableGiveup", 1).Set("main", tab == 0);

    /// <summary>
    /// SelectQuest: null when the quest has no data, or (sub-tab filter other than 0) the character has no record of it;
    /// the client then sends nothing. <paramref name="subTabFilter"/> is GetSubTabFilter (0, 1, 2, or 4).
    /// </summary>
    public static QuestInfoObject Select(QuestLog log, int id, int subTabFilter, QuestDetailContext ctx)
    {
        var table = log.Table;
        var d = table.Quest(id);
        if (d == null) return null;
        var info = new QuestInfoObject()
            .Set("id", (uint)id)
            .Set("title", log.QuestTitle(id))
            // BlockFormat(0x459, level): GetStringCommon(1113) = s_quest_standard_level (commonstringtable.cpp:2712)
            .Set("level", ClientFormat.Format(table.Common("s_quest_standard_level"), d.Level))
            .Set("hideReward", 0)
            .Set("main", d.Type == 0);
        if (subTabFilter == 0)
        {
            info.Set("date", "").Set("story", QuestStory(log, d, ctx)).Set("progress", 0);
        }
        else
        {
            var q = log.State.Get(id);
            if (q == null) return null;
            info.Set("date", QuestDate(table, q.CompleteTime, ctx))     // FUN_141aa4980: m_completeTime (:1942)
                .Set("story", QuestProgressStory(table, d, q, ctx))
                .Set("progress", !q.IsRewarded);
            if (ctx.Buttons?.Invoke(d, q) is { } buttons) info.Set("gotoButton", (IReadOnlyList<object>)buttons.Cast<object>().ToList());
            AddConditions(log, d, info);
        }
        AddRewardInfo(log, d, subTabFilter, ctx, info);
        return info;
    }

    /// <summary>
    /// QuestStory (:1068), the story of a quest that can begin: a quest that can be accepted now names its start npc (none:
    /// the title), with the field nearest the player of the npc's fields (else remoteAccept's requireField):
    /// s_quest_desc_beginable_invalid_field without one, _current_field when it is the player's, else s_quest_desc_beginable
    /// with the field; bold words replaced. Otherwise s_quest_desc_require with the required quests' titles ", " joined,
    /// then the selectable ones joined with s_quest_desc_require_or.
    /// </summary>
    public static string QuestStory(QuestLog log, QuestData d, QuestDetailContext ctx)
    {
        var table = log.Table;
        if (log.State.CanStart(d.Id))
        {
            int npc = d.StartNpc;
            if (npc == 0) return log.QuestTitle(d.Id);
            var maps = log.Meta != null && log.Meta.NpcFields.TryGetValue(npc, out var l) ? l : new List<int>();
            int map = ctx.Map?.NearestMap(ctx.FieldId, maps) ?? 0;
            if (map == 0) map = d.RemoteRequireField;
            string text = map == 0 ? ClientFormat.Format(table.Common("s_quest_desc_beginable_invalid_field"), (uint)npc)
                : map == ctx.FieldId ? ClientFormat.Format(table.Common("s_quest_desc_beginable_current_field"), (uint)npc)
                : ClientFormat.Format(table.Common("s_quest_desc_beginable"), (uint)map, (uint)npc);
            return table.Words.ReplaceBoldWords(text);
        }
        string require = "";
        foreach (var q in d.RequireQuest)
        {
            if (require.Length != 0) require += ", ";
            require += log.QuestTitle((int)q);
        }
        string alternative = "";
        foreach (var q in d.SelectableQuest)
        {
            if (alternative.Length != 0) alternative += table.Common("s_quest_desc_require_or");
            alternative += log.QuestTitle((int)q);
        }
        if (alternative.Length != 0)
        {
            if (require.Length != 0) require += ", ";
            require += alternative;
        }
        return ClientFormat.Format(table.Common("s_quest_desc_require"), require);
    }

    /// <summary>
    /// QuestProgressStory (:1020): a field quest (type 8) with fields starts with s_quest_field_quest_link of its first
    /// field; a time-limited quest (FUN_14074b090) with an end time replaces that with s_quest_dialog_end_time (or
    /// _expired) of the end time and a line break, without one but expired with s_quest_end_time_expired and a line break;
    /// then the quest's description (GetQuestDesc), value words replaced.
    /// </summary>
    public static string QuestProgressStory(QuestTable table, QuestData d, QuestRecord q, QuestDetailContext ctx)
    {
        string result = "";
        if (d.Type == 8 && d.Fields.Length > 0)
            result += ClientFormat.Format(table.Common("s_quest_field_quest_link"), d.Fields[0]);
        if (QuestCommon.IsTimeLimited(d))
        {
            long? end = ctx.EndTime?.Invoke(d, q);
            if (end is long e && e != 0)
            {
                var t = ctx.LocalTm(e);
                string key = q.Expired ? "s_quest_dialog_end_time_expired" : "s_quest_dialog_end_time";
                result = ClientFormat.Format(table.Common(key), t.Year, t.Month, t.Day, t.Hour, t.Minute) + "\n";
            }
            else if (end != null && q.Expired)
                result = table.Common("s_quest_end_time_expired") + "\n";
        }
        result += table.QuestDesc(d.Id);
        return table.Words.ReplaceValueWords(result);
    }

    /// <summary>
    /// QuestDate (:1203): "" for 0, else GetStringCommon(0x456) = s_quest_clear_time (commonstringtable.cpp:2709) with the
    /// year, month, day, 0x457 / 0x458 (s_quest_clear_morning before 12, else _afternoon) and the hour less 12 above 12.
    /// </summary>
    public static string QuestDate(QuestTable table, long time, QuestDetailContext ctx)
    {
        if (time == 0) return "";
        var t = ctx.LocalTm(time);
        string ampm = t.Hour < 12 ? table.Common("s_quest_clear_morning") : table.Common("s_quest_clear_afternoon");
        int hour = t.Hour > 12 ? t.Hour - 12 : t.Hour;
        return ClientFormat.Format(table.Common("s_quest_clear_time"), t.Year, t.Month, t.Day, ampm, hour);
    }

    /// <summary>
    /// AddConditions (:1397): "summary", one object per condition: its text (GetQuestConditionText) and the binding of its
    /// item icon slot for an item condition (type 2), else "".
    /// </summary>
    static void AddConditions(QuestLog log, QuestData d, QuestInfoObject info)
    {
        var summary = new List<object>();
        for (int i = 0; i < d.Conditions.Count; i++)
        {
            // STILL-OPEN: a type 2 condition's binding is the name of the item info slot RewardIconName opens
            // (CreateItemInfoSlot, :980); the slots are not hosted, so the binding stays empty.
            summary.Add(new QuestInfoObject().Set("condition", log.Table.ConditionText(d.Id, i, log.State)).Set("binding", ""));
        }
        info.Set("summary", summary);
    }

    /// <summary>
    /// AddRewardInfo (:1338): "reward" with meso, exp (as text), karma, lu, guildExp, guildFund and famePoint; below level
    /// 50 and off the completed sub-tab, expRate = s_quest_reward_exp_percent of the exp over the level's next exp in
    /// percent, rounded to 2 places in float, when above 0; then "items".
    /// </summary>
    static void AddRewardInfo(QuestLog log, QuestData d, int subTabFilter, QuestDetailContext ctx, QuestInfoObject info)
    {
        var table = log.Table;
        var rewards = table.Rewards;
        int level = log.State.PlayerLevel;
        var c = new QuestRewards.Context(level, ctx.GuildNpcLevel);
        long exp = rewards.Exp(d, c);
        var reward = new QuestInfoObject()
            .Set("meso", rewards.Meso(d, c))
            .Set("exp", exp.ToString(CultureInfo.InvariantCulture))
            .Set("karma", rewards.Karma(d))
            .Set("lu", rewards.Lu(d))
            .Set("guildExp", rewards.GuildExp(d, c))
            .Set("guildFund", rewards.GuildFund(d, c))
            .Set("famePoint", rewards.FamePoint(d));
        if (level < MaxExpRateLevel && subTabFilter != 2)
        {
            long need = rewards.GetNextExp(level);
            float rate = (float)exp / (float)need * 100.0f;
            rate = MathF.Floor(rate * 100.0f + 0.5f) / 100.0f;
            if (0.0f < rate) reward.Set("expRate", ClientFormat.Format(table.Common("s_quest_reward_exp_percent"), rate));
        }
        // STILL-OPEN: the choice rewards and the guild coin are item info slots (RewardIconName / CreateItemInfoSlot,
        // :980), which are not hosted; the list stays empty.
        reward.Set("items", new List<object>());
        info.Set("reward", reward);
    }
}
