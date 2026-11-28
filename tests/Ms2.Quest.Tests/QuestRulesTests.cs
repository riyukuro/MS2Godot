using System.Xml.Linq;
using NUnit.Framework;

namespace Ms2.Quest.Tests;

public class QuestRulesTests
{
    sealed class Ctx : IQuestContext
    {
        public readonly Dictionary<int, QuestRecord> Records = new();
        public QuestRecord Get(int questId) => Records.TryGetValue(questId, out var r) ? r : null;
        public long ServerTime { get; set; }
        public long ZoneOffset { get; set; }
        public long CharacterId { get; set; } = 0x0000_0007_0000_0001;
        public int? Selector { get; set; } = 0;
        public int? Level { get; set; } = 30;
        public int? GearScore { get; set; }
        public int? JobGroup { get; set; } = 10;
        public int? SubJob { get; set; }
        public long? CharacterTime { get; set; }
        public bool? InGuild { get; set; }
        public int? GuildLevel { get; set; }
        public int? GuildNpcLevel { get; set; }
        public long? GuildMemberTime { get; set; }
        public bool? AchievementDone(uint id) => null;
        public bool? AchievementGradeCleared(uint id, int grade) => null;
        public int? FameGrade(int alliance) => null;
        public int? MasteryGrade(int type) => null;
        public int? DungeonRecord(uint dungeon) => null;
        public int? DungeonEnterState(uint dungeon) => null;
        public int? DungeonHelperReward(uint dungeon) => null;
        public int? MentoringRole { get; set; }
        public bool? Vip { get; set; }
        public bool? HasEquipment(uint[] items) => null;
        public readonly Dictionary<string, TermTag> Tags = new();
        public (bool Known, TermTag Tag) TermTag(string name) => Tags.TryGetValue(name, out var t) ? (true, t) : (false, null);
        public bool? HasStamp(int questId) => null;
        public int? FieldId { get; set; }
        public int? ParentField(int field) => null;
        public readonly HashSet<int> Daily = new();
        public int? DailyCount(int questId) => Daily.Contains(questId) ? 1 : 0;
        public int? WeeklyCount(int questId) => 0;
    }

    static QuestTable Table(string quests, string groups = null)
    {
        var t = new QuestTable("", null);
        t.AddQuests(XDocument.Parse("<ms2>" + quests + "</ms2>"));
        t.BuildIndexes();
        if (groups != null) t.AddGroups(XDocument.Parse("<ms2>" + groups + "</ms2>"));
        return t;
    }

    static string Quest(int id, string basic = "", string require = "") =>
        $@"<quest id=""{id}""><basic {basic} /><require {require} /></quest>";

    // 2026-11-17 (a Tuesday) 12:00 UTC
    static readonly long Noon = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    [Test]
    public void RandomFollowsTheShiftRegisters()
    {
        var r = new QuestRandom(12345);
        Assert.That(new[] { r.Next(), r.Next(), r.Next() }, Is.EqualTo(new uint[] { 165881554, 2549403341, 1042936200 }));
        r = new QuestRandom(-7);
        Assert.That(r.Next(), Is.EqualTo(2473977120u));
    }

    [Test]
    public void WeekdaysAndWeeksFollowTheLocalDate()
    {
        // Tuesday (3) noon: the next Friday (6) midnight is 3 days on; the next Tuesday is a week on
        long midnight = Noon - 12 * 3600;
        Assert.That(QuestTime.NextWeekdayStart(6, Noon, 0), Is.EqualTo(midnight + 3 * 86400));
        Assert.That(QuestTime.NextWeekdayStart(3, Noon, 0), Is.EqualTo(midnight + 7 * 86400));
        // 9 hours east (zone -32400): 21:00 local, still Tuesday; midnight local is 15:00 UTC the day before
        Assert.That(QuestTime.NextWeekdayStart(6, Noon, -32400), Is.EqualTo(midnight - 9 * 3600 + 3 * 86400));
        Assert.That(QuestTime.DayOfWeek(Noon, 0), Is.EqualTo(3));
        Assert.That(QuestTime.DayOfWeek(Noon + 13 * 3600, -32400), Is.EqualTo(4));
        Assert.That(QuestTime.WeekOfYear(2026, 11, 20), Is.EqualTo(47));
        Assert.That(QuestTime.WeekOfYear(2023, 1, 6), Is.EqualTo(2));
    }

    [Test]
    public void RequireAndPeriodLoad()
    {
        var t = Table(Quest(5, @"usePeriod=""FRI"" eventTag=""tag"" autoStart=""1""",
                               @"level=""20"" maxLevel=""40"" job=""10,20"" dayOfWeek=""1,7"" groupID=""9"" guildLevel=""0"" unreqAchievement=""3,2""")
                    + Quest(6, @"usePeriod=""1440"" locking=""1""", @"groupID=""9"""));
        var q = t.Quest(5);
        Assert.That((q.ResetType, q.DurationMinutes, q.Schedule, q.AutoStart), Is.EqualTo((6, 0, "tag", true)));
        Assert.That((q.Require.Level, q.Require.MaxLevel, q.Require.GroupId, q.Require.GuildLevel), Is.EqualTo((20, 40, 9, 0)));
        Assert.That(q.Require.Job, Is.EqualTo(new[] { 10, 20 }));
        Assert.That(q.Require.DayOfWeek, Is.EqualTo(new[] { 1, 7 }));
        Assert.That((q.Require.UnreqAchievement, q.Require.UnreqAchievementGrade), Is.EqualTo((3, 2)));
        Assert.That((t.Quest(6).DurationMinutes, t.Quest(6).Require.GroupId), Is.EqualTo((1440, 0)), "a locked quest keeps no group");
        Assert.That(t.Quest(6).Require.GuildLevel, Is.EqualTo(-1));
        Assert.That(t.GroupQuests(9), Is.EqualTo(new uint[] { 5 }));
    }

    [Test]
    public void CanStartReadsLevelJobDayAndRecords()
    {
        var t = Table(Quest(1, "", @"level=""20"" job=""10""") + Quest(2, "", @"quest=""1""") + Quest(3, "", @"dayOfWeek=""1,7""")
                      + Quest(4, @"repeatable=""2""") + Quest(7, "", @"level=""50"""));
        var c = new Ctx { ServerTime = Noon };
        var rules = new QuestRules(t, c);
        Assert.That(rules.CanStart(1), Is.True);
        Assert.That(rules.CanStart(7), Is.False, "under the level");
        c.JobGroup = 20;
        Assert.That(rules.CanStart(1), Is.False, "another job");
        c.JobGroup = null;
        Assert.That(rules.CanStart(1), Is.Null, "the job unknown");
        Assert.That(rules.CanStart(7), Is.False, "a known failure wins over an unknown");
        Assert.That(rules.CanStart(2), Is.False, "the required quest is not rewarded");
        c.Records[1] = new QuestRecord(1, 2, Noon - 100, Noon - 50, 0, false, false);
        Assert.That(rules.CanStart(2), Is.True);
        Assert.That(rules.CanStart(1), Is.False, "a rewarded quest that does not repeat");
        Assert.That(rules.CanStart(3), Is.False, "not on Sunday or Saturday");
        // a daily quest rewarded today waits for tomorrow
        c.Records[4] = new QuestRecord(4, 2, Noon - 3600, Noon - 60, 0, false, false);
        Assert.That(rules.CanStart(4), Is.False);
        c.ServerTime = Noon + 86400;
        Assert.That(rules.CanStart(4), Is.True);
        c.Records[4] = new QuestRecord(4, 1, Noon, 0, 0, false, false);
        Assert.That(rules.CanStart(4), Is.False, "in progress");
    }

    [Test]
    public void TermTagsBoundTheQuest()
    {
        var t = Table(Quest(1, @"eventTag=""ev"""));
        var c = new Ctx { ServerTime = Noon };
        var rules = new QuestRules(t, c);
        Assert.That(rules.CanStart(1), Is.Null, "no word on the tag yet");
        c.Tags["ev"] = null;
        Assert.That(rules.CanStart(1), Is.False, "the server has no such tag");
        c.Tags["ev"] = new TermTag(Noon - 10, Noon + 10, false);
        Assert.That(rules.CanStart(1), Is.True);
        c.Tags["ev"] = new TermTag(Noon + 1, Noon + 10, false);
        Assert.That(rules.CanStart(1), Is.False);
        // a quest of the tag ends with it
        c.Tags["ev"] = new TermTag(Noon - 10, Noon + 10, false);
        Assert.That(rules.EndTime(t.Quest(1), Noon - 5), Is.EqualTo(Noon + 10));
        Assert.That(rules.IsExpired(t.Quest(1), Noon - 5), Is.False);
        c.ServerTime = Noon + 10;
        Assert.That(rules.IsExpired(t.Quest(1), Noon - 5), Is.True);
    }

    [Test]
    public void PeriodsEndAtTheResetOrTheDuration()
    {
        var t = Table(Quest(1, @"usePeriod=""fri""") + Quest(2, @"usePeriod=""60""") + Quest(3));
        var c = new Ctx { ServerTime = Noon };
        var rules = new QuestRules(t, c);
        Assert.That(rules.EndTime(t.Quest(1), Noon), Is.EqualTo(Noon - 12 * 3600 + 3 * 86400));
        Assert.That(rules.EndTime(t.Quest(2), 0), Is.EqualTo(Noon + 3600), "no start: from the server time");
        Assert.That(rules.IsExpired(t.Quest(2), Noon - 3600), Is.True);
        Assert.That(rules.IsExpired(t.Quest(3), 1), Is.False, "no period");
    }

    [Test]
    public void GroupsPickBySeed()
    {
        string qs = "";
        for (int i = 1; i <= 6; i++) qs += Quest(i, @"repeatable=""2""", @"groupID=""4""");
        var t = Table(qs, @"<group id=""4"" pickCount=""2"" dependency=""character"" />");
        var c = new Ctx { ServerTime = Noon, Selector = 0 };
        var rules = new QuestRules(t, c);
        Assert.That(t.GroupPickKind(4), Is.EqualTo(1));
        Assert.That(t.GroupPickCount(9), Is.EqualTo(1));
        // daily: (20261117 * 4 + seed) as Unk_Random's seed, each pick removed from the candidates
        int seed = unchecked(20261117 * 4 + 7);
        var rnd = new QuestRandom(seed);
        var ids = new List<uint> { 1, 2, 3, 4, 5, 6 };
        var expect = new List<uint>();
        for (int i = 0; i < 2; i++) { int k = (int)(rnd.Next() % (uint)ids.Count); expect.Add(ids[k]); ids.RemoveAt(k); }
        Assert.That(rules.Pick(4, 7), Is.EqualTo(expect));
        int startable = Enumerable.Range(1, 6).Count(i => rules.CanStart(i) == true);
        Assert.That(startable, Is.EqualTo(2), "the character seed is the id's high half, 7");
        c.Selector = null;
        Assert.That(rules.CanStart(1), Is.Null);
    }

    [Test]
    public void CompleteNeedsEveryGoal()
    {
        var q = new QuestData { Id = 1 };
        q.Conditions.Add(new QuestCondition { Goal = 3 });
        q.Conditions.Add(new QuestCondition { Goal = 1 });
        Assert.That(QuestRules.IsComplete(q, 1, new long[] { 3, 1 }), Is.True);
        Assert.That(QuestRules.IsComplete(q, 1, new long[] { 2, 1 }), Is.False);
        Assert.That(QuestRules.IsComplete(q, 2, new long[] { 3, 1 }), Is.False);
    }

    /// <summary>
    /// CConditionCommonData starts its goal at 1 (conditionutil.cpp:287) and Load only reads "value" when it is there
    /// (:330), so a condition without a value needs one count: quest 50001670's interact_object condition has none, and a
    /// goal of 0 read the quest complete the moment it was taken, which closed its object's quest mask.
    /// </summary>
    [Test]
    public void AConditionWithoutValueNeedsOneCount()
    {
        var c = QuestCondition.Load(XElement.Parse("<condition type=\"interact_object\" code=\"10001229\" />"));
        Assert.That(c.Goal, Is.EqualTo(1));
        Assert.That(QuestCondition.Load(XElement.Parse("<condition type=\"npc\" code=\"7\" value=\"3\" />")).Goal, Is.EqualTo(3));
        Assert.That(QuestCondition.Load(XElement.Parse("<condition type=\"npc\" code=\"7\" value=\"0\" />")).Goal, Is.EqualTo(0), "a written 0 stays 0");
        Assert.That(new QuestCondition().Goal, Is.EqualTo(1), "a trophy grade without a condition element (achievedata.cpp:701)");
        var q = new QuestData { Id = 50001670 };
        q.Conditions.Add(c);
        Assert.That(QuestRules.IsComplete(q, 1, new long[] { 0 }), Is.False, "taken, the object not used yet");
        Assert.That(QuestRules.IsComplete(q, 1, new long[] { 1 }), Is.True);
    }
}
