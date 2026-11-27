using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// The client's fog-off subtrees (<see cref="NifModel.FogOff"/>, FUN_141567b60 @0x141567b60): a mesh under a "nofog"
/// UserPropBuffer, a flag 0x400 object or an object whose own NiAlphaProperty blends with destination ONE draws without the
/// depth fog (<see cref="ModelMesh.NoFog"/>). In the map models only the last occurs (no "nofog" tag, no flag 0x400, no
/// additive node above a mesh), so every map mesh is fog-off exactly when its own alpha property is additive.
/// </summary>
public class NoFogTests
{
    const string Archive = "Resource/Model/Map";

    // the mesh's own NiAlphaProperty, read from the blocks: blending on (bit 0), destination ONE (bits 5-8 = 0)
    static bool OwnAdditive(NifFile nif, int block)
    {
        foreach (var p in nif.Blocks[block].Ints("Properties"))
        {
            if (p < 0 || nif.Blocks[(int)p].Type != "NiAlphaProperty") continue;
            int f = (int)nif.Blocks[(int)p].Int("Flags");
            return (f & 1) != 0 && ((f >> 5) & 15) == 0;
        }
        return false;
    }

    /// <summary>A lamp's additive glow plane draws unfogged; its lamp body keeps the fog.</summary>
    [Test]
    public void TheLampGlowIsFogOff()
    {
        var (packs, spec) = NifEnv.Open();
        var nif = NifFile.Load(spec, packs.Read(Archive, "cave/field/ca_fi_prop_lamp_a02.nif"));
        var md = ModelData.Load(nif);
        var glow = md.Meshes.Single(m => m.Name == "Plane02");
        Assert.That(glow.NoFog, Is.True);
        Assert.That(md.Meshes.Where(m => !OwnAdditive(nif, m.Block)), Is.Not.Empty);
        Assert.That(md.Meshes.Where(m => !OwnAdditive(nif, m.Block)).All(m => !m.NoFog), Is.True);
    }

    /// <summary>Every map model: NoFog on exactly the meshes whose own alpha property is additive.</summary>
    [Test]
    public void EveryMapMeshIsFogOffExactlyWhenAdditive()
    {
        var (packs, spec) = NifEnv.Open();
        int nifs = 0, fogOff = 0;
        var bad = new List<string>();
        foreach (var entry in packs.List(Archive).Where(e => e.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e, StringComparer.Ordinal))
        {
            NifFile nif; ModelData md;
            try { nif = NifFile.Load(spec, packs.Read(Archive, entry)); md = ModelData.Load(nif); }
            catch (Exception) { continue; }
            nifs++;
            foreach (var m in md.Meshes)
            {
                bool want = OwnAdditive(nif, m.Block);
                if (m.NoFog) fogOff++;
                if (m.NoFog != want && bad.Count < 20) bad.Add($"{entry} {m.Name}: NoFog {m.NoFog}, additive {want}");
            }
        }
        TestContext.Progress.WriteLine($"map nifs {nifs}, fog-off meshes {fogOff}");
        Assert.That(nifs, Is.GreaterThan(1000));
        Assert.That(fogOff, Is.GreaterThan(100));
        Assert.That(bad, Is.Empty);
    }
}
