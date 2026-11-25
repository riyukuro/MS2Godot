using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>Every map's MS2InteractMesh objects on the data: the assets CInteractMeshObject::vfunc_1 (@0x1413febf0,
/// interactobject.cpp:752-777) takes from the entity (NifAsset, reactableAssetName), and what state 1 swaps to. Every
/// reactable model that differs from the entity's own resolves to a model, and LiveInteractObjects.ReactableModel names it for
/// exactly the registered, visible, non-tower meshes whose reactable model differs.</summary>
public class InteractMeshAssetDataTests
{
    [Test]
    public void EveryInteractMeshsReactableAssetIsAModel()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        using var packs = new PackSet(d);
        var flats = new FlatLibrary(packs);
        var table = InteractObjectTable.Shared(packs);
        var assets = AssetIndex.For(packs);
        int maps = 0, meshes = 0, same = 0, emptyB = 0, differ = 0, unresolved = 0, twins = 0, hidden = 0;
        var wrong = new List<string>();
        var kinds = new SortedDictionary<string, int>();
        foreach (var entry in packs.List(XBlock.Archive).Where(n => n.StartsWith("xblock/") && n.EndsWith(".xblock")).OrderBy(n => n, StringComparer.Ordinal))
        {
            string name = entry["xblock/".Length..^".xblock".Length];
            FieldMap map;
            try { map = FieldMap.Load(packs, flats, name); } catch (Exception) { continue; }
            var live = new LiveInteractObjects(new LiveInteractObjectsTests.Host(), table);
            live.Attach(map.Entities);
            bool any = false;
            foreach (var o in live.All)
            {
                if (o.AssetType != 1 || !LiveInteractObjects.IsMeshKind(o.Kind)) continue;
                var reactable = LiveInteractObjects.ReactableModel(o.Entity, table);
                bool visible = o.Entity.Bool("IsVisible", true);
                bool wanted = visible && o.Kind != InteractObjectMessage.KindWatchTower && o.AssetB.Length > 0
                              && !string.Equals(o.AssetA, o.AssetB, StringComparison.Ordinal);
                if ((reactable != null) != wanted || reactable != null && reactable != o.AssetB)
                    wrong.Add($"{name} {o.Name}: reactable model {reactable ?? "none"}, wanted {(wanted ? o.AssetB : "none")}");
                if (reactable != null) twins++;
                else if (!visible && o.AssetB.Length > 0 && !string.Equals(o.AssetA, o.AssetB, StringComparison.Ordinal)) hidden++;
                any = true;
                meshes++;
                string k = $"kind {o.Kind} states {o.Row?.NormalCollision}/{o.Row?.ReactableCollision}";
                if (o.AssetB.Length == 0) { emptyB++; k += " B empty"; }
                else if (string.Equals(o.AssetA, o.AssetB, StringComparison.OrdinalIgnoreCase)) { same++; k += " B same"; }
                else
                {
                    differ++;
                    k += " B differs";
                    bool ok = assets.RelPath(o.AssetB) != null;
                    if (!ok) unresolved++;
                    TestContext.Out.WriteLine($"{name} {o.Name} row {o.RowId} kind {o.Kind} A {assets.RelPath(o.AssetA) ?? o.AssetA} B {assets.RelPath(o.AssetB) ?? o.AssetB}");
                }
                kinds[k] = kinds.GetValueOrDefault(k) + 1;
            }
            if (any) maps++;
        }
        TestContext.Out.WriteLine($"{maps} maps, {meshes} interact meshes: B same {same}, B empty {emptyB}, B differs {differ} ({unresolved} not a model)");
        TestContext.Out.WriteLine($"{twins} reactable models drawn apart, {hidden} hidden at the load (not drawn)");
        foreach (var (k, n) in kinds) TestContext.Out.WriteLine($"  {k}: {n}");
        foreach (var w in wrong.Take(20)) TestContext.Out.WriteLine(w);
        Assert.That(meshes, Is.GreaterThan(0));
        Assert.That(twins, Is.GreaterThan(0));
        Assert.That(unresolved, Is.Zero, "a reactable model that is not a model");
        Assert.That(wrong, Is.Empty);
    }
}
