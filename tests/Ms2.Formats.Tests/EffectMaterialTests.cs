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

    /// <summary>An NiMaterialColorController on the ambient colour (Target 0) replaces it; diffuse + emissive is clamped to 1.</summary>
    [Test]
    public void ColourControllersReplaceTheirTerm()
    {
        var m = new EffectMesh { Mesh = new ModelMesh { Ambient = new[] { 1f, 1f, 1f }, Diffuse = new[] { 0.8f, 0.2f, 0f }, Emissive = new[] { 0.5f, 0f, 0f } } };
        m.Colors.Add(new ColorAnim { Target = 0, Curve = _ => new[] { 0.25, 0.5, 0.75 } });
        double[] lit = new double[3], amb = new double[3];
        m.Shade(0, lit, amb);
        Assert.That(lit, Is.EqualTo(new[] { 1.0, 0.2, 0.0 }).Within(1e-6));
        Assert.That(amb, Is.EqualTo(new[] { 0.25, 0.5, 0.75 }));
    }
}
