using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// G10 — skill damage flags still unread (C3): SkillAttackDamage does not read isConstDamageValue,
/// IgnoreOptimalHideDamageHitEffect/RemainEffect, damageEventDisable or constDamageByTargetSP. GMS2 skill 1's damage row has
/// both flags false; skill 70000212 has isConstDamageValue="1", 70003124 IgnoreOptimalHideDamageHitEffect="true" and
/// 10700302 damageEventDisable="1". KMS skilldata/000.xml skill 1 damage keeps rate 0.2.
/// </summary>
public class RcG10SkillDamageFlagsTests
{
    [Test]
    public void TheKmsSkillOneDamageIsUnchanged()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var d = new GameTables(packs, "en").SkillDamage.Get(1, 1, 0, 0);
        Assert.That(d, Is.Not.Null, "kms skilldata/000.xml skill 1 level 1 motion 0 attack 0");
        Assert.That(d.Rate, Is.EqualTo(0.2f));
        Assert.That(d.HasDamage, Is.True);
    }

    [Test]
    [Ignore("G10: SkillAttackDamage reads none of isConstDamageValue/IgnoreOptimalHideDamageHitEffect/RemainEffect/damageEventDisable/constDamageByTargetSP yet")]
    public void TheGms2DamageFlagsAreRead()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var g = new GameTables(packs, "en");
        var one = g.SkillDamage.Get(1, 1, 0, 0);
        Assert.That(one, Is.Not.Null, "gms/skill/00/00000001.xml skill 1");
        Assert.That(RcGap.Member<bool>(one, "IsConstDamageValue"), Is.False, "isConstDamageValue=\"0\"");
        Assert.That(RcGap.Member<bool>(one, "IgnoreOptimalHideDamageHitEffect"), Is.False);
        Assert.That(RcGap.Member<bool>(one, "IgnoreOptimalHideDamageRemainEffect"), Is.False);
        Assert.That(RcGap.Member<bool>(one, "DamageEventDisable"), Is.False);

        var constant = g.SkillDamage.Get(70000212, 1, 0, 0);
        Assert.That(constant, Is.Not.Null, "gms/skill/70/70000212.xml");
        Assert.That(RcGap.Member<bool>(constant, "IsConstDamageValue"), Is.True, "isConstDamageValue=\"1\"");

        var hidden = g.SkillDamage.Get(70003124, 1, 0, 0);
        Assert.That(hidden, Is.Not.Null, "gms/skill/70/70003124.xml");
        Assert.That(RcGap.Member<bool>(hidden, "IgnoreOptimalHideDamageHitEffect"), Is.True, "IgnoreOptimalHideDamageHitEffect=\"true\"");
        Assert.That(RcGap.Member<bool>(hidden, "IgnoreOptimalHideDamageRemainEffect"), Is.True);

        var noEvent = g.SkillDamage.Get(10700302, 1, 0, 0);
        Assert.That(noEvent, Is.Not.Null, "gms/skill/10/10700302.xml");
        Assert.That(RcGap.Member<bool>(noEvent, "DamageEventDisable"), Is.True, "damageEventDisable=\"1\"");
    }
}
