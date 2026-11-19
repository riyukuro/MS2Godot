using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Navigation.Tests;

public class NavigationTests
{
    const string Tria = "02000001_tw_tria";

    static (PackSet packs, NifSpec spec) Data()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        var xml = Environment.GetEnvironmentVariable("MS2_NIFXML");
        if (string.IsNullOrEmpty(xml)) xml = SubmoduleNifXml();
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA is not set");
        if (string.IsNullOrEmpty(xml) || !File.Exists(xml)) Assert.Ignore("MS2_NIFXML is not set and external/nifxml is not checked out");
        return (new PackSet(data), new NifSpec(xml));
    }

    /// <summary>external/nifxml/nif.xml of the repo holding the test assembly (git submodule), or null.</summary>
    static string SubmoduleNifXml()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "MS2Godot.sln")))
            {
                var f = Path.Combine(d.FullName, "external", "nifxml", "nif.xml");
                return File.Exists(f) ? f : null;
            }
        return null;
    }

    [Test]
    public void ConfigRoundTrip()
    {
        var p = NavConfig.ToNav(150, 300, 75);
        Assert.That(p.X, Is.EqualTo(1.5f).Within(1e-6));
        Assert.That(p.Y, Is.EqualTo(0.75f).Within(1e-6));
        Assert.That(p.Z, Is.EqualTo(-3f).Within(1e-6));
        var (x, y, z) = NavConfig.FromNav(p);
        Assert.That((x, y, z), Is.EqualTo((150.0, 300.0, 75.0)).Within(1e-3));
    }

    [Test]
    public void FlatFloorGivesPath()
    {
        // a 20 m x 20 m slab, 1 m thick: a path across it is a straight line and its closest point sits on the top
        var g = new NavGeometry();
        float[] c = { -10, 0, -10, 10, 0, -10, 10, 0, 10, -10, 0, 10, -10, -1, -10, 10, -1, -10, 10, -1, 10, -10, -1, 10 };
        g.Verts.AddRange(c);
        g.Tris.AddRange(new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7 });
        g.Areas.AddRange(new[] { 0, 0, 0, 0 });
        var mesh = NavMeshBuilder.Build(g);
        Assert.That(mesh, Is.Not.Null);
        var nav = FieldNavigation.FromMesh("slab", mesh);
        var path = nav.FindPath(-500, -500, 0, 500, 500, 0);
        Assert.That(path, Is.Not.Null);
        Assert.That(path.Count, Is.GreaterThanOrEqualTo(2));
        var cp = nav.ClosestPoint(0, 0, 300);   // 3 m above: on the floor
        Assert.That(cp, Is.Not.Null);
    }

    [Test]
    public void TriaBuildsCachesAndFindsPath()
    {
        var (packs, spec) = Data();
        var assets = AssetIndex.For(packs);
        var flats = new FlatLibrary(packs);
        var dir = Path.Combine(Path.GetTempPath(), "ms2nav-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var nav = FieldNavigation.Load(packs, assets, spec, flats, Tria, dir);
            Assert.That(nav, Is.Not.Null, "Tria has navigation geometry");
            Assert.That(Directory.GetFiles(dir, "*.navmesh").Length, Is.EqualTo(1));
            var cached = FieldNavigation.Load(packs, assets, spec, flats, Tria, dir);   // second load reads the file
            Assert.That(cached, Is.Not.Null);

            // spawn point of the field: the first PC spawn entity, snapped to the mesh
            var map = FieldMap.Load(packs, flats, Tria);
            var spawn = map.Entities.FirstOrDefault(e => e.ModelName == "SpawnPointPC");
            Assert.That(spawn, Is.Not.Null);
            var p = spawn.Position;
            var on = cached.ClosestPoint(p.X, p.Y, p.Z);
            Assert.That(on, Is.Not.Null, "spawn point lies on the navmesh");
            TestContext.Out.WriteLine($"spawn {p.X:F0},{p.Y:F0},{p.Z:F0} -> {on.Value.x:F0},{on.Value.y:F0},{on.Value.z:F0}");

            // some portal of the field is reachable from the spawn point over the mesh
            int found = 0, tried = 0;
            foreach (var portal in map.Entities.Where(e => e.HasMixin("Portal")).Take(20))
            {
                var q = portal.Position;
                var to = cached.ClosestPoint(q.X, q.Y, q.Z);
                if (to == null) continue;
                tried++;
                var path = cached.FindPath(on.Value.x, on.Value.y, on.Value.z, to.Value.x, to.Value.y, to.Value.z);
                if (path != null && path.Count >= 2) found++;
            }
            TestContext.Out.WriteLine($"portals on the mesh {tried}, reachable {found}");
            Assert.That(found, Is.GreaterThan(0));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Test]
    public void PrepareBuildsUnlessTheLoadsOwnFileIsThere()
    {
        var (packs, spec) = Data();
        var assets = AssetIndex.For(packs);
        var flats = new FlatLibrary(packs);
        var dir = Path.Combine(Path.GetTempPath(), "ms2nav-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.That(FieldNavigation.Prepare(packs, assets, spec, flats, Tria, dir), Is.True, "empty cache: built");
            var exact = Directory.GetFiles(dir, "*.navmesh").Single();
            var staleKey = Path.Combine(dir, $"{Tria}.v{NavConfig.Version}.0000000000000000.navmesh");
            var oldVersion = Path.Combine(dir, $"{Tria}.v0.0000000000000000.navmesh");
            var other = Path.Combine(dir, "02000002_other.v1.0000000000000000.navmesh");
            File.Copy(exact, staleKey);
            File.Copy(exact, oldVersion);
            File.Copy(exact, other);

            Assert.That(FieldNavigation.Prepare(packs, assets, spec, flats, Tria, dir), Is.False, "the load's own file: not built");
            Assert.That(Directory.GetFiles(dir, "*.navmesh").OrderBy(f => f), Is.EqualTo(new[] { other, exact }.OrderBy(f => f)),
                "the field's files of another key or version are deleted, another field's are kept");

            File.Move(exact, staleKey);   // the file of the data before a patch: a load would not read it
            Assert.That(FieldNavigation.Prepare(packs, assets, spec, flats, Tria, dir), Is.True, "only a stale-key file: built");
            Assert.That(File.Exists(exact), Is.True);
            Assert.That(File.Exists(staleKey), Is.False);
            Assert.That(FieldNavigation.Load(packs, assets, spec, flats, Tria, dir), Is.Not.Null);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
