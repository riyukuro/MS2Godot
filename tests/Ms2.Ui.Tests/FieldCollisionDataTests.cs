using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The GeneratePhysX box rule (<see cref="FieldCollision.GeneratesBox"/>) on the data. The client builds the box with no
/// look at the entity's NIF (CubeMapInformationService::FUN_1410cf160, cubemapinformationservice_3.cpp:4026-4052), so an
/// entity without a NifAsset gets it too; the field builder used to skip such an entity before the box.
/// </summary>
public class FieldCollisionDataTests
{
    /// <summary>02000390: the PC spawn 11001 (z 1050) and portal 2 stand on trigger mesh 1001, which has a ProxyNifAsset
    /// only: its box (20000 x 15000 x 150 at z 900) is the floor that holds the player over the trigger box under it until
    /// the field's script hides the mesh.</summary>
    [Test]
    public void TheSpawnOf02000390StandsOnTheBoxOfATriggerMeshWithoutANif()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var map = FieldMap.Load(packs, new FlatLibrary(packs), "02000390_bf");
        var mesh = map.Entities.Single(e => Ms2.Net.Packets.TriggerObjects.KindOf(e.HasMixin) == Ms2.Net.Packets.TriggerObjectKind.Mesh
                                            && (int)e.Number("TriggerObjectID") == 1001);
        var spawn = map.Entities.Single(e => e.Name == "SpawnPointPC_11001");
        Assert.That(FieldCollision.NifLlid(mesh), Is.Null);
        Assert.That(FieldCollision.GeneratesBox(mesh), Is.True);
        var (c, size, rotZ) = FieldCollision.GeneratedBox(mesh);
        Assert.That(rotZ, Is.EqualTo(0).Within(1e-6));
        Assert.That(c.Z + size.Z / 2, Is.EqualTo(spawn.Position.Z).Within(1), "the box top is the spawn height");
        Assert.That(Math.Abs(spawn.Position.X - c.X), Is.LessThan(size.X / 2));
        Assert.That(Math.Abs(spawn.Position.Y - c.Y), Is.LessThan(size.Y / 2));
    }

    static (int Maps, int Boxes, int WithoutNif, string Models) Sweep(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var flats = new FlatLibrary(packs);
        int maps = 0, boxes = 0, withoutNif = 0;
        var models = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in packs.List(XBlock.Archive).Where(n => n.StartsWith("xblock/") && n.EndsWith(".xblock")).OrderBy(n => n, StringComparer.Ordinal))
        {
            string name = entry["xblock/".Length..^".xblock".Length];
            FieldMap map;
            try { map = FieldMap.Load(packs, flats, name); } catch (Exception) { continue; }
            maps++;
            foreach (var e in map.Entities)
            {
                if (!FieldCollision.GeneratesBox(e)) continue;
                boxes++;
                var (_, size, _) = FieldCollision.GeneratedBox(e);
                Assert.That(size.X > 0 && size.Y > 0 && size.Z > 0, Is.True, $"{name}: {e.Raw?.Id} box {size}");
                if (FieldCollision.NifLlid(e) != null) continue;
                withoutNif++;
                models[e.ModelName ?? ""] = models.GetValueOrDefault(e.ModelName ?? "") + 1;
            }
        }
        string list = string.Join(", ", models.Select(kv => $"{kv.Key} {kv.Value}"));
        TestContext.Out.WriteLine($"{variable}: {maps} maps, {boxes} GeneratePhysX boxes, {withoutNif} of them on entities without a NIF: {list}");
        return (maps, boxes, withoutNif, list);
    }

    /// <summary>Every map: each entity the rule gives a box has a box of positive size, NIF or not; the ones without a NIF
    /// (the field builder's old skip) are counted by model.</summary>
    [Test]
    public void EveryKmsMapsGeneratePhysXBoxesNeedNoNif()
    {
        var (maps, boxes, withoutNif, models) = Sweep("MS2_DATA");
        Assert.That(maps, Is.GreaterThan(100));
        Assert.That(boxes, Is.GreaterThan(1000));
        Assert.That(withoutNif, Is.GreaterThan(0));
        Assert.That(models, Does.Contain("MS2TriggerMesh"));
    }

    [Test]
    public void EveryGms2MapsGeneratePhysXBoxesNeedNoNif()
    {
        var (maps, boxes, withoutNif, models) = Sweep("MS2_GMS2_DATA");
        Assert.That(maps, Is.GreaterThan(100));
        Assert.That(boxes, Is.GreaterThan(1000));
        Assert.That(withoutNif, Is.GreaterThan(0));
        Assert.That(models, Does.Contain("MS2TriggerMesh"));
    }
}
