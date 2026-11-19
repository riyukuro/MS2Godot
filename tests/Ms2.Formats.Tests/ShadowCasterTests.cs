using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>FieldEntity.CastsShadow: FUN_141567000's SetShadowCaster(root, IsCastingShadow) for Mesh and Actor entities.</summary>
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
    public void OtherEntitiesKeepTheirFlag() => Assert.That(Entity("Placeable", "False", "False").CastsShadow, Is.True);

    /// <summary>Tria's water, grass, flowers and fences are Mesh entities placed with IsCastingShadow off (981 of 15186 entities; 19 PhysXWhitebox are not Mesh).</summary>
    [Test]
    public void TriaNatureCastsNone()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var packs = new Ms2.Formats.Pack.PackSet(data);
        var map = FieldMap.Load(packs, new FlatLibrary(packs), "02000001_tw_tria");
        var off = map.Entities.Where(e => !e.CastsShadow).ToList();
        Assert.That(off.Count, Is.EqualTo(981));
        foreach (var name in new[] { "co_fluid_water_B02_", "he_fi_nature_grass_A01_", "he_fi_prop_fence_C02_" })
            Assert.That(map.Entities.Where(e => e.ModelName == name).All(e => !e.CastsShadow), Is.True, name);
        Assert.That(map.Entities.Count(e => e.CastsShadow && e.HasMixin("Mesh")), Is.GreaterThan(5000));
    }
}
