namespace Ms2.Quest;

/// <summary>A term tag the server sent (TermTagContext::Read): its window and its m_48 flag.</summary>
/// <param name="Start">m_28, the start time.</param>
/// <param name="End">m_38, the end time.</param>
/// <param name="CheckCharacterTime">m_48 (FUN_140866760): a quest of the tag needs the character's time (vfunc_o10_8) to not be before the start.</param>
public sealed record TermTag(long Start, long End, bool CheckCharacterTime)
{
    /// <summary>IsActiveAt / FUN_140866880: Start &lt;= t &lt; End.</summary>
    public bool Contains(long t) => Start <= t && t < End;
}

/// <summary>
/// What CQuestManager reads from the rest of the client to decide whether a quest can start (its vfunc_o10_* and the game
/// rule). A null answer means the client does not know it yet; a rule that reads an unknown answer makes the result
/// unknown, never a guess (<see cref="QuestRules.CanStart"/>).
/// </summary>
public interface IQuestContext
{
    /// <summary>vfunc_o10_3: the character's record of a quest, null when it has none.</summary>
    QuestRecord Get(int questId);
    /// <summary>GetServerTime (CMainSystem: the local time moved by the server's base).</summary>
    long ServerTime { get; }
    /// <summary>CMsTime::CalcZoneOffset: the zone seconds minus the daylight hours.</summary>
    long ZoneOffset { get; }
    /// <summary>vfunc_o10_22 (DAT_143ab2828): the character id.</summary>
    long CharacterId { get; }
    /// <summary>m_selector (SetSelector): a group pick's seed when not 0.</summary>
    int? Selector { get; }
    /// <summary>vfunc_o10_4: the level.</summary>
    int? Level { get; }
    /// <summary>vfunc_o10_5: the gear score.</summary>
    int? GearScore { get; }
    /// <summary>vfunc_o10_6: the job group.</summary>
    int? JobGroup { get; }
    /// <summary>vfunc_o10_7: the sub job.</summary>
    int? SubJob { get; }
    /// <summary>vfunc_o10_8 (PC FUN_141658eb0): the character time a term tag with the flag compares with its start.</summary>
    long? CharacterTime { get; }
    /// <summary>vfunc_o10_9: in a guild.</summary>
    bool? InGuild { get; }
    /// <summary>CGuildXmlData::GetLevelByExp(vfunc_o10_10): the guild's level.</summary>
    int? GuildLevel { get; }
    /// <summary>vfunc_o10_11: the guild npc level of kind 6.</summary>
    int? GuildNpcLevel { get; }
    /// <summary>vfunc_o10_12: the guild member time a type 6 quest waits a day or a week after.</summary>
    long? GuildMemberTime { get; }
    /// <summary>vfunc_o10_1: the achievement is in state 3.</summary>
    bool? AchievementDone(uint id);
    /// <summary>vfunc_o10_0: the achievement grade is cleared.</summary>
    bool? AchievementGradeCleared(uint id, int grade);
    /// <summary>vfunc_o10_13: the fame grade of an alliance.</summary>
    int? FameGrade(int alliance);
    /// <summary>vfunc_o10_14: the mastery grade of a mastery type.</summary>
    int? MasteryGrade(int type);
    /// <summary>vfunc_o10_15: the dungeon record.</summary>
    int? DungeonRecord(uint dungeon);
    /// <summary>vfunc_o10_16: the dungeon enter state.</summary>
    int? DungeonEnterState(uint dungeon);
    /// <summary>vfunc_o10_17: CanClaimHelperReward of a dungeon.</summary>
    int? DungeonHelperReward(uint dungeon);
    /// <summary>vfunc_o10_18: the mentoring role (5 mentor, 2 mentee).</summary>
    int? MentoringRole { get; }
    /// <summary>vfunc_o10_19: a vip.</summary>
    bool? Vip { get; }
    /// <summary>vfunc_o10_20: wears the equipment.</summary>
    bool? HasEquipment(uint[] items);
    /// <summary>CGameRule::GetTermTag: (known, the tag or null when the server sent none).</summary>
    (bool Known, TermTag Tag) TermTag(string name);
    /// <summary>The stamp event's HasStamp (CGameRule::GetStampEvent): null when unknown, false also when there is no event.</summary>
    bool? HasStamp(int questId);
    /// <summary>WorldService::GetFieldId.</summary>
    int? FieldId { get; }
    /// <summary>CFieldMetaTable::GetParentField: the field's outdoor field, null when it has none.</summary>
    int? ParentField(int field);
    /// <summary>m_dailyCounts / m_weeklyCounts (OnQuest 0x1f / 0x20 set 1): the quest's count.</summary>
    int? DailyCount(int questId);
    int? WeeklyCount(int questId);
}

/// <summary>CMsTime's calendar over a zone offset (mstime.cpp, timeutil.cpp).</summary>
public static class QuestTime
{
    /// <summary>CMsTime::GetLocalTm: gmtime of the time minus the zone offset.</summary>
    public static DateTime LocalTm(long t, long zone) => DateTimeOffset.FromUnixTimeSeconds(t - zone).UtcDateTime;

    /// <summary>The local CMsTime(Y, M, D, 0, 0, 0)'s Get(): _mkgmtime of the fields plus the zone offset.</summary>
    public static long LocalMidnight(int y, int m, int d, long zone) =>
        new DateTimeOffset(y, m, d, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds() + zone;

    /// <summary>GetDayOfWeek: 1 Sunday .. 7 Saturday.</summary>
    public static int DayOfWeek(long t, long zone) => (int)LocalTm(t, zone).DayOfWeek + 1;

    /// <summary>NextWeekdayStart (timeutil.cpp @0x14086b190): the next local midnight on the weekday (7 days on when today is that day).</summary>
    public static long NextWeekdayStart(int weekday, long t, long zone)
    {
        var tm = LocalTm(t, zone);
        long start = LocalMidnight(tm.Year, tm.Month, tm.Day, zone);
        int dow = (int)tm.DayOfWeek + 1;
        long delta = dow < weekday ? weekday - dow : 7 - (dow - weekday);
        return start + delta * 86400;
    }

    /// <summary>GetWeekOfYear (timeutil.cpp @0x14086b590).</summary>
    public static long WeekOfYear(int year, int month, int day)
    {
        var first = new DateTime(year, 1, 1);
        long days = (long)(new DateTime(year, month, day) - first).TotalDays;
        int dow = (int)first.DayOfWeek + 1;
        int offset = dow == 1 ? 5 : dow - 3;
        return (days + 1 + offset) / 7 + 1;
    }
}

/// <summary>Unk_Random (questmasktable.cpp): three shift registers seeded by an LCG.</summary>
public sealed class QuestRandom
{
    uint s1, s2, s3;

    /// <summary>Unk_Random(int): three Unk_RandomStep values, each with its low bit set (SetState).</summary>
    public QuestRandom(int seed)
    {
        uint Step() { int v = seed; seed = unchecked(seed * 0x343fd + 0x269ec3); return (uint)v; }
        uint a = Step(), b = Step(), c = Step();
        s1 = a | 0x100000; s2 = b | 0x1000; s3 = c | 0x10;
    }

    /// <summary>FUN_1407684c0.</summary>
    public uint Next()
    {
        s1 = (s1 & 0xfffffffe) << 12 ^ (s1 << 13 ^ s1) >> 19;
        s2 = (s2 & 0xfffffff8) << 4 ^ (s2 << 2 ^ s2) >> 25;
        s3 = (s3 & 0xfffffff0) << 17 ^ (s3 << 3 ^ s3) >> 11;
        return s1 ^ s2 ^ s3;
    }
}

/// <summary>
/// The quest rules CQuestManager runs over a <see cref="QuestTable"/> and an <see cref="IQuestContext"/>: CQuest::IsComplete,
/// CQuestCommon::GetEndTime / IsExpired, CQuestManagerCommon::CanAcceptQuest and vfunc_o10_21, the group pick
/// (QuestDataTable::FUN_14074e6c0) and CQuestManager::vfunc_1.
/// </summary>
public sealed class QuestRules
{
    readonly QuestTable table;
    readonly IQuestContext ctx;

    public QuestRules(QuestTable table, IQuestContext ctx) { this.table = table; this.ctx = ctx; }

    /// <summary>CQuest::IsComplete (questmanager_1.cpp @0x1416c63b0): in progress and every count at its condition's goal.
    /// CQuest::Init gives m_progress one 0 per condition and SetProgress drops an index past them (:127-139), so a condition
    /// the packet sent no count for counts 0, and a count past the conditions is not read.</summary>
    public static bool IsComplete(QuestData data, int state, long[] progress)
    {
        if (state != 1) return false;
        for (int i = 0; i < data.Conditions.Count; i++)
            if ((progress != null && i < progress.Length ? progress[i] : 0) < data.Conditions[i].Goal) return false;
        return true;
    }

    /// <summary>
    /// CQuestCommon::GetEndTime (questdata.cpp @0x1407448a0): the reset weekday or the duration from the start (the server
    /// time when it has none), and the term tag's end; the earlier of the two set. Null when the term tag is unknown.
    /// </summary>
    public long? EndTime(QuestData data, long startTime)
    {
        long period = 0, ev = 0;
        long start = startTime != 0 ? startTime : ctx.ServerTime;
        if (data.ResetType != 0) period = QuestTime.NextWeekdayStart(data.ResetType, start, ctx.ZoneOffset);
        else if (data.DurationMinutes != 0) period = start + data.DurationMinutes * 60;
        if (data.Schedule.Length != 0)
        {
            var (known, tag) = ctx.TermTag(data.Schedule);
            if (!known) return null;
            if (tag == null) return 0;
            if (startTime != 0 && !tag.Contains(startTime)) return 0;
            ev = tag.End;
        }
        if (period != 0 && ev != 0) return Math.Min(period, ev);
        return period != 0 ? period : ev;
    }

    /// <summary>
    /// CQuestCommon::IsExpired (@0x1407447c0): a quest with a period (FUN_14074b090: a duration, a term tag or a reset
    /// weekday) whose end time has come. Null when the end time is unknown.
    /// </summary>
    public bool? IsExpired(QuestData data, long startTime)
    {
        if (data == null) return false;
        if (data.DurationMinutes == 0 && data.Schedule.Length == 0 && data.ResetType == 0) return false;
        return EndTime(data, startTime) is long end ? ctx.ServerTime >= end : null;
    }

    /// <summary>CQuestManager::vfunc_1(id, 0) (questmanager_2.cpp @0x1416c9e10): null when an input it reads is unknown.</summary>
    public bool? CanStart(int questId) => CanStart(questId, 0);

    /// <summary>CQuestManager::vfunc_1(id, npc): with an npc, also only from the quest's start npc (FUN_1416e6100 asks it
    /// with the talk npc).</summary>
    public bool? CanStart(int questId, int npcId)
    {
        var u = new Tri();
        if (!u.Pass(CanStartChecks(questId, npcId, 0))) return false;
        var data = table.Quest(questId);
        if (data == null) return false;
        switch (data.Type)
        {
            case 4:
            {
                if (ctx.FieldId is not int map) return u.Unknown();
                if (data.Fields.Length == 0) return false;
                int field = (int)data.Fields[0];
                if (field != map)
                {
                    if (ctx.ParentField(map) is not int parent) return false;
                    if (field != parent) return false;
                }
                break;
            }
            case 5:
                if (data.EventMission == 2 && !u.Pass(ctx.HasStamp(questId))) return false;
                break;
            case 9:
                if (data.Repeat == 2) return u.Result(ctx.DailyCount(questId) is int d ? d != 0 : null);
                if (data.Repeat == 3 && data.Require.GroupId == 0) return u.Result(ctx.WeeklyCount(questId) is int w ? w != 0 : null);
                break;
        }
        return u.Result(true);
    }

    /// <summary>CQuestManagerCommon::vfunc_o10_21 (questdata.cpp @0x140744d40).</summary>
    bool? CanStartChecks(int questId, int npcId, int flag)
    {
        var u = new Tri();
        if (!u.Pass(CanAccept(questId, flag))) return false;
        var data = table.Quest(questId);
        if (data == null) return false;
        if (npcId != 0 && data.StartNpc != npcId) return false;
        long now = ctx.ServerTime, zone = ctx.ZoneOffset;
        if (data.Require.GroupId != 0)
        {
            if (Seed(data) is not int seed) return u.Unknown();
            if (Pick(data.Require.GroupId, seed) is not { } picked) return u.Unknown();
            if (!picked.Contains((uint)data.Id)) return false;
        }
        if (data.Require.DayOfWeek.Length > 0 && Array.IndexOf(data.Require.DayOfWeek, QuestTime.DayOfWeek(now, zone)) < 0) return false;
        long tStart = 0;
        int state = 4;
        if (ctx.Get(questId) is { } quest) { tStart = quest.StartTime; state = quest.State; }
        if (data.Schedule.Length != 0)
        {
            var (known, tag) = ctx.TermTag(data.Schedule);
            if (!known) return u.Unknown();
            if (tag == null || !tag.Contains(now)) return false;
            if (tag.CheckCharacterTime)
            {
                if (ctx.CharacterTime is not long ct) return u.Unknown();
                if (ct < tag.Start) return false;
            }
            if (tStart != 0 && !tag.Contains(tStart)) return u.Result(true);
        }
        if (state == 1) return false;
        switch (data.Repeat)
        {
            case 0:
                if (state == 2) return false;
                break;
            case 2:
                if (state == 2)
                {
                    var last = QuestTime.LocalTm(tStart, zone);
                    var today = QuestTime.LocalTm(now, zone);
                    if (last.Year == today.Year && last.Month == today.Month && last.Day == today.Day) return false;
                }
                break;
            case 3:
                if (state == 2 && now < QuestTime.NextWeekdayStart(data.ResetType, tStart, zone)) return false;
                break;
        }
        if (data.Type == 6 && (data.Repeat == 2 || data.Repeat == 3))
        {
            if (ctx.GuildMemberTime is not long g) return u.Unknown();
            if (now < g + (data.Repeat == 2 ? 1 : 7) * 86400) return false;
        }
        return u.Result(true);
    }

    bool? Rewarded(uint id) => ctx.Get((int)id)?.IsRewarded ?? false;
    bool? Active(uint id) => ctx.Get((int)id)?.IsActive ?? false;

    /// <summary>CQuestManagerCommon::CanAcceptQuest (questdata.cpp @0x140745820).</summary>
    bool? CanAccept(int questId, int flag)
    {
        var data = table.Quest(questId);
        if (data == null) return false;
        var r = data.Require;
        var u = new Tri();
        if (data.Locking) return false;
        if (ctx.Level is not int level) u.Unknown();
        else if (level < r.Level || (r.MaxLevel != 0 && level > r.MaxLevel)) return false;
        if (r.GearScore != 0)
        {
            if (ctx.GearScore is not int gs) u.Unknown();
            else if ((uint)gs < r.GearScore) return false;
        }
        if (r.Job.Length != 0)
        {
            if (ctx.JobGroup is not int job) u.Unknown();
            else if (Array.IndexOf(r.Job, job) < 0) return false;
        }
        if (r.SubJob.Length != 0)
        {
            if (ctx.SubJob is not int sub) u.Unknown();
            else if (Array.IndexOf(r.SubJob, sub) < 0) return false;
        }
        foreach (uint q in data.RequireQuest)
            if (q != 0 && Rewarded(q) == false) return false;
        if (data.SelectableQuest.Length > 0 && !data.SelectableQuest.Any(q => q != 0 && Rewarded(q) == true)) return false;
        foreach (uint q in r.ProgressQuest)
            if (q != 0 && Active(q) == false) return false;
        foreach (uint q in r.Unrequire)
            if (q != 0 && (Rewarded(q) == true || Active(q) == true)) return false;
        if (flag == 0)
            foreach (int group in r.UnrequireGroup)
            {
                // the seed is the asking quest's (FUN_140747000(questId)), the pick the listed group's
                if (Seed(data) is not int seed || Pick(group, seed) is not { } picked) { u.Unknown(); continue; }
                foreach (uint p in picked)
                {
                    if (Active(p) == true) return false;
                    if (Rewarded(p) == true && !u.Pass(CanStartChecks((int)p, 0, 1))) return false;
                }
            }
        if (r.Achievement != 0)
        {
            var done = r.AchievementGrade == 0 ? ctx.AchievementDone(r.Achievement) : ctx.AchievementGradeCleared(r.Achievement, r.AchievementGrade);
            if (!u.Pass(done)) return false;
        }
        if (r.UnreqAchievement != 0 && r.UnreqAchievementGrade != 0)
        {
            var c = ctx.AchievementGradeCleared((uint)r.UnreqAchievement, r.UnreqAchievementGrade);
            if (!u.Pass(c is bool b ? !b : null)) return false;
        }
        if (r.GuildLevel != -1)
        {
            if (ctx.InGuild is not bool inGuild) u.Unknown();
            else if (r.GuildLevel == 0) { if (inGuild) return false; }
            else if (!inGuild) return false;
            else if (ctx.GuildLevel is not int gl) u.Unknown();
            else if (gl < r.GuildLevel) return false;
        }
        if (r.GuildNpcLevel != 0)
        {
            if (ctx.InGuild is not bool inGuild) u.Unknown();
            else if (!inGuild) return false;
            else if (ctx.GuildNpcLevel is not int n) u.Unknown();
            else if ((ushort)n != r.GuildNpcLevel) return false;
        }
        if (r.Alliance != 0 && r.FameGrade != 0)
        {
            if (ctx.FameGrade(r.Alliance) is not int fg) u.Unknown();
            else if (fg < r.FameGrade) return false;
        }
        if (r.MasteryType != 0 && r.MasteryGrade != 0)
        {
            if (ctx.MasteryGrade(r.MasteryType) is not int mg) u.Unknown();
            else if (mg < r.MasteryGrade) return false;
        }
        if (r.Dungeon != 0 && !u.Pass(ctx.DungeonRecord(r.Dungeon) is int d1 ? d1 >= 1 : null)) return false;
        if (r.UnrequireDungeon != 0 && !u.Pass(ctx.DungeonRecord(r.UnrequireDungeon) is int d2 ? d2 == 0 : null)) return false;
        if (r.EnterDungeon != 0 && !u.Pass(ctx.DungeonEnterState(r.EnterDungeon) is int d3 ? d3 < 10 : null)) return false;
        if (r.DungeonBeginner.Length != 0)
        {
            bool any = false, unknown = false;
            foreach (uint dg in r.DungeonBeginner)
            {
                if (ctx.DungeonHelperReward(dg) is not int h) { unknown = true; continue; }
                if (h == 0) { any = true; break; }
            }
            if (!any && !unknown) return false;
            if (!any) u.Unknown();
        }
        if (r.Mentor != 0 && !u.Pass(ctx.MentoringRole is int m1 ? m1 == 5 : null)) return false;
        if (r.Mentee != 0 && !u.Pass(ctx.MentoringRole is int m2 ? m2 == 2 : null)) return false;
        if (r.Vip != 0 && !u.Pass(ctx.Vip)) return false;
        if (r.Equipment.Length != 0 && !u.Pass(ctx.HasEquipment(r.Equipment))) return false;
        return u.Result(true);
    }

    /// <summary>
    /// CQuestManagerCommon::FUN_140747000: the group pick seed of a quest's group kind (character: the high half of the
    /// character id; global and account: 0). Null when the character id is needed and unknown.
    /// </summary>
    int? Seed(QuestData data) => table.GroupPickKind(data.Require.GroupId) switch
    {
        1 => ctx.CharacterId != 0 ? (int)((ulong)ctx.CharacterId >> 32) : null,
        _ => 0,
    };

    /// <summary>
    /// QuestDataTable::FUN_14074e6c0 (@0x14074e6c0): the group's quests (quest id order) picked by Unk_Random; its seed the
    /// selector, else from the date by the group's first quest's repeat (daily: Y*10000+M*100+D, weekly: the year and week
    /// of the next Friday) times the group id plus the seed, else 0. Null when the selector is unknown.
    /// </summary>
    public List<uint> Pick(int groupId, int seed)
    {
        var picked = new List<uint>();
        var ids = table.GroupQuests(groupId);
        if (ids == null || ids.Count == 0) return picked;
        var first = table.Quest(ids[0]);
        if (first == null) return picked;
        if (ctx.Selector is not int selector) return null;
        long zone = ctx.ZoneOffset;
        var now = QuestTime.LocalTm(ctx.ServerTime, zone);
        int rs = 0;
        unchecked
        {
            if (selector != 0) rs = selector;
            else if (first.Repeat == 2) rs = ((now.Year * 10000 + now.Month * 100) + now.Day) * groupId + seed;
            else if (first.Repeat == 3)
            {
                var week = QuestTime.LocalTm(QuestTime.NextWeekdayStart(6, ctx.ServerTime, zone), zone);
                int dayNo = (int)QuestTime.WeekOfYear(week.Year, week.Month, week.Day);
                rs = (week.Year * 10000 + dayNo) * groupId + seed;
            }
        }
        var rnd = new QuestRandom(rs);
        var candidates = new List<uint>(ids);
        int picks = table.GroupPickCount(groupId);
        for (int i = 0; i < picks && candidates.Count > 0; i++)
        {
            int index = (int)(rnd.Next() % (uint)candidates.Count);
            picked.Add(candidates[index]);
            candidates.RemoveAt(index);
        }
        return picked;
    }

    /// <summary>A rule's answer so far: false ends it, an unknown input leaves it unknown unless a later rule fails.</summary>
    sealed class Tri
    {
        bool unknown;
        public bool? Unknown() { unknown = true; return null; }
        /// <summary>False only when the value is known false; an unknown one is remembered.</summary>
        public bool Pass(bool? v) { if (v == null) unknown = true; return v != false; }
        public bool? Result(bool? v) => v == false ? false : unknown || v == null ? null : true;
    }
}
