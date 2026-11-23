using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// the skill visibility gap 3 — level/consume: GMS2 skill/10/10100091.xml level 1 has &lt;consume money="0"&gt;&lt;stat ep="40"/&gt;;
/// SkillDataTable reads level@sp/@ep/@meso only, so the 40 EP is 0. KMS skilldata/101.xml level 1 carries ep="40" and is
/// unchanged.
/// </summary>
public class RcSkillvisConsumeTests
{
    [Test]
    public void TheKmsLevelKeepsItsEp()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var lv = new GameTables(packs, "en").Skills.Skills.GetSkillLevelData(10100091, 1);
        Assert.That(lv, Is.Not.Null, "kms skilldata/101.xml skill 10100091 level 1");
        Assert.That(lv.Ep, Is.EqualTo(40), "level@ep=40");
    }

    [Test]
    [Ignore("skill visibility consume: SkillDataTable reads level@sp/@ep/@meso only; GMS2 level/consume/stat@sp|@ep and consume@money are unread")]
    public void TheGms2LevelReadsItsConsumeBlock()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var lv = new GameTables(packs, "en").Skills.Skills.GetSkillLevelData(10100091, 1);
        Assert.That(lv, Is.Not.Null, "gms/skill/10/10100091.xml level 1");
        Assert.That(lv.Ep, Is.EqualTo(40), "consume/stat ep=\"40\"");
        Assert.That(lv.Sp, Is.EqualTo(0));
        Assert.That(lv.Meso, Is.EqualTo(0), "consume money=\"0\"");
    }
}
