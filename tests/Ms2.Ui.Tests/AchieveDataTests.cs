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
        texts.PaletteColors = tables.ColorPalettes.Rows;
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

    /// <summary>
    /// FUN_1418ab000 (uiachievedialog.cpp:872): an item-coloring trophy's row gives palette0.. for the reward palette's
    /// colours of its start grade, and getTooltipColor (:509) of each gives the colour's name and description text
    /// tooltip. Only the GMS2 data ties colours to trophies; a hand-made palette covers both data sets.
    /// </summary>
    [Test]
    public void AColoringTrophyRowListsItsColoursAndTheirTooltips()
    {
        var m = Model();
        var (book, texts) = Book(m);
        texts.FeatureOn = _ => true;
        var c = new AchieveController(book, texts, AchieveCategory.Load(packs))
        {
            Colors = (palette, sn) => tables.ColorPalettes.BySN((int)palette, (int)sn),
        };
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 50_000_000);
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, m);
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));

        int rowsWithColours = 0, colours = 0;
        var untipped = new List<string>();
        foreach (var a in book.GetListFromTable(0, 0, false, false).Where(a => a.Reward is { Type: 8 }))
        {
            var row = c.Row(a);
            var want = texts.RewardColors(a.Id, a.StartGrade, a.Reward.Code).ToList();
            var got = row.Keys.Where(k => k.StartsWith("palette", StringComparison.Ordinal)).ToList();
            Assert.That(got, Is.EqualTo(Enumerable.Range(0, want.Count).Select(i => $"palette{i}")), $"{a.Id}: the palette members");
            if (want.Count > 0) rowsWithColours++;
            for (int i = 0; i < want.Count; i++)
            {
                colours++;
                var o = (Dictionary<string, object>)row[$"palette{i}"];
                Assert.That((o["paletteID"], o["realColor"], o["visibleColor"], o["ch2Color"], o["colorSN"]),
                    Is.EqualTo(((object)a.Reward.Code, (object)want[i].Ch0, (object)want[i].Visible, (object)want[i].Ch2, (object)want[i].ColorSN)));
                Assert.That(o.ContainsKey("realColor2"), Is.EqualTo(want[i].Show2Color));
                var tip = c.GetUserValue("getTooltipColor", new object[] { 0.0, 0.0, 0.0, 0.0, h.ToAs(o) }) as AsTooltip;
                string name = texts.TableString(0x48, want[i].StringKey);
                if (tip == null || !((string)tip["text"]).Contains(name, StringComparison.Ordinal) || name.Length == 0)
                    untipped.Add($"{a.Id} colour {want[i].ColorSN}");
                else Assert.That(tip["tooltipAlign"], Is.EqualTo(1));
            }
        }
        TestContext.Progress.WriteLine($"coloring trophy rows: {rowsWithColours} with {colours} colours, {untipped.Count} without a tooltip: {string.Join(", ", untipped.Take(5))}");
        bool achievePalettes = packs.Exists("Xml", "table/na/colorpalette_achieve.xml");
        Assert.That(colours, achievePalettes ? Is.GreaterThan(100) : Is.Zero, "the coloring rows' colours");
        Assert.That(untipped, Is.Empty, "every colour has its tooltip");

        // a hand-made palette: a colour without a name key, or whose name is empty, has no tooltip; show2color adds the
        // second colours (both ch2) and keeps visibleColor the palette colour
        var hand = Ms2.Ui.Items.ColorPaletteTable.Parse(System.Xml.Linq.XElement.Parse(
            @"<ms2><colorPalette id=""5""><color ch0=""ff010203"" ch2=""ff070809"" palette=""ff0a0b0c"" show2color=""1"" colorSN=""1"" stringKey=""9"" />
              <color ch0=""ff010203"" colorSN=""2"" /><color ch0=""ff010203"" colorSN=""3"" stringKey=""9"" /></colorPalette></ms2>"));
        c.Colors = (palette, sn) => hand.BySN((int)palette, (int)sn);
        texts.TableString = (t, id) => t == 0x48 ? (id == 9 ? "Nine" : "") : t == 0x49 ? "about nine" : tables.StringTableText(t, id);
        object Tip(int sn) => c.GetUserValue("getTooltipColor", new object[] { 0.0, 0.0, 0.0, 0.0,
            h.ToAs(new Dictionary<string, object> { ["paletteID"] = 5u, ["colorSN"] = sn }) });
        var t1 = (AsTooltip)Tip(1);
        Assert.That(t1["text"], Is.EqualTo(Ms2.Quest.ClientFormat.Format(tables.Common("s_beauty_tooltip_color_desc"), "Nine", "about nine") + "\n"));
        Assert.That(t1["tooltip"], Is.EqualTo("export_common_tooltip_tooltipText"));
        Assert.That(Tip(2), Is.Null, "no name key");
        Assert.That(Tip(4), Is.Null, "no such colour");
        texts.TableString = (t, id) => t == 0x48 ? "" : tables.StringTableText(t, id);
        Assert.That(Tip(3), Is.Null, "an empty name");
        Assert.That(c.GetUserValue("getTooltipColor", new object[] { 0.0, 0.0 }), Is.Null, "too few arguments");
        Assert.That(rt.Errors, Is.Empty);
    }

    /// <summary>
    /// ASTooltip::SetAchieveTooltip (astooltip.cpp:4937) over every grade of every trophy: the achieve tooltip class, align
    /// 7, the grade, name, requirement, reward and medal icon, and the state of the player's trophy (complete with the
    /// grade's date when the grade has a clear time, else in progress; none when the player lacks it). The trophy window's
    /// slot asks for it through its own getTooltipData (getTooltipAchieve, uiachievedialog.cpp:536) and gets the comment too.
    /// </summary>
    [Test]
    public void EveryTrophyGradeGivesItsAchieveTooltip()
    {
        var m = Model();
        var (book, texts) = Book(m);
        texts.FeatureOn = _ => true;
        int tips = 0, complete = 0, progress = 0;
        foreach (var d in book.GetListFromTable(0, 0, false, false).Select(a => a.Data).Where(d => d != null))
            foreach (int g in d.GradeConditions.Keys.OrderBy(k => k))
            {
                var t = texts.Tooltip(d.Id, g);
                Assert.That(t, Is.Not.Null, $"{d.Id} grade {g}");
                Assert.That((t["tooltip"], t["tooltipAlign"], t["grade"], t["title"], t["desc"], t["reward"], t["imgPath"]),
                    Is.EqualTo(((object)"export_common_tooltip_kindsAchieve", (object)7, (object)g, (object)texts.Name(d.Id),
                                (object)texts.RequirementText(d.Id, g, false), (object)texts.RewardText(d.Id, g), (object)d.IconPath)));
                var a = book.Find(d.Id);
                int value = (int)a.GetValue(g);
                Assert.That(t["state"], Is.EqualTo(value != 0
                    ? Ms2.Quest.ClientFormat.Format("{0} ({1})", texts.Common(0x780), AchieveTexts.RecordedDate(a, g))
                    : texts.Common(0x77f)), $"{d.Id} grade {g}");
                tips++;
                if (value != 0) complete++; else progress++;
            }
        TestContext.Progress.WriteLine($"achieve tooltips: {tips} grades, {complete} complete, {progress} in progress");
        Assert.That(complete, Is.GreaterThan(100));
        Assert.That(progress, Is.GreaterThan(100));
        Assert.That(texts.Tooltip(1, 1), Is.Null, "no such trophy");
        int some = book.GetListFromTable(0, 0, false, false).First(a => a.Data != null).Id;
        var find = texts.Find;
        texts.Find = _ => null;
        Assert.That(texts.Tooltip(some, 1)?.ContainsKey("state"), Is.False, "not the player's trophy: no state");
        texts.Find = find;

        // the trophy window: its slot class's getTooltipData asks getTooltipAchieve(m_id, m_grade)
        var c = new AchieveController(book, texts, AchieveCategory.Load(packs));
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 50_000_000);
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, m);
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        var (done, doneGrade) = book.GetListFromTable(0, 0, false, false).Where(a => a.Data != null)
            .SelectMany(a => a.Data.GradeConditions.Keys.Select(g => (a, g))).First(x => x.a.GetValue(x.g) != 0);
        var slot = h.RT.Construct(h.RT.GetClass("achieve_detail_slot"), Array.Empty<object>());
        h.Invoke(h.Entity, "addChild", slot);
        h.Invoke(slot, "setData", h.ToAs(new Dictionary<string, object> { ["id"] = (uint)done.Id, ["grade"] = doneGrade, ["complete"] = true }));
        var tip = h.Invoke(slot, "getTooltipData");
        var want = texts.Tooltip(done.Id, doneGrade);
        foreach (var k in new[] { "tooltip", "title", "desc", "reward", "imgPath", "state" })
            Assert.That(Rt.ToString(h.Get(tip, k)), Is.EqualTo(want[k].ToString()), k);
        Assert.That(Rt.ToNumber(h.Get(tip, "grade")), Is.EqualTo(doneGrade));
        Assert.That(Rt.ToString(h.Get(tip, "comment")), Is.EqualTo(texts.Common(0x781)));
        Assert.That(texts.Common(0x781), Is.Not.Empty, "s_achieve_tooltip_comment");
        Assert.That(c.GetUserValue("GETTOOLTIPACHIEVE", new object[] { (double)done.Id, 1.0 }), Is.Not.Null, "compared without case");
        Assert.That(c.GetUserValue("getTooltipAchieve", new object[] { 1.0, 1.0 }), Is.Null, "no such trophy");
        Assert.That(rt.Errors, Is.Empty);
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
        var unnamedSkills = new List<string>();
        int dynamicActions = 0;
        int paletteNotices = 0;
        var unnamedColors = new List<string>();
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
                    8 => texts.RewardColors(d.Id, g.Grade, r.Code).Count(),   // one per colour of this trophy and grade
                    _ => 0,                 // a title (2), etc (13) or none
                };
                Assert.That(toasts.Log, Has.Count.EqualTo(want), $"{d.Id}/{g.Grade}: type {r.Type}'s toast");
                if (want == 1 && r.Type is 5 or 6 or 7 or 9 or 10 or 11 or 12)
                {
                    Assert.That(lines.Select(l => l.Kind), Is.EqualTo(new[] { TalkKind.Auth }), $"{d.Id}/{g.Grade}: the unlock line");
                    if (toasts.Log[0].EndsWith("|", StringComparison.Ordinal) || lines[0].Text.Length == 0) empty.Add($"{d.Id}/{g.Grade}");
                }
                if (r.Type is 3 or 4) Assert.That(lines.Select(l => l.Kind), Is.EqualTo(new[] { TalkKind.SystemNotice }));
                if (r.Type == 8 && want > 0)
                {
                    // each colour's notice and Auth line name it (string table 0x48), and the reward text has one line per colour
                    paletteNotices += want;
                    Assert.That(lines.Select(l => l.Kind), Is.EqualTo(Enumerable.Repeat(TalkKind.Auth, want)), $"{d.Id}/{g.Grade}: the coloring lines");
                    var names = texts.RewardColors(d.Id, g.Grade, r.Code).Select(c => texts.TableString(0x48, c.StringKey)).ToList();
                    for (int i = 0; i < want; i++)
                        if (names[i].Length == 0 || !lines[i].Text.Contains(names[i], StringComparison.Ordinal)
                            || !toasts.Log[i].Contains(names[i], StringComparison.Ordinal))
                            unnamedColors.Add($"{d.Id}/{g.Grade} colour {i}");
                    if (texts.FindDesc(d.Id) != null && texts.FindSpecialReward(d.Id, g.Grade) == null)
                        Assert.That(texts.RewardText(d.Id, g.Grade).Split('\n'), Has.Length.EqualTo(want), $"{d.Id}/{g.Grade}: a reward line per colour");
                }
                if (r.Type == 12)
                {
                    // the dynamic action's name (string table 4 skillName) in the notice line and the reward text
                    dynamicActions++;
                    string skill = texts.TableString(4, r.Code);
                    if (skill.Length == 0 || (want == 1 && !lines[0].Text.Contains(skill, StringComparison.Ordinal))
                        || (texts.FindSpecialReward(d.Id, g.Grade) == null && texts.RewardText(d.Id, g.Grade) is { Length: > 0 } rewardText
                            && !rewardText.Contains(skill, StringComparison.Ordinal)))
                        unnamedSkills.Add($"{d.Id}/{g.Grade} skill {r.Code}");
                }
            }
        TestContext.Progress.WriteLine("reward types: " + string.Join(", ", byType.Select(kv => $"{AchieveData.RewardTypeName(kv.Key)}({kv.Key}) {kv.Value}")));
        Assert.That(empty, Is.Empty, "every unlock notice has its text");
        TestContext.Progress.WriteLine($"dynamic action rewards: {dynamicActions}, {unnamedSkills.Count} without the skill's name: {string.Join(", ", unnamedSkills.Take(5))}");
        Assert.That(dynamicActions, Is.GreaterThan(0), "the dynamic action rewards");
        Assert.That(unnamedSkills, Is.Empty, "every dynamic action reward names its skill");
        // only the GMS2 data ties palette colours to trophies (table/na/colorpalette_achieve.xml's achieveID rows)
        TestContext.Progress.WriteLine($"palette reward notices: {paletteNotices}, {unnamedColors.Count} unnamed: {string.Join(", ", unnamedColors.Take(5))}");
        bool achievePalettes = packs.Exists("Xml", "table/na/colorpalette_achieve.xml");
        Assert.That(paletteNotices, achievePalettes ? Is.GreaterThan(100) : Is.Zero, "the palette reward notices");
        Assert.That(unnamedColors, Is.Empty, "every palette notice names its colour");
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
