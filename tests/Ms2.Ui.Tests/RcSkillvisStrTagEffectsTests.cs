using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// the skill visibility gap 1 — strTagEffects on the cast's SkillMotionData: GMS2 skill/00/00000001.xml motion has
/// strTagEffects="e0=CH/Weapon/Hand/Eff_Hand_Attack01_Cast_01.xml" (lifted from motionProperty); SkillMotionData, which
/// HudScene.Cast walks by text key, does not carry it. KMS skilldata/000.xml skill 1 has the same motion.
/// </summary>
public class RcSkillvisStrTagEffectsTests
{
    [Test]
    public void TheKmsMotionKeepsItsSequenceAndAttack()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var m = new GameTables(packs, "en").Skills.GetSkillData(1).GetLevelData(1).Motions[0];
        Assert.That(m.SequenceName, Is.EqualTo("Attack_01_A"));
        Assert.That(m.Attacks[0].Point, Is.EqualTo("p0"));
    }

    [Test]
    [Ignore("skill visibility strTagEffects: SkillMotionData does not read strTagEffects/endEffect etc. yet; the cast's text-key walk has no tag map")]
    public void TheGms2MotionExposesItsTagEffects()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var m = new GameTables(packs, "en").Skills.GetSkillData(1).GetLevelData(1).Motions[0];
        var tags = RcGap.Member<List<(string Key, string Path)>>(m, "StrTagEffects");
        Assert.That(tags, Is.EqualTo(new List<(string, string)> { ("e0", "CH/Weapon/Hand/Eff_Hand_Attack01_Cast_01.xml") }));
    }
}
