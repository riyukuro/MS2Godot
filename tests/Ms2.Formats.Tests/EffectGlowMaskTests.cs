using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// The effect meshes that draw a glow mask into the glow target (EffectMesh.GlowMask): a glow map with no blend or a
/// SRCALPHA / INVSRCALPHA blend, the client's glow-click rule for a mesh.
/// </summary>
[TestFixture]
public class EffectGlowMaskTests
{
    const string Archive = "Resource/Model/Effect";

    static ModelMesh Mesh(int alphaFlags, bool glow)
    {
        var m = new ModelMesh { AlphaFlags = alphaFlags };
        if (glow) m.Textures["glow"] = "g.dds";
        return m;
    }

    [Test]
    public void BlendRule()
    {
        Assert.That(new EffectMesh { Mesh = Mesh(0xED, true) }.GlowMask, Is.True, "SRCALPHA / INVSRCALPHA");
        Assert.That(new EffectMesh { Mesh = Mesh(0, true) }.GlowMask, Is.True, "no blend");
        Assert.That(new EffectMesh { Mesh = Mesh(-1, true) }.GlowMask, Is.True, "no alpha property");
        Assert.That(new EffectMesh { Mesh = Mesh(0x100D, true) }.GlowMask, Is.False, "SRCALPHA / ONE");
        Assert.That(new EffectMesh { Mesh = Mesh(0xED, false) }.GlowMask, Is.False, "no glow map");
    }

    /// <summary>The healing spot's blended glow-mapped meshes, the column among them, draw masks (the client's capture has
    /// a mask draw for the column and the ring); its additive meshes do not.</summary>
    [Test]
    public void HealingSpotMasks()
    {
        var (packs, spec) = NifEnv.Open();
        var sc = EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, "bg/common/eff_com_recovery.nif")));
        foreach (var em in sc.Meshes)
            TestContext.Out.WriteLine($"{em.Mesh.Name} flags 0x{em.Mesh.AlphaFlags:X} glow {em.Mesh.Textures.ContainsKey("glow")} mask {em.GlowMask}");
        Assert.That(sc.Meshes.Single(m => m.Mesh.Name == "Editable Mesh").GlowMask, Is.True);
        Assert.That(sc.Meshes.Count(m => m.GlowMask), Is.GreaterThanOrEqualTo(2));
        Assert.That(sc.Meshes.Where(m => m.Mesh.AlphaFlags >= 0 && (m.Mesh.AlphaFlags & 1) != 0 && ((m.Mesh.AlphaFlags >> 5) & 15) == 0).Any(m => m.GlowMask), Is.False);
    }

    /// <summary>Every glow-mapped mesh of every effect nif on both data sets, counted by whether it draws a mask, is left out
    /// for its blend, or draws through an _HO technique (STILL-OPEN: no mask here).</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryGlowMappedEffectMesh(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        var spec = new NifSpec(NifEnv.NifXml());
        var count = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var example = new Dictionary<string, string>();
        int nifs = 0;
        foreach (var entry in packs.List(Archive).Where(e => e.EndsWith(".nif", StringComparison.Ordinal)))
        {
            EffectScene sc;
            try { sc = EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, entry))); }
            catch (Exception) { continue; }
            nifs++;
            foreach (var em in sc.Meshes.Where(m => m.Mesh.Textures.ContainsKey("glow")))
            {
                var f = em.Mesh.AlphaFlags;
                string k = em.GlowMask ? "mask" : em.Ho != null ? "ho (STILL-OPEN)" : $"blend {(f >> 1) & 15}/{(f >> 5) & 15}";
                Assert.That(em.GlowMask, Is.EqualTo(em.Ho == null && (f < 0 || (f & 1) == 0 || ((f >> 1) & 15) == 6 && ((f >> 5) & 15) == 7)), entry);
                count[k] = count.GetValueOrDefault(k) + 1;
                example.TryAdd(k, $"{entry} {em.Mesh.Name}");
            }
        }
        TestContext.Out.WriteLine($"{env}: {nifs} nifs");
        foreach (var (k, c) in count) TestContext.Out.WriteLine($"  {k}: {c} (e.g. {example[k]})");
        Assert.That(nifs, Is.GreaterThan(1000));
        Assert.That(count.GetValueOrDefault("mask"), Is.GreaterThan(0));
    }
}
