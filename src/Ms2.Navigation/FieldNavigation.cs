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

    // one build at a time: a build is seconds of CPU, and a build waiting for its turn is dropped once its field is gone
    static readonly SemaphoreSlim building = new(1, 1);

    /// <summary>
    /// Loads the field's navmesh from <paramref name="cacheDir"/> or builds and stores it; null when the field has no
    /// navigation geometry. The cache key hashes everything the build reads: the navigation geometry itself (so the
    /// xblock, the flat defaults, the NIF collision and the geometry rules), the build settings, the DotRecast version
    /// and NavConfig.Version (the builder's own rules). Cancelling drops the build at its next step; a build already in
    /// Recast runs to its end and is still cached.
    /// </summary>
    public static FieldNavigation Load(PackSet packs, AssetIndex assets, NifSpec spec, FlatLibrary flats, string xblock, string cacheDir,
        CancellationToken cancel = default)
    {
        var (geometry, path) = Locate(packs, assets, spec, flats, xblock, cacheDir);
        if (geometry == null) return null;
        cancel.ThrowIfCancellationRequested();
        if (Cached(xblock, path) is { } hit) return hit;
        return Build(xblock, geometry, path, cancel);
    }

    /// <summary>
    /// Makes sure the field's navmesh is in <paramref name="cacheDir"/> under the name <see cref="Load"/> looks for, and
    /// deletes the field's other navmesh files there (another key or version: a load never reads them, such as the file
    /// of the data before a patch). True when it was built, false when the file was there, null when the field has no
    /// navigation geometry. With <paramref name="inputs"/> (a key of the data the geometry is built from, such as the
    /// archives' index hashes and nif.xml's), the result is recorded next to the file for <see cref="Current"/>.
    /// </summary>
    public static bool? Prepare(PackSet packs, AssetIndex assets, NifSpec spec, FlatLibrary flats, string xblock, string cacheDir,
        string inputs = null)
    {
        var (geometry, path) = Locate(packs, assets, spec, flats, xblock, cacheDir);
        bool? built = null;
        if (geometry != null)
        {
            if (Directory.Exists(cacheDir))
                foreach (var f in Directory.EnumerateFiles(cacheDir, $"{xblock}.v*.navmesh"))
                    if (!string.Equals(Path.GetFullPath(f), Path.GetFullPath(path), StringComparison.Ordinal))
                        try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            built = Cached(xblock, path) == null;
            if (built == true) Build(xblock, geometry, path, default);
        }
        if (inputs != null && (geometry == null || File.Exists(path)))
            try
            {
                Directory.CreateDirectory(cacheDir);
                var rec = RecordPath(xblock, cacheDir);
                var tmp = $"{rec}.{Guid.NewGuid():N}.tmp";
                File.WriteAllText(tmp, $"{RecordKey(inputs)}\t{(path == null ? "" : Path.GetFileName(path))}\n");
                File.Move(tmp, rec, true);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }   // not recorded: the next launch checks again
        return built;
    }

    /// <summary>
    /// Whether <see cref="Prepare"/> recorded this field for the same <paramref name="inputs"/>, builder version and
    /// DotRecast version, and the file it named is still there: a launch then skips the geometry (about 0.2 s and 120 MB
    /// of allocations for the start field) that the key is computed from.
    /// </summary>
    public static bool Current(string xblock, string cacheDir, string inputs)
    {
        try
        {
            var rec = RecordPath(xblock, cacheDir);
            if (!File.Exists(rec)) return false;
            var line = File.ReadAllText(rec).TrimEnd('\n');
            int tab = line.IndexOf('\t');
            if (tab < 0 || line[..tab] != RecordKey(inputs)) return false;
            var name = line[(tab + 1)..];
            return name.Length == 0 || (Path.GetFileName(name) == name && File.Exists(Path.Combine(cacheDir, name)));
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    static string RecordPath(string xblock, string cacheDir) => Path.Combine(cacheDir, $"{xblock}.inputs");
    static string RecordKey(string inputs) => $"v{NavConfig.Version};dotrecast={typeof(DtNavMesh).Assembly.GetName().Version};{inputs}";

    /// <summary>The field's navigation geometry (null when it has none) and the cache file its navmesh goes in.</summary>
    static (NavGeometry geometry, string path) Locate(PackSet packs, AssetIndex assets, NifSpec spec, FlatLibrary flats, string xblock, string cacheDir)
    {
        var map = FieldMap.Load(packs, flats, xblock);
        var geometry = NavGeometry.Build(packs, assets, spec, map);
        if (geometry.Empty) return (null, null);
        return (geometry, Path.Combine(cacheDir, $"{xblock}.v{NavConfig.Version}.{Key(geometry)}.navmesh"));
    }

    static FieldNavigation Build(string xblock, NavGeometry geometry, string path, CancellationToken cancel)
    {
        building.Wait(cancel);
        try
        {
            if (Cached(xblock, path) is { } built) return built;                     // built by the load before this one
            cancel.ThrowIfCancellationRequested();
            var mesh = NavMeshBuilder.Build(geometry);
            if (mesh == null) return null;
            try { NavMeshBuilder.Save(mesh, path); }
            catch { /* not cached: the mesh still serves this visit */ }
            return new FieldNavigation(xblock, mesh);
        }
        finally { building.Release(); }
    }

    static FieldNavigation Cached(string xblock, string path)
    {
        if (!File.Exists(path)) return null;
        try { return new FieldNavigation(xblock, NavMeshBuilder.Load(path)); }
        catch { return null; /* damaged: rebuild */ }
    }

    /// <summary>The cache key: a hash of the geometry's vertices, triangles and areas, the build settings and DotRecast's version.</summary>
    static string Key(NavGeometry g)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        h.AppendData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(g.Verts)));
        h.AppendData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(g.Tris)));
        h.AppendData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(g.Areas)));
        var s = NavConfig.BuildSettings();
        var text = string.Join(";", typeof(RcNavMeshBuildSettings).GetFields().OrderBy(f => f.Name).Select(f => $"{f.Name}={Convert.ToString(f.GetValue(s), System.Globalization.CultureInfo.InvariantCulture)}"))
            + $";vpp={NavConfig.VertsPerPoly};dotrecast={typeof(DtNavMesh).Assembly.GetName().Version}";
        h.AppendData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(h.GetHashAndReset())[..16].ToLowerInvariant();
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

    /// <summary>
    /// The server's position test (kms2525 FieldManager.ValidPosition / FindNearestPoly, FieldManager.cs:373-398): a point
    /// with a NaN or infinite axis fails; else FindNearestPoly with extents (2, 4, 2) and the crowd's filter 0 must
    /// succeed with a non-zero polygon. Map space.
    /// </summary>
    public bool ValidPosition(double x, double y, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z)) return false;
        var p = NavConfig.ToNav(x, y, z);
        return query.FindNearestPoly(p, Ext, filter, out long r, out _, out _).Succeeded() && r != 0;
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
