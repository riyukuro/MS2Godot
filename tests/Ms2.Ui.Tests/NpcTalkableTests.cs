using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Quest;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// TalkAction::vfunc_1's talk check (actionkeyobject.cpp:1385-1398, <see cref="NpcQuestMarks.HasTalk"/>) over the real data:
/// the job tutorial's quest givers have no function kind and no npc script, and talk through their quests. Runs on MS2_DATA
/// and, when set, MS2_GMS2_DATA.
/// </summary>
public class NpcTalkableTests
{
    sealed class Ctx : IQuestContext
    {
        public readonly Dictionary<int, QuestRecord> Records = new();
        public QuestRecord Get(int questId) => Records.TryGetValue(questId, out var r) ? r : null;
        public long ServerTime { get; set; }
        public long ZoneOffset { get; set; }
        public long CharacterId { get; set; } = 1;
        public int? Selector { get; set; } = 0;
        public int? Level { get; set; } = 1;
        public int? GearScore { get; set; }
        public int? JobGroup { get; set; }
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

    sealed record Data(string Name, QuestTable Quests, NpcTable Npcs, NpcScriptTable Scripts, QuestScriptTable QuestScripts);

    static readonly Dictionary<string, Data> loaded = new();

    static Data Load(string env, string locale)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} is not set");
        if (loaded.TryGetValue(env, out var d)) return d;
        var packs = new PackSet(dir);
        var strings = new GameStrings(packs, "en");
        return loaded[env] = new Data(env, QuestTable.Load(packs, strings.Languages, locale, _ => true), new NpcTable(packs),
            new NpcScriptTable(packs, strings, e => FeatureElements.IsAvailable(packs, e)),
            new QuestScriptTable(packs, (p, c, k) => FeatureElements.Collect(packs, p, c, k)));
    }

    static bool HasTalk(Data d, NpcQuestMarks marks, int npc, Ctx ctx) =>
        marks.HasTalk(npc, d.Npcs.Find(npc)?.Kind ?? 0, d.Scripts.Find(npc) != null, ctx, new QuestRules(d.Quests, ctx),
            (q, s) => d.QuestScripts.Find(q, s) != null);

    /// <summary>The wizard tutorial's first quest (20002286) starts at an npc with no kind and no npc script: a new level 1
    /// wizard (job group 30) talks to her through it.</summary>
    [TestCase("MS2_DATA", "KR")]
    [TestCase("MS2_GMS2_DATA", "NA")]
    public void TheWizardTutorialQuestGiverTalks(string env, string locale)
    {
        var d = Load(env, locale);
        var q = d.Quests.Quest(20002286);
        if (q == null) Assert.Ignore($"{env}: no quest 20002286");
        int npc = q.StartNpc;
        var row = d.Npcs.Find(npc);
        Assert.That(row, Is.Not.Null);
        Assert.That(row.Kind, Is.EqualTo(0), "no function kind");
        Assert.That(d.Scripts.Find(npc), Is.Null, "no npc script: the old check (kind or npc script) refused her");
        var marks = new NpcQuestMarks(d.Quests);
        Assert.That(HasTalk(d, marks, npc, new Ctx { JobGroup = 30 }), Is.True, "the quest she starts can start");
        Assert.That(HasTalk(d, marks, npc, new Ctx { JobGroup = 10 }), Is.False, "another job: no quest of hers, nothing to say");
    }

    /// <summary>
    /// Every quest of the table: its complete npc talks while the quest is done and not handed in (the completable mark), and
    /// its start npc talks while the quest runs when it has a talk 200 (HasTalkScriptForNpc). The counts of the quest-only
    /// npcs (no kind, no npc script) the old check refused are printed.
    /// </summary>
    [TestCase("MS2_DATA", "KR")]
    [TestCase("MS2_GMS2_DATA", "NA")]
    public void EveryQuestsNpcsTalkWhileItAsksThemTo(string env, string locale)
    {
        var d = Load(env, locale);
        var marks = new NpcQuestMarks(d.Quests);
        int complete = 0, start200 = 0, questOnly = 0, skippedNoNpc = 0;
        var fails = new List<string>();
        var questOnlyNpcs = new HashSet<int>();
        foreach (var q in d.Quests.Quests)
        {
            if (q.CompleteNpc != 0 && d.Npcs.Find(q.CompleteNpc) is { } row)
            {
                var ctx = new Ctx();
                ctx.Records[q.Id] = new QuestRecord(q.Id, 1, 0, 0, 0, true, false);
                complete++;
                if (row.Kind == 0 && d.Scripts.Find(row.Id) == null) questOnlyNpcs.Add(row.Id);
                if (!HasTalk(d, marks, row.Id, ctx)) fails.Add($"complete {q.Id} at {row.Id}");
            }
            else skippedNoNpc++;
            if (q.StartNpc != 0 && d.Npcs.Find(q.StartNpc) is { } s && d.QuestScripts.Find(q.Id, 200) != null)
            {
                var ctx = new Ctx();
                ctx.Records[q.Id] = new QuestRecord(q.Id, 1, 0, 0, 0, false, false);
                start200++;
                if (!marks.HasTalkScriptForNpc(s.Id, ctx, (x, y) => d.QuestScripts.Find(x, y) != null)) fails.Add($"talk 200 {q.Id} at {s.Id}");
            }
        }
        questOnly = questOnlyNpcs.Count;
        TestContext.Out.WriteLine($"{env}: quests {d.Quests.Quests.Count()}, complete-npc checks {complete}, talk-200 checks {start200}, " +
                                  $"no npc row {skippedNoNpc}, quest-only npcs (refused before) {questOnly}, fails {fails.Count}");
        Assert.That(fails, Is.Empty, string.Join("; ", fails.Take(20)));
        Assert.That(questOnly, Is.GreaterThan(0));
    }

    /// <summary>
    /// Each job's tutorial chain (job codes 1 to 110: the first quest with the job's require and no require quest, then its
    /// following quests while they keep the job's require): at each step a level-ready character of the job with the chain
    /// before it handed in talks to the quest's start npc, and with it done to its complete npc. Prints one line per job with
    /// the npcs the old check (kind or npc script) refused.
    /// </summary>
    [TestCase("MS2_DATA", "KR")]
    [TestCase("MS2_GMS2_DATA", "NA")]
    public void EveryJobsTutorialChainTalks(string env, string locale)
    {
        var d = Load(env, locale);
        var marks = new NpcQuestMarks(d.Quests);
        var fails = new List<string>();
        int jobsWithChain = 0;
        foreach (int job in new[] { 1, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110 })
        {
            var first = d.Quests.Quests.Where(q => q.Require.Job.Contains(job) && q.RequireQuest.Length == 0 && q.Require.Level <= 1
                                                  && q.StartNpc != 0 && d.QuestScripts.Find(q.Id, 100) != null)
                                       .OrderBy(q => q.Id).FirstOrDefault();
            if (first == null) { TestContext.Out.WriteLine($"{env} job {job}: no tutorial chain"); continue; }
            jobsWithChain++;
            var ctx = new Ctx { JobGroup = job };
            var steps = new List<string>();
            var refused = new SortedSet<int>();
            for (var q = first; q != null; )
            {
                ctx.Level = Math.Max(1, q.Require.Level);
                bool start = HasTalk(d, marks, q.StartNpc, ctx);
                if (!start) fails.Add($"job {job} quest {q.Id}: start npc {q.StartNpc}");
                ctx.Records[q.Id] = new QuestRecord(q.Id, 1, 0, 0, 0, true, false);
                bool done = q.CompleteNpc == 0 || HasTalk(d, marks, q.CompleteNpc, ctx);
                if (!done) fails.Add($"job {job} quest {q.Id}: complete npc {q.CompleteNpc}");
                foreach (int n in new[] { q.StartNpc, q.CompleteNpc })
                    if (n != 0 && (d.Npcs.Find(n)?.Kind ?? 0) == 0 && d.Scripts.Find(n) == null) refused.Add(n);
                steps.Add($"{q.Id}:{q.StartNpc}>{q.CompleteNpc}");
                ctx.Records[q.Id] = new QuestRecord(q.Id, 2, 0, 0, 0, true, false);
                q = q.Following.Select(id => d.Quests.Quest((int)id)).FirstOrDefault(x => x != null && x.Require.Job.Contains(job));
                if (steps.Count > 40) break;
            }
            TestContext.Out.WriteLine($"{env} job {job}: {steps.Count} quests, quest-only npcs (refused before) [{string.Join(" ", refused)}]: {string.Join(" ", steps)}");
        }
        Assert.That(fails, Is.Empty, string.Join("; ", fails.Take(20)));
        Assert.That(jobsWithChain, Is.GreaterThan(0));
    }
}
