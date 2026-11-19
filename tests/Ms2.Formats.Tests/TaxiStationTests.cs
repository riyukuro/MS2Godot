using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// The data the taxi station port (CSceneStateGame::FUN_141739450) reads: the field's MS2TaxiStation entity with its NifAsset and
/// DisabledAsset, and the CommonResource effects EFFECT_TAXI_DEFAULT and EFFECT_TAXI_REVEAL.
/// </summary>
public class TaxiStationTests
{
    [Test]
    public void TriaPlacesOneStationWithItsDisabledModel()
    {
        var (packs, spec) = NifEnv.Open();
        var map = FieldMap.Load(packs, new FlatLibrary(packs), "02000001_tw_tria");
        var stations = map.Entities.Where(e => e.HasMixin("MS2TaxiStation")).ToList();
        Assert.That(stations, Has.Count.EqualTo(1));   // the client asserts at most one
        var assets = AssetIndex.For(packs);
        var on = assets.Resolve(stations[0].Get("NifAsset"));
        var off = assets.Resolve(stations[0].Get("DisabledAsset"));
        Assert.That(on.entry, Is.EqualTo("common/field/co_fi_struc_station_a01.nif"));
        Assert.That(off.entry, Is.EqualTo("common/field/co_fi_struc_station_a01_off.nif"));
        // the closed model keeps the open one's PhysX shape: the swap before the build leaves the collision as it was
        int Shapes(NifFile n) => n.Blocks.Count(b => b.Type == "NiPhysXShapeDesc");
        Assert.That(Shapes(NifFile.Load(spec, packs.Read(off.archive, off.entry))), Is.EqualTo(Shapes(NifFile.Load(spec, packs.Read(on.archive, on.entry)))).And.EqualTo(1));
    }

    [Test]
    public void TheTaxiEffectsAreTheCommonResourceOnes()
    {
        var (packs, _) = NifEnv.Open();
        var common = System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/commonresource.xml"));
        Assert.That(common, Does.Contain("Eff_Taxi_Default.xml").IgnoreCase);
        Assert.That(common, Does.Contain("Eff_Taxi_Reveal.xml").IgnoreCase);
        var t = new EffectTable(packs);
        var def = t.Get("ETC/Eff_Taxi_Default.xml");
        Assert.That(def, Is.Not.Null);
        var nif = def.Single(p => p.Type == "nif");
        Assert.That(nif.FilePath, Does.EndWith("Eff_co_fi_struc_station_A01.nif").IgnoreCase);
        Assert.That(nif.StartDelay, Is.EqualTo(2f));
        Assert.That(nif.Loop, Is.True);
        Assert.That(nif.AttachToNode, Is.False);
        var rev = t.Get("ETC/Eff_Taxi_Reveal.xml");
        Assert.That(rev, Is.Not.Null);
        var rn = rev.Single(p => p.Type == "nif");
        Assert.That(rn.FilePath, Does.EndWith("Eff_co_fi_struc_taxistation_A01.nif").IgnoreCase);
        Assert.That(rn.Duration, Is.EqualTo(2f));
        Assert.That(rev.Single(p => p.Type == "soundEvent").SoundEvent, Is.EqualTo("02100035"));
        foreach (var p in new[] { nif, rn })
            Assert.That(packs.Exists("Resource/Model/Effect", p.FilePath.Replace('\\', '/').ToLowerInvariant()), p.FilePath);
    }
}
