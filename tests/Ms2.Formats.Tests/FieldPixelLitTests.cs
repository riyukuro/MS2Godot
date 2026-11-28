using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// Which field meshes the client lights per pixel without a shadow map (ModelMesh.PixelLit).
/// - NiStandardMaterial::GenerateDescriptor (NiStandardMaterial.cpp:648-666) turns PERVERTEXFORLIGHTS off when the mesh
///   has a shadow map for a light, when ForcePerPixel is set with a light (MS2StandardMaterial::Init sets it only for the
///   character types 1-3, ms2standardmaterial.cpp:1850-1851), or when the descriptor has a normal or parallax map with
///   NORMAL_NBT (NiStandardMaterialDescriptor.cpp:695-728).
/// - So without the sun shadow, a field mesh lights per vertex unless it has a normal or parallax map and a tangent frame.
/// </summary>
public class FieldPixelLitTests
{
    const string Archive = "Resource/Model/Map";

    [Test]
    public void TheRuleNeedsAMapAndATangentFrame()
    {
        var m = new ModelMesh();
        Assert.That(m.PixelLit, Is.False);
        m.Textures["normal"] = "n.dds";
        Assert.That(m.PixelLit, Is.False, "a normal map without tangents has no NBT frame");
        m.Tangents = new float[3];
        Assert.That(m.PixelLit, Is.True);
        m.Textures.Remove("normal");
        m.Textures["parallax"] = "p.dds";
        Assert.That(m.PixelLit, Is.True);
        m.Textures.Remove("parallax");
        m.Textures["bump"] = "b.dds";
        Assert.That(m.PixelLit, Is.False, "a bump map does not count (BUMPMAPCOUNT is not in the rule)");
    }

    /// <summary>Census over every map model: meshes, those with a normal or parallax map, those that also have tangents (lit
    /// per pixel), and the files that do not load.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryMapMeshByLightingForm(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        var spec = new NifSpec(NifEnv.NifXml());
        int nifs = 0, skipped = 0, meshes = 0, mapped = 0, pixel = 0, mappedNoTangents = 0, nifsWithPixel = 0;
        foreach (var entry in packs.List(Archive).Where(e => e.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e, StringComparer.Ordinal))
        {
            nifs++;
            ModelData md;
            try { md = ModelData.Load(NifFile.Load(spec, packs.Read(Archive, entry))); }
            catch (NotSupportedException) { skipped++; continue; }
            bool any = false;
            foreach (var m in md.Meshes)
            {
                meshes++;
                bool map = m.Textures.ContainsKey("normal") || m.Textures.ContainsKey("parallax");
                if (map) mapped++;
                if (map && m.Tangents == null) mappedNoTangents++;
                if (m.PixelLit) { pixel++; any = true; }
                Assert.That(m.PixelLit, Is.EqualTo(map && m.Tangents != null), entry);
            }
            if (any) nifsWithPixel++;
        }
        TestContext.WriteLine($"{env}: {nifs} map nifs ({skipped} skipped), {meshes} meshes: {mapped} with a normal or parallax map, "
            + $"{pixel} of them with tangents (lit per pixel, in {nifsWithPixel} nifs), {mappedNoTangents} without (per vertex); "
            + $"{meshes - pixel} meshes lit per vertex without the sun shadow");
        Assert.That(nifs, Is.GreaterThan(1000));
        Assert.That(meshes - pixel, Is.GreaterThan(0));
    }
}
