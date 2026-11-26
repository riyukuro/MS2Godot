using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The trophy window and the trophy notices over every trophy and category of the data (needs MS2_DATA; the CI's two data
/// sets run it on both regions): UIAchieveDialog's movie shows each category's trophies with their progress, and every
/// reward of every grade gives the notice of its type.
/// </summary>
public class AchieveDataTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static UiModel Model()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = TestData.Packs(d);
            src = TestData.Movies(packs);
            tables = new GameTables(packs, "en");
        }
        var m = new UiModel(tables) { HasLocalPlayer = true };
        UiFixture.Apply(m);
        return m;
    }

    static AchieveProgress P(byte state, int cur, int start, long count, IReadOnlyDictionary<int, long> values = null) =>
        new(state, 0, cur, start, false, count, values ?? new Dictionary<int, long>());

    /// <summary>
    /// The data's every trophy in the book: a third in progress at the lowest grade (half its goal), a third done at the
    /// last grade with every reward waiting (each grade cleared at its own time), a third not started.
    /// </summary>
    static (AchieveBook Book, AchieveTexts Texts) Book(UiModel m)
    {
        var order = new List<AchieveData>();
        var table = AchieveData.LoadAll(packs, order);
        var book = new AchieveBook(table, order);
        book.Apply(new AchieveMessage.Reload());
        var rows = new List<(int, int, AchieveProgress)>();
        int i = 0;
        foreach (var d in table.Values.OrderBy(d => d.Id))
        {
            switch (i++ % 3)
            {
                case 0:
                    rows.Add((d.Id, d.FirstGrade, P(2, d.FirstGrade, d.FirstGrade, Math.Max(1, (d.Grade(d.FirstGrade)?.Condition.Goal ?? 2) / 2))));
                    break;
                case 1:
                    var times = Enumerable.Range(d.FirstGrade, d.MaxGrade - d.FirstGrade + 1).ToDictionary(g => g, g => 1_700_000_000L + g * 3600);
                    rows.Add((d.Id, d.FirstGrade, P(3, d.MaxGrade, d.FirstGrade, d.Grade(d.MaxGrade)?.Condition.Goal ?? 1, times)));
                    break;
                default:
                    rows.Add((d.Id, d.FirstGrade, P(2, d.FirstGrade, d.FirstGrade, 0)));
                    break;
            }
        }
        book.Apply(new AchieveMessage.Rows(rows));
        var texts = AchieveTexts.Load(packs, new GameStrings(packs, "en").Languages, s => s, s => s);
        texts.Data = book.DataOf;
        texts.Find = book.Find;
        texts.CommonByKey = tables.Common;
        texts.TableString = tables.StringTableText;
        texts.TitleName = tables.TitleName;
        texts.FeatureOn = m.IsFeatureEnabled;
        texts.Gender = () => 0;
        return (book, texts);
    }

    [Test]
    public void TheTrophyWindowShowsEveryCategoryWithItsTrophiesAndProgress()
    {
        var m = Model();
        var (book, texts) = Book(m);
        var cats = AchieveCategory.Load(packs);
        var c = new AchieveController(book, texts, cats);
        var (rt, p) = src.Open(c.Movie, r => { r.Budget = 50_000_000; r.FixedWallClock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc); });
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, m);
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();

        var rows = cats.GetRows();
        Assert.That(rows.Count(r => r.Tab == 1), Is.EqualTo(1), "one summary row");
        Assert.That(rows.Count(r => r.Tab == 2), Is.GreaterThanOrEqualTo(1), "an emblem row");
        Assert.That(rows.Count(r => r.Tab == 0), Is.GreaterThan(5), "the trophy rows");
        var unnamed = rows.Where(r => texts.TableString(0x4a, r.Id).Length == 0).Select(r => r.Id).ToList();
        Assert.That(unnamed, Is.Empty, "every category row has its label (string table 0x4a)");

        // the summary page: the score is the tally of the three categories
        Assert.That(book.Sum, Is.GreaterThan(0u));
        var listed = new HashSet<int>();
        var report = new List<string>();
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            Assert.That(c.OnCallback("onTabChange", new object[] { (double)i, (double)row.Id }), Is.True);
            for (int t = 0; t < 3; t++) p.Tick();
            if (row.Tab == 1)
            {
                Assert.That(h.Child("m_summaryTrophy"), Is.Not.Null, "the summary page");
                continue;
            }
            var list = h.Child("m_list", h.Child("m_heroTrophy"));
            Assert.That(list, Is.Not.Null, $"category {row.Id}: the trophy list");
            int shown = Rt.ToInt32(h.Get(h.Get(list, "dataProvider"), "length"));
            var opts = new AchieveController.FilterOptions { Survival = row.Tab == 2, CategoryTag = row.CategoryTag, ContentsTag = row.ContentsTag, Filter = 4 };
            var expect = book.GetListFromTable(0, 0, false, false).Where(a => c.RowFilter(a, "", opts)).ToList();
            report.Add($"{row.Id} {texts.TableString(0x4a, row.Id)}: {shown}");
            Assert.That(shown, Is.EqualTo(Math.Max(1, expect.Count)), $"category {row.Id}: the movie's rows");
            Assert.That(expect, Is.Not.Empty, $"category {row.Id} ({texts.TableString(0x4a, row.Id)}) shows trophies");
            foreach (var a in expect)
            {
                listed.Add(a.Id);
                Assert.That(row.Matches(a.Data), Is.True);
            }
            // the progress of an in-progress row is its count over the shown grade's goal
            if (expect.FirstOrDefault(a => a.State == 2 && a.IsStarted && !a.Data.Grades.Values.Any(g => g.Condition.Goal == 0)) is { } inProgress)
            {
                var r = c.Row(inProgress);
                if (r.ContainsKey("cur"))
                    Assert.That((r["cur"], r["max"]), Is.EqualTo((Math.Min(inProgress.Count, inProgress.GradeGoal((int)r["grade"])), inProgress.GradeGoal((int)r["grade"]))));
            }
        }
        Assert.That(rt.Errors, Is.Empty);
        TestContext.Progress.WriteLine($"trophy window: {rows.Count} category rows, {listed.Count} trophies listed of {book.GetList(0, 0, false, false).Count}; sum {book.Sum} ({string.Join("/", book.Tally)})");
        foreach (var line in report) TestContext.WriteLine("  " + line);

        // every trophy the window can list (not type 8, not a hidden or locked one) is in a category
        var hiddenOrLocked = new AchieveController.FilterOptions { Filter = 4 };
        var anyTrophy = book.GetListFromTable(0, 0, false, false).Where(a => a.Data.Type != 9 && c.RowFilter(a, "", hiddenOrLocked)).Select(a => a.Id).ToList();
        var outside = anyTrophy.Where(id => !listed.Contains(id)).ToList();
        TestContext.Progress.WriteLine($"trophies in no category: {outside.Count} ({string.Join(", ", outside.Take(10))})");
        Assert.That(outside.Count, Is.LessThan(anyTrophy.Count / 20 + 1), "nearly every listable trophy is in a category");
    }

    sealed class Count : IAchieveToasts
    {
        public readonly List<string> Log = new();
        public void ShowAchieve(string name, string icon, int achieveType) => Log.Add($"achieve|{name}|{icon}");
        public void ShowItemReward(int itemId, int grade) => Log.Add($"item|{itemId}|{grade}");
        public void ShowStatPoint(int points) => Log.Add($"stat|{points}");
        public void ShowSkillPoint(int points, int kind) => Log.Add($"skill|{points}|{kind}");
        public void ShowIconNotice(string icon, string msg, int effect) => Log.Add($"icon|{icon}|{msg}");
    }

    /// <summary>
    /// Every trophy name gets its object words as CStringTable::Load gives every table at load (stringtable.cpp:640): a
    /// name such as "$map:61000001$ Participant" shows the map's name in the window, the toast and the chat link.
    /// </summary>
    [Test]
    public void EveryTrophyNameShowsItsObjectWords()
    {
        var m = Model();
        var (book, texts) = Book(m);
        var token = new System.Text.RegularExpressions.Regex(@"\$(item|itemPlural|map|skill|npc|npcTitle|npcName|npcNamePlural|npcPlural|npcFunction|achieve|dungeonTitle):");
        var table = new GameStrings(packs, "en").Table("achievename", "name");
        int named = 0, worded = 0;
        var raw = new List<string>();
        foreach (var a in book.GetListFromTable(0, 0, false, false))
        {
            string name = texts.Name(a.Id);
            if (name.Length > 0) named++;
            if (table.TryGetValue(a.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), out var t) && t != null && token.IsMatch(t))
            {
                worded++;
                Assert.That(texts.GradeTitle(a.Id, 1), Does.Contain(name), $"trophy {a.Id}: the grade title");
            }
            if (token.IsMatch(name)) raw.Add($"{a.Id} {name}");
        }
        TestContext.Progress.WriteLine($"trophy names: {named} named, {worded} with object words in the table, {raw.Count} left raw");
        Assert.That(named, Is.GreaterThan(1000));
        Assert.That(worded, Is.GreaterThan(100), "the data's names hold object words");
        Assert.That(raw, Is.Empty);
    }

    [Test]
    public void EveryTrophyClearAndEveryRewardGivesItsNotice()
    {
        var m = Model();
        var (book, texts) = Book(m);
        texts.FeatureOn = _ => true;        // both reward features, so every type's branch runs
        var toasts = new Count();
        var lines = new List<(string Text, int Kind)>();
        var n = new AchieveNotices(texts)
        {
            Toasts = () => toasts,
            Line = (t, k) => lines.Add((t, k)),
            PlayerName = () => "Tester",
            SkillIcon = code => tables.Skills.GetSkillData(code)?.Icon,
        };

        // NotifyClear: every trophy but type 8 gives its toast and its chat line with the trophy's link
        int clears = 0, unnamedClears = 0;
        foreach (var a in book.GetList(0, 0, false, false))
        {
            toasts.Log.Clear(); lines.Clear();
            n.NotifyClear(a);
            if (a.Data.Type == 8) { Assert.That(toasts.Log.Count + lines.Count, Is.Zero, $"{a.Id}: type 8"); continue; }
            clears++;
            Assert.That(toasts.Log, Has.Count.EqualTo(1), $"{a.Id}: the toast");
            Assert.That(lines, Has.Count.EqualTo(1), $"{a.Id}: the line");
            Assert.That(lines[0].Kind, Is.EqualTo(TalkKind.SystemNotice));
            Assert.That(lines[0].Text, Does.Contain($"event:achieveTooltip,{a.Id},4,"), $"{a.Id}: the link");
            if (texts.Name(a.Id).Length == 0) unnamedClears++;
        }
        Assert.That(clears, Is.GreaterThan(1000));
        Assert.That(unnamedClears, Is.LessThan(clears / 50 + 1), "nearly every trophy has its name");

        // NotifyReward: every grade's reward by type
        var byType = new SortedDictionary<int, int>();
        var empty = new List<string>();
        foreach (var d in book.GetList(0, 0, false, false).Select(a => a.Data))
            foreach (var g in d.Grades.Values)
            {
                toasts.Log.Clear(); lines.Clear();
                n.NotifyReward(d.Id, g.Grade);
                var r = g.Reward;
                byType[r.Type] = byType.GetValueOrDefault(r.Type) + 1;
                if (texts.SpecialNotify(d.Id, g.Grade, out _, out _))
                {
                    Assert.That((toasts.Log.Count, lines.Count), Is.EqualTo((1, 1)), $"{d.Id}/{g.Grade}: the special notice");
                    continue;
                }
                int want = r.Type switch
                {
                    1 or 3 or 4 or 5 or 6 or 7 => 1,
                    9 or 10 or 11 => r.Extra.ContainsKey("male") ? 1 : 0,
                    12 => tables.Skills.GetSkillData(r.Code)?.Icon != null ? 1 : 0,
                    _ => 0,                 // a title (2), a palette (8, STILL-OPEN), etc (13) or none
                };
                Assert.That(toasts.Log, Has.Count.EqualTo(want), $"{d.Id}/{g.Grade}: type {r.Type}'s toast");
                if (want == 1 && r.Type is 5 or 6 or 7 or 9 or 10 or 11 or 12)
                {
                    Assert.That(lines.Select(l => l.Kind), Is.EqualTo(new[] { TalkKind.Auth }), $"{d.Id}/{g.Grade}: the unlock line");
                    if (toasts.Log[0].EndsWith("|", StringComparison.Ordinal) || lines[0].Text.Length == 0) empty.Add($"{d.Id}/{g.Grade}");
                }
                if (r.Type is 3 or 4) Assert.That(lines.Select(l => l.Kind), Is.EqualTo(new[] { TalkKind.SystemNotice }));
            }
        TestContext.Progress.WriteLine("reward types: " + string.Join(", ", byType.Select(kv => $"{AchieveData.RewardTypeName(kv.Key)}({kv.Key}) {kv.Value}")));
        Assert.That(empty, Is.Empty, "every unlock notice has its text");
        Assert.That(byType.GetValueOrDefault(1) + byType.GetValueOrDefault(3), Is.GreaterThan(100), "the item and stat rewards");
    }

    [Test]
    public void TheToastAndTheProgressLineReachTheirMovies()
    {
        var m = Model();
        var notify = new RewardNotifyController();
        var (rt, p) = src.Open(notify.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        var h = new UiHost(p, notify, m);
        Assert.That(h.Load(), Is.True);
        for (int i = 0; i < 30; i++) p.Tick();
        notify.ShowAchieve("A trophy", ".\\Data\\Resource\\Image\\Emblem\\Medal\\Icon\\medal.png", 2);
        for (int i = 0; i < 5; i++) p.Tick();
        var shown = h.Get(h.Entity, "m_notify");
        Assert.That(shown, Is.Not.Null);
        Assert.That(Rt.ToString(h.Invoke(shown, "getType")), Is.EqualTo("achieve"));
        Assert.That(rt.Errors, Is.Empty);

        var info = new MainPlayerInfoController();
        var (rt2, p2) = src.Open(info.Movie, r => r.Budget = 20_000_000);
        p2.StageW = 1280; p2.StageH = 720;
        var h2 = new UiHost(p2, info, m);
        Assert.That(h2.Load(), Is.True);
        for (int i = 0; i < 30; i++) p2.Tick();
        Assert.That(h2.Get(h2.Entity, "appendMessage"), Is.InstanceOf<AsFunction>(), "the movie's appendMessage");
        info.AppendMessage(" A trophy [25%]", "achieve");
        for (int i = 0; i < 5; i++) p2.Tick();
        Assert.That(rt2.Errors, Is.Empty);
    }
}
