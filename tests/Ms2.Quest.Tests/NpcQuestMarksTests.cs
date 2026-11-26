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

    /// <summary>
    /// The minimap's quest marks over every npc with a quest in a data set (KMS: MS2_DATA, GMS2: MS2_GMS2_DATA): for every
    /// displayed quest, each state's ResolveQuestIcon frame and layer is one the minimap movie draws, FUN_140763210's
    /// description and format keys are common strings, FUN_140763550's line is filled; at the complete npc the quest's
    /// own record gives state 1 (not complete) and state 2 (complete), and the complete one is the npc's minimap mark
    /// (0x30 beats any start) with its line in the tooltip.
    /// </summary>
    [TestCase("MS2_DATA", "KR")]
    [TestCase("MS2_GMS2_DATA", "NA")]
    public void EveryNpcQuestHasAMinimapIconPerState(string env, string region)
    {
        var data = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(data) || !System.IO.Directory.Exists(data)) Assert.Ignore(env + " is not set");
        using var packs = new Ms2.Formats.Pack.PackSet(data);
        var t = QuestTable.Load(packs, new[] { "en" }, region, _ => true);
        var marks = new NpcQuestMarks(t);
        var ctx = new Ctx { Level = 99 };
        var rules = new QuestRules(t, ctx);
        var suffixes = new[] { "_epic", "_world", "_repeat", "_event" };
        var frames = new HashSet<string>(suffixes.Select(x => "quest" + x).Concat(suffixes.Select(x => "quest_complete" + x))) { "quest_start" };
        var layers = new HashSet<string>(suffixes.Select(x => "quest" + x));
        var bad = new List<string>();
        var seen = new SortedDictionary<string, int>();
        int quests = 0, npcs = 0;
        var withQuests = new HashSet<int>();
        foreach (var q in t.Quests.OrderBy(q => q.Id))
        {
            int id = (int)q.Id;
            if (!marks.IsDisplayed(id)) continue;
            foreach (int npc in new[] { q.StartNpc, q.CompleteNpc })
                if (npc != 0 && marks.Quests(npc).Contains(id)) withQuests.Add(npc);
            if (q.CompleteNpc == 0 || !marks.Quests(q.CompleteNpc).Contains(id)) continue;
            quests++;
            ctx.Records.Clear();
            void Check(string what, int state, int level)
            {
                var (frame, layer) = marks.MinimapIcon(new NpcQuestMarks.Mark(state, id, q.Type), ctx);
                seen[what + " " + frame] = seen.GetValueOrDefault(what + " " + frame) + 1;
                if (!frames.Contains(frame) || !layers.Contains(layer)) bad.Add($"{id} {what}: frame {frame} layer {layer}");
                var (desc, format) = marks.GuideKeys(id, state, ctx, level);
                if (desc == null || string.IsNullOrEmpty(t.Common(desc))) bad.Add($"{id} {what}: desc {desc}");
                if (format == null || string.IsNullOrEmpty(t.Common(format))) bad.Add($"{id} {what}: format {format}");
                if (string.IsNullOrEmpty(marks.StateLine(id, state, ctx, level, t.Common))) bad.Add($"{id} {what}: no line");
            }
            Check("start", 0, 99);
            if (q.Require.Level > 0) Check("start below level", 0, 0);
            ctx.Records[id] = Active(id, false);
            Check("progress", 1, 99);
            if (marks.Score(q.CompleteNpc, id, ctx, rules, out int s1) == 0 || s1 != 1) bad.Add($"{id} progress at {q.CompleteNpc}: state {s1}");
            ctx.Records[id] = Active(id, true);
            Check("taken complete", 1, 99);
            Check("completable", 2, 99);
            var m = marks.GetMinimap(q.CompleteNpc, ctx, rules, 99, t.Common);
            if (m.Mark.State != 2 || m.Mark.QuestId != id) bad.Add($"{id} at {q.CompleteNpc}: mark {m.Mark}");
            else if (!m.HasQuest || !frames.Contains(m.Frame) || !layers.Contains(m.Layer) || string.IsNullOrEmpty(m.Guide)
                || !m.Text.Contains(marks.StateLine(id, 2, ctx, 99, t.Common)))
                bad.Add($"{id} at {q.CompleteNpc}: minimap {m.Frame} {m.Layer} guide '{m.Guide}'");
        }
        npcs = withQuests.Count;
        TestContext.Out.WriteLine($"{env}: {npcs} npcs with a displayed quest, {quests} quests checked");
        foreach (var (k, v) in seen) TestContext.Out.WriteLine($"  {k}: {v}");
        foreach (var b in bad.Take(40)) TestContext.Out.WriteLine("bad " + b);
        Assert.That(quests, Is.GreaterThan(1000));
        Assert.That(bad, Is.Empty, $"{bad.Count} bad");
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

    /// <summary>CMouseInteractNpc::FindTarget's pick test and FUN_14147ad10's ray test.</summary>
    [Test]
    public void TheClickPicksAsTheMouseInteract()
    {
        Assert.That(NpcMousePick.Picks(0, false, 4, false, true, false), Is.False, "no kind, no talks, no mark");
        Assert.That(NpcMousePick.Picks(0, false, 0, false, true, false), Is.True, "a quest it starts");
        Assert.That(NpcMousePick.Picks(0, false, 2, false, true, false), Is.True, "a quest it completes");
        Assert.That(NpcMousePick.Picks(3, false, 4, false, true, false), Is.True, "a function kind");
        Assert.That(NpcMousePick.Picks(0, true, 4, false, true, false), Is.True, "npc talks");
        Assert.That(NpcMousePick.Picks(3, true, 0, false, false, false), Is.False, "talking restricted");
        Assert.That(NpcMousePick.Picks(0, false, 4, true, false, true), Is.True, "held");
        var o = System.Numerics.Vector3.Zero;
        var x = new System.Numerics.Vector3(1, 0, 0);
        Assert.That(NpcMousePick.RayHitsSphere(o, x * 5, new(100, 10, 0), 10), Is.True, "passes at the radius");
        Assert.That(NpcMousePick.RayHitsSphere(o, x, new(100, 11, 0), 10), Is.False, "passes outside");
        Assert.That(NpcMousePick.RayHitsSphere(o, x, new(-100, 0, 0), 10), Is.False, "behind the origin");
        Assert.That(NpcMousePick.RayHitsSphere(o, x, new(-5, 0, 0), 10), Is.True, "the origin inside");
    }
}
