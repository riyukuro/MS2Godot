using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// Every flat model of the data with the MS2Breakable mixin: ecrPhysX PropService::HandleSceneGraphAddedMessage gives a NIF
/// its PhysX meshes only when the model holds PhysXProp (PropService.cpp:455-458), so <see cref="BreakableObjects.NifPhysX"/>
/// must answer that for each of them, and the field entities that use them must count among the dropped ones.
/// </summary>
public class BreakableNifPhysXCensusTests
{
    static void Census(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var flats = new FlatLibrary(packs);
        var models = new List<string>();
        var withPhysX = new List<string>();
        foreach (var entry in packs.List(XBlock.Archive))
        {
            if (!entry.StartsWith("flat/", StringComparison.OrdinalIgnoreCase) || !entry.EndsWith(".flat", StringComparison.OrdinalIgnoreCase)) continue;
            var name = Path.GetFileNameWithoutExtension(entry);
            var f = flats.Get(name);
            var e = new FieldEntity { Raw = new XEntity { Id = name, ModelName = name, Name = name }, Flat = f };
            if (!e.HasMixin("MS2Breakable")) continue;
            models.Add(name);
            bool client = e.HasMixin("PhysXProp");
            if (client) withPhysX.Add(name);
            Assert.That(BreakableObjects.NifPhysX(e), Is.EqualTo(client), name);
        }
        TestContext.WriteLine($"{variable}: {models.Count} MS2Breakable models ({string.Join(", ", models)}); with PhysXProp: {(withPhysX.Count == 0 ? "none" : string.Join(", ", withPhysX))}");
        Assert.That(models, Is.Not.Empty);
    }

    [Test] public void EveryKmsBreakableModelKeepsItsNifMeshesOnlyWithPhysXProp() => Census("MS2_DATA");
    [Test] public void EveryGms2BreakableModelKeepsItsNifMeshesOnlyWithPhysXProp() => Census("MS2_GMS2_DATA");
}
