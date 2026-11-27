using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// The light a character-path mesh takes by the material its NIF names. MS2StandardMaterial::Init (@0x142754010,
/// ms2standardmaterial.cpp:1816-1851) gives MS2StandardMaterial and MS2AlphaBoostMaterial type 0 and the character and chaos
/// materials types 1-3 (GetMaterialType, :465-476); HandleMS2Light (:858-1048) lights type 0 with the "MS2Light" node and the
/// light's own direction, types 1-3 with the character light (m_bCharacterLighting, :1850). The field's actors (trigger,
/// interact and breakable actors, Resource/Model/Map kfm models) are mostly type 0, so they are lit as the field's meshes, not
/// from the camera with the half-lambert, the rim, the colour boost and the dye.
/// </summary>
public class StandardLightFormTests
{
    const string Map = "Resource/Model/Map";
    const string Brick = "common/breakable/sw_break_brick_a01/sw_break_bricka_01.nif";

    [Test]
    public void TheMaterialTypeFollowsTheClientsNames()
    {
        Assert.That(ModelData.StandardMaterialType("MS2StandardMaterial"), Is.EqualTo(0));
        Assert.That(ModelData.StandardMaterialType("MS2AlphaBoostMaterial"), Is.EqualTo(0));
        Assert.That(ModelData.StandardMaterialType("MS2CharacterMaterial"), Is.EqualTo(1));
        Assert.That(ModelData.StandardMaterialType("MS2ChaosMaterial"), Is.EqualTo(1));
        Assert.That(ModelData.StandardMaterialType("MS2CharacterSkinMaterial"), Is.EqualTo(2));
        Assert.That(ModelData.StandardMaterialType("MS2ChaosSkinMaterial"), Is.EqualTo(2));
        Assert.That(ModelData.StandardMaterialType("MS2CharacterHairMaterial"), Is.EqualTo(3));
        Assert.That(ModelData.StandardMaterialType("MS2ChaosHairMaterial"), Is.EqualTo(3));
        foreach (var name in ModelData.StandardMaterialNames)
            Assert.That(ModelData.StandardMaterialType(name), Is.InRange(0, 3), name);
        Assert.That(ModelData.StandardMaterialType("MS2GlassMaterial"), Is.EqualTo(-1));
        Assert.That(ModelData.StandardMaterialType(null), Is.EqualTo(-1));
        Assert.That(ModelData.IsStandardLightMaterial("MS2StandardMaterial"), Is.True);
        Assert.That(ModelData.IsStandardLightMaterial("MS2CharacterMaterial"), Is.False);
        Assert.That(ModelData.IsStandardLightMaterial(null), Is.False);
    }

    /// <summary>Every field actor model (a Map pack kfm's nif, as FieldNpcs.Model loads it): its meshes by material type.
    /// The breakable brick of 02000110 is all type 0, and type 0 is most of the actor meshes on both data sets.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void FieldActorModelsAreMostlyTypeZero(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} is not set");
        using var packs = new PackSet(dir);
        var spec = new NifSpec(NifEnv.NifXml());
        var byType = new Dictionary<int, int>();
        var byForm = new Dictionary<int, int>();
        var others = new Dictionary<string, int>();
        int boosted = 0;
        var nifs = new HashSet<string>();
        var failed = new List<string>();
        foreach (var kfmEntry in packs.List(Map).Where(e => e.EndsWith(".kfm", StringComparison.OrdinalIgnoreCase)))
        {
            string nif;
            try
            {
                string folder = kfmEntry[..kfmEntry.LastIndexOf('/')];
                nif = $"{folder}/{KfmFile.Load(packs.Read(Map, kfmEntry)).Nif.Replace('\\', '/').TrimStart('.', '/').ToLowerInvariant()}";
            }
            catch (Exception ex) { failed.Add($"{kfmEntry}: {ex.Message}"); continue; }
            if (!packs.Exists(Map, nif) || !nifs.Add(nif)) continue;
            ModelData md;
            try { md = ModelData.Load(NifFile.Load(spec, packs.Read(Map, nif))); }
            catch (Exception ex) { failed.Add($"{nif}: {ex.Message}"); continue; }
            foreach (var m in md.Meshes)
            {
                int t = ModelData.StandardMaterialType(m.Material);
                byType[t] = byType.GetValueOrDefault(t) + 1;
                int f = ModelData.LightForm(m.ActiveMaterial);
                byForm[f] = byForm.GetValueOrDefault(f) + 1;
                if (m.AlphaBoost) boosted++;
                if (m.ActiveMaterial != null && ModelData.StandardMaterialType(m.ActiveMaterial) < 0)
                    others[m.ActiveMaterial] = others.GetValueOrDefault(m.ActiveMaterial) + 1;
            }
        }
        int all = byType.Values.Sum();
        TestContext.WriteLine($"{env}: {nifs.Count} actor models, {all} meshes; by type: " +
            string.Join(", ", byType.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")) + $"; {failed.Count} failed to load");
        TestContext.WriteLine($"  by light form: " + string.Join(", ", byForm.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")) +
            $"; {boosted} swapped to MS2AlphaBoostMaterial; other names: " +
            string.Join(", ", others.OrderByDescending(k => k.Value).Select(k => $"{k.Key}={k.Value}")));
        foreach (var f in failed.Take(20)) TestContext.WriteLine("  " + f);

        Assert.That(packs.Exists(Map, Brick), Is.True, "the 02000110 breakable brick");
        var brick = ModelData.Load(NifFile.Load(spec, packs.Read(Map, Brick)));
        Assert.That(brick.Meshes, Is.Not.Empty);
        Assert.That(brick.Meshes.Select(m => m.Material), Is.All.EqualTo("MS2StandardMaterial"));
        Assert.That(brick.Meshes.All(m => ModelData.IsStandardLightMaterial(m.Material)), Is.True);
        Assert.That(byType.GetValueOrDefault(0), Is.GreaterThan(all / 2), "type 0 is most of the actor meshes");
        Assert.That(boosted, Is.GreaterThan(0), "meshes the load swaps to MS2AlphaBoostMaterial");
        Assert.That(byForm.GetValueOrDefault(ModelData.NiStandardLightForm), Is.GreaterThan(0), "meshes naming no material");
    }

    /// <summary>The light form a mesh takes by the material it draws with: types 1-3 the character light, type 0 MS2Light, no
    /// material the renderer's default NiStandardMaterial; and the alpha-boost swap makes any mesh MS2AlphaBoostMaterial
    /// (type 0), a character material too.</summary>
    [Test]
    public void TheLightFormFollowsTheActiveMaterial()
    {
        Assert.That(ModelData.LightForm("MS2StandardMaterial"), Is.EqualTo(ModelData.MS2LightForm));
        Assert.That(ModelData.LightForm("MS2AlphaBoostMaterial"), Is.EqualTo(ModelData.MS2LightForm));
        foreach (var name in new[] { "MS2CharacterMaterial", "MS2CharacterSkinMaterial", "MS2CharacterHairMaterial", "MS2ChaosMaterial" })
            Assert.That(ModelData.LightForm(name), Is.EqualTo(ModelData.CharacterLightForm), name);
        Assert.That(ModelData.LightForm(null), Is.EqualTo(ModelData.NiStandardLightForm));

        var hair = new ModelMesh { Material = "MS2CharacterHairMaterial" };
        Assert.That(hair.ActiveMaterial, Is.EqualTo("MS2CharacterHairMaterial"));
        Assert.That(ModelData.LightForm(hair.ActiveMaterial), Is.EqualTo(ModelData.CharacterLightForm));
        hair.AlphaBoost = true;
        Assert.That(hair.ActiveMaterial, Is.EqualTo("MS2AlphaBoostMaterial"));
        Assert.That(ModelData.LightForm(hair.ActiveMaterial), Is.EqualTo(ModelData.MS2LightForm));
        var unnamed = new ModelMesh { AlphaBoost = true };
        Assert.That(ModelData.LightForm(unnamed.ActiveMaterial), Is.EqualTo(ModelData.MS2LightForm));
    }
}
