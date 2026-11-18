using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Quest.Tests;

public class QuestTests
{
    // a line 1 - 2 - 3 - 4, a branch 2 - 5, and an island 9
    static QuestMapGraph Chain()
    {
        var e = new HashSet<(int, int)> { (1, 2), (2, 1), (2, 3), (3, 2), (3, 4), (4, 3), (2, 5), (5, 2) };
        return new QuestMapGraph(new[] { 1, 2, 3, 4, 5, 9 }, (a, b) => e.Contains((a, b)));
    }

    [Test]
    public void MapPathsFollowPortals()
    {
        var g = Chain();
        Assert.That(g.FindMapPath(1, 4), Is.EqualTo(new[] { 1, 2, 3, 4 }));
        Assert.That(g.FindMapPath(4, 5), Is.EqualTo(new[] { 4, 3, 2, 5 }));
        Assert.That(g.FindMapPath(2, 3), Is.EqualTo(new[] { 2, 3 }));
        Assert.That(g.FindMapPath(3, 3), Is.EqualTo(new[] { 3 }));
        Assert.That(g.FindMapPath(1, 9), Is.Null);
        Assert.That(g.FindMapPath(1, 77), Is.Null);
        Assert.That(g.GetNextMapOnPath(1, 4), Is.EqualTo(2));
        Assert.That(g.GetNextMapOnPath(1, 9), Is.EqualTo(0));
    }

    [Test]
    public void SlowFieldCostsMore()
    {
        // 1 -> 2 -> 4 and 1 -> 3 -> 4 are equal in portals; 3 is the slow field, so the walk through 2 wins
        var e = new HashSet<(int, int)> { (1, 3), (3, 1), (3, 4), (4, 3), (1, 2), (2, 1), (2, 4), (4, 2) };
        var g = new QuestMapGraph(new[] { 1, 2, 3, 4 }, (a, b) => e.Contains((a, b)));
        Assert.That(g.FindMapPath(1, 4).Count, Is.EqualTo(3));
        var e2 = new HashSet<(int, int)> { (1, QuestMapGraph.SlowField), (QuestMapGraph.SlowField, 4), (1, 2), (2, 3), (3, 4) };
        var g2 = new QuestMapGraph(new[] { 1, 2, 3, 4, QuestMapGraph.SlowField }, (a, b) => e2.Contains((a, b)));
        Assert.That(g2.FindMapPath(1, 4), Is.EqualTo(new[] { 1, 2, 3, 4 }));    // 3 steps beats 1 + 3 + 3
    }

    // The client's loop as written (BuildMapPaths): every k, i, j, no row skipped; paths read back as FindMapPath does.
    static List<int> ReferencePath(int[] fields, Func<int, int, bool> portal, int from, int to)
    {
        int n = fields.Length + 1;
        var map = new int[n];
        for (int i = 0; i < fields.Length; i++) map[i + 1] = fields[i];
        var dist = new short[n, n];
        var mid = new short[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                dist[i, j] = i == j ? (short)0 : portal(map[i], map[j]) ? (map[i] == QuestMapGraph.SlowField || map[j] == QuestMapGraph.SlowField ? (short)3 : (short)1) : (short)0x7fff;
        for (int k = 0; k < n; k++)
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    if (dist[i, k] + dist[k, j] < dist[i, j]) { mid[i, j] = (short)k; dist[i, j] = (short)(dist[i, k] + dist[k, j]); }
        if (from == to) return new List<int> { from };
        int fi = Array.IndexOf(fields, from) + 1, ti = Array.IndexOf(fields, to) + 1;
        if (fi == 0 || ti == 0) return null;
        if (mid[fi, ti] == 0 && !portal(from, to)) return null;
        var path = new List<int> { from };
        int depth = 0;
        void Collect(int a, int b)
        {
            if (++depth > fields.Length) return;
            int m = mid[a, b];
            if (m == 0) return;
            Collect(a, m); path.Add(map[m]); Collect(m, b);
        }
        Collect(fi, ti);
        path.Add(to);
        return path;
    }

    [Test]
    public void MapPathsMatchTheFullLoop()
    {
        // random sparse one-way portals with the slow field among the nodes: every pair's path as the unskipped loop gives it
        for (int seed = 1; seed <= 6; seed++)
        {
            var rnd = new Random(seed);
            var fields = Enumerable.Range(1, 70).Select(i => i == 35 ? QuestMapGraph.SlowField : 1000 + i).ToArray();
            var e = new HashSet<(int, int)>();
            for (int c = 0; c < 70 * (seed % 3 + 1); c++) e.Add((fields[rnd.Next(70)], fields[rnd.Next(70)]));
            bool Portal(int a, int b) => e.Contains((a, b));
            var g = new QuestMapGraph(fields, Portal);
            foreach (int a in fields)
                foreach (int b in fields)
                    Assert.That(g.FindMapPath(a, b), Is.EqualTo(ReferencePath(fields, Portal, a, b)), $"seed {seed}: {a} -> {b}");
        }
    }

    [Test]
    public void NearestMapPicksFewestHops()
    {
        var g = Chain();
        Assert.That(g.NearestMap(1, new[] { 4, 5 }), Is.EqualTo(5));
        Assert.That(g.NearestMap(1, new[] { 9 }), Is.EqualTo(0));
    }

    [Test]
    public void SameFieldPointsAtTheTarget()
    {
        var r = QuestTrace.Trace(Chain(), 3, 3, new TraceTarget(TraceKind.Npc, 1000, 0, 0), new TracePortal[0], (0, 0, 0));
        Assert.That(r.Kind, Is.EqualTo(TraceKind.Npc));
        Assert.That(r.DistanceText, Is.EqualTo("10 m"));
        Assert.That(QuestTrace.Trace(Chain(), 3, 3, null, new TracePortal[0], (0, 0, 0)), Is.Null);
    }

    [Test]
    public void OtherFieldPointsAtAPortalOnThePath()
    {
        var portals = new[] { new TracePortal(1, 0, 500, 0), new TracePortal(3, 300, 0, 0), new TracePortal(5, 0, 0, 900) };
        var r = QuestTrace.Trace(Chain(), 2, 4, null, portals, (0, 0, 0));
        Assert.That(r.Kind, Is.EqualTo(TraceKind.Portal));
        Assert.That((r.X, r.Y), Is.EqualTo((300.0, 0.0)), "the portal to 1 is not on the path 2-3-4, the one to 3 is");
    }

    [Test]
    public void PortalToTheNextMapIsTheFallback()
    {
        // the path 2-3-4 does not list field 1, so the portal to 1 is skipped and the one to 3 is taken
        var portals = new[] { new TracePortal(1, 0, 500, 0), new TracePortal(3, 300, 0, 0, Shown: false), new TracePortal(3, 400, 0, 0) };
        var r = QuestTrace.Trace(Chain(), 2, 4, null, portals, (0, 0, 0));
        Assert.That((r.Kind, r.X), Is.EqualTo((TraceKind.Portal, 400.0)));
        Assert.That(QuestTrace.Trace(Chain(), 2, 9, null, portals, (0, 0, 0)), Is.Null, "unreachable field");
    }

    [Test]
    public void BeamYawFacesAwayFromTheTargetPlus90()
    {
        // target straight ahead on +X: player - target = (-d, 0) -> atan2(-0, -d) = 180, +90
        static double Wrap(double a) => ((a % 360) + 360) % 360;
        Assert.That(Wrap(QuestTrace.BeamYaw((0, 0, 0), (500, 0, 0))), Is.EqualTo(270).Within(1e-9));
        Assert.That(Wrap(QuestTrace.BeamYaw((0, 0, 0), (0, 500, 0))), Is.EqualTo(180).Within(1e-9));
        Assert.That(Wrap(QuestTrace.BeamYaw((0, 0, 0), (-500, 0, 0))), Is.EqualTo(90).Within(1e-9));
    }

    [Test]
    public void DistanceTextUnits()
    {
        var s = TraceStrings.Default;
        Assert.That(QuestTrace.DistanceText(50, s), Is.EqualTo("Arrived"));
        Assert.That(QuestTrace.DistanceText(100, s), Is.EqualTo("1 m"));
        Assert.That(QuestTrace.DistanceText(99999, s), Is.EqualTo("999 m"));
        Assert.That(QuestTrace.DistanceText(100000, s), Is.EqualTo("Arrived"));   // the client's gap
        Assert.That(QuestTrace.DistanceText(250000, s), Is.EqualTo("2 km"));
    }

    static PackSet Packs()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA is not set");
        return new PackSet(data);
    }

    [Test]
    public void RealPortalGraphLinksTria()
    {
        var packs = Packs();
        var meta = FieldMeta.Load(packs);
        var g = QuestMapGraph.From(meta);
        TestContext.Out.WriteLine($"fields {meta.PortalFields.Count}, graph {g.Count}, npcs {meta.NpcFields.Count}");
        Assert.That(meta.PortalFields, Does.Contain(2000001));
        var p = g.FindMapPath(2000001, 2000118);
        Assert.That(p, Is.Not.Null);
        Assert.That(p, Has.Count.EqualTo(2), "a direct portal");
        // some other field is several portals away
        int far = meta.PortalFields.Select(f => g.FindMapPath(2000001, f)?.Count ?? 0).Max();
        TestContext.Out.WriteLine($"longest path from Tria: {far} fields");
        Assert.That(far, Is.GreaterThan(2));
        var s = QuestStrings.Load(packs);
        Assert.That(s.DistanceM, Does.Contain("{0}"));
    }
}
