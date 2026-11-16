using System.Security.Cryptography;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Recast.Toolset;
using DotRecast.Recast.Toolset.Builder;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Navigation;

/// <summary>The navmesh of one field with path and nearest-point queries in map space (Z up, file units).</summary>
public sealed class FieldNavigation
{
    public string XBlock { get; }
    public DtNavMesh Mesh { get; }
    readonly DtNavMeshQuery query;
    readonly IDtQueryFilter filter = new DtQueryDefaultFilter(
        SampleAreaModifications.SAMPLE_POLYFLAGS_ALL, SampleAreaModifications.SAMPLE_POLYFLAGS_DISABLED,
        new float[] { 1f, 10f, 1f, 1f, 2f, 1.5f });

    FieldNavigation(string xblock, DtNavMesh mesh)
    {
        XBlock = xblock; Mesh = mesh;
        query = new DtNavMeshQuery(mesh);
    }

    /// <summary>
    /// Loads the field's navmesh from <paramref name="cacheDir"/> or builds and stores it. The cache key is the
    /// build version plus a hash of the field's xblock; null when the field has no navigation geometry.
    /// </summary>
    public static FieldNavigation Load(PackSet packs, AssetIndex assets, NifSpec spec, FlatLibrary flats, string xblock, string cacheDir)
    {
        var raw = packs.Read(Ms2.Formats.World.XBlock.Archive, $"xblock/{xblock.ToLowerInvariant()}.xblock");
        var key = Convert.ToHexString(SHA1.HashData(raw))[..12].ToLowerInvariant();
        var path = Path.Combine(cacheDir, $"{xblock}.v{NavConfig.Version}.{key}.navmesh");
        if (File.Exists(path))
        {
            try { return new FieldNavigation(xblock, NavMeshBuilder.Load(path)); }
            catch { /* damaged: rebuild */ }
        }
        var map = FieldMap.Load(packs, flats, xblock);
        var mesh = NavMeshBuilder.Build(NavGeometry.Build(packs, assets, spec, map));
        if (mesh == null) return null;
        NavMeshBuilder.Save(mesh, path);
        return new FieldNavigation(xblock, mesh);
    }

    public static FieldNavigation FromMesh(string xblock, DtNavMesh mesh) => new(xblock, mesh);

    static RcVec3f Ext => NavConfig.NearestExtents;

    /// <summary>The point of the navmesh closest to p (map space), or null when none lies within reach.</summary>
    public (double x, double y, double z)? ClosestPoint(double x, double y, double z)
    {
        var p = NavConfig.ToNav(x, y, z);
        if (query.FindNearestPoly(p, Ext, filter, out long r, out var near, out _).Failed() || r == 0) return null;
        return NavConfig.FromNav(near);
    }

    /// <summary>Straight-path corners from a to b over the navmesh (map space), or null when no route exists.</summary>
    public List<(double x, double y, double z)> FindPath(double ax, double ay, double az, double bx, double by, double bz)
    {
        var a = NavConfig.ToNav(ax, ay, az); var b = NavConfig.ToNav(bx, by, bz);
        if (query.FindNearestPoly(a, Ext, filter, out long ra, out var na, out _).Failed() || ra == 0) return null;
        if (query.FindNearestPoly(b, Ext, filter, out long rb, out var nb, out _).Failed() || rb == 0) return null;
        var polys = new List<long>();
        query.FindPath(ra, rb, na, nb, filter, ref polys, new DtFindPathOption(0, float.MaxValue));
        if (polys.Count == 0) return null;
        var end = nb;
        if (polys[^1] != rb) query.ClosestPointOnPoly(polys[^1], nb, out end, out _);   // partial route: ends at the nearest reachable point
        var straight = new DtStraightPath[256];
        if (query.FindStraightPath(na, end, polys, polys.Count, straight, out int n, straight.Length, 0).Failed() || n == 0) return null;
        var o = new List<(double x, double y, double z)>(n);
        for (int i = 0; i < n; i++) o.Add(NavConfig.FromNav(straight[i].pos));
        return o;
    }
}
