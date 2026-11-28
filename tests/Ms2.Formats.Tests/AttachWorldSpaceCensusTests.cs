using System.Xml.Linq;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// The "World Space" flag of every particle system in the nif parts of the skill effects that ride the actor (attach type 1,
/// <see cref="EffectPart.AttachKind"/>), and of Flame Wave's cast effect by system name. NiPSParticleSystem's flag (nif.xml,
/// NiPSParticleSystem "World Space", default true; NiPSMeshParticleSystem inherits it): on, a particle keeps the world place it
/// was emitted at and only new particles start from the moved emitter (NiPSParticleSystem::UpdateWorldData draws the system
/// with the identity world transform and the emitters emit in world coordinates); off, the particles live in the system node's
/// space and move with it.
/// </summary>
public class AttachWorldSpaceCensusTests
{
    const string Archive = "Resource/Model/Effect";
    const string FlameWave = "effect/ch/skill/wizard/eff_wizard_flamewave_cast_01.xml";

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void AttachedSkillParticleSystemsBySpace(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        var spec = new NifSpec(NifEnv.NifXml());
        var nifs = new SortedSet<string>(StringComparer.Ordinal);
        var flame = new List<string>();
        int xmls = 0, parts = 0;
        foreach (var entry in packs.List("Xml").Where(e => e.StartsWith("effect/", StringComparison.Ordinal) && e.EndsWith(".xml", StringComparison.Ordinal)
                     && e.Contains("/skill/", StringComparison.OrdinalIgnoreCase)))
        {
            XDocument x;
            try { x = PackXml.Parse(packs.Read("Xml", entry)); } catch (Exception) { continue; }
            xmls++;
            foreach (var p in EffectTable.Parse(x))
            {
                if (p.AttachKind != 1 || string.IsNullOrEmpty(p.FilePath) || !p.FilePath.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)) continue;
                parts++;
                string nif = p.FilePath.Replace('\\', '/').ToLowerInvariant();
                nifs.Add(nif);
                if (string.Equals(entry, FlameWave, StringComparison.OrdinalIgnoreCase)) flame.Add(nif);
            }
        }
        int on = 0, off = 0, meshOn = 0, meshOff = 0, loaded = 0, withSystems = 0, offMoving = 0, onMoving = 0;
        var cache = new Dictionary<string, EffectScene>(StringComparer.Ordinal);
        foreach (var nif in nifs)
        {
            if (!packs.Exists(Archive, nif)) continue;
            EffectScene sc;
            try { sc = EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, nif))); } catch (Exception) { continue; }
            cache[nif] = sc;
            loaded++;
            if (sc.Systems.Count > 0) withSystems++;
            foreach (var s in sc.Systems)
            {
                if (Moves(sc, s.Node)) { if (s.WorldSpace) onMoving++; else offMoving++; }
                if (s.MeshParticles) { if (s.WorldSpace) meshOn++; else meshOff++; }
                else if (s.WorldSpace) on++; else off++;
            }
        }
        TestContext.Out.WriteLine($"{env}: {xmls} skill effect xmls, {parts} attach type 1 nif parts, {nifs.Count} nifs ({loaded} loaded, {withSystems} with particle systems)");
        TestContext.Out.WriteLine($"{env}:   quad systems: world space {on}, local {off}; mesh particle systems: world space {meshOn}, local {meshOff}");
        TestContext.Out.WriteLine($"{env}:   systems whose node moves in the file (a keyframed or billboard node on the way to the root): world space {onMoving}, local {offMoving}");
        foreach (var nif in flame.Distinct())
        {
            if (!cache.TryGetValue(nif, out var sc)) { TestContext.Out.WriteLine($"{env}: flame wave {nif}: not loaded"); continue; }
            foreach (var s in sc.Systems)
                TestContext.Out.WriteLine($"{env}: flame wave {nif}: {s.Name} {(s.MeshParticles ? "mesh" : "quad")} world space {(s.WorldSpace ? "on" : "off")} (node {(s.Node >= 0 && s.Node < sc.Nodes.Count ? sc.Nodes[s.Node].Name : "-")}{(Moves(sc, s.Node) ? ", moving" : ", still")})");
        }
        Assert.That(xmls, Is.GreaterThan(100));
        Assert.That(flame, Is.Not.Empty, "the Flame Wave cast effect has an attach type 1 nif part");
        Assert.That(on + off + meshOn + meshOff, Is.GreaterThan(0));
    }

    /// <summary>Whether node <paramref name="i"/>'s transform in the effect changes over time: it or a node on the way to the
    /// root is keyframed or a billboard.</summary>
    internal static bool Moves(EffectScene sc, int i)
    {
        for (; i >= 0 && i < sc.Nodes.Count; i = sc.Nodes[i].Parent)
            if (sc.Nodes[i].Anim != null || sc.Nodes[i].Billboard) return true;
        return false;
    }
}
