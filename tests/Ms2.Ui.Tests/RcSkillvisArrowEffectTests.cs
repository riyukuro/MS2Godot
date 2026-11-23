using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// the skill visibility gap 2 — arrow@effect: GMS2 skill/10/10800001.xml (Assassin Lucky Seven) arrowProperty carries
/// effect="CH/Skill/Assassin/Eff_Assassin_LuckySeven_Ball_01.xml"; SkillArrow.Load reads effectRemain/effectDestroy/
/// effectInvoke only, and no play site uses an arrow effect. KMS skilldata/000.xml skill 1 keeps its nonTarget arrow.
/// </summary>
public class RcSkillvisArrowEffectTests
{
    [Test]
    public void TheKmsArrowIsUnchanged()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var arrow = new GameTables(packs, "en").Skills.GetSkillData(1).GetLevelData(1).Motions[0].Attacks[0].Arrow;
        Assert.That(arrow.IsNonTargetOne, Is.True, "kms skill 1 <arrow nonTarget=\"1\">");
    }

    [Test]
    [Ignore("skill visibility arrow@effect: SkillArrow has no Effect and no magic plays arrow@effect")]
    public void TheGms2ArrowCarriesItsEffect()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var arrow = new GameTables(packs, "en").Skills.Skills.GetSkillLevelData(10800001, 1).Motions[0].Attacks[0].Arrow;
        Assert.That(RcGap.Member<string>(arrow, "Effect"), Is.EqualTo("CH/Skill/Assassin/Eff_Assassin_LuckySeven_Ball_01.xml"));
    }
}
