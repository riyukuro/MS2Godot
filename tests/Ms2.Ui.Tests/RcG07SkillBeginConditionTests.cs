using Ms2.Formats.Pack;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// G07 — skill level/beginCondition (C1, C6, D1): GMS2 skill/10/10000001.xml level 1 carries
/// beginCondition cooldownTime="0.4" onlyShadowWorld="0" onlyFlyableMap="0" with four weapon rows (0,30)..(0,33);
/// KMS skilldata/100.xml skill 10000001 level 1 has cooldown="400" and the same basic/condition/weapon list. SkillDataTable
/// reads the GMS2 cooldown from beginCondition@cooldownTime and the condition from the first level's beginCondition.
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

    [Test]
    public void AGms2RowTakesItsConditionFromTheFirstLevelsBeginCondition()
    {
        // no data: a GMS2-shaped skill (no basic/condition, a beginCondition per level) and the same rows read as a KMS file
        var e = System.Xml.Linq.XElement.Parse("<skill id=\"5\"><basic/>"
            + "<level value=\"1\"><beginCondition cooldownTime=\"0.4\" useCoolDownShowUI=\"1\" onlyFlyableMap=\"1\" allowBattleRidingState=\"1\" defaultRechargingCooldownTime=\"2.5\">"
            + "<weapon lh=\"0\" rh=\"51\"/><weapon lh=\"52\" rh=\"53\"/><requireEffect code=\"7\"/><requireMapCodes code=\"8\"/><hasNotAdditionalEffectGroup id=\"9\"/></beginCondition></level>"
            + "<level value=\"2\"><beginCondition cooldownTime=\"0.4\"><weapon lh=\"0\" rh=\"30\"/></beginCondition></level></skill>");
        static List<System.Xml.Linq.XElement> Rows(System.Xml.Linq.XElement x, string n, string _) => x.Elements(n).ToList();
        var g = SkillData.Load(e, Rows, gms2Row: true);
        Assert.That(g.Weapons, Is.EqualTo(new[] { new SkillWeapon(0, 51), new SkillWeapon(52, 53) }), "the first level's rows");
        var c = g.Condition;
        Assert.That((c.UseCoolDownShowUI, c.OnlyFlyableMap, c.AllowBattleRidingState, c.OnlyShadowWorld, c.DefaultRechargingCooldownTime),
            Is.EqualTo((true, true, true, false, 2.5f)));
        Assert.That((c.RequireEffect.Single(), c.RequireMapCodes.Single(), c.HasNotAdditionalEffectGroup.Single()), Is.EqualTo((7u, 8u, 9)));
        Assert.That(g.GetLevelData(1).CooldownMs, Is.EqualTo(400u));
        // a KMS row reads basic/condition only
        var k = SkillData.Load(e, Rows, gms2Row: false);
        Assert.That((k.Weapons.Count, k.Condition.OnlyFlyableMap), Is.EqualTo((0, false)));
        // a basic/condition, where a file has one, stays the condition
        var both = System.Xml.Linq.XElement.Parse(e.ToString());
        both.Element("basic").Add(System.Xml.Linq.XElement.Parse("<condition onlyShadowWorld=\"1\"><weapon lh=\"0\" rh=\"40\"/></condition>"));
        var b = SkillData.Load(both, Rows, gms2Row: true);
        Assert.That((b.Weapons.Single(), b.Condition.OnlyShadowWorld, b.Condition.OnlyFlyableMap), Is.EqualTo((new SkillWeapon(0, 40), true, false)));
    }
}
