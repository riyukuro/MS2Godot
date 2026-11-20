using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// Which EffectMesh entities play through the effect runtime (FieldEntity.PlaysAsEffect). The rule follows Gamebryo's
/// SceneGraphService: an IsStatic False entity's controllers run, whatever archive its model is in. The 02020120 crystal rings
/// (Eff_hv_redlapenta_03_, a map-archive model) were batched as static meshes, and their ring plane has material alpha 0.
/// </summary>
public class FieldEffectRouteTests
{
    static FieldEntity Entity(params string[] mixins)
    {
        var flat = new FlatModel { Name = "m" };
        foreach (var x in mixins) flat.Mixins.Add(x);
        return new FieldEntity { Raw = new XEntity { ModelName = "m" }, Flat = flat };
    }

    static ModelData Model(bool skinned)
    {
        var md = new ModelData();
        md.Meshes.Add(new ModelMesh { Skinned = skinned });
        return md;
    }

    [Test]
    public void EffectMeshPlaysFromAnyArchiveUnlessSkinnedOutsideTheEffectArchive()
    {
        var fx = Entity("EffectMesh");
        Assert.That(fx.PlaysAsEffect(FieldEntity.EffectArchive, Model(false)), Is.True);
        Assert.That(fx.PlaysAsEffect(FieldEntity.EffectArchive, Model(true)), Is.True);   // as before: the effect archive always
        Assert.That(fx.PlaysAsEffect("Resource/Model/Map", Model(false)), Is.True);       // the crystal rings, the light effects
        Assert.That(fx.PlaysAsEffect("Resource/Model/Map", Model(true)), Is.False);       // butterflies: no skinning in the runtime
        Assert.That(fx.PlaysAsEffect(null, Model(false)), Is.False);
        Assert.That(Entity("Mesh").PlaysAsEffect(FieldEntity.EffectArchive, Model(false)), Is.False);
        fx.Raw.Props["IsVisible"] = "False";
        Assert.That(fx.PlaysAsEffect(FieldEntity.EffectArchive, Model(false)), Is.False);
    }

    [Test]
    public void CrystalRingsOf02020120PlayAndTheirRingPlaneShowsOnlyByItsAlphaController()
    {
        var (packs, spec) = NifEnv.Open();
        var assets = AssetIndex.For(packs);
        var map = FieldMap.Load(packs, new FlatLibrary(packs), "02020120_bf");
        var rings = map.Entities.Where(e => e.ModelName == "Eff_hv_redlapenta_03_").ToList();
        Assert.That(rings, Has.Count.GreaterThan(0));
        var (archive, entry) = assets.Resolve(rings[0].Get("NifAsset"));
        Assert.That(archive, Is.EqualTo("Resource/Model/Map"));
        var nif = NifFile.Load(spec, packs.Read(archive, entry));
        var md = ModelData.Load(nif);
        Assert.That(rings.All(e => e.PlaysAsEffect(archive, md)), Is.True);

        // what a static batch drew: the ring plane at its file alpha 0; what the effect runtime draws: the controller's alpha
        var scene = EffectScene.Load(nif);
        Assert.That(scene.Systems, Is.Not.Empty);
        var ring = scene.Meshes.Single(m => m.Mesh.Name == "Editable Poly#75");
        Assert.That(ring.Mesh.MaterialAlpha, Is.EqualTo(0).Within(1e-6));
        Assert.That(ring.Alpha, Is.Not.Null);
        double peak = 0;
        for (double t = 0; t <= Math.Max(scene.Duration, 1); t += 1 / 30.0) peak = Math.Max(peak, ring.Opacity(t));
        Assert.That(peak, Is.EqualTo(1.5).Within(1e-3));   // the controller's peak key
    }

    [Test]
    public void SkinnedMapEffectMeshStaysOnThePropPath()
    {
        var (packs, spec) = NifEnv.Open();
        var assets = AssetIndex.For(packs);
        var flats = new FlatLibrary(packs);
        // 02000556's butterflies: EffectMesh entities whose model (common/etc/ks_npc_butterfly_b01.nif) is skinned
        var e = FieldMap.Load(packs, flats, "02000556_bf").Entities.First(k => k.HasMixin("EffectMesh") && k.Get("NifAsset") is { } l
            && assets.Resolve(l).entry?.EndsWith("ks_npc_butterfly_b01.nif") == true);
        string llid = e.Get("NifAsset");
        var (archive, entry) = assets.Resolve(llid);
        var md = ModelData.Load(NifFile.Load(spec, packs.Read(archive, entry)));
        Assert.That(md.Meshes.Any(m => m.Skinned), Is.True);
        Assert.That(e.PlaysAsEffect(archive, md), Is.False);
    }
}
