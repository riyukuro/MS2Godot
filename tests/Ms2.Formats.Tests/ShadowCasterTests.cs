using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>FieldEntity.CastsShadow: FUN_141567000's SetShadowCaster(root, IsCastingShadow) for Mesh and Actor entities and
/// FUN_141bbd490's caster list for every entity with IsCastingShadow.</summary>
public class ShadowCasterTests
{
    static FieldEntity Entity(string mixin, string own, string flat)
    {
        var raw = new XEntity { ModelName = "m" };
        if (own != null) raw.Props["IsCastingShadow"] = own;
        var f = new FlatModel { Name = "m" };
        if (mixin != null) f.Mixins.Add(mixin);
        if (flat != null) f.Props["IsCastingShadow"] = flat;
        return new FieldEntity { Raw = raw, Flat = f };
    }

    [Test]
    public void MeshAndActorFollowTheProperty()
    {
        foreach (var m in new[] { "Mesh", "Actor" })
        {
            Assert.That(Entity(m, null, "True").CastsShadow, Is.True, m);
            Assert.That(Entity(m, null, "False").CastsShadow, Is.False, m);
            Assert.That(Entity(m, "False", "True").CastsShadow, Is.False, m + " placed override");
            Assert.That(Entity(m, "True", "False").CastsShadow, Is.True, m + " placed override");
            Assert.That(Entity(m, null, null).CastsShadow, Is.False, m + " unset reads false");
        }
    }

    [Test]
    public void OtherEntitiesFollowTheProperty()
    {
        // FUN_141bbd490 lists any entity with IsCastingShadow; one without it never gets the caster mark
        Assert.That(Entity("Placeable", "False", "False").CastsShadow, Is.False);
        Assert.That(Entity("Placeable", null, null).CastsShadow, Is.False);
        Assert.That(Entity("Placeable", "True", "False").CastsShadow, Is.True);
        Assert.That(Entity(null, null, "True").CastsShadow, Is.True);
    }

    /// <summary>Tria's water, grass, flowers and fences are Mesh entities placed with IsCastingShadow off (981 of 15186 entities).
    /// The other 254 without IsCastingShadow are not Mesh or Actor and have no model: way points, spawn points, whiteboxes,
    /// sounds and lights.</summary>
    [Test]
    public void TriaNatureCastsNone()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var packs = new Ms2.Formats.Pack.PackSet(data);
        var map = FieldMap.Load(packs, new FlatLibrary(packs), "02000001_tw_tria");
        var off = map.Entities.Where(e => !e.CastsShadow).ToList();
        Assert.That(off.Count, Is.EqualTo(1235));
        Assert.That(off.Count(e => e.HasMixin("Mesh")), Is.EqualTo(981));
        Assert.That(off.Where(e => !e.HasMixin("Mesh") && !e.HasMixin("Actor")).All(e => e.Get("NifAsset") == null), Is.True);
        foreach (var name in new[] { "co_fluid_water_B02_", "he_fi_nature_grass_A01_", "he_fi_prop_fence_C02_" })
            Assert.That(map.Entities.Where(e => e.ModelName == name).All(e => !e.CastsShadow), Is.True, name);
        Assert.That(map.Entities.Count(e => e.CastsShadow && e.HasMixin("Mesh")), Is.GreaterThan(5000));
    }

    static ModelData Map(string path)
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA"); var nx = Environment.GetEnvironmentVariable("MS2_NIFXML");
        if (string.IsNullOrEmpty(data) || string.IsNullOrEmpty(nx)) Assert.Ignore("MS2_DATA / MS2_NIFXML not set");
        return ModelData.Load(NifFile.Load(new NifSpec(nx), new Ms2.Formats.Pack.PackSet(data).Read("Resource/Model/Map", path)));
    }

    /// <summary>ModelMesh.NoShadow: the generator's opaque core sits under a "NoShadow" UserPropBuffer, its body does not.
    /// Kritias 02020035 places 74 of them with IsCastingShadow on.</summary>
    [Test]
    public void GeneratorCoreTagged()
    {
        var md = Map("kritias/field/ks_fi_cubric_generator_a01.nif");
        Assert.That(md.Meshes.Single(m => m.FileName == "Editable Mesh").NoShadow, Is.True);
        Assert.That(md.Meshes.Single(m => m.FileName == "ks_fi_cubric_generator_A01").NoShadow, Is.False);
    }

    /// <summary>The lamp's two glow planes are tagged, its base is not.</summary>
    [Test]
    public void LampPlanesTagged()
    {
        var md = Map("common/field/co_fi_prop_light_a01.nif");
        Assert.That(md.Meshes.Where(m => m.NoShadow).Select(m => m.FileName), Is.EquivalentTo(new[] { "Plane", "Plane02" }));
        Assert.That(md.Meshes.Single(m => m.FileName == "decal_test_way_01").NoShadow, Is.False);
    }
}
