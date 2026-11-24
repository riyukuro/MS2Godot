using System.Xml.Linq;
using NUnit.Framework;

namespace Ms2.Quest.Tests;

/// <summary>The npc quest marks (GetNpcQuestState, its score FUN_1416d1100 and SortNpcQuestIds) over a small quest table.</summary>
public class NpcQuestMarksTests
{
    sealed class Ctx : IQuestContext
    {
        public readonly Dictionary<int, QuestRecord> Records = new();
        public QuestRecord Get(int questId) => Records.TryGetValue(questId, out var r) ? r : null;
        public long ServerTime { get; set; }
        public long ZoneOffset { get; set; }
        public long CharacterId { get; set; } = 1;
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
        public (bool Known, TermTag Tag) TermTag(string name) => (false, null);
        public bool? HasStamp(int questId) => null;
        public int? FieldId { get; set; }
        public int? ParentField(int field) => null;
        public int? DailyCount(int questId) => 0;
        public int? WeeklyCount(int questId) => 0;
    }

    static string Quest(int id, int type, int level, int start, int complete, int repeat = 0) =>
        $@"<quest id=""{id}""><basic questType=""{type}"" standardLevel=""{level}"" repeatable=""{repeat}"" /><require />" +
        $@"<start npc=""{start}"" /><complete npc=""{complete}"" /></quest>";

    static QuestTable Table(params string[] quests)
    {
        var t = new QuestTable("", null);
        t.AddQuests(XDocument.Parse("<ms2>" + string.Concat(quests) + "</ms2>"));
        t.BuildIndexes();
        return t;
    }

    static QuestRecord Active(int id, bool complete) => new(id, 1, 0, 0, 0, complete, false);

    [Test]
    public void AnNpcsQuestsSortByTypeRankThenLevel()
    {
        var t = Table(Quest(1, 1, 10, 500, 500), Quest(2, 0, 20, 500, 500), Quest(3, 2, 1, 500, 500), Quest(4, 0, 5, 500, 500),
                      Quest(5, 9, 1, 500, 600));
        var marks = new NpcQuestMarks(t);
        // epic (rank 1) by level, event (2), world (3), the others (4)
        Assert.That(marks.Quests(500), Is.EqualTo(new[] { 4, 2, 3, 1, 5 }));
        // a quest completed at another npc is listed there too
        Assert.That(marks.Quests(600), Is.EqualTo(new[] { 5 }));
        Assert.That(marks.HasQuests(600), Is.True);
        Assert.That(marks.HasQuests(700), Is.False);
        Assert.That(marks.Quests(700), Is.Empty);
    }

    [Test]
    public void TheHighestScoreWinsAndCompletableBeatsStartable()
    {
        var t = Table(Quest(1, 1, 10, 500, 500), Quest(2, 0, 20, 500, 500), Quest(3, 1, 10, 400, 500));
        var marks = new NpcQuestMarks(t);
        var ctx = new Ctx();
        var rules = new QuestRules(t, ctx);
        // two startable quests: the epic one scores 0x24 over the world one's 0x22
        Assert.That(marks.Get(500, ctx, rules), Is.EqualTo(new NpcQuestMarks.Mark(0, 2, 0)));
        // quest 3 in progress, completed at 500: 0x10 | 2 loses to the startable epic
        ctx.Records[3] = Active(3, false);
        Assert.That(marks.Score(500, 3, ctx, rules, out int state), Is.EqualTo(0x12));
        Assert.That(state, Is.EqualTo(1));
        Assert.That(marks.Get(500, ctx, rules).QuestId, Is.EqualTo(2));
        // its objectives done: 0x30 | 2 beats it
        ctx.Records[3] = Active(3, true);
        Assert.That(marks.Get(500, ctx, rules), Is.EqualTo(new NpcQuestMarks.Mark(2, 3, 1)));
        // at its start npc it is neither startable (taken) nor completable (another npc): no mark
        Assert.That(marks.Get(400, ctx, rules), Is.EqualTo(NpcQuestMarks.Mark.None));
    }

    [Test]
    public void HiddenKindsAreSkippedUnlessAllIsAsked()
    {
        var t = Table(Quest(2, 0, 20, 500, 500), Quest(1, 1, 10, 500, 500, repeat: 1));
        var marks = new NpcQuestMarks(t) { DisplayEpic = false };
        var ctx = new Ctx();
        var rules = new QuestRules(t, ctx);
        Assert.That(marks.Get(500, ctx, rules).QuestId, Is.EqualTo(1));
        Assert.That(marks.Get(500, ctx, rules, all: true).QuestId, Is.EqualTo(2));
        marks.DisplayRepeat = false;
        Assert.That(marks.Get(500, ctx, rules), Is.EqualTo(NpcQuestMarks.Mark.None));
        Assert.That(marks.IsDisplayed(99), Is.False);
    }

    [Test]
    public void AQuestOnlyNpcTalksThroughItsQuests()
    {
        // TalkAction::vfunc_1 (actionkeyobject.cpp:1385-1398): npc 500 has no kind and no npc talks, only quests
        var t = Table(Quest(1, 0, 1, 500, 600), Quest(2, 1, 1, 700, 600));
        var marks = new NpcQuestMarks(t);
        var ctx = new Ctx { Level = 1 };
        var rules = new QuestRules(t, ctx);
        bool NoTalk(int q, int s) => false;
        // quest 1 can start at 500: its mark talks (the job tutorial's first quest giver)
        Assert.That(marks.HasTalk(500, 0, false, ctx, rules, NoTalk), Is.True);
        // taken and not done: no mark at 500 (it completes at 600) and no talk 200 for it, so no talk
        ctx.Records[1] = Active(1, false);
        Assert.That(marks.HasTalk(500, 0, false, ctx, rules, NoTalk), Is.False);
        // HasTalkScriptForNpc: the started quest's start npc with a quest talk 200
        Assert.That(marks.HasTalk(500, 0, false, ctx, rules, (q, s) => q == 1 && s == 200), Is.True);
        Assert.That(marks.HasTalkScriptForNpc(600, ctx, (q, s) => true), Is.False, "600 completes it; it does not start it");
        // a function kind or npc talks talk on their own
        Assert.That(marks.HasTalk(500, 1, false, ctx, rules, NoTalk), Is.True);
        Assert.That(marks.HasTalk(500, 0, true, ctx, rules, NoTalk), Is.True);
        // the hidden kinds still count (GetNpcQuestState with all = 1)
        var hidden = new NpcQuestMarks(t) { DisplayEpic = false };
        ctx.Records.Clear();
        Assert.That(hidden.HasTalk(500, 0, false, ctx, rules, NoTalk), Is.True);
        // nothing at all: no talk
        Assert.That(marks.HasTalk(800, 0, false, ctx, rules, NoTalk), Is.False);
    }

    [Test]
    public void TheIconSuffixFollowsTheQuestKind()
    {
        Assert.That(NpcQuestMarks.Suffix(new QuestData { Type = 0 }), Is.EqualTo("_epic"));
        Assert.That(NpcQuestMarks.Suffix(new QuestData { Type = 2 }), Is.EqualTo("_event"));
        Assert.That(NpcQuestMarks.Suffix(new QuestData { Type = 1, Repeat = 1 }), Is.EqualTo("_repeat"));
        Assert.That(NpcQuestMarks.Suffix(new QuestData { Type = 6, Repeat = 1 }), Is.EqualTo("_repeat"));
        Assert.That(NpcQuestMarks.Suffix(new QuestData { Type = 1 }), Is.EqualTo(""));
        Assert.That(NpcQuestMarks.Suffix(new QuestData { Type = 9, Repeat = 1 }), Is.EqualTo(""));
        Assert.That(NpcQuestMarks.Suffix(null), Is.EqualTo(""));
    }
}
