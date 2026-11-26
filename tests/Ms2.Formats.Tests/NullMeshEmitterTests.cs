using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// A NiPSMeshEmitter whose mesh refs are null. Before, the reader indexed the block list with the null ref and the whole
/// effect failed to load (a wizard frost nova, three such emitters). The client keeps the null in its list and creates no particle when it
/// picks it (NiPSMeshEmitter.cpp:103-125), so <see cref="EmitterDef.MeshNullShare"/> carries the share of null refs.
/// </summary>
public class NullMeshEmitterTests
{
    [Test]
    public void TheFrostNovaLoadsAndItsNullMeshEmitterEmitsNothing()
    {
        var (packs, spec) = NifEnv.Open();
        var nif = NifFile.Load(spec, packs.Read("Resource/Model/Effect", "ch/skill/wizard/eff_wizard_frostnova_cast_01_a.nif"));
        var sc = EffectScene.Load(nif);
        var mesh = sc.Systems.SelectMany(s => s.Emitters).Where(e => e.Kind == EmitterKind.Mesh).ToList();
        Assert.That(mesh, Has.Count.EqualTo(3));
        foreach (var e in mesh)
        {
            Assert.That(e.MeshNullShare, Is.EqualTo(1), e.Name);
            Assert.That(e.MeshPositions, Is.Null, e.Name);
        }
        // the rest of the effect is read
        Assert.That(sc.Meshes, Is.Not.Empty);
    }

    /// <summary>Every model in both data sets with a mesh emitter loads; its emitters are counted by their refs (all null,
    /// some null, several meshes) and by what the reader takes from the first usable one (positions, or none: a skinned
    /// mesh has bind-pose streams only).</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryMeshEmitterModelLoads(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        var spec = new NifSpec(NifEnv.NifXml());
        var key = System.Text.Encoding.ASCII.GetBytes("NiPSMeshEmitter");
        var count = new SortedDictionary<string, int>(StringComparer.Ordinal);
        void Add(string k) => count[k] = count.GetValueOrDefault(k) + 1;
        var bad = new List<string>();
        foreach (var archive in PackIndex.Archives(dir).Where(a => a.StartsWith("Resource/Model/", StringComparison.Ordinal)))
            foreach (var entry in packs.List(archive).Where(e => e.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e, StringComparer.Ordinal))
            {
                byte[] raw;
                try { raw = packs.Read(archive, entry); } catch (Exception) { continue; }
                if (raw.AsSpan().IndexOf(key) < 0) continue;
                NifFile nif;
                try { nif = NifFile.Load(spec, raw); } catch (Exception) { continue; }
                if (!nif.Blocks.Any(b => b.Type == "NiPSMeshEmitter")) continue;
                Add("models");
                EffectScene sc;
                try { sc = EffectScene.Load(nif); }
                catch (Exception e) { bad.Add($"{archive}:{entry}: {e.GetType().Name} {e.Message}"); continue; }
                foreach (var b in nif.Blocks.Where(b => b.Type == "NiPSMeshEmitter"))
                {
                    var refs = b.Ints("Mesh Emitters");
                    int nulls = refs.Count(r => r < 0 || r >= nif.Blocks.Count);
                    Add("emitters");
                    if (refs.Length == 0) { Add("emitters with no refs"); TestContext.Out.WriteLine($"{env}: no refs: {archive}:{entry}"); }
                    else if (nulls == refs.Length) { Add("emitters with every ref null"); TestContext.Out.WriteLine($"{env}: every ref null: {archive}:{entry}"); }
                    else if (nulls > 0) Add("emitters with some refs null");
                    if (refs.Length > 1) Add("emitters with several refs");
                }
                foreach (var e in sc.Systems.SelectMany(s => s.Emitters).Where(e => e.Kind == EmitterKind.Mesh))
                {
                    Add(e.MeshNullShare >= 1 ? "read: emits nothing" : e.MeshPositions != null ? "read: positions" : "read: no positions (skinned)");
                    if (e.MeshNullShare >= 1 && e.MeshPositions != null) bad.Add($"{archive}:{entry} {e.Name}: no usable ref but positions");
                }
            }
        foreach (var (k, c) in count) TestContext.Out.WriteLine($"{env}:   {k}: {c}");
        Assert.That(bad, Is.Empty, string.Join("\n", bad.Take(20)));
        Assert.That(count.GetValueOrDefault("models"), Is.GreaterThan(0));
    }
}
