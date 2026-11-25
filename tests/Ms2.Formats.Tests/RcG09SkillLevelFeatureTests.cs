using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// G09 — skill level feature selection (D1): a GMS2 skill file can list a level more than once, under features and plain
/// (skill/10/10600161.xml: each level under CharacterBalance_01, then plain). SkillTable.Add takes the rows through the
/// client's level pick (SkillData::Load's FUN_14028faa0 on "level"/"value", FeatureElements.Collect), so a level has the row
/// of the enabled feature, else the plain one. KMS skilldata/000.xml skill id 1 has one level row and is unchanged.
/// </summary>
public class RcG09SkillLevelFeatureTests
{
    [Test]
    public void TheKmsSkillKeepsItsSingleLevelRow()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var rows = new SkillTable(packs).Levels(1);
        Assert.That(rows, Has.Count.EqualTo(1), "kms/skilldata/000.xml skill id 1");
        Assert.That(rows[0].Level, Is.EqualTo(1));
    }

    [Test]
    public void TheGms2DuplicateLevelRowsCollapseToThePlainRow()
    {
        // each level of 10600161 is listed twice, first under CharacterBalance_01 (NA level 100, off at the server's 47), then plain:
        // the pick keeps the twelve plain rows, one per level
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var rows = new SkillTable(packs).Levels(10600161);
        Assert.That(rows.Select(r => r.Level), Is.EqualTo(Enumerable.Range(1, 12)), "gms/skill/10/10600161.xml: 24 <level> rows, 12 after the feature pick");
        Assert.That(rows[0].Motions, Is.Not.Empty);
    }

    [Test]
    public void TheGms2LevelRowOfTheEnabledFeatureWins()
    {
        // 11000031 (Rising Kick) lists each level under CharacterBalance_04 (NA 100, off), _02 (NA 1, on), _01 (off) and plain;
        // SelectFeatureElement keeps the _02 row, whose tag effect is the feature's; the file's first row (_04) has the old one
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var t = new SkillTable(packs);
        Assert.That(t.Levels(11000031).Select(r => r.Level), Is.EqualTo(Enumerable.Range(1, 12)));
        Assert.That(t.Motion(11000031, 1, 0).TagEffects["e0"], Is.EqualTo("CH/Skill_Feature_CharacterBalance_02/Striker/Eff_Striker_RisingKick_Cast_01.xml"));
    }

    [Test]
    public void NoSkillKeepsTwoRowsOfOneLevel()
    {
        // SkillData::Load keys m_levels by the level (CRBMap::SetAt), so the client has one row per level: after the
        // FUN_14028faa0 pick every skill of either data set has distinct level values
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var t = new SkillTable(packs);
        var dup = t.Ids.Where(id => t.Levels(id).GroupBy(r => r.Level).Any(g => g.Count() > 1)).OrderBy(id => id).ToList();
        Assert.That(t.Count, Is.GreaterThan(1000));
        Assert.That(dup, Is.Empty, "skills with two rows of one level");
    }
}
