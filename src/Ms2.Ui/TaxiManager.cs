using System;
using System.Collections.Generic;
using Ms2.Quest;

namespace Ms2.Ui;

/// <summary>
/// CTaxiManager's fare (commonfiles/taximanager.cpp): the portal hops between two fields priced by the client's precompiled Lua.
/// </summary>
public sealed class TaxiManager
{
    sealed class PathInfo
    {
        public readonly int Station;
        public int Hops;
        public PathInfo(int station, int hops) { Station = station; Hops = hops; }
    }

    readonly FieldMeta meta;
    readonly Dictionary<(uint, uint), int> routeLength = new();    // m_mapRouteLength

    public TaxiManager(FieldMeta meta) { this.meta = meta; }

    /// <summary>MakeTaxiKey (@0x14085fcb0, :133): the pair, smaller id first.</summary>
    static (uint, uint) Key(uint a, uint b) => a > b ? (b, a) : (a, b);

    /// <summary>
    /// CTaxiManager::GetTaxiFare (@0x14085f780, taximanager.cpp:138): 0 for the same field; else the fewest portal hops
    /// (breadth first over CFieldMetaTable::GetPortalTargets, -1 when unreachable, cached per unordered pair) priced by
    /// <see cref="CalcTaxiCharge"/> with the player's level.
    /// </summary>
    public long GetTaxiFare(int from, int to, short level)
    {
        if (from == to) return 0;
        var key = Key((uint)from, (uint)to);
        if (routeLength.TryGetValue(key, out int cached)) return CalcTaxiCharge(cached, (ushort)level);

        int best = -1;
        var open = new Queue<PathInfo>();
        var seen = new Dictionary<int, PathInfo>();
        open.Enqueue(new PathInfo(from, 0));
        while (open.Count > 0)
        {
            var cur = open.Dequeue();
            int hops = cur.Hops + 1;
            if (hops >= best && best >= 0) continue;
            if (meta == null || !meta.PortalTargets.TryGetValue(cur.Station, out var links)) continue;
            foreach (int next in links)
            {
                if (next == 0 || cur.Station == next) continue;
                if (next == to && (best > hops || best == -1)) { best = hops; continue; }
                if (!seen.TryGetValue(next, out var known))
                {
                    var n = new PathInfo(next, hops);
                    open.Enqueue(n);
                    seen[next] = n;
                }
                else if (known.Hops > hops)
                {
                    known.Hops = hops;
                    open.Enqueue(known);
                }
            }
        }
        routeLength[key] = best;
        return CalcTaxiCharge(best, (ushort)level);
    }

    /// <summary>CTaxiManager::GetAirTaxiFare (@0x14085fc80, :188): CLua::CalcAirTaxiCharge(level).</summary>
    public static long GetAirTaxiFare(short level) => CalcAirTaxiCharge((ushort)level);

    /// <summary>
    /// CLua::CalcTaxiCharge (@0x1406764e0, luautil.cpp:819) = the Lua global calcTaxiCharge(hops, level) of the precompiled
    /// script (read with luac -l): f = 0.35307 L^2 - 1.4401 L + 34.075 for L &lt;= 24, 0.23451 (L-24)^2 + 24.221 (L-24) + 265.66
    /// for L &gt;= 25 (0 between); the charge is floor(f * hops / 2 + 0.5) as the (int) of the returned number.
    /// </summary>
    public static int CalcTaxiCharge(int hops, ushort level)
    {
        double l = level, f = 0;
        if (l <= 24) f = 0.35307 * Math.Pow(l, 2) + -1.4401 * l + 34.075;
        else if (25 <= l) f = 0.23451 * Math.Pow(l - 24, 2) + 24.221 * (l - 24) + 265.66;
        return (int)Math.Floor(f * hops / 2 + 0.5);
    }

    /// <summary>CLua::CalcAirTaxiCharge (@0x1406766a0, :837) = calcAirTaxiCharge(level): 30000 + max(level - 10, 0) * 500.</summary>
    public static int CalcAirTaxiCharge(ushort level) => 30000 + Math.Max(level - 10, 0) * 500;
}
