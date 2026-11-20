using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The blend form (ModelMesh.Blend, NiDX9RenderState::ApplyAlpha, NiDX9RenderState.cpp:450-464), the vertex colour use
/// (ModelMesh.VertexColors, NiStandardMaterial.cpp:4377-4450) and the opacity they give (ModelMesh.Opacity), on character meshes.</summary>
public class AlphaOpacityTests
{
    static ModelMesh Flags(int alpha, int vertexColor = -1, bool colors = false)
        => new() { AlphaFlags = alpha, VertexColorFlags = vertexColor, Colors = colors ? new float[4] : null };

    /// <summary>SRCALPHA/INVSRCALPHA mixes, SRCALPHA/ONE adds weighted by the opacity, ONE/ONE adds unweighted, no blend bit is opaque.</summary>
    [Test]
    public void BlendForms()
    {
        Assert.That(Flags(0xED).Blend, Is.EqualTo(BlendForm.Mix));
        Assert.That(Flags(0x100D).Blend, Is.EqualTo(BlendForm.Add));
        Assert.That(Flags(1).Blend, Is.EqualTo(BlendForm.AddUnweighted));
        Assert.That(Flags(0xEC).Blend, Is.EqualTo(BlendForm.Opaque));
        Assert.That(Flags(-1).Blend, Is.EqualTo(BlendForm.Opaque));
    }

    /// <summary>EMISSIVE under LIGHTING_E (0x10) and AMB_DIFF under E_A_D (0x28) carry the opacity; source 3 under E_A_D is the
    /// ambient only; AMB_DIFF under LIGHTING_E (0x20) and a mesh without colours use nothing.</summary>
    [Test]
    public void VertexColorTerms()
    {
        Assert.That(Flags(0xED, 0x10, true).VertexColors, Is.EqualTo(new VertexColorUse(true, false, false, true)));
        Assert.That(Flags(0xED, 0x18, true).VertexColors, Is.EqualTo(new VertexColorUse(true, false, false, false)));
        Assert.That(Flags(0xED, 0x28, true).VertexColors, Is.EqualTo(new VertexColorUse(false, true, true, true)));
        Assert.That(Flags(0xED, 0x38, true).VertexColors, Is.EqualTo(new VertexColorUse(false, false, true, false)));
        Assert.That(Flags(0xED, 0x20, true).VertexColors, Is.EqualTo(default(VertexColorUse)));
        Assert.That(Flags(0xED, 0x28, false).VertexColors, Is.EqualTo(default(VertexColorUse)));
    }

    [Test]
    public void OpacityTakesVertexOrMaterialAlpha()
    {
        Assert.That(ModelMesh.Opacity(0.5f, 0.4f, 0.2f, true), Is.EqualTo(0.1f).Within(1e-6));
        Assert.That(ModelMesh.Opacity(0.5f, 0.4f, 0.2f, false), Is.EqualTo(0.2f).Within(1e-6));
    }

    static ModelData Load(PackSet packs, NifSpec spec, string archive, string file, string mesh)
    {
        foreach (var n in packs.List(archive).Where(n => n.EndsWith(file)).OrderBy(n => n))
        {
            var md = ModelData.Load(NifFile.Load(spec, packs.Read(archive, n)));
            if (md.Meshes.Any(m => m.FileName == mesh)) return md;
        }
        Assert.Fail($"{file} with mesh {mesh} not in {archive}");
        return null;
    }

    /// <summary>bluelapenta's "Editable Poly@#1": additive (0x100D), vertex colours as the emissive under LIGHTING_E, so its
    /// opacity is the vertex alpha, which falls to 0 at the fringe where the material alpha would keep it whole.</summary>
    [Test]
    public void BluelapentaFringeFadesByVertexAlpha()
    {
        var (packs, spec) = NifEnv.Open();
        var md = Load(packs, spec, "Resource/Model/Npc", "bluelapenta.nif", "Editable Poly@#1");
        var m = md.Meshes.Single(x => x.FileName == "Editable Poly@#1");
        Assert.That(m.Blend, Is.EqualTo(BlendForm.Add));
        var use = m.VertexColors;
        Assert.That(use.Emissive && use.Opacity, Is.True);
        float lo = 1f;
        for (int i = 3; i < m.Colors.Length; i += 4)
            lo = Math.Min(lo, ModelMesh.Opacity(1f, m.MaterialAlpha, m.Colors[i], use.Opacity));
        Assert.That(lo, Is.LessThan(0.05f));
        Assert.That(m.MaterialAlpha, Is.GreaterThan(0.5f));
    }

    /// <summary>Item 11020395's smoke planes (the m and the f model): additive, material alpha 0, so only their
    /// NiAlphaController makes them show.</summary>
    [TestCase("11020395_m_fhwinter01.nif")]
    [TestCase("11020395_f_fhwinter01.nif")]
    public void WinterHatSmokeIsAnimated(string file)
    {
        var (packs, spec) = NifEnv.Open();
        var md = Load(packs, spec, "Resource/Model/Item", file, "Plane");
        var smoke = md.Meshes.Where(m => m.Blend == BlendForm.Add && m.MaterialAlpha == 0f).ToList();
        Assert.That(smoke.Select(m => m.Name), Does.Contain("Plane"));
        foreach (var m in smoke)
        {
            Assert.That(m.AlphaAnim, Is.Not.Null, m.Name);
            Assert.That(m.AlphaAnim.Values.Max(), Is.GreaterThan(0f), m.Name);
        }
    }
}
