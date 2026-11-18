using System.Text;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The display list's change marks (AsDisplayObject.MarkChanged), which let the renderer's sync skip what did not change:
/// on every movie of Resource/Gfx, over frames of ticks, mouse moves and clicks, every object whose drawn state changed
/// in a frame is one a sync following the marks reaches and draws again. The drawn state is what the sync and its nodes
/// read: the object's own fields, its children in order, its text, drawing, bitmap or button state, and what it is drawn
/// through (its matrix on screen and the blends above it).
/// </summary>
public class SyncMarksTests
{
    static PackSet packs;
    static MovieSource src;

    static void Packs()
    {
        if (packs != null) return;
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs = new PackSet(d);
        src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
    }

    static string S(object o) => o == null ? "null" : o is string str ? str : Rt.IsNullish(o) ? "nullish" : Rt.ToString(o);
    static int Id(object o) => o == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);

    /// <summary>What the sync reads from the object, as one string.</summary>
    static string Drawn(AsDisplayObject o, Player p, Mat global, string blends)
    {
        var m = o.Matrix; var g = global; var c = o.Color;
        var b = new StringBuilder();
        b.Append($"m {m.A},{m.B},{m.C},{m.D},{m.Tx},{m.Ty} g {g.A},{g.B},{g.C},{g.D},{g.Tx},{g.Ty} c {c} v {o.IsVisible} sym {Id(o.Symbol)} scope {Id(o.Scope)}");
        b.Append($" depth {o.Depth} clip {o.ClipDepth} mask {Id(o.MaskObject)} blend {blends} f {Id(o.Filters)} pf {Id(o.PlaceFilters)} top {o == p.DrawnOnTop}");
        if (o is AsContainer k) { b.Append(" kids"); foreach (var x in k.Children) b.Append(' ').Append(Id(x)); }
        if (o is AsTextField t)
        {
            var f = t.Format;
            b.Append($" text {t.Text} html {t.Html} {t.IsHtml} box {t.Box} fmt {Id(f)} {S(f?.font)} {S(f?.size)} {S(f?.color)} {S(f?.align)} {S(f?.leading)}");
            b.Append($" ver {t.Version} va {t.VerticalAlign} as {t.TextAutoSize} ww {t.wordWrap} ml {t.multiline} aa {t.antiAliasType}");
        }
        var gr = (o as AsSprite)?.GraphicsOrNull ?? (o as AsShape)?.GraphicsOrNull;
        if (gr != null)
        {
            b.Append($" gfx {gr.Ops.Count} {gr.Bounds}");
            foreach (var (op, v, col, a) in gr.Ops) b.Append($" {op} {string.Join(",", v)} {col} {a}");
        }
        if (o is AsBitmap bm) b.Append($" bmp {Id(bm.bitmapData)} {bm.bitmapData?.Source} {bm.bitmapData?.width} {bm.bitmapData?.height}");
        if (o is AsSimpleButton btn) { b.Append($" state {btn.State}"); foreach (var x in btn.Drawn) b.Append(' ').Append(Id(x)); }
        return b.ToString();
    }

    static IEnumerable<AsDisplayObject> Below(AsDisplayObject o) =>
        o is AsContainer c ? c.Children : o is AsSimpleButton b ? b.Drawn : Enumerable.Empty<AsDisplayObject>();

    sealed class Checker
    {
        readonly Dictionary<AsDisplayObject, string> last = new(ReferenceEqualityComparer.Instance);
        public int Objects, Changed, Frames;
        public readonly List<string> Missed = new();

        /// <summary>One frame: every changed object must be reached by the marked walk; then the marks are cleared.</summary>
        public void Frame(Player p, string movie, int frame)
        {
            Frames++;
            var reached = new HashSet<AsDisplayObject>(ReferenceEqualityComparer.Instance);
            void Walk(AsDisplayObject o, bool deep)
            {
                deep |= o.ChangedDeep;
                if (deep || o.ChangedSelf) reached.Add(o);
                foreach (var k in Below(o).ToList())
                    if (deep || k.ChangedBelow) Walk(k, deep);
            }
            Walk(p.Stage, false);
            void Check(AsDisplayObject o, Mat parentGlobal, string blends, string path)
            {
                var global = o.Matrix.Then(parentGlobal);
                blends = blends + "/" + o.Blend;
                var now = Drawn(o, p, global, blends);
                Objects++;
                if (!last.TryGetValue(o, out var before) || before != now)
                {
                    Changed++;
                    if (frame > 0 && !reached.Contains(o) && Missed.Count < 20)
                        Missed.Add($"{movie} frame {frame} {path}: {Diff(before, now)}");
                }
                last[o] = now;
                o.ClearMarks();
                foreach (var k in Below(o).ToList()) Check(k, global, blends, path + "/" + (k.InstanceName ?? k.Class?.LocalName));
            }
            Check(p.Stage, Mat.Identity, "", "stage");
        }

        static string Diff(string a, string b)
        {
            if (a == null) return "new object, unmarked: " + b[..Math.Min(200, b.Length)];
            int i = 0; while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
            int from = Math.Max(0, i - 40);
            return $"...{a[from..Math.Min(a.Length, i + 60)]} -> ...{b[from..Math.Min(b.Length, i + 60)]}";
        }
    }

    [Test]
    public void EveryChangedObjectIsMarked()
    {
        Packs();
        var check = new Checker();
        int movies = 0, failed = 0;
        foreach (var key in packs.List("Resource/Gfx").Where(n => n.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase))
                     .Select(n => Path.GetFileNameWithoutExtension(n)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Player p;
            try
            {
                (_, p) = src.Open(key, r => r.Budget = 20_000_000);
                p.StageW = 1280; p.StageH = 720;
                if (!p.Start()) { failed++; continue; }
            }
            catch (Exception) { failed++; continue; }
            movies++;
            for (int f = 0; f < 40; f++)
            {
                // the mouse sweeps the stage; a click every tenth frame
                p.MouseMove(40 + f * 29 % 1200, 30 + f * 17 % 660);
                if (f % 10 == 5) p.MouseButton(true);
                if (f % 10 == 6) p.MouseButton(false);
                p.Tick();
                check.Frame(p, key, f);
            }
        }
        TestContext.WriteLine($"movies {movies} (not started {failed}), {check.Frames} frames, objects {check.Objects}, changed {check.Changed}");
        foreach (var m in check.Missed) TestContext.WriteLine("missed: " + m);
        Assert.That(movies, Is.GreaterThan(100));
        Assert.That(check.Missed, Is.Empty);
    }
}
