using System.Text;
using System.Xml.Linq;
using Maple2.PacketLib.Tools;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The field game score board with its dialogs loaded (FieldGameScoreBoardCli, uishadowexpeditionbossbar.cpp:75-186): an open while open
/// refreshes, the close closes the stored dialog, modes 2 and 3 place the dark stream score dialog (uidarkstreamscoredialog.cpp), and the
/// trigger scripts' ShadowExpedition actions and conditions on both data sets. Needs MS2_DATA (and MS2_GMS2_DATA for the GMS2 sweep).
/// </summary>
public class ShadowExpeditionTests
{
    /// <summary>The body the client reads (FUN_140f9c3a0): u8 0, u8 mode, the wide title, i32 current, i32 most.</summary>
    static byte[] OpenBody(byte mode, string title, int cur, int max)
    {
        var b = new List<byte> { 0, mode };
        b.AddRange(BitConverter.GetBytes((ushort)title.Length));
        b.AddRange(Encoding.Unicode.GetBytes(title));
        b.AddRange(BitConverter.GetBytes(cur));
        b.AddRange(BitConverter.GetBytes(max));
        return b.ToArray();
    }

    static byte[] UpdateBody(int cur) => new byte[] { 1 }.Concat(BitConverter.GetBytes(cur)).ToArray();
    static readonly byte[] CloseBody = { 2 };

    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static GameTables Tables()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        if (packs == null)
        {
            packs = TestData.Packs(d);
            src = TestData.Movies(packs);
            tables = new GameTables(packs, "en");
        }
        return tables;
    }

    static (UiHost H, Runtime RT) Load(UiController c)
    {
        var t = Tables();
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000, c.Libraries);
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, new UiModel(t));
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
        return (h, rt);
    }

    /// <summary>The tweens run to their end (both ctors turn the tween on).</summary>
    static void Settle(UiHost h)
    {
        for (int i = 0; i < 120; i++) h.Player.Tick();
    }

    /// <summary>
    /// The owner as the game scene holds it: FindObject gives the open instance, else a new one; the open loads its movie when
    /// <see cref="loaded"/> (the sweep runs without movies).
    /// </summary>
    sealed class Ui
    {
        public readonly FieldGameScoreBoard Board = new();
        public readonly List<string> Calls = new();
        public readonly Dictionary<UiController, UiHost> Hosts = new();
        public ShadowExpeditionBossBarController Bar;
        public DarkStreamScoreDialogController Score;
        readonly bool loaded;

        public Ui(bool loaded, Func<string, string> serverString = null)
        {
            this.loaded = loaded;
            Board.FindBossBar = () => Bar != null && !Bar.IsClosed ? Bar : (Bar = new ShadowExpeditionBossBarController());
            Board.FindScoreDialog = () => Score != null && !Score.IsClosed ? Score : (Score = new DarkStreamScoreDialogController());
            Board.Open = c =>
            {
                Calls.Add(c == Bar ? "open bar" : "open score");
                if (this.loaded) Hosts[c] = Load(c).H;
            };
            if (serverString != null) Board.ServerString = serverString;
        }

        public void Recv(byte[] body) => Board.OnPacket(FieldGameScoreBoardPacket.Read(new ByteReader(body)));

        public void Settle()
        {
            foreach (var h in Hosts.Values) ShadowExpeditionTests.Settle(h);
        }
    }

    [Test]
    public void AnOpenWhileOpenRefreshesAndTheCloseClosesTheBar()
    {
        Tables();
        var ui = new Ui(true, k => k == "$t$" ? "Boss" : "?");
        ui.Recv(OpenBody(1, "$t$", 3, 40));
        var bar = ui.Bar;
        Assert.That((ui.Board.Mode, ui.Board.Dialog, bar.IsClosed), Is.EqualTo((1, (IScoreBoardDialog)bar, false)));
        Assert.That((bar.Cur, bar.Max, bar.Title), Is.EqualTo((3, 40, "Boss")), "the title is the server string of the key");
        ui.Recv(UpdateBody(12));
        Assert.That(bar.Cur, Is.EqualTo(12));
        ui.Recv(OpenBody(1, "$t$", 20, 40));                         // open again while open: the same bar, no second open
        Assert.That((ui.Bar, bar.Cur), Is.EqualTo((bar, 20)));
        var gauge = bar.Gauge;
        var h = ui.Hosts[bar];
        ui.Recv(CloseBody);
        Assert.That((ui.Board.Dialog, bar.CloseAsked), Is.EqualTo(((IScoreBoardDialog)null, true)));
        ui.Settle();
        Assert.That(bar.IsClosed, Is.True);
        Assert.That(h.Get(gauge, "parent"), Is.Null, "vfunc_26 takes the gauge off the root");
        Assert.That(ui.Calls, Is.EqualTo(new[] { "open bar" }));
    }

    [Test]
    public void ModesTwoAndThreePlaceTheScoreDialog()
    {
        Tables();
        var ui = new Ui(true);
        ui.Recv(OpenBody(2, "", 5, 0));
        var score = ui.Score;
        Assert.That((ui.Board.Dialog, score.Score, score.OffsetY, score.Align), Is.EqualTo(((IScoreBoardDialog)score, 5, 0, 1)));
        ui.Recv(OpenBody(3, "", 9, 0));
        Assert.That((ui.Score, score.Score, score.OffsetY, score.Align), Is.EqualTo((score, 9, -80, 6)));
        ui.Recv(UpdateBody(11));
        Assert.That(score.Score, Is.EqualTo(11));
        Assert.That(ui.Calls, Is.EqualTo(new[] { "open score" }));
        Assert.That(ui.Bar, Is.Null, "no boss bar was asked for");
    }

    [Test]
    public void AnUnknownModeKeepsTheEarlierDialog()
    {
        Tables();
        var ui = new Ui(true);
        ui.Recv(OpenBody(4, "", 1, 1));
        Assert.That((ui.Board.Mode, ui.Board.Dialog, ui.Calls.Count), Is.EqualTo((4, (IScoreBoardDialog)null, 0)), "no dialog yet: nothing opens");
        ui.Recv(OpenBody(1, "", 1, 10));
        ui.Recv(CloseBody);
        ui.Settle();
        ui.Recv(OpenBody(1, "", 2, 10));
        var bar = ui.Bar;
        ui.Recv(OpenBody(0, "", 7, 9));                              // mode 0 keeps the bar, untouched, and refreshes it
        Assert.That((ui.Board.Mode, ui.Board.Dialog, bar.Cur, bar.Max), Is.EqualTo((0, (IScoreBoardDialog)bar, 2, 10)));
        Assert.That(ui.Calls, Is.EqualTo(new[] { "open bar", "open bar" }));
    }

    [Test]
    public void ADialogClosedBySomethingElseStaysTheStoredOne()
    {
        Tables();
        var ui = new Ui(true);
        ui.Recv(UpdateBody(5));
        ui.Recv(CloseBody);
        Assert.That((ui.Bar, ui.Score, ui.Calls.Count), Is.EqualTo(((ShadowExpeditionBossBarController)null, (DarkStreamScoreDialogController)null, 0)));
        ui.Recv(OpenBody(1, "", 1, 10));
        var bar = ui.Bar;
        bar.Close();                                                 // closed by something else: the board keeps it
        ui.Settle();
        ui.Recv(CloseBody);
        Assert.That(ui.Board.Dialog, Is.SameAs(bar));
        ui.Recv(UpdateBody(4));
        Assert.That(bar.Cur, Is.EqualTo(4), "the update still goes to it");
    }

    static string Text(UiHost h, object gauge, string name)
    {
        var n = h.Child(name, gauge);
        Assert.That(n, Is.Not.Null, name);
        var f = h.Has(n, "htmlText") ? n : h.Child("textField", n);
        return Rt.ToString(h.Get(f, "text"));
    }

    [Test]
    public void TheBossBarSitsAtItsOffsetAndANegativeCountShowsNoPercent()
    {
        foreach (var title in new[] { "Siege", "" })
        {
            var c = new ShadowExpeditionBossBarController();
            c.SetCurMax(7, 20);
            c.SetTitle(title);
            var (h, rt) = Load(c);
            c.SetCur(-3);
            c.Refresh();
            Assert.That(Text(h, c.Gauge, "m_progressText"),
                Is.EqualTo(ClientFormat.Format(tables.Common(ShadowExpeditionBossBarController.ProgressStringId) ?? "", "0")), "the percent clamps to 0");
            Settle(h);
            Assert.That(Rt.ToNumber(h.Get(h.Entity, "y")), Is.EqualTo(ShadowExpeditionBossBarController.TopOffset).Within(0.5), "align 1 (top centre) at 0x8c down");
            Assert.That(rt.Errors, Is.Empty, "title '" + title + "'");
        }
    }

    [Test]
    public void TheBossBarWithNoMostPointsSaysSoOnce()
    {
        var c = new ShadowExpeditionBossBarController();
        var notes = new List<string>();
        c.NotApplied = notes.Add;
        c.SetCurMax(3, 0);
        c.SetTitle("x");
        Load(c);
        Assert.That(notes.Count, Is.EqualTo(1));
        c.SetCur(5);                                                 // an update packet while the most is still 0
        c.Refresh();
        Assert.That(notes.Distinct().Count(), Is.EqualTo(1), "the same text each time, so the run's once-log prints one line");
    }

    [Test]
    public void TheScoreDialogsAlignAndOffsetLayItOut()
    {
        var c = new DarkStreamScoreDialogController();
        c.SetCur(12);
        var (h, rt) = Load(c);
        Settle(h);
        Assert.That(Rt.ToNumber(h.Get(h.Entity, "y")), Is.EqualTo(DarkStreamScoreDialogController.TopOffset).Within(0.5), "align 1 at offset + 0x82");
        double x1 = Rt.ToNumber(h.Get(h.Entity, "x"));
        c.SetOffsetY(-80);
        c.SetAlign(6);
        Assert.That(Rt.ToNumber(h.Get(h.Entity, "y")), Is.EqualTo(DarkStreamScoreDialogController.TopOffset - 80).Within(0.5));
        Assert.That(Rt.ToNumber(h.Get(h.Entity, "x")), Is.GreaterThan(x1), "align 6 is the top right");
        c.SetCur(-1);
        Assert.That(rt.Errors, Is.Empty);
    }

    static string Attr(XElement e, string name) =>
        (string)e.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? "";

    /// <summary>
    /// Every ShadowExpedition action and ShadowExpeditionReachPoint condition of the trigger xmls: the type is OpenBossGauge
    /// or CloseBossGauge; an open's maxGaugePoint is a number above 0 (the client divides by it) and its title, a server
    /// string key, a text or empty, is in the trigger strings (all but one key of the GMS2 data); the open the Maple2 server sends for
    /// it (mode 1, 0 points) reads and asks the boss bar with that title and most; every reach point is at most the most of its field's
    /// open, and its update reaches the bar.
    /// </summary>
    static void EveryUse(string variable)
    {
        using var data = new PackSet(RcGap.Data(variable));
        var strings = new GameTables(data, "en").TriggerStrings;
        int opens = 0, closes = 0, points = 0, files = 0, keyed = 0;
        var shapes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var uses = new List<(string Entry, string Field, List<XElement> Acts, List<XElement> Conds)>();
        foreach (var entry in data.List("Xml").Where(e => e.StartsWith("trigger/", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e, StringComparer.Ordinal))
        {
            var bytes = data.Read("Xml", entry);
            if (bytes.AsSpan().IndexOf("ShadowExpedition"u8) < 0) continue;
            XDocument doc;
            try { doc = PackXml.Parse(bytes); } catch (System.Xml.XmlException) { continue; }
            var acts = doc.Descendants("action").Where(a => Attr(a, "name").Equals("ShadowExpedition", StringComparison.OrdinalIgnoreCase)).ToList();
            var conds = doc.Descendants("condition").Where(a => Attr(a, "name").Equals("ShadowExpeditionReachPoint", StringComparison.OrdinalIgnoreCase)).ToList();
            if (acts.Count == 0 && conds.Count == 0) continue;
            uses.Add((entry, entry.Split('/')[1], acts, conds));
        }
        // The board is the field's: a reach point is tested against the gauge another trigger of the field opened.
        var most = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in uses)
        {
            files++;
            most.TryAdd(u.Field, 0);
            var ui = new Ui(false, k => strings.GetString(k));
            foreach (var a in u.Acts)
            {
                string type = Attr(a, "type").Trim();
                Assert.That(type, Is.EqualTo("OpenBossGauge").Or.EqualTo("CloseBossGauge"), u.Entry);
                if (type == "CloseBossGauge")
                {
                    Assert.DoesNotThrow(() => ui.Recv(CloseBody), u.Entry);
                    closes++;
                    continue;
                }
                string raw = Attr(a, "maxGaugePoint").Trim(), title = Attr(a, "title");
                Assert.That(int.TryParse(raw, out int max) && max > 0, Is.True, $"{u.Entry}: maxGaugePoint '{raw}'");
                most[u.Field] = Math.Max(most[u.Field], max);
                string shown = strings.GetString(title);
                if (shown.StartsWith("NO KEY", StringComparison.Ordinal)) missing.Add($"{u.Entry} {title}");
                ui.Recv(OpenBody(1, title, 0, max));
                Assert.That((ui.Board.Dialog, ui.Bar.Cur, ui.Bar.Max, ui.Bar.Title), Is.EqualTo(((IScoreBoardDialog)ui.Bar, 0, max, shown)), u.Entry);
                Assert.That(ui.Calls[^1], Is.EqualTo("open bar"), u.Entry);
                opens++;
                if (title.StartsWith('$') && title.EndsWith('$')) keyed++;
                string shape = (title.Length == 0 ? "no title" : title.StartsWith('$') ? "key" : "text") + ", max " + max;
                shapes[shape] = shapes.GetValueOrDefault(shape) + 1;
            }
        }
        foreach (var u in uses)
            foreach (var c in u.Conds)
            {
                string raw = Attr(c, "point").Trim();
                int m = most[u.Field];
                Assert.That(int.TryParse(raw, out int p) && p > 0, Is.True, $"{u.Entry}: point '{raw}'");
                Assert.That(m, Is.GreaterThan(0), $"{u.Entry}: a reach point on a field with no open");
                Assert.That(p, Is.LessThanOrEqualTo(m), $"{u.Entry}: point {p} of most {m}");
                var ui = new Ui(false);
                ui.Recv(OpenBody(1, "", 0, m));
                ui.Recv(UpdateBody(p));
                Assert.That(ui.Bar.Cur, Is.EqualTo(p), u.Entry);
                points++;
                string shape = $"point {p} of {m} ({p * 100 / m}%)";
                shapes[shape] = shapes.GetValueOrDefault(shape) + 1;
            }
        var fields = new SortedSet<string>(most.Keys, StringComparer.OrdinalIgnoreCase);
        TestContext.Progress.WriteLine($"{variable}: {opens} OpenBossGauge ({keyed} keyed titles), {closes} CloseBossGauge, {points} ShadowExpeditionReachPoint "
            + $"in {files} files of {fields.Count} fields ({string.Join(", ", fields)}); {string.Join("; ", shapes.Select(kv => $"{kv.Key}: {kv.Value}"))}; "
            + $"titles with no trigger string: {(missing.Count == 0 ? "none" : string.Join(", ", missing))}");
        // The GMS2 data's stringtrigger.xml has no 02000543_BF__GAUGE__0: its bar shows GetString's "NO KEY : $key$", as the client's.
        Assert.That(missing, Is.EqualTo(variable == "MS2_GMS2_DATA" ? new[] { "trigger/02000543_bf/gauge.xml $02000543_BF__GAUGE__0$" } : Array.Empty<string>()));
        Assert.That(opens, Is.GreaterThan(0));
        Assert.That(closes, Is.GreaterThan(0));
        Assert.That(points, Is.GreaterThan(0));
    }

    [Test] public void EveryKmsUse() => EveryUse("MS2_DATA");
    [Test] public void EveryGms2Use() => EveryUse("MS2_GMS2_DATA");
}
