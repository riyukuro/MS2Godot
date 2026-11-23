using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The Wizard's Magic Claw cast effect (skill 10300001, e0 of its strTagEffects, CH/Skill/Wizard/
/// Eff_Wizard_MagicClaw_Cast_01.xml) plays eff_wizard_magicclaw_cast_01_a.nif, whose Sphere01 scale keys are 1 at 0.333 s and
/// NaN at 0.7 s, in the KMS and the GMS2 data alike. The node's scale curve is therefore 1 up to the first key and NaN after
/// it: the game's effect runtime (EffectInstance.Place) has to hide what that node carries, not hand the NaN to the renderer.</summary>
public class MagicClawEffectTests
{
    const string Nif = "ch/skill/wizard/eff_wizard_magicclaw_cast_01_a.nif";

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void TheCastSpheresScaleTurnsNaNAfterItsFirstKey(string env)
    {
        var d = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore(env + " is not set");
        using var packs = new PackSet(d);
        var sc = EffectScene.Load(NifFile.Load(new NifSpec(NifEnv.NifXml()), packs.Read("Resource/Model/Effect", Nif)));
        var sphere = sc.Nodes.Single(n => n.Name == "Sphere01");
        Assert.That(sphere.Anim?.S, Is.Not.Null, "the sphere has a scale curve");
        double At(double t) => sphere.Anim.S(sphere.AnimCtl?.Local(t) ?? t);
        Assert.That(At(0.2), Is.EqualTo(1).Within(1e-6), "before the first key");
        Assert.That(double.IsNaN(At(0.5)), "between the keys");
        Assert.That(double.IsNaN(At(0.69)), "up to the NaN key");
        Assert.That(sc.Nodes.Where(n => n.Name != "Sphere01" && n.Anim?.S != null).All(n => double.IsFinite(n.Anim.S(n.AnimCtl?.Local(0.5) ?? 0.5))),
            "only the sphere's scale is NaN");
    }
}
