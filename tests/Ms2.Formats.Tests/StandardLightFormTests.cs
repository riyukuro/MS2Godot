using System.Text.RegularExpressions;
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
            }
        }
        int all = byType.Values.Sum();
        TestContext.WriteLine($"{env}: {nifs.Count} actor models, {all} meshes; by type: " +
            string.Join(", ", byType.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")) + $"; {failed.Count} failed to load");
        foreach (var f in failed.Take(20)) TestContext.WriteLine("  " + f);

        Assert.That(packs.Exists(Map, Brick), Is.True, "the 02000110 breakable brick");
        var brick = ModelData.Load(NifFile.Load(spec, packs.Read(Map, Brick)));
        Assert.That(brick.Meshes, Is.Not.Empty);
        Assert.That(brick.Meshes.Select(m => m.Material), Is.All.EqualTo("MS2StandardMaterial"));
        Assert.That(brick.Meshes.All(m => ModelData.IsStandardLightMaterial(m.Material)), Is.True);
        Assert.That(byType.GetValueOrDefault(0), Is.GreaterThan(all / 2), "type 0 is most of the actor meshes");
    }

    static string Source(string relative)
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, relative);
            if (File.Exists(path)) return Regex.Replace(File.ReadAllText(path), @"\s+", " ");
        }
        Assert.Fail($"{relative} not found above the test directory");
        return null!;
    }

    static void Has(string source, string text, string why = null) =>
        Assert.That(source.Contains(text, StringComparison.Ordinal), Is.True, why == null ? $"missing: {text}" : $"missing: {text} ({why})");

    /// <summary>character_lit's standard form: ModelBuilder sets standard_light on a type 0 mesh; the shader then takes the
    /// light's world direction (ms2_sun_dir, as field_lit), MS2Light's plain N.L with no rim, and leaves out the colour boost
    /// and the dye (HandleColorBoost :652-662 and HandleCustomMaps :667-676 are types 1-3 only).</summary>
    [Test]
    public void CharacterShaderHasTheStandardForm()
    {
        var shader = Source("game/shaders/character_lit.gdshader");
        Has(shader, "uniform bool standard_light");
        Has(shader, "global uniform vec3 ms2_sun_dir;");
        Has(shader, "bool std_form = standard_light && !own;");
        Has(shader, "vec3 l = std_form ? -ms2_sun_dir : -light_dir;");
        Has(shader, "float intensity = std_form ? max(dot(l, n), 0.0) : half_lambert + rim;");
        Has(shader, "if (has_ctrl && !standard_light) {", "the dye is types 1-3 only");
        int boosts = Regex.Matches(shader, @"diffuse \*= color_boost;").Count;
        Assert.That(boosts, Is.EqualTo(1));
        Has(shader, "if (!standard_light) { diffuse *= color_boost; }", "the colour boost is types 1-3 only");
        Has(shader, "if (std_form) { diffuse += ms2_point_lights(world_pos, n, 0);",
            "the field's point lights through MS2Light, as field_lit");

        Has(Source("game/scripts/ShaderParam.cs"), "StandardLight = \"standard_light\"");
        var builder = Source("game/scripts/ModelBuilder.cs");
        Has(builder, "sm.SetShaderParameter(ShaderParam.StandardLight, IsStandardLightMaterial(m));");
        Has(builder, "IsStandardLightMaterial(ModelMesh m) => ModelData.IsStandardLightMaterial(m.Material);");
    }
}
