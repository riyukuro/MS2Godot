using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The fame point board and the fame contents notice over every alliance, grade and fame log entry of the data (needs
/// MS2_DATA; the CI's two data sets run it on both regions): the texts the two read, the board movie's tabs and logs, and
/// the notice movie's banners of every base alliance, shown and drained in order.
/// </summary>
public class FameDataTests
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

    static string Plain(string html) => System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", "");

    static FameTexts Texts(FameBook book) => new() { Table = tables.Fame, Book = book, CommonByKey = tables.Common };

    /// <summary>Every alliance of fame.xml: its name and symbol, and at each grade's first and last point the grade, its name
    /// and the progress (GetGradeByPoint, GetFamePointProgress).</summary>
    [Test]
    public void EveryAllianceAndGradeHasItsNameAndProgress()
    {
        Model();
        var fame = tables.Fame;
        Assert.That(fame.AllianceIds, Is.Not.Empty, "fame.xml's alliances");
        Assert.That(fame.AllianceIds.Take(5).OrderBy(a => a), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }), "the five base alliances");
        var book = new FameBook();
        var texts = Texts(book);
        foreach (int a in fame.AllianceIds)
        {
            var grades = fame.Grades(a).ToList();
            Assert.That(grades, Is.Not.Empty, $"alliance {a}: grades");
            Assert.That(texts.AllianceName(a), Is.Not.Empty, $"alliance {a} ({FameTable.AllianceName(a)}): s_quest_alliance_ name");
            Assert.That(texts.AllianceSymbol(a), Is.Not.Empty, $"alliance {a}: fameDef symbol");
            Assert.That(fame.GetSeasonRequireQuest(a), Is.GreaterThan(0), $"alliance {a}: its season's requireQuest");
            int max = fame.GetMaxGrade(a);
            Assert.That(max, Is.EqualTo(grades[^1].Number));
            var line = new List<string>();
            for (int i = 0; i < grades.Count; i++)
            {
                var g = grades[i];
                int last = i + 1 < grades.Count ? grades[i + 1].RequirePoint - 1 : g.RequirePoint + 1_000_000;
                foreach (int point in new[] { g.RequirePoint, last })
                {
                    Assert.That(fame.GetGradeByPoint(a, point), Is.EqualTo(g.Number), $"alliance {a} point {point}");
                    Assert.That(texts.GradeName(a, point), Is.Not.Empty, $"alliance {a} grade {g.Number}: s_fame_grade_ name");
                    book.Apply(new UserEnvMessage.FameByAlliance(false, new Dictionary<ushort, int> { [(ushort)a] = point }));
                    var (cur, span, text) = texts.Progress(a);
                    if (g.Number == max)
                        Assert.That((cur, span, text), Is.EqualTo((1, 1, tables.Common("s_fame_point_board_max"))), $"alliance {a}: at the max grade");
                    else
                    {
                        Assert.That((cur, span), Is.EqualTo((point - g.RequirePoint, grades[i + 1].RequirePoint - g.RequirePoint)), $"alliance {a} point {point}");
                        Assert.That(text, Is.EqualTo(AchieveTexts.Group(cur) + " / " + AchieveTexts.Group(span)));
                    }
                }
                line.Add($"{g.Number} {texts.GradeName(a, g.RequirePoint)} from {g.RequirePoint}");
            }
            TestContext.Progress.WriteLine($"alliance {a} {texts.AllianceName(a)} ({texts.AllianceSymbol(a)}, season quest {fame.GetSeasonRequireQuest(a)}): {string.Join("; ", line)}");
        }
        Assert.That(tables.Common("s_fame_point_board_max"), Is.Not.Empty);
    }

    /// <summary>Every fameLog.xml entry reads its text from string table 67 (stringFameLog) with no raw word left.</summary>
    [Test]
    public void EveryFameLogEntryHasItsText()
    {
        var m = Model();
        var logs = tables.Fame.Logs;
        Assert.That(logs.Count, Is.GreaterThan(100), "fameLog.xml");
        var c = new FamePointBoardController { TableString = tables.StringTableText };
        var empty = new List<long>();
        var raw = new List<string>();
        int worded = 0;
        foreach (var l in logs)
        {
            string text = c.TableString(FamePointBoardController.LogTable, l.Id);
            if (string.IsNullOrEmpty(text)) { empty.Add(l.Id); continue; }
            if (text.Contains('$')) worded++;
            // the board's ReplaceObjectAndValueWords: the player's name and gender ($MyPCName, $male / $female)
            foreach (int gender in new[] { 0, 1 })
            {
                string shown = tables.ReservedWords.ReplaceValueWords(tables.ReservedWords.ReplaceObjectWords(text), "Tester", gender);
                if (shown.Contains('$') || shown.Length == 0) raw.Add($"{l.Id} {shown}");
            }
        }
        var byAlliance = logs.GroupBy(l => l.Alliance).OrderBy(g => g.Key).Select(g => $"{g.Key}: {g.Count()}");
        TestContext.Progress.WriteLine($"fame log: {logs.Count} entries ({string.Join(", ", byAlliance)}), {empty.Count} without text, {worded} with words, {raw.Count} raw after them");
        foreach (var r in raw.Take(10)) TestContext.Progress.WriteLine("  raw " + r);
        Assert.That(empty, Is.Empty, "every fame log id has its stringFameLog text");
        Assert.That(raw, Is.Empty);
        Assert.That(logs.All(l => tables.Fame.IsLogOfAlliance(l.Id, l.Alliance)));
    }

    /// <summary>
    /// The board with every alliance open, points in each and the data's whole fame log: one tab per alliance in fame.xml's
    /// order, each tab shows its alliance with as many log lines as the alliance has entries.
    /// </summary>
    [Test]
    public void TheBoardShowsEveryAllianceWithItsLog()
    {
        var m = Model();
        var fame = tables.Fame;
        var book = new FameBook();
        var points = new Dictionary<ushort, int>();
        foreach (int a in fame.AllianceIds) points[(ushort)a] = 15_000 * a;
        book.Apply(new UserEnvMessage.FameByAlliance(true, points));
        const long now = 1_800_000_000;
        var log = new Dictionary<long, int>();
        long t = now - 100L * 86_400;
        foreach (var l in fame.Logs.OrderBy(l => l.Id)) log[t += 600] = (int)l.Id;
        book.Apply(new UserEnvMessage.FameLog(true, log));

        var c = new FamePointBoardController
        {
            Fame = Texts(book),
            QuestRewarded = _ => true,
            ServerTime = () => now,
            TableString = tables.StringTableText,
            Words = t => tables.ReservedWords.ReplaceValueWords(tables.ReservedWords.ReplaceObjectWords(t), "Tester", 1),
        };
        var (rt, p) = src.Open(c.Movie, r => { r.Budget = 50_000_000; r.FixedWallClock = new DateTime(2027, 1, 15, 8, 0, 0, DateTimeKind.Utc); });
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, m);
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();

        Assert.That(c.Tabs, Is.EqualTo(fame.AllianceIds), "a tab per alliance, in fame.xml's order");
        var bar = h.Child("m_tab");
        Assert.That(bar, Is.Not.Null);
        Assert.That(Rt.ToInt32(h.Get(h.Get(bar, "dataProvider"), "length")), Is.EqualTo(c.Tabs.Count), "the tab bar's rows");
        Assert.That(c.Shown, Is.EqualTo(c.Tabs[0]), "selectedIndex 0 shows the first alliance");
        int lines = 0;
        for (int i = 0; i < c.Tabs.Count; i++)
        {
            int a = c.Tabs[i];
            h.Set(bar, "selectedIndex", i);
            for (int k = 0; k < 3; k++) p.Tick();
            Assert.That(c.Shown, Is.EqualTo(a), $"tab {i}: its onChangeTab");
            var contents = h.Get(h.Child("m_fameLog"), "m_contents");
            int shown = Rt.ToInt32(h.Get(contents, "numChildren"));
            int expect = fame.Logs.Count(l => l.Alliance == a);
            Assert.That(shown, Is.EqualTo(expect), $"alliance {a}: its log lines");
            Assert.That(Rt.ToString(h.Get(h.Child("m_allianceName"), "text")), Is.EqualTo(Plain(c.Fame.AllianceName(a))), $"alliance {a}: the name");
            Assert.That(Rt.ToString(h.Get(h.Child("m_allianceGrade"), "text")), Is.EqualTo(Plain(c.Fame.GradeName(a))), $"alliance {a}: the grade (htmlText)");
            var data = c.AllianceData(a);
            var msgs = ((List<object>)data["log"]).Cast<Dictionary<string, object>>().ToList();
            Assert.That(msgs.Where(l => ((string)l["msg"]).Contains('$') || ((string)l["date"]).Length == 0), Is.Empty, $"alliance {a}: each line worded and dated");
            TestContext.Progress.WriteLine($"tab {i}: {data["allianceName"]} {data["allianceGrade"]} {data["famePoint"]}, {shown} log lines" +
                (shown > 0 ? $", last \"{((Dictionary<string, object>)((List<object>)data["log"])[^1])["msg"]}\" {((Dictionary<string, object>)((List<object>)data["log"])[^1])["date"]}" : ""));
            lines += shown;
        }
        Assert.That(lines, Is.EqualTo(fame.Logs.Count(l => c.Tabs.Contains(l.Alliance))), "every log entry on its alliance's tab");
        Assert.That(rt.Errors, Is.Empty);
    }

    /// <summary>
    /// The notice movie over every base alliance's opening and ending banner: each reaches addNotify with its message, the
    /// movie plays it out and onHideComplete shows the next, and after the last the movie unloads.
    /// </summary>
    [Test]
    public void EveryAllianceBannerPlaysAndTheQueueDrains()
    {
        var m = Model();
        var book = new FameBook();
        book.Apply(new UserEnvMessage.FameByAlliance(true, new Dictionary<ushort, int> { [1] = 12_000, [2] = 40_000, [3] = 500_000 }));
        Runtime rt = null;
        UiHost h = null;
        var notified = new List<Dictionary<string, object>>();
        int loads = 0, unloads = 0;
        FameContentsNotifyController c = null;
        c = new FameContentsNotifyController
        {
            Fame = Texts(book),
            QuestAlliance = id => (int)(id / 10),
            LoadMovie = () =>
            {
                loads++;
                var (r, p) = src.Open(c.Movie, o => { o.Budget = 50_000_000; o.FixedWallClock = new DateTime(2027, 1, 15, 8, 0, 0, DateTimeKind.Utc); });
                rt = r;
                p.StageW = 1920; p.StageH = 1080;
                h = new UiHost(p, c, m);
                Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", r.Errors));
            },
            UnloadMovie = () => { unloads++; h.Unload(); },
        };
        c.Notified += n => notified.Add(n);
        var expect = new List<string>();
        for (int a = 1; a <= 5; a++)
        {
            c.QueueOpening(a * 10);
            c.QueueEnding(a * 10 + 1);
            expect.Add("Opening_" + FameTable.AllianceName(a));
            expect.Add("Ending_" + FameTable.AllianceName(a));
        }
        c.QueueOpening(10);                         // queued already: not again
        Assert.That(loads, Is.EqualTo(1));
        Assert.That(c.Pending.Count(), Is.EqualTo(10));
        int frames = 0;
        for (; frames < 30 * 900 && unloads == 0; frames++) h.Player.Tick();
        TestContext.Progress.WriteLine($"{frames} frames; notices: {string.Join("; ", notified.Select(n => $"{n["alliance"]} \"{n["msg"]}\" {n["current"]}/{n["max"]} {n["point"]}"))}");
        Assert.That(notified.Select(n => (string)n["alliance"]), Is.EqualTo(expect), "every banner in queue order");
        Assert.That(notified.All(n => ((string)n["msg"]).Length > 0 && !((string)n["msg"]).Contains('{')), "each with its message");
        Assert.That(unloads, Is.EqualTo(1), "the movie unloads after the last");
        Assert.That(c.Pending, Is.Empty);
        Assert.That(rt.Errors, Is.Empty);
        var dw = notified[1];
        int g = tables.Fame.GetGradeByPoint(1, 12_000), start = tables.Fame.GetGradePoint(1, g);
        Assert.That((dw["current"], dw["max"]), Is.EqualTo((12_000 - start, tables.Fame.GetNextGradePoint(1, g) - start)), "darkwind's ending: its progress in its grade");
    }
}
