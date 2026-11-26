using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// A particle system's and an effect mesh's detail map alpha.
/// - The client: a system block names its material as a mesh does, and NiRenderObject::LoadBinary builds it by that name
///   (NiRenderObject.cpp:385-418, ApplyMaterial / NiMaterialLibrary::CreateMaterial :77-86); MS2MaterialLibrary builds
///   eight names as an MS2StandardMaterial (ms2materiallibrary.cpp:486-500), whose HandleDetailMap multiplies the opacity
///   by the detail alpha (ms2standardmaterial.cpp:681-694, :342-343). A system that names no material draws through the
///   renderer's default NiStandardMaterial, which takes the detail colour only (NiStandardMaterial.cpp:4765-4774).
/// - Here: <see cref="EffectMesh.DetailOpacity"/> and <see cref="ParticleSystemDef.DetailOpacity"/>; the mesh and particle
///   shaders multiply by the detail alpha when it is set. The _HO names are effect shader techniques that read the detail
///   as a red mask (<see cref="HoMaterialTests"/>), and a block boosted to MS2AlphaBoostMaterial takes the detail alpha
///   whatever it names (<see cref="ModelData.AlphaBoost"/>).
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
    public void AnActiveIndexPastTheNamesIsNoMaterial()
    {
        // GetActiveMaterial returns null for an index past the materials (NO_MATERIAL is 0xFFFFFFFF;
        // NiRenderObject.inl:90-95), and the renderer draws the block with its default material
        // (NiDX9Renderer.cpp:5004-5006). No shipped block has one; this edits the hookfish smoke and the boss portal.
        var (packs, spec) = NifEnv.Open();
        var n = NifFile.Load(spec, packs.Read(Archive, "npc/skill/captainhookfish/eff_captainhookfish_attack_event_01_a_c.nif"));
        int blk = Enumerable.Range(0, n.Blocks.Count).Single(i => n.Blocks[i].Type.EndsWith("ParticleSystem", StringComparison.Ordinal) && n.Name(i) == "Supersmoke01");
        var md = n.Blocks[blk].Struct("Material Data");
        md["Active Material"] = 0xFFFFFFFFL;
        var smoke = EffectScene.Load(n).Systems.Single(s => s.Name == "Supersmoke01");
        Assert.That(smoke.Material.Mesh.Material, Is.Null);
        Assert.That(smoke.DetailOpacity, Is.False);
        md["Active Material"] = 1L;
        Assert.That(EffectScene.Load(n).Systems.Single(s => s.Name == "Supersmoke01").Material.Mesh.Material, Is.Null);

        var p = NifFile.Load(spec, packs.Read(Archive, "bg/common/eff_bossportal_a01_opened.nif"));
        var portal = EffectScene.Load(p).Meshes.First(m => m.Mesh.Material == "MS2StandardMaterial" && m.DetailOpacity);
        p.Blocks[portal.Mesh.Block].Struct("Material Data")["Active Material"] = 0xFFFFFFFFL;
        var edited = EffectScene.Load(p).Meshes.Single(m => m.Mesh.Block == portal.Mesh.Block);
        Assert.That(edited.Mesh.Material, Is.Null);
        Assert.That(edited.DetailOpacity, Is.False);
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

    [Test]
    public void EffectMeshesTakeTheDetailAlphaOnlyUnderTheStandardNames()
    {
        var (packs, spec) = NifEnv.Open();
        EffectMesh One(string nif, string material)
        {
            var sc = EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, nif)));
            return sc.Meshes.First(m => m.Mesh.Material == material && m.Mesh.Maps.TryGetValue("detail", out var d) && d.File != null);
        }
        // the yeti face trap's face discs name no material: the one whose alpha keys stay at or below 1 draws the default
        // material, the detail colour only; the two whose alpha key reaches 2 draw MS2AlphaBoostMaterial, the detail alpha
        var yeti = EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, "npc/skill/yetitwoheadwhite/eff_yetitwoheadwhite_facetrap.nif")));
        var faces = yeti.Meshes.Where(m => m.Mesh.Material == null && m.Mesh.Maps.TryGetValue("detail", out var d) && d.File != null).ToList();
        Assert.That(faces.All(m => m.Mesh.Maps["detail"].File.EndsWith("gradient-circle2.dds", StringComparison.OrdinalIgnoreCase)));
        Assert.That(faces.Where(m => !m.AlphaBoost).Select(m => m.Mesh.Name), Is.EqualTo(new[] { "PolyMesh#103" }));
        Assert.That(faces.Where(m => m.AlphaBoost).Select(m => m.Mesh.Name), Is.EquivalentTo(new[] { "Cylinder03", "Cylinder04" }));
        Assert.That(faces.All(m => m.DetailOpacity == m.AlphaBoost));
        Assert.That(One("bg/common/eff_bossportal_a01_opened.nif", "MS2StandardMaterial").DetailOpacity, Is.True);
        Assert.That(One("bg/common/eff_ugc_tank_01_hologram.nif", "MS2CharacterMaterial").DetailOpacity, Is.True);
        // jumpup's _HO meshes draw their technique, which reads the detail red as the mask, not this alpha
        var jump = One("additional/etc/eff_additional_jumpup_01_keep.nif", "EffectUVDistortionMaskGlowWrap_HO");
        Assert.That(jump.Ho?.Name, Is.EqualTo("EffectUVDistortionMaskGlowWrap_HO"));
        Assert.That(jump.DetailOpacity, Is.False);
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
        // an index past the names (NO_MATERIAL is 0xFFFFFFFF) is no material (NiRenderObject.inl:90-95)
        return act >= 0 && act < names.Count ? names[(int)act] : null;
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EverySystemAndMeshTakesTheDetailAlphaByItsMaterial(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        var spec = new NifSpec(NifEnv.NifXml());
        var marker = System.Text.Encoding.ASCII.GetBytes("NiPS");
        string[] standard = { "MS2StandardMaterial", "MS2CharacterMaterial", "MS2CharacterSkinMaterial", "MS2CharacterHairMaterial",
                              "MS2AlphaBoostMaterial", "MS2ChaosMaterial", "MS2ChaosSkinMaterial", "MS2ChaosHairMaterial" };
        int systems = 0, opacity = 0, opacityDetail = 0, meshes = 0, meshOpacity = 0;
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
            foreach (var m in sc.Meshes)
            {
                meshes++;
                var name = Active(nif, nif.Blocks[m.Mesh.Block]);
                if (m.Mesh.Material != name) bad.Add($"{entry} mesh {m.Mesh.Name}: material {m.Mesh.Material}, the block names {name}");
                bool want = HoMaterialTests.Boosted(nif, m.Mesh.Block) || name != null && Array.IndexOf(standard, name) >= 0;
                if (m.DetailOpacity != want) bad.Add($"{entry} mesh {m.Mesh.Name}: {name} DetailOpacity {m.DetailOpacity}");
                if (m.DetailOpacity) meshOpacity++;
            }
            foreach (var s in sc.Systems)
            {
                systems++;
                var name = Active(nif, nif.Blocks[s.Material.Mesh.Block]);
                byName[name ?? "(none)"] = byName.GetValueOrDefault(name ?? "(none)") + 1;
                if (s.Material.Mesh.Material != name) bad.Add($"{entry} {s.Name}: material {s.Material.Mesh.Material}, the block names {name}");
                bool want = HoMaterialTests.Boosted(nif, s.Material.Mesh.Block) || name != null && Array.IndexOf(standard, name) >= 0;
                if (s.DetailOpacity != want) bad.Add($"{entry} {s.Name}: {name} DetailOpacity {s.DetailOpacity}");
                if (s.DetailOpacity) { opacity++; if (s.Material.Mesh.Maps.TryGetValue("detail", out var dm) && dm.File != null) opacityDetail++; }
            }
        }
        TestContext.Out.WriteLine($"{env}: {systems} systems, {opacity} under an MS2StandardMaterial, {opacityDetail} of those with a detail map");
        TestContext.Out.WriteLine($"{env}: {meshes} meshes, {meshOpacity} under an MS2StandardMaterial");
        foreach (var (k, c) in byName) TestContext.Out.WriteLine($"  {k}: {c}");
        Assert.That(bad, Is.Empty, string.Join("\n", bad.Take(20)));
        Assert.That(opacity, Is.GreaterThan(0));
        Assert.That(meshOpacity, Is.GreaterThan(0).And.LessThan(meshes));
        Assert.That(byName.Keys, Does.Contain("(none)"));
    }
}
