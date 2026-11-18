using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// Player.HitTestShapes, the cursor-over-the-UI test every movie view makes every 50 ms, keeps each object's bounds for the
/// length of one test. Its answers must be the answers without that memo (the reference) on every movie of Resource/Gfx at
/// every point of a 16 px grid over the stage (a 4 px grid, 28.4 million points, took 13 minutes and matched too).
/// </summary>
public class HitTestShapesTests
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

    static Player Open(string key)
    {
        try
        {
            var (_, p) = src.Open(key, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            if (!p.Start()) return null;
            for (int i = 0; i < 10; i++) p.Tick();
            return p;
        }
        catch (Exception) { return null; }
    }

    const int Step = 16, W = 1280 / Step, H = 720 / Step;

    static bool[] Grid(Player p, bool memo)
    {
        BoundsMemo.Off = !memo;
        try
        {
            var r = new bool[W * H];
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) r[y * W + x] = p.HitTestShapes(x * Step + 0.5, y * Step + 0.5);
            return r;
        }
        finally { BoundsMemo.Off = false; }
    }

    [Test]
    public void EveryMovieHitsWhereItDidWithoutTheMemo()
    {
        Packs();
        int movies = 0, hits = 0, failed = 0;
        foreach (var key in packs.List("Resource/Gfx").Where(n => n.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase))
                     .Select(n => Path.GetFileNameWithoutExtension(n)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var p = Open(key);
            if (p == null) { failed++; continue; }
            var reference = Grid(p, false);
            var memo = Grid(p, true);
            for (int i = 0; i < reference.Length; i++)
                if (memo[i] != reference[i]) Assert.Fail($"{key}: ({i % W * Step + 0.5}, {i / W * Step + 0.5}) {memo[i]} vs {reference[i]}");
            movies++; hits += reference.Count(b => b);
        }
        TestContext.WriteLine($"movies {movies} (not started {failed}), points hit {hits} of {movies * W * H}");
        Assert.That(movies, Is.GreaterThan(100));
        Assert.That(hits, Is.GreaterThan(0));
    }

    [Test, Explicit("timing")]
    public void Bench()
    {
        Packs();
        foreach (var key in new[] { "uiinventorydialog", "uimyinfodialog", "uiskillbooktreedialog", "uiworldmapdialog", "uishopdialog" })
        {
            var p = Open(key);
            if (p == null) { TestContext.WriteLine(key + ": not started"); continue; }
            var b = p.Stage.GetBounds(null);
            var rnd = new Random(3);
            var pts = Enumerable.Range(0, 300).Select(_ => (b.X + rnd.NextDouble() * b.W, b.Y + rnd.NextDouble() * b.H)).ToArray();
            double Time(bool memo)
            {
                BoundsMemo.Off = !memo;
                try
                {
                    foreach (var (x, y) in pts.Take(20)) p.HitTestShapes(x, y);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    foreach (var (x, y) in pts) p.HitTestShapes(x, y);
                    return sw.Elapsed.TotalMilliseconds / pts.Length * 1000;
                }
                finally { BoundsMemo.Off = false; }
            }
            TestContext.WriteLine($"{key}: without memo {Time(false):0} us, with {Time(true):0} us per test (points inside the stage bounds {b})");
        }
    }
}
