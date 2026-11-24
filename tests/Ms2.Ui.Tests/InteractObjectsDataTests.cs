using Ms2.Formats.World;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The field's interact objects (WorldService m_678, worldservice_1.cpp:6871-6912) on the data: every map's
/// MS2InteractObject entities, by row kind and model, against what registers and the assets each state draws.</summary>
public class InteractObjectsDataTests
{
    static Ms2.Formats.Pack.PackSet Packs()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        return new Ms2.Formats.Pack.PackSet(d);
    }

    static bool IsInteract(FieldEntity e) =>
        string.Equals(e.ModelName, "MS2InteractObject", StringComparison.OrdinalIgnoreCase) || e.HasMixin("MS2InteractObject");

    /// <summary>Every map: each interact entity with a row registers under its lowercase id, with the row's kind; a mesh-kind
    /// object (mesh, gathering, bill board, watch tower) takes its assets by its model (CInteractMeshObject::vfunc_1): an
    /// MS2InteractMesh NifAsset / reactableAssetName, an MS2InteractActor InitialSequence / reactableSequenceName. The other
    /// kinds have none.</summary>
    [Test]
    public void EveryMapsInteractObjectsRegisterWithTheirModelsAssets()
    {
        var packs = Packs();
        var flats = new FlatLibrary(packs);
        var table = InteractObjectTable.Shared(packs);
        int maps = 0, failed = 0, entities = 0, noRow = 0, registered = 0, meshSwap = 0, actorSwap = 0, hiddenRows = 0;
        var byKind = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var kfmArchives = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var assets = Ms2.Formats.Pack.AssetIndex.For(packs);
        foreach (var entry in packs.List(XBlock.Archive).Where(n => n.StartsWith("xblock/") && n.EndsWith(".xblock")).OrderBy(n => n, StringComparer.Ordinal))
        {
            string name = entry["xblock/".Length..^".xblock".Length];
            FieldMap map;
            try { map = FieldMap.Load(packs, flats, name); } catch (Exception) { failed++; continue; }
            maps++;
            var live = new LiveInteractObjects(new LiveInteractObjectsTests.Host(), table);
            live.Attach(map.Entities);
            var seen = new HashSet<string>();
            foreach (var e in map.Entities)
            {
                if (!IsInteract(e)) continue;
                entities++;
                var row = table.Get((int)e.Number("interactID"));
                if (row == null) { noRow++; continue; }
                var (type, a, b) = LiveInteractObjects.MeshAssets(e);
                bool meshKind = LiveInteractObjects.IsMeshKind(row.Type);
                string key = e.Raw.Id.ToLowerInvariant();
                var o = live.ByName(key);
                if (meshKind && type == 0) { Assert.That(o, Is.Null, $"{name} {e.Raw.Id}: a mesh kind of another model"); continue; }
                if (!seen.Add(key)) continue;
                Assert.That(o, Is.Not.Null, $"{name} {e.Raw.Id}");
                Assert.That(o.Entity, Is.SameAs(e), $"{name} {e.Raw.Id}");
                Assert.That((o.Kind, o.RowId), Is.EqualTo(((byte)row.Type, row.Id)), $"{name} {e.Raw.Id}");
                if (meshKind)
                {
                    Assert.That((o.AssetType, o.AssetA, o.AssetB), Is.EqualTo((type, a, b)), $"{name} {e.Raw.Id}");
                    if (type == 1 && b.Length > 0 && b != a) meshSwap++;
                    if (type == 2 && b.Length > 0 && b != a) actorSwap++;
                    if (type == 2)
                    {
                        var (arc, _) = assets.Resolve(e.Get("KfmAsset"));
                        kfmArchives[arc ?? "(none)"] = kfmArchives.GetValueOrDefault(arc ?? "(none)") + 1;
                    }
                }
                else Assert.That((o.AssetType, o.AssetA, o.AssetB), Is.EqualTo(((byte)0, "", "")), $"{name} {e.Raw.Id}");
                if (row.NormalCollision != row.ReactableCollision) hiddenRows++;
                registered++;
                string k = $"kind {row.Type} {(type == 1 ? "mesh" : type == 2 ? "actor" : "other")}";
                byKind[k] = byKind.GetValueOrDefault(k) + 1;
            }
        }
        TestContext.Out.WriteLine($"{maps} maps ({failed} unreadable); {entities} interact entities, {noRow} without a row, {registered} registered; " +
                                  $"{meshSwap} meshes and {actorSwap} actors with another state-1 asset; {hiddenRows} whose collision differs by state");
        foreach (var (k, n) in byKind) TestContext.Out.WriteLine($"{k}: {n}");
        foreach (var (k, n) in kfmArchives) TestContext.Out.WriteLine($"actor kfm {k}: {n}");
        Assert.That(registered, Is.GreaterThan(0));
        Assert.That(byKind.Keys.Any(k => k.EndsWith("actor")), Is.True);
    }
}
