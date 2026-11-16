namespace Ms2.Quest;

/// <summary>
/// CQuestNavigationMap's map graph (BuildMapPaths / FindMapPath / GetNextMapOnPath / NearestMap): all-pairs shortest paths
/// between fields over their portal links, Floyd-Warshall on a short matrix with the intermediate-map table kept for path
/// recovery. Index 0 is an unused slot, as in the client. A portal costs 1, 3 into or out of field 52000066.
/// </summary>
public sealed class QuestMapGraph
{
    public const int SlowField = 0x3197542;     // 52000066
    const short Unreachable = 0x7fff;

    readonly Dictionary<int, short> indexOf = new();
    readonly short[][] mid;                     // m_78: the intermediate index of the best path, 0 = direct
    readonly Func<int, int, bool> hasPortal;
    readonly int[] mapIds;                      // index -> field id

    public int Count => indexOf.Count;

    /// <param name="fields">the nodes (CFieldMetaTable portal-target keys).</param>
    /// <param name="hasPortalTarget">HasPortalTarget(from, to).</param>
    public QuestMapGraph(IReadOnlyList<int> fields, Func<int, int, bool> hasPortalTarget)
    {
        hasPortal = hasPortalTarget;
        int n = fields.Count + 1;
        var map = new int[n];
        for (int i = 0; i < fields.Count; i++) { map[i + 1] = fields[i]; indexOf[fields[i]] = (short)(i + 1); }
        mapIds = map;
        var dist = new short[n][];
        mid = new short[n][];
        for (int i = 0; i < n; i++)
        {
            dist[i] = new short[n];
            mid[i] = new short[n];
            for (int j = 0; j < n; j++)
            {
                if (i == j) dist[i][j] = 0;
                else if (hasPortalTarget(map[i], map[j])) dist[i][j] = (map[i] == SlowField || map[j] == SlowField) ? (short)3 : (short)1;
                else dist[i][j] = Unreachable;
            }
        }
        for (int k = 0; k < n; k++)
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    if (dist[i][k] + dist[k][j] < dist[i][j])
                    {
                        mid[i][j] = (short)k;
                        dist[i][j] = (short)(dist[i][k] + dist[k][j]);
                    }
    }

    public static QuestMapGraph From(FieldMeta meta) =>
        new(meta.PortalFields, (a, b) => meta.PortalTargets.TryGetValue(a, out var l) && l.Contains(b));

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

    /// <summary>NearestMap: of the fields, the one with the fewest path entries from <paramref name="current"/> (the last of equals), 0 when none is reachable.</summary>
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
