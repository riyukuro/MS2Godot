using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>Effect mesh materials: the static NiMaterialProperty alpha and the controllers that replace it.</summary>
public class EffectMaterialTests
{
    const string Archive = "Resource/Model/Effect";

    static (PackSet, NifSpec) Env() => NifEnv.Open();

    static EffectScene Load(string entry)
    {
        var (packs, spec) = Env();
        return EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, entry)));
    }

    /// <summary>The portal entrance arrow: every mesh's material is translucent (0.2-0.75), so none of it may draw at full opacity.</summary>
    [Test]
    public void EntranceArrowMeshesAreTranslucent()
    {
        var sc = Load("bg/common/eff_com_entrance_mark.nif");
        Assert.That(sc.Meshes, Is.Not.Empty);
        foreach (var m in sc.Meshes)
            for (double t = 0; t < 2; t += 0.25)
                Assert.That(m.Opacity(t), Is.InRange(0.0, 0.76), m.Mesh.Name);
        Assert.That(sc.Meshes.Any(m => m.Alpha == null && Math.Abs(m.Mesh.MaterialAlpha - 0.25f) < 1e-3f), "a static 0.25 mesh");
    }

    /// <summary>Without a controller the opacity is the static alpha; an NiAlphaController's value replaces it (it animates m_fAlpha).</summary>
    [Test]
    public void AlphaControllerReplacesTheStaticAlpha()
    {
        var sc = Load("bg/common/eff_com_portal_b.nif");
        var still = sc.Meshes.Where(m => m.Alpha == null).ToList();
        Assert.That(still.Any(m => Math.Abs(m.Mesh.MaterialAlpha - 0.05f) < 1e-3f), "the 0.05 glow shell");
        foreach (var m in still) Assert.That(m.Opacity(0.7), Is.EqualTo(m.Mesh.MaterialAlpha).Within(1e-6), m.Mesh.Name);
        var animated = sc.Meshes.Where(m => m.Alpha != null).ToList();
        Assert.That(animated, Is.Not.Empty);
        foreach (var m in animated)
            for (double t = 0; t < 3; t += 0.3)
                Assert.That(m.Opacity(t), Is.EqualTo(m.Alpha(m.AlphaCtl.Local(t))).Within(1e-9), m.Mesh.Name);
    }

    /// <summary>A mesh with no NiMaterialProperty takes Gamebryo's default material alpha of 1.</summary>
    [Test]
    public void NoMaterialIsOpaque()
    {
        var m = new EffectMesh { Mesh = new ModelMesh() };
        Assert.That(m.Opacity(0), Is.EqualTo(1.0));
    }

    /// <summary>The portal arch (portal_b's Box01, additive): diffuse and emissive black, ambient blue. Its colour is the
    /// ambient term alone, never the white an additive mesh without diffuse or emissive used to draw.</summary>
    [Test]
    public void AmbientOnlyMeshKeepsItsAmbientColour()
    {
        var sc = Load("bg/common/eff_com_portal_b.nif");
        var box = sc.Meshes.Single(m => m.Mesh.Name == "Box01");
        Assert.That(box.Mesh.AlphaFlags & 1, Is.EqualTo(1), "blended");
        double[] lit = new double[3], dif = new double[3], amb = new double[3];
        for (double t = 0; t < 3; t += 0.5)
        {
            box.Shade(t, lit, dif, amb);
            Assert.That(lit, Is.EqualTo(new[] { 0.0, 0.0, 0.0 }).Within(1e-6), "emissive");
            Assert.That(dif, Is.EqualTo(new[] { 0.0, 0.0, 0.0 }).Within(1e-6), "diffuse");
            Assert.That(amb, Is.EqualTo(new[] { 0.1137, 0.3412, 0.9137 }).Within(1e-3), "ambient");
        }
    }

    /// <summary>Without a material: diffuse white (the lights at full), no emissive and no ambient.</summary>
    [Test]
    public void NoMaterialShadesWhite()
    {
        var m = new EffectMesh { Mesh = new ModelMesh() };
        double[] lit = new double[3], dif = new double[3], amb = new double[3];
        m.Shade(0, lit, dif, amb);
        Assert.That(lit, Is.EqualTo(new[] { 0.0, 0.0, 0.0 }));
        Assert.That(dif, Is.EqualTo(new[] { 1.0, 1.0, 1.0 }));
        Assert.That(amb, Is.EqualTo(new[] { 0.0, 0.0, 0.0 }));
    }

    /// <summary>An NiMaterialColorController on the ambient colour (Target 0) replaces it; the diffuse and emissive stay apart
    /// (the diffuse is scaled by the field's lights in the shader, the emissive is not).</summary>
    [Test]
    public void ColourControllersReplaceTheirTerm()
    {
        var m = new EffectMesh { Mesh = new ModelMesh { Ambient = new[] { 1f, 1f, 1f }, Diffuse = new[] { 0.8f, 0.2f, 0f }, Emissive = new[] { 0.5f, 0f, 0f } } };
        m.Colors.Add(new ColorAnim { Target = 0, Curve = _ => new[] { 0.25, 0.5, 0.75 } });
        double[] lit = new double[3], dif = new double[3], amb = new double[3];
        m.Shade(0, lit, dif, amb);
        Assert.That(lit, Is.EqualTo(new[] { 0.5, 0.0, 0.0 }).Within(1e-6));
        Assert.That(dif, Is.EqualTo(new[] { 0.8, 0.2, 0.0 }).Within(1e-6));
        Assert.That(amb, Is.EqualTo(new[] { 0.25, 0.5, 0.75 }));
    }

    /// <summary>The Fairy Tree Lake milkyway ribbons (NiVertexColorProperty 40: AMB_DIFF, E_A_D): the vertex colours are the
    /// diffuse and ambient terms, so the material's grey diffuse and ambient (0.71) drop out of the shading and the vertex alpha,
    /// which fades to 0 at the ribbon ends, is the opacity (NiStandardMaterial::HandleInitialSpecAmbDiffEmissiveColor, NiStandardMaterial.cpp:4377-4450).</summary>
    [Test]
    public void MilkywayRibbonTakesItsVertexColours()
    {
        double[] lit = new double[3], dif = new double[3], amb = new double[3];
        foreach (var entry in new[] { "bg/eliinia/el_eff_milkywaya_02.nif", "bg/eliinia/el_eff_milkywaya_03.nif" })
        {
            var m = Load(entry).Meshes.Single();
            Assert.That(m.Mesh.Colors, Is.Not.Null, entry);
            Assert.That(Enumerable.Range(0, m.Mesh.Colors.Length / 4).Select(i => m.Mesh.Colors[i * 4 + 3]).Min(), Is.EqualTo(0f), entry + ": the ribbon ends fade out");
            m.Shade(0, lit, dif, amb);
            Assert.That(lit, Is.EqualTo(new[] { 0.0, 0.0, 0.0 }).Within(1e-6), entry + ": emissive black");
            Assert.That(dif, Is.EqualTo(new[] { 0.0, 0.0, 0.0 }).Within(1e-6), entry + ": diffuse from the vertices");
            Assert.That(amb, Is.EqualTo(new[] { 0.0, 0.0, 0.0 }).Within(1e-6), entry + ": ambient from the vertices");
            Assert.That(m.VertexColors, Is.EqualTo(new VertexColorUse(false, true, true, true)), entry);
        }
    }

    /// <summary>The vertex colour's terms by source (flags bits 4-5) and lighting mode (bit 3), HandleInitialSpecAmbDiffEmissiveColor's switch
    /// (NiStandardMaterial.cpp:4377-4450): EMISSIVE in both modes (opacity under E), AMB_DIFF and the MS2 source 3 only with
    /// E_A_D, IGNORE never; a mesh without vertex colours or without the property takes none.</summary>
    [Test]
    public void VertexColourTermsBySourceAndLighting()
    {
        var mesh = new ModelMesh { Colors = new float[] { 1, 1, 1, 1 } };
        var e = new EffectMesh { Mesh = mesh };
        foreach (var (flags, want) in new[]
        {
            (0x08, default(VertexColorUse)), (0x00, default),
            (0x18, new VertexColorUse(true, false, false, false)), (0x10, new VertexColorUse(true, false, false, true)),
            (0x28, new VertexColorUse(false, true, true, true)), (0x20, default),
            (0x38, new VertexColorUse(false, false, true, false)), (0x30, default),
            (-1, default),
        })
        {
            mesh.VertexColorFlags = flags;
            Assert.That(e.VertexColors, Is.EqualTo(want), $"flags {flags}");
        }
        mesh.Colors = null; mesh.VertexColorFlags = 0x28;
        Assert.That(e.VertexColors, Is.EqualTo(default(VertexColorUse)), "no vertex colour stream");
    }

    /// <summary>NiVertexColorProperty LIGHTING_E (flags bit 3 clear) keeps the emissive term alone: no diffuse, no ambient,
    /// animated or static. Flags with bit 3 set, or no property (-1), keep all three.</summary>
    [Test]
    public void LightingEKeepsOnlyEmissive()
    {
        double[] lit = new double[3], dif = new double[3], amb = new double[3];
        var mat = new ModelMesh { Ambient = new[] { 0.3f, 0.3f, 0.3f }, Diffuse = new[] { 0.5f, 0.5f, 0.5f }, Emissive = new[] { 0.2f, 0.1f, 0f } };
        var e = new EffectMesh { Mesh = mat };
        e.Colors.Add(new ColorAnim { Target = 1, Curve = _ => new[] { 0.9, 0.9, 0.9 } });
        foreach (var (flags, d, a) in new[] { (0, 0.0, 0.0), (8, 0.9, 0.3), (-1, 0.9, 0.3) })
        {
            mat.VertexColorFlags = flags;
            e.Shade(0, lit, dif, amb);
            Assert.That(lit, Is.EqualTo(new[] { 0.2, 0.1, 0.0 }).Within(1e-6), $"flags {flags}");
            Assert.That(dif, Is.EqualTo(new[] { d, d, d }).Within(1e-6), $"flags {flags}");
            Assert.That(amb, Is.EqualTo(new[] { a, a, a }).Within(1e-6), $"flags {flags}");
        }
        // the shadow spin cast: its LIGHTING_E meshes (grey ambient and diffuse, black emissive) shade black, its others keep their terms
        var sc = Load("ch/skill/thief/eff_thief_skill_shadow_spin_cast.nif");
        var modeE = sc.Meshes.Where(m => m.LightingE).ToList();
        Assert.That(modeE, Has.Count.GreaterThanOrEqualTo(10));
        foreach (var m in modeE)
        {
            Assert.That(m.Mesh.Diffuse[0], Is.GreaterThan(0.5f), m.Mesh.Name);
            m.Shade(0.2, lit, dif, amb);
            Assert.That(lit.Concat(dif).Concat(amb), Is.All.EqualTo(0.0), m.Mesh.Name);
        }
        Assert.That(sc.Meshes.Any(m => !m.LightingE), "meshes with LIGHTING_E_A_D");
    }

    /// <summary>The healing spot pillars (bg/common/eff_com_recovery.nif PArray01): the block names no material, so it is the
    /// default NiStandardMaterial (light form 1), and its NiVertexColorProperty is AMB_DIFF with E_A_D, so the key colour is the
    /// diffuse and ambient. Its aligned quads face world +X; under Tria's lights (ambient 1 x dimmer 0.8 x 1.1 without HDR,
    /// a white sun at 0.8 along (0.501, -0.214, -0.839)) N . L is below 0, so the sun adds nothing and a pillar shades
    /// emissive + key colour x 0.88. The effect shading used to take the diffuse at a full white light: key colour x 1.88,
    /// which drew the pillars about twice too bright and showed their quad edges.</summary>
    [Test]
    public void HealingSpotPillarsTakeTheFieldLights()
    {
        var sc = Load("bg/common/eff_com_recovery.nif");
        var p = sc.Systems.Single(s => s.Name == "PArray01").Material;
        Assert.That(p.ActiveMaterial, Is.Null.Or.Not.StartWith("MS2"), "no MS2 material");
        Assert.That(p.LightForm, Is.EqualTo(1));
        Assert.That(p.VertexColors, Is.EqualTo(new VertexColorUse(false, true, true, true)));
        double[] lit = new double[3], dif = new double[3], amb = new double[3];
        p.Shade(0, lit, dif, amb);
        Assert.That(lit, Is.EqualTo(p.Mesh.Emissive.Select(x => (double)x).ToArray()).Within(1e-6), "the emissive");
        Assert.That(dif.Concat(amb), Is.All.EqualTo(0.0), "the material's diffuse and ambient give way to the key colour");
        double ndl = -(1 * 0.501);   // N (1, 0, 0) . -dir
        var (la, ld) = EffectMesh.Light(p.LightForm, true, ndl, 1 * 0.8 * 1.1, 1 * 0.8, 0);
        Assert.That(la + ld, Is.EqualTo(0.88).Within(1e-9), "the key colour's factor: the ambient light alone");
        // the same quad facing the sun takes it
        var (fa, fd) = EffectMesh.Light(1, true, 1, 0.88, 0.8, 0);
        Assert.That(fa + fd, Is.EqualTo(1.68).Within(1e-9));
    }

    /// <summary>The light forms: none under LIGHTING_E, form 1 adds the sun's ambient colour to the ambient accumulator,
    /// form 2 (MS2Light) puts it into the diffuse weighted by 1 - N.L, and a block without normals keeps the ambient light alone.</summary>
    [Test]
    public void LightFormsByMaterial()
    {
        var mat = new ModelMesh { VertexColorFlags = 8 };
        var e = new EffectMesh { Mesh = mat };
        Assert.That(e.LightForm, Is.EqualTo(1), "no material");
        mat.Material = "MS2StandardMaterial";
        Assert.That(e.LightForm, Is.EqualTo(2));
        mat.Material = "MS2AlphaBoostMaterial";
        Assert.That(e.LightForm, Is.EqualTo(2));
        mat.Material = "NiStandardMaterial";
        Assert.That(e.LightForm, Is.EqualTo(1), "a non-MS2 name");
        mat.VertexColorFlags = 0;
        Assert.That(e.LightForm, Is.EqualTo(0), "LIGHTING_E");
        Assert.That(EffectMesh.Light(0, true, 1, 0.5, 0.8, 0.3), Is.EqualTo((0.0, 0.0)));
        Assert.That(EffectMesh.Light(1, true, 0.25, 0.5, 0.8, 0.3), Is.EqualTo((0.8, 0.2)));
        var (a2, d2) = EffectMesh.Light(2, true, 0.25, 0.5, 0.8, 0.3);
        Assert.That(a2, Is.EqualTo(0.5));
        Assert.That(d2, Is.EqualTo(0.2 + 0.3 * 0.75).Within(1e-12));
        Assert.That(EffectMesh.Light(1, false, 1, 0.5, 0.8, 0.3), Is.EqualTo((0.5, 0.0)), "no normals: the ambient light alone");
        Assert.That(EffectMesh.Light(1, true, -0.5, 0.5, 0.8, 0.3), Is.EqualTo((0.8, 0.0)), "facing away");
    }

    /// <summary>Every effect nif's blocks by light form, on both data sets: LIGHTING_E blocks take none, MS2 standard
    /// materials (and the boost) MS2Light, the rest the default Light fragment. Counts the mesh blocks without normals (the
    /// ambient light alone), the character-type names (half-Lambert, drawn as form 2) and the vertex-coloured blocks per form.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryBlockTakesItsLightForm(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        var spec = new NifSpec(NifEnv.NifXml());
        var count = new SortedDictionary<string, int>(StringComparer.Ordinal);
        void Add(string k) => count[k] = count.GetValueOrDefault(k) + 1;
        var bad = new List<string>();
        int nifs = 0;
        foreach (var entry in packs.List(Archive).Where(e => e.EndsWith(".nif", StringComparison.Ordinal)))
        {
            EffectScene sc;
            try { sc = EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, entry))); }
            catch (Exception) { continue; }
            nifs++;
            var items = sc.Meshes.Select(m => ("mesh", m)).Concat(sc.Systems.Select(s => ("system", s.Material)));
            foreach (var (kind, em) in items)
            {
                int f = em.LightForm, want = em.LightingE ? 0 : ModelData.IsStandardMaterial(em.ActiveMaterial) ? 2 : 1;
                if (f != want) bad.Add($"{entry} {kind} {em.Mesh.Name}: form {f}, want {want}");
                Add($"{kind} form {f}");
                if (em.VertexColors.Any) Add($"{kind} form {f}, vertex colours");
                if (kind == "mesh" && em.Mesh.Normals == null) Add($"mesh form {f}, no normals");
                if (f == 2 && em.ActiveMaterial.Contains("Character", StringComparison.Ordinal) || f == 2 && em.ActiveMaterial.Contains("Chaos", StringComparison.Ordinal))
                    Add($"{kind} {em.ActiveMaterial} (half-Lambert, open)");
            }
        }
        TestContext.Out.WriteLine($"{env}: {nifs} nifs");
        foreach (var (k, c) in count) TestContext.Out.WriteLine($"  {k}: {c}");
        Assert.That(nifs, Is.GreaterThan(1000));
        Assert.That(bad, Is.Empty, string.Join("\n", bad.Take(20)));
        Assert.That(count.GetValueOrDefault("system form 1"), Is.GreaterThan(0));
    }

    const string Flamethrower = "ch/skill/heavygunner/eff_heavygunner_flamethrower_loop_01_a.nif";

    /// <summary>The flamethrower's smoke (PArray01, alpha blended): its quads take the material like a mesh. Its
    /// NiVertexColorProperty is AMB_DIFF with E_A_D (flags 40), so the key colour is the ambient and diffuse, and the material's
    /// emissive (about 0.31) is added to it: a late dark key colour still shades at least that. Before, the particles drew
    /// texture times key colour alone, and the smoke went near-black.</summary>
    [Test]
    public void ParticleSmokeTakesItsMaterialEmissive()
    {
        var sc = Load(Flamethrower);
        var smoke = sc.Systems.Single(s => s.Name == "PArray01");
        var m = smoke.Material;
        Assert.That(m.Mesh.VertexColorFlags, Is.EqualTo(40));
        Assert.That(m.Mesh.Colors, Is.Not.Null, "the system has colours");
        Assert.That(smoke.Additive, Is.False);
        Assert.That(m.VertexColors, Is.EqualTo(new VertexColorUse(false, true, true, true)));
        double[] lit = new double[3], dif = new double[3], amb = new double[3];
        m.Shade(0, lit, dif, amb);
        Assert.That(lit, Is.EqualTo(m.Mesh.Emissive.Select(x => (double)x).ToArray()).Within(1e-6));
        Assert.That(lit, Is.All.GreaterThan(0.3));
        Assert.That(dif.Concat(amb), Is.All.EqualTo(0.0), "the key colour is the diffuse and ambient");
    }

    /// <summary>A colour controller on a system's material reaches the system: the flamethrower's additive PArray05 animates
    /// its ambient, diffuse and emissive (NiMaterialColorController targets 0, 1, 3), and the shaded emissive is the
    /// controller's value, the diffuse and ambient left to the key colour.</summary>
    [Test]
    public void ParticleMaterialColourControllers()
    {
        var sc = Load(Flamethrower);
        var fire = sc.Systems.Single(s => s.Name == "PArray05");
        var m = fire.Material;
        Assert.That(fire.Additive);
        Assert.That(m.Colors.Select(c => c.Target), Is.EquivalentTo(new[] { 0, 1, 3 }));
        var emi = m.Colors.Single(c => c.Target == 3);
        double[] lit = new double[3], dif = new double[3], amb = new double[3];
        for (double t = 0; t < 2; t += 0.25)
        {
            m.Shade(t, lit, dif, amb);
            Assert.That(lit, Is.EqualTo(emi.Curve(emi.Ctl != null ? emi.Ctl.Local(t) : t)).Within(1e-9), $"t {t}");
            Assert.That(dif.Concat(amb), Is.All.EqualTo(0.0));
        }
    }

    /// <summary>A system without colours has no vertex colour stream, so its material terms are all it has; one whose
    /// NiVertexColorProperty ignores the vertex colours (source 0) is the same.</summary>
    [Test]
    public void ParticleWithoutColoursKeepsTheMaterial()
    {
        var mat = new ModelMesh { Diffuse = new[] { 0.5f, 0.5f, 0.5f }, Emissive = new[] { 0.25f, 0.25f, 0.25f }, VertexColorFlags = 40 };
        var e = new EffectMesh { Mesh = mat };
        Assert.That(e.VertexColors.Any, Is.False);
        mat.Colors = Array.Empty<float>();
        Assert.That(e.VertexColors.Diffuse);
        mat.VertexColorFlags = 8;
        Assert.That(e.VertexColors.Any, Is.False);
    }
}
