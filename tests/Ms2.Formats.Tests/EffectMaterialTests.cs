using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>Effect mesh materials: the static NiMaterialProperty alpha and the controllers that replace it.</summary>
public class EffectMaterialTests
{
    const string Archive = "Resource/Model/Effect";

    static (PackSet, NifSpec) Env()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA"); var nx = Environment.GetEnvironmentVariable("MS2_NIFXML");
        if (string.IsNullOrEmpty(data) || string.IsNullOrEmpty(nx)) Assert.Ignore("MS2_DATA / MS2_NIFXML not set");
        return (new PackSet(data), new NifSpec(nx));
    }

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
}
