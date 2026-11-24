using Ms2.Formats.Pack;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// G07 — skill level/beginCondition (C1, C6, D1): GMS2 skill/10/10000001.xml level 1 carries
/// beginCondition cooldownTime="0.4" onlyShadowWorld="0" onlyFlyableMap="0" with four weapon rows (0,30)..(0,33);
/// SkillDataTable reads basic/condition and level@cooldown only, so CooldownMs is 0 and Weapons empty. KMS
/// skilldata/100.xml skill 10000001 level 1 has cooldown="400" and the same basic/condition/weapon list.
/// </summary>
public class RcG07SkillBeginConditionTests
{
    static readonly SkillWeapon[] Weapons =
    {
        new(0, 30), new(0, 31), new(0, 32), new(0, 33),
    };

    [Test]
    public void TheKmsSkillKeepsItsCooldownAndWeapons()
    {
        using var packs = new PackSet(RcGap.KmsData());
        var lv = new GameTables(packs, "en").Skills.Skills.GetSkillLevelData(10000001, 1);
        Assert.That(lv, Is.Not.Null, "kms skilldata/100.xml skill 10000001 level 1");
        Assert.That(lv.CooldownMs, Is.EqualTo(400u), "level@cooldown=400");
        Assert.That(lv.Skill.Weapons, Is.EqualTo(Weapons), "basic/condition/weapon");
    }

    [Test]
    [Ignore("G07: SkillDataTable reads basic/condition and level@cooldown only; GMS2 level/beginCondition is unread")]
    public void TheGms2LevelReadsItsBeginCondition()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var lv = new GameTables(packs, "en").Skills.Skills.GetSkillLevelData(10000001, 1);
        Assert.That(lv, Is.Not.Null, "gms/skill/10/10000001.xml level 1");
        Assert.That(lv.CooldownMs, Is.EqualTo(400u), "round(beginCondition@cooldownTime 0.4 * 1000)");
        Assert.That(lv.Skill.Weapons, Is.EqualTo(Weapons), "beginCondition/weapon rows");
        Assert.That(lv.Skill.Condition.OnlyShadowWorld, Is.False);
        Assert.That(lv.Skill.Condition.OnlyFlyableMap, Is.False);
        Assert.That(lv.Skill.Condition.AllowBattleRidingState, Is.False);
    }
}
