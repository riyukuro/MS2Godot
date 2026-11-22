using System.Numerics;
using Ms2.Formats.Cache;
using Ms2.Formats.Pack;

namespace Ms2.Quest;

/// <summary>
/// CQuestNavigationMap's map graph (BuildMapPaths / FindMapPath / GetNextMapOnPath / NearestMap): all-pairs shortest paths
/// between fields over their portal links, Floyd-Warshall on a short matrix with the intermediate-map table kept for path
/// recovery. Index 0 is an unused slot, as in the client. A portal costs 1, 3 into or out of field 52000066.
/// </summary>
public sealed class QuestMapGraph
{
    public const int SlowField = 0x3197542;     // 52000066
    const ushort Unreachable = 0x7fff;

    readonly Dictionary<int, short> indexOf = new();
    readonly short[][] mid;                     // m_78: the intermediate index of the best path, 0 = direct
    Func<int, int, bool> hasPortal;
    readonly int[] mapIds;                      // index -> field id

    public int Count => indexOf.Count;

    /// <param name="fields">the nodes (CFieldMetaTable portal-target keys).</param>
    /// <param name="hasPortalTarget">HasPortalTarget(from, to).</param>
    public QuestMapGraph(IReadOnlyList<int> fields, Func<int, int, bool> hasPortalTarget)
    {
        hasPortal = hasPortalTarget;
        var map = mapIds = Index(fields, indexOf);
        int n = map.Length;
        var dist = new ushort[n][];
        mid = new short[n][];
        for (int i = 0; i < n; i++)
        {
            dist[i] = new ushort[n];
            mid[i] = new short[n];
            for (int j = 0; j < n; j++)
            {
                if (i == j) dist[i][j] = 0;
                else if (hasPortalTarget(map[i], map[j])) dist[i][j] = Cost(map[i], map[j]);
                else dist[i][j] = Unreachable;
            }
        }
        FloydWarshall(dist, mid);
    }

    /// <summary>The same graph from the portal lists: HasPortalTarget(a, b) is "b is in a's list", so the start table is
    /// filled from the lists (one entry per listed target, at every index holding it, index 0 holding field 0) instead of
    /// asking every pair (n² list scans).</summary>
    public QuestMapGraph(IReadOnlyList<int> fields, IReadOnlyDictionary<int, List<int>> portalTargets)
    {
        hasPortal = HasPortal(portalTargets);
        var map = mapIds = Index(fields, indexOf);
        int n = map.Length;
        var at = new Dictionary<int, List<short>>();
        for (short i = 0; i < n; i++)
        {
            if (!at.TryGetValue(map[i], out var l)) at[map[i]] = l = new List<short>();
            l.Add(i);
        }
        var dist = new ushort[n][];
        mid = new short[n][];
        for (int i = 0; i < n; i++)
        {
            var d = dist[i] = new ushort[n];
            mid[i] = new short[n];
            Array.Fill(d, Unreachable);
            d[i] = 0;
            if (!portalTargets.TryGetValue(map[i], out var targets)) continue;
            foreach (int t in targets)
                if (at.TryGetValue(t, out var js))
                    foreach (short j in js)
                        if (j != i) d[j] = Cost(map[i], t);
        }
        FloydWarshall(dist, mid);
    }

    static int[] Index(IReadOnlyList<int> fields, Dictionary<int, short> indexOf)
    {
        var map = new int[fields.Count + 1];
        for (int i = 0; i < fields.Count; i++) { map[i + 1] = fields[i]; indexOf[fields[i]] = (short)(i + 1); }
        return map;
    }

    static ushort Cost(int from, int to) => from == SlowField || to == SlowField ? (ushort)3 : (ushort)1;

    /// <summary>
    /// Floyd-Warshall, as the client's loop runs it. A row with no path to k is skipped: its sum through k is at least
    /// Unreachable, which is never below a distance, so the loop would change nothing there (the same tables, k by k).
    /// The j loop runs on vectors: each j is decided from di[k] and dk[j] only, neither of which the loop changes (dk[k] is
    /// 0 and di[i] is 0), so updating a block of j at once gives the same tables. Distances are unsigned: a sum is at most
    /// 2 * 0x7fff, so it never wraps and compares as the client's int sum does.
    /// </summary>
    static void FloydWarshall(ushort[][] dist, short[][] mid)
    {
        int n = dist.Length, w = Vector<ushort>.Count;
        for (int k = 0; k < n; k++)
        {
            var dk = dist[k];
            var vk = new Vector<short>((short)k);
            for (int i = 0; i < n; i++)
            {
                var di = dist[i];
                int ik = di[k];
                if (ik == Unreachable) continue;
                var mi = mid[i];
                var vik = new Vector<ushort>((ushort)ik);
                int j = 0;
                for (; j + w <= n; j += w)
                {
                    var sum = vik + new Vector<ushort>(dk, j);
                    var cur = new Vector<ushort>(di, j);
                    var less = Vector.LessThan(sum, cur);
                    if (less == Vector<ushort>.Zero) continue;
                    Vector.Min(sum, cur).CopyTo(di, j);
                    Vector.ConditionalSelect(Vector.AsVectorInt16(less), vk, new Vector<short>(mi, j)).CopyTo(mi, j);
                }
                for (; j < n; j++)
                    if (ik + dk[j] < di[j])
                    {
                        mi[j] = (short)k;
                        di[j] = (ushort)(ik + dk[j]);
                    }
            }
        }
    }

    // its own method, so the closure holds the lists only (a graph read from the cache gets the same function)
    static Func<int, int, bool> HasPortal(IReadOnlyDictionary<int, List<int>> portalTargets) =>
        (a, b) => portalTargets.TryGetValue(a, out var l) && l.Contains(b);

    /// <summary>The cache build key of the field meta table and the graph (<see cref="CacheStore"/>).</summary>
    public static string CacheBuild => CacheStore.Build(typeof(QuestMapGraph).Assembly);

    sealed class Built
    {
        public Lazy<FieldMeta> Meta;
        public Lazy<QuestMapGraph> Graph;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, Built> shared = new();

    static Built For(PackSet packs) => shared.GetValue(packs, p =>
    {
        // per install (CacheStore.Of): the table and the graph's path tables, read back instead of parsed and built
        var b = new Built { Meta = new Lazy<FieldMeta>(() => CacheStore.Load(p, "fieldmeta", "", CacheBuild, () => FieldMeta.Load(p), _ => { })) };
        b.Graph = new Lazy<QuestMapGraph>(() =>
        {
            var meta = b.Meta.Value;
            return CacheStore.Load(p, "questmap", "", CacheBuild, () => From(meta), g => g.hasPortal = HasPortal(meta.PortalTargets));
        });
        return b;
    });

    /// <summary>Writes the cache files of the field meta table and its graph when they are missing (the setup step); the names built.</summary>
    public static List<string> BuildCache(PackSet packs, CacheStore cache)
    {
        var built = new List<string>();
        FieldMeta meta = null;
        FieldMeta Meta() => meta ??= FieldMeta.Load(packs);
        if (cache.Ensure("fieldmeta", "", CacheBuild, Meta)) built.Add("fieldmeta");
        if (cache.Ensure("questmap", "", CacheBuild, () => From(Meta()))) built.Add("questmap");
        return built;
    }

    /// <summary>The field meta table of <paramref name="packs"/>, read once, on the first call (without the map graph).</summary>
    public static FieldMeta SharedMeta(PackSet packs) => For(packs).Meta.Value;

    /// <summary>
    /// The field meta table and its map graph, each read or built once per pack set, on first use (the client builds its
    /// map paths once): the quest log, the world map and the quest guide share them. Both are read-only once built.
    /// </summary>
    public static (FieldMeta Meta, QuestMapGraph Graph) Shared(PackSet packs)
    {
        var b = For(packs);
        return (b.Meta.Value, b.Graph.Value);
    }

    public static QuestMapGraph From(FieldMeta meta) => new(meta.PortalFields, meta.PortalTargets);

    /// <summary>FindMapPath: the fields from <paramref name="from"/> to <paramref name="to"/> inclusive, or null.</summary>
    public List<int> FindMapPath(int from, int to)
    {
        if (from == to) return new List<int> { from };
        if (!indexOf.TryGetValue(from, out var fi) || !indexOf.TryGetValue(to, out var ti)) return null;
        if (mid[fi][ti] == 0 && !hasPortal(from, to)) return null;
        var path = new List<int> { from };
        int depth = 0;
        Collect(fi, ti, path, ref depth);
        path.Add(to);
        return path;
    }

    void Collect(short from, short to, List<int> path, ref int depth)
    {
        depth++;
        if (depth > indexOf.Count) return;
        short m = mid[from][to];
        if (m == 0) return;
        Collect(from, m, path, ref depth);
        path.Add(mapIds[m]);
        Collect(m, to, path, ref depth);
    }

    /// <summary>GetNextMapOnPath: the second field of the path, 0 when none.</summary>
    public int GetNextMapOnPath(int from, int to)
    {
        var p = FindMapPath(from, to);
        return p != null && p.Count > 1 ? p[1] : 0;
    }

    /// <summary>
    /// NearestMap: of the fields, the one with the fewest path entries from <paramref name="current"/> (the last of equals),
    /// 0 when none is reachable. STILL-OPEN: "the last of equals" is in the caller's order (a Dictionary's insertion order
    /// for the trace table's fields); the client walks a CAtlMap in its bucket order, so a tie between two fields at the
    /// same path length may pick the other one.
    /// </summary>
    public int NearestMap(int current, IEnumerable<int> maps)
    {
        int best = int.MaxValue, nearest = 0;
        foreach (int m in maps)
        {
            var p = FindMapPath(current, m);
            if (p != null && best >= p.Count) { best = p.Count; nearest = m; }
        }
        return nearest;
    }
}
