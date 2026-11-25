using Ms2.Formats;
using Ms2.Formats.World;
using Ms2.Quest;
using Ms2.Ui;
using NUnit.Framework;
using QuestRecord = Ms2.Net.Packets.QuestRecord;
using Status = Ms2.Formats.World.QuestMaskTable.QuestStatus;

namespace Ms2.Ui.Tests;

/// <summary>
/// The quest mask's "complete" stage: a taken quest whose every condition is at its goal (CQuest::IsComplete) is complete for
/// FUN_140764480 (questmasktable.cpp:1198), so a range starting on stage 2 opens and a range ending on stage 1 closes there.
/// </summary>
[TestFixture]
public class QuestMaskStatusTests
{
    static QuestData Quest(int id, params long[] goals)
    {
        var q = new QuestData { Id = id };
        foreach (var g in goals) q.Conditions.Add(new QuestCondition { Goal = g });
        return q;
    }

    static QuestRecord Rec(int id, int state, params long[] progress) => new() { Id = id, State = state, Progress = progress };

    [Test]
    public void RecordShapesMapAsTheMaskCheckReadsThem()
    {
        var data = new Dictionary<int, QuestData> { [1] = Quest(1, 3, 1), [2] = Quest(2) };
        QuestData D(int id) => data.GetValueOrDefault(id);
        Assert.That(QuestMaskStatus.Of(null, D), Is.EqualTo(Status.Absent));
        Assert.That(QuestMaskStatus.Of(Rec(1, 1, 2, 1), D), Is.EqualTo(Status.Active));
        Assert.That(QuestMaskStatus.Of(Rec(1, 1, 3, 1), D), Is.EqualTo(Status.Completed));
        Assert.That(QuestMaskStatus.Of(Rec(1, 1, 3), D), Is.EqualTo(Status.Active), "a condition without a count counts 0 (CQuest::Init)");
        Assert.That(QuestMaskStatus.Of(Rec(1, 1, 3, 1, 0), D), Is.EqualTo(Status.Completed), "a count past the conditions is not read");
        Assert.That(QuestMaskStatus.Of(Rec(1, 2, 0, 0), D), Is.EqualTo(Status.Rewarded));
        Assert.That(QuestMaskStatus.Of(Rec(1, 0, 3, 1), D), Is.EqualTo(Status.Absent), "another state sets no flag");
        Assert.That(QuestMaskStatus.Of(Rec(2, 1), D), Is.EqualTo(Status.Completed), "no conditions: complete once taken");
        Assert.That(QuestMaskStatus.Of(Rec(9, 1), D), Is.EqualTo(Status.Absent), "a quest the data lacks is never kept");
        Assert.That(QuestMaskStatus.Of(Rec(1, 1, 3, 1), null), Is.EqualTo(Status.Active), "no quest data: taken, never complete");
    }

    // Lith (2000062) table rows: 13 is "60100000/1-60100005/1" (from the first taken until the second is complete), 3 is
    // "60100005/1-60100005/2" (from taken until past complete, i.e. rewarded).
    [Test]
    public void LithEntranceClosesWhenItsEndQuestIsCompleteAndTheCubeOnlyWhenRewarded()
    {
        var masks = new QuestMaskTable(
            System.Xml.Linq.XDocument.Parse("<ms2><field fieldID='2000062'>"
                + "<key id='13'><mask startQuestID='60100000' startQuestState='1' endQuestID='60100005' endQuestState='1'/></key>"
                + "<key id='3'><mask startQuestID='60100005' startQuestState='1' endQuestID='60100005' endQuestState='2'/></key></field></ms2>"),
            System.Xml.Linq.XDocument.Parse("<ms2/>"));
        Assume.That(masks.PortalMasks, Is.EqualTo(2), "the table xml shape");
        var data = new Dictionary<int, QuestData> { [60100000] = Quest(60100000, 1), [60100005] = Quest(60100005, 2) };
        var recs = new Dictionary<int, QuestRecord> { [60100000] = Rec(60100000, 2, 1) };
        Status S(int id) => QuestMaskStatus.Of(recs.GetValueOrDefault(id), data.GetValueOrDefault);
        bool Open(int portal) => masks.CheckPortal(2000062, portal, null, S);

        recs[60100005] = Rec(60100005, 1, 1);
        Assert.That((Open(13), Open(3)), Is.EqualTo((true, true)), "60100005 taken, not complete");
        recs[60100005] = Rec(60100005, 1, 2);
        Assert.That((Open(13), Open(3)), Is.EqualTo((false, true)), "60100005 complete: 13 closes, 3 stays open");
        recs[60100005] = Rec(60100005, 2, 2);
        Assert.That((Open(13), Open(3)), Is.EqualTo((false, false)), "60100005 rewarded");
    }

    /// <summary>FUN_140764480 as written (questmasktable.cpp:1198-1238), over its four flags.</summary>
    static int ClientState(bool absent, bool active, bool completed, bool rewarded, int mode)
    {
        switch (mode)
        {
            case 1: if (absent) return 2; if (active) return 0; if (completed || rewarded) return 1; break;
            case 2: if (absent || active) return 2; if (completed) return 0; if (rewarded) return 1; break;
            case 3: if (rewarded) return 0; if (absent || active || completed) return 2; break;
        }
        return 2;
    }

    /// <summary>CQuest::IsComplete as written (questmanager_1.cpp:106-113) over the Init-sized progress.</summary>
    static bool ClientComplete(QuestData d, long[] progress)
    {
        var p = new long[d.Conditions.Count];
        for (int i = 0; i < p.Length && i < progress.Length; i++) p[i] = progress[i];
        for (int i = 0; i < p.Length; i++) if (p[i] < d.Conditions[i].Goal) return false;
        return true;
    }

    static readonly Dictionary<string, (QuestTable Quests, QuestMaskTable Masks)> loaded = new();

    static (QuestTable Quests, QuestMaskTable Masks) Load(string env, string locale)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} is not set");
        if (loaded.TryGetValue(env, out var d)) return d;
        var packs = TestData.Packs(dir);
        var strings = new GameStrings(packs, "en");
        return loaded[env] = (QuestTable.Load(packs, strings.Languages, locale, _ => true), new QuestMaskTable(packs));
    }

    /// <summary>
    /// Every portal and npc mask row: each quest it names, in each record shape (none, taken short of a goal, taken with every
    /// goal met, rewarded), reads as the client's flags do in every mode; and the rows whose answer moves once a quest is
    /// complete are counted against the old reading (taken = never complete).
    /// </summary>
    [TestCase("MS2_DATA", "KR")]
    [TestCase("MS2_GMS2_DATA", "NA")]
    public void EveryMaskRowReadsTheCompleteStageAsTheClient(string env, string locale)
    {
        var (quests, masks) = Load(env, locale);
        var rows = masks.PortalRows.Select(r => ("portal", r.Key, r.Value)).Concat(masks.NpcRows.Select(r => ("npc", r.Key, r.Value))).ToList();
        Assert.That(rows, Is.Not.Empty);
        int checkedShapes = 0, missing = 0, completeOnTake = 0, stage2Starts = 0, stage1Ends = 0;
        var changed = new List<string>();
        var fails = new List<string>();
        var seen = new HashSet<int>();
        foreach (var (kind, key, mask) in rows)
        {
            foreach (var r in mask.Ranges) { if (r.StartState == 2) stage2Starts++; if (r.EndState == 1) stage1Ends++; }
            var ids = mask.Ranges.SelectMany(r => new[] { r.StartQuest, r.EndQuest }).Where(i => i != 0).Distinct().ToList();
            foreach (int id in ids)
            {
                var d = quests.Quest(id);
                if (d == null) { if (seen.Add(id)) missing++; continue; }
                var goals = d.Conditions.Select(c => c.Goal).ToArray();
                bool canBeShort = goals.Any(g => g > 0);
                if (seen.Add(id) && !canBeShort) completeOnTake++;
                var shapes = new List<QuestRecord> { null, Rec(id, 1, goals), Rec(id, 2, goals) };
                if (canBeShort) shapes.Add(Rec(id, 1, goals.Select(g => Math.Max(0, g - 1)).ToArray()));
                foreach (var rec in shapes)
                {
                    var s = QuestMaskStatus.Of(rec, quests.Quest);
                    bool absent = rec == null, active = rec?.State == 1 && !ClientComplete(d, rec.Progress),
                         completed = rec?.State == 1 && ClientComplete(d, rec.Progress), rewarded = rec?.State == 2;
                    for (int mode = 1; mode <= 3; mode++)
                    {
                        checkedShapes++;
                        int ours = QuestMaskTable.Eval(s, mode), client = ClientState(absent, active, completed, rewarded, mode);
                        if (ours != client && fails.Count < 20) fails.Add($"{kind} {key}: quest {id} state {rec?.State} mode {mode}: ours {ours}, client {client}");
                    }
                }
            }
            // every named quest taken with its goals met: the new reading against the old one (state 1 = taken)
            var met = ids.ToDictionary(i => i, i => quests.Quest(i) is { } q ? Rec(i, 1, q.Conditions.Select(c => c.Goal).ToArray()) : null);
            bool now = QuestMaskTable.Check(mask, i => QuestMaskStatus.Of(met.GetValueOrDefault(i), quests.Quest));
            bool old = QuestMaskTable.Check(mask, i => met.GetValueOrDefault(i) is { } q && quests.Quest(i) != null ? Status.Active : Status.Absent);
            if (now != old) changed.Add($"{kind} {key.Field}/{key.Key} {(now ? "opens" : "closes")}");
        }
        TestContext.Out.WriteLine($"{env}: {rows.Count} rows ({masks.PortalMasks} portal, {masks.NpcMasks} npc), {seen.Count} quests " +
                                  $"({missing} not in the quest data, {completeOnTake} complete once taken), {stage2Starts} stage-2 starts, " +
                                  $"{stage1Ends} stage-1 ends, {checkedShapes} shape x mode checks, {fails.Count} fail");
        TestContext.Out.WriteLine($"{changed.Count} rows answer otherwise with every quest complete: " + string.Join("; ", changed.Take(40)));
        Assert.That(fails, Is.Empty);
        Assert.That(changed, Is.Not.Empty, "rows end on the taken stage, so the complete stage moves some");
    }
}
