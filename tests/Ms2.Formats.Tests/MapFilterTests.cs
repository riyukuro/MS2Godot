using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// An effect map's filter mode (TexMap.Filter): the NiTexturingProperty map flags' bits 8-11, which the renderer turns into
/// the map's sampler states (NiDX9FragmentShader::SetupTextures; ms_akD3DFilterMapping, NiDX9Renderer.cpp:2197-2217). The
/// effect shaders take NEAREST, BILERP and TRILERP (MapDecl); every effect map on both data sets is one of those.
/// </summary>
[TestFixture]
public class MapFilterTests
{
    const string Archive = "Resource/Model/Effect";

    /// <summary>The flags' filter bits, and BILERP when the map has no flags (NiTexturingProperty::Map::Map).</summary>
    [Test]
    public void DefaultIsBilerp() => Assert.That(new TexMap().Filter, Is.EqualTo(1));

    /// <summary>The healing spot's column (Editable Mesh, MS2StandardMaterial) samples its base, glow and detail maps
    /// TRILERP: the client's draw reads them LINEAR with a LINEAR mip filter, so a minified column blends its lower mip
    /// levels rather than the top level's texels.</summary>
    [Test]
    public void HealingSpotColumnIsTrilerp()
    {
        var (packs, spec) = NifEnv.Open();
        var sc = EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, "bg/common/eff_com_recovery.nif")));
        var column = sc.Meshes.Single(m => m.Mesh.Name == "Editable Mesh").Mesh;
        Assert.That(column.Maps.Keys, Is.SupersetOf(new[] { "base", "glow", "detail" }));
        foreach (var k in new[] { "base", "glow", "detail" }) Assert.That(column.Maps[k].Filter, Is.EqualTo(2), k);
    }

    /// <summary>Every map of every effect nif's meshes and particle materials, on both data sets, has a filter mode the
    /// effect shaders take (0 NEAREST, 1 BILERP, 2 TRILERP), counted per mode.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryEffectMapHasAShaderFilter(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        var spec = new NifSpec(NifEnv.NifXml());
        var count = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var example = new Dictionary<string, string>();
        var bad = new List<string>();
        int nifs = 0, skipped = 0;
        foreach (var entry in packs.List(Archive).Where(e => e.EndsWith(".nif", StringComparison.Ordinal)))
        {
            EffectScene sc;
            try { sc = EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, entry))); }
            catch (Exception) { skipped++; continue; }
            nifs++;
            var items = sc.Meshes.Select(m => ("mesh", m)).Concat(sc.Systems.Select(s => ("system", s.Material)));
            foreach (var (kind, em) in items)
                foreach (var (slot, map) in em.Mesh.Maps)
                {
                    if (map.Filter is < 0 or > 2) bad.Add($"{entry} {kind} {em.Mesh.Name} {slot}: filter {map.Filter}");
                    string k = $"{kind} filter {map.Filter}";
                    count[k] = count.GetValueOrDefault(k) + 1;
                    example.TryAdd(k, $"{entry} {em.Mesh.Name} {slot}");
                }
        }
        TestContext.Out.WriteLine($"{env}: {nifs} nifs, {skipped} skipped (do not load)");
        foreach (var (k, c) in count) TestContext.Out.WriteLine($"  {k}: {c} (e.g. {example[k]})");
        Assert.That(nifs, Is.GreaterThan(1000));
        Assert.That(bad, Is.Empty, string.Join("\n", bad.Take(20)));
        Assert.That(count.GetValueOrDefault("mesh filter 2"), Is.GreaterThan(0));
        Assert.That(count.GetValueOrDefault("mesh filter 0") + count.GetValueOrDefault("system filter 0"), Is.GreaterThan(0));
        Assert.That(count.GetValueOrDefault("mesh filter 1") + count.GetValueOrDefault("system filter 1"), Is.GreaterThan(0));
    }
}
