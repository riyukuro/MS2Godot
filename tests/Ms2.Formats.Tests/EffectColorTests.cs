using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

public class EffectColorTests
{
    const string Archive = "Resource/Model/Effect";

    static (PackSet, NifSpec) Env()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA"); var nx = Environment.GetEnvironmentVariable("MS2_NIFXML");
        if (string.IsNullOrEmpty(data) || string.IsNullOrEmpty(nx)) Assert.Ignore("MS2_DATA / MS2_NIFXML not set");
        return (new PackSet(data), new NifSpec(nx));
    }

    /// <summary>The quest navigator beam has no colour controller: its orange comes from the NiMaterialProperty emissive colour.</summary>
    [Test]
    public void QuestBeamColourIsTheEmissiveColour()
    {
        var (packs, spec) = Env();
        var nf = NifFile.Load(spec, packs.Read(Archive, "etc/eff_quest_navigation.nif"));
        Assert.That(nf.Blocks.Any(b => b.Type == "NiMaterialColorController"), Is.False);
        var sc = EffectScene.Load(nf);
        var ring = sc.Meshes.First(m => m.Mesh.Name == "Torus01").Mesh;
        Assert.That(ring.Emissive, Is.EqualTo(new[] { 1.0f, 0.74f, 0.34f }).Within(0.01f));
        Assert.That(sc.Unsupported, Does.Not.Contain("NiMaterialColorController"));
    }

    /// <summary>Effects that animate a material colour read it as a rgb curve on the meshes that share the material.</summary>
    [Test]
    public void MaterialColorControllersAreRead()
    {
        var (packs, spec) = Env();
        int found = 0, animated = 0;
        foreach (var e in packs.List(Archive).Where(e => e.EndsWith(".nif")).Take(2500))
        {
            NifFile nf; try { nf = NifFile.Load(spec, packs.Read(Archive, e)); } catch { continue; }
            if (!nf.Blocks.Any(b => b.Type == "NiMaterialColorController")) continue;
            found++;
            var sc = EffectScene.Load(nf);
            Assert.That(sc.Unsupported, Does.Not.Contain("NiMaterialColorController"), e);
            foreach (var a in sc.Meshes.SelectMany(m => m.Colors))
            {
                animated++;
                var v = a.Curve(a.Ctl.Local(0.3));
                Assert.That(v.Length, Is.EqualTo(3)); Assert.That(v.All(double.IsFinite), e);
                Assert.That(a.Target, Is.InRange(0, 3));
            }
            if (animated > 20) break;
        }
        Assert.That(found, Is.GreaterThan(0));
        Assert.That(animated, Is.GreaterThan(0));
    }

    /// <summary>The arrow's material is all zero (black); what it shows comes from its glow map, which adds on top of the lit colour.</summary>
    [Test]
    public void QuestArrowHasAGlowMapAndNoColour()
    {
        var (packs, spec) = Env();
        var sc = EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, "etc/eff_quest_navigation.nif")));
        var arrow = sc.Meshes.First(m => m.Mesh.Name == "Pyramid01").Mesh;
        Assert.That(arrow.Colors, Is.Null);
        Assert.That(arrow.Textures.ContainsKey("glow"));
        Assert.That(arrow.Diffuse.Concat(arrow.Emissive).All(v => v == 0));
    }
}
