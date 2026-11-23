using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// G09 — skill level feature selection (D1): GMS2 skill/10/10600161.xml has two &lt;level value="1"&gt; rows, the first with
/// feature="CharacterBalance_01" and then the plain one; SkillTable.Add keeps both and Row() takes the first. With the
/// feature off (NA) the plain row is the client's pick, so Levels(10600161) must be one row. KMS skilldata/000.xml skill
/// id 1 has one level row and is unchanged.
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
    [Ignore("G09: SkillTable.Add keeps both level rows; FeatureElements.Collect is not applied")]
    public void TheGms2DuplicateLevelRowsCollapseToThePlainRow()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var rows = new SkillTable(packs).Levels(10600161);
        Assert.That(rows, Has.Count.EqualTo(1), "two <level value=\"1\"> rows in gms/skill/10/10600161.xml, one after the feature pick");
        Assert.That(rows[0].Level, Is.EqualTo(1));
        Assert.That(rows[0].Motions, Is.Not.Empty);
    }
}
