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
        double[] lit = new double[3], amb = new double[3];
        for (double t = 0; t < 3; t += 0.5)
        {
            box.Shade(t, lit, amb);
            Assert.That(lit, Is.EqualTo(new[] { 0.0, 0.0, 0.0 }).Within(1e-6), "emissive + diffuse");
            Assert.That(amb, Is.EqualTo(new[] { 0.1137, 0.3412, 0.9137 }).Within(1e-3), "ambient");
        }
    }

    /// <summary>Without a material: diffuse white (full light), no emissive and no ambient.</summary>
    [Test]
    public void NoMaterialShadesWhite()
    {
        var m = new EffectMesh { Mesh = new ModelMesh() };
        double[] lit = new double[3], amb = new double[3];
        m.Shade(0, lit, amb);
        Assert.That(lit, Is.EqualTo(new[] { 1.0, 1.0, 1.0 }));
        Assert.That(amb, Is.EqualTo(new[] { 0.0, 0.0, 0.0 }));
    }

    /// <summary>An NiMaterialColorController on the ambient colour (Target 0) replaces it; diffuse + emissive is not clamped (a sum above 1 stays).</summary>
    [Test]
    public void ColourControllersReplaceTheirTerm()
    {
        var m = new EffectMesh { Mesh = new ModelMesh { Ambient = new[] { 1f, 1f, 1f }, Diffuse = new[] { 0.8f, 0.2f, 0f }, Emissive = new[] { 0.5f, 0f, 0f } } };
        m.Colors.Add(new ColorAnim { Target = 0, Curve = _ => new[] { 0.25, 0.5, 0.75 } });
        double[] lit = new double[3], amb = new double[3];
        m.Shade(0, lit, amb);
        Assert.That(lit, Is.EqualTo(new[] { 1.3, 0.2, 0.0 }).Within(1e-6));
        Assert.That(amb, Is.EqualTo(new[] { 0.25, 0.5, 0.75 }));
    }

    /// <summary>The Fairy Tree Lake milkyway ribbons (NiVertexColorProperty 40: AMB_DIFF, E_A_D): the vertex colours are the
    /// diffuse and ambient terms, so the material's grey diffuse and ambient (0.71) drop out of the shading and the vertex alpha,
    /// which fades to 0 at the ribbon ends, is the opacity (NiStandardMaterial::HandleInitialSpecAmbDiffEmissiveColor, NiStandardMaterial.cpp:4377-4450).</summary>
    [Test]
    public void MilkywayRibbonTakesItsVertexColours()
    {
        double[] lit = new double[3], amb = new double[3];
        foreach (var entry in new[] { "bg/eliinia/el_eff_milkywaya_02.nif", "bg/eliinia/el_eff_milkywaya_03.nif" })
        {
            var m = Load(entry).Meshes.Single();
            Assert.That(m.Mesh.Colors, Is.Not.Null, entry);
            Assert.That(Enumerable.Range(0, m.Mesh.Colors.Length / 4).Select(i => m.Mesh.Colors[i * 4 + 3]).Min(), Is.EqualTo(0f), entry + ": the ribbon ends fade out");
            m.Shade(0, lit, amb);
            Assert.That(lit, Is.EqualTo(new[] { 0.0, 0.0, 0.0 }).Within(1e-6), entry + ": emissive black, diffuse from the vertices");
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
        double[] lit = new double[3], amb = new double[3];
        var mat = new ModelMesh { Ambient = new[] { 0.3f, 0.3f, 0.3f }, Diffuse = new[] { 0.5f, 0.5f, 0.5f }, Emissive = new[] { 0.2f, 0.1f, 0f } };
        var e = new EffectMesh { Mesh = mat };
        e.Colors.Add(new ColorAnim { Target = 1, Curve = _ => new[] { 0.9, 0.9, 0.9 } });
        foreach (var (flags, l, a) in new[] { (0, new[] { 0.2, 0.1, 0.0 }, 0.0), (8, new[] { 1.1, 1.0, 0.9 }, 0.3), (-1, new[] { 1.1, 1.0, 0.9 }, 0.3) })
        {
            mat.VertexColorFlags = flags;
            e.Shade(0, lit, amb);
            Assert.That(lit, Is.EqualTo(l).Within(1e-6), $"flags {flags}");
            Assert.That(amb, Is.EqualTo(new[] { a, a, a }).Within(1e-6), $"flags {flags}");
        }
        // the shadow spin cast: its LIGHTING_E meshes (grey ambient and diffuse, black emissive) shade black, its others keep their terms
        var sc = Load("ch/skill/thief/eff_thief_skill_shadow_spin_cast.nif");
        var modeE = sc.Meshes.Where(m => m.LightingE).ToList();
        Assert.That(modeE, Has.Count.GreaterThanOrEqualTo(10));
        foreach (var m in modeE)
        {
            Assert.That(m.Mesh.Diffuse[0], Is.GreaterThan(0.5f), m.Mesh.Name);
            m.Shade(0.2, lit, amb);
            Assert.That(lit.Concat(amb), Is.All.EqualTo(0.0), m.Mesh.Name);
        }
        Assert.That(sc.Meshes.Any(m => !m.LightingE), "meshes with LIGHTING_E_A_D");
    }
}
