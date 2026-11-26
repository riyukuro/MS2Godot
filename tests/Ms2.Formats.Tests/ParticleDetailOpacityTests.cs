using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// A particle system's detail map alpha.
/// - The client: a system block names its material as a mesh does, and NiRenderObject::LoadBinary builds it by that name
///   (NiRenderObject.cpp:385-418, ApplyMaterial / NiMaterialLibrary::CreateMaterial :77-86); MS2MaterialLibrary builds
///   eight names as an MS2StandardMaterial (ms2materiallibrary.cpp:486-500), whose HandleDetailMap multiplies the opacity
///   by the detail alpha (ms2standardmaterial.cpp:681-694, :342-343). A system that names no material draws through the
///   renderer's default NiStandardMaterial, which takes the detail colour only (NiStandardMaterial.cpp:4765-4774).
/// - Here: <see cref="ParticleSystemDef.DetailOpacity"/>; the particle shader multiplies by the detail alpha when it is set.
/// The captain hookfish's smoke (Supersmoke01) draws the opaque aura_00913 under the detail smoke_004, whose alpha makes
/// the round puffs; with the colour only, each puff was a dark translucent square.
/// </summary>
public class ParticleDetailOpacityTests
{
    const string Archive = "Resource/Model/Effect";

    [Test]
    public void HookfishSmokeTakesTheDetailAlphaAndItsUnnamedSystemsDoNot()
    {
        var (packs, spec) = NifEnv.Open();
        var sc = EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, "npc/skill/captainhookfish/eff_captainhookfish_attack_event_01_a_c.nif")));
        var smoke = sc.Systems.Single(s => s.Name == "Supersmoke01");
        Assert.That(smoke.Material.Mesh.Material, Is.EqualTo("MS2StandardMaterial"));
        Assert.That(smoke.Material.Mesh.Maps["detail"].File, Does.EndWith("smoke_004.dds").IgnoreCase);
        Assert.That(smoke.DetailOpacity, Is.True);
        var fire = sc.Systems.Single(s => s.Name == "Supe_fire_add");
        Assert.That(fire.Material.Mesh.Material, Is.Null);
        Assert.That(fire.DetailOpacity, Is.False);
    }

    [Test]
    public void SoftParticlesSystemTakesTheDetailColourOnly()
    {
        var (packs, spec) = NifEnv.Open();
        var sc = EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, "ch/skill/thief/eff_thief_skill_poison_cloud_cube_01.nif")));
        var soft = sc.Systems.Where(s => s.Material.Mesh.Material == "SoftParticlesMaterial").ToList();
        Assert.That(soft, Is.Not.Empty);
        Assert.That(soft.All(s => !s.DetailOpacity));
    }

    /// <summary>The active material name, read here from the block on its own.</summary>
    static string Active(NifFile n, NifStruct b)
    {
        if (!b.TryGetValue("Material Data", out var v) || v is not NifStruct md) return null;
        long num = md.IntOr("Num Materials", 0), act = md.IntOr("Active Material", -1);
        if (num <= 0) return null;
        var names = new List<string>();
        switch (md.TryGetValue("Material Name", out var nv) ? nv : null)
        {
            case long[] l: foreach (var x in l) names.Add(n.Str(x)); break;
            case object[] o: foreach (var x in o) names.Add(x is long li ? n.Str(li) : x is NifStruct st && st.TryGetValue("Index", out var ix) && ix is long q ? n.Str(q) : null); break;
        }
        return names.Count == 0 ? null : names[act >= 0 && act < names.Count ? (int)act : 0];
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EverySystemTakesTheDetailAlphaByItsMaterial(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        var spec = new NifSpec(NifEnv.NifXml());
        var marker = System.Text.Encoding.ASCII.GetBytes("NiPS");
        string[] standard = { "MS2StandardMaterial", "MS2CharacterMaterial", "MS2CharacterSkinMaterial", "MS2CharacterHairMaterial",
                              "MS2AlphaBoostMaterial", "MS2ChaosMaterial", "MS2ChaosSkinMaterial", "MS2ChaosHairMaterial" };
        int systems = 0, opacity = 0, opacityDetail = 0;
        var byName = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var bad = new List<string>();
        foreach (var entry in packs.List(Archive).Where(e => e.EndsWith(".nif", StringComparison.Ordinal)).OrderBy(e => e, StringComparer.Ordinal))
        {
            var bytes = packs.Read(Archive, entry);
            if (bytes.AsSpan().IndexOf(marker) < 0) continue;
            NifFile nif;
            EffectScene sc;
            try { nif = NifFile.Load(spec, bytes); sc = EffectScene.Load(nif); }
            catch (Exception) { continue; }   // the nifs that do not load are listed by the particle census, not here
            foreach (var s in sc.Systems)
            {
                systems++;
                var name = Active(nif, nif.Blocks[s.Material.Mesh.Block]);
                byName[name ?? "(none)"] = byName.GetValueOrDefault(name ?? "(none)") + 1;
                if (s.Material.Mesh.Material != name) bad.Add($"{entry} {s.Name}: material {s.Material.Mesh.Material}, the block names {name}");
                bool want = name != null && Array.IndexOf(standard, name) >= 0;
                if (s.DetailOpacity != want) bad.Add($"{entry} {s.Name}: {name} DetailOpacity {s.DetailOpacity}");
                if (s.DetailOpacity) { opacity++; if (s.Material.Mesh.Maps.TryGetValue("detail", out var dm) && dm.File != null) opacityDetail++; }
            }
        }
        TestContext.Out.WriteLine($"{env}: {systems} systems, {opacity} under an MS2StandardMaterial, {opacityDetail} of those with a detail map");
        foreach (var (k, c) in byName) TestContext.Out.WriteLine($"  {k}: {c}");
        Assert.That(bad, Is.Empty, string.Join("\n", bad.Take(20)));
        Assert.That(opacity, Is.GreaterThan(0));
        Assert.That(byName.Keys, Does.Contain("(none)"));
    }
}
