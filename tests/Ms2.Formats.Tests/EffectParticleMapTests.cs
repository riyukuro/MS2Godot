using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>A particle system keeps its texturing property's maps (ParticleSystemDef.Material.Mesh.Maps): the quads go through
/// NiStandardMaterial, which applies each map's texture transform and the detail map. The healing spot's PArray01
/// (eff_com_recovery.nif) draws aura_mix_uo2 at scale (2, 0.5) about the centre, clamped, under the detail gradient_light_01
/// at scale (4, 1.4), and adds a glow map; the reader kept only the base texture's file, so the quads drew the streak texture once, untiled,
/// without the detail or the glow.</summary>
public class EffectParticleMapTests
{
    [Test]
    public void RecoveryParticlesKeepTheirBaseTransformAndDetailMap()
    {
        var (packs, spec) = NifEnv.Open();
        var sc = EffectScene.Load(NifFile.Load(spec, packs.Read("Resource/Model/Effect", "bg/common/eff_com_recovery.nif")));
        var ps = sc.Systems.Single(s => s.Name == "PArray01");
        var maps = ps.Material.Mesh.Maps;
        Assert.That(maps.Keys, Is.EquivalentTo(new[] { "base", "detail", "glow" }));
        var b = maps["base"];
        Assert.That(b.File, Does.EndWith("aura_mix_uo2.dds").IgnoreCase);
        Assert.That(ps.Texture, Is.EqualTo(b.File));
        Assert.That(b.Transform, Is.Not.Null);
        Assert.That((b.Transform.SU, b.Transform.SV, b.Transform.CU, b.Transform.CV), Is.EqualTo((2f, 0.5f, 0.5f, 0.5f)));
        Assert.That(b.ClampS && b.ClampT, "the base map's flags 512: clamp S, clamp T");
        var d = maps["detail"];
        Assert.That(d.File, Does.EndWith("gradient_light_01.dds").IgnoreCase);
        Assert.That(d.Transform.SU, Is.EqualTo(4f));
        Assert.That(d.Transform.SV, Is.EqualTo(1.4f).Within(1e-6));
        Assert.That(d.ClampS || d.ClampT, Is.False);
        Assert.That(ps.Material.Mesh.Textures["detail"], Is.EqualTo(d.File));
        Assert.That(maps["glow"].File, Is.Not.Null);
        TestContext.Out.WriteLine($"glow {maps["glow"].File} clamp {maps["glow"].Clamp} transform {maps["glow"].Transform != null}");
    }
}
