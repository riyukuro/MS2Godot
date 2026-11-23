using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// G13 — job learn level (D8): GMS2 table/job.xml job code 001 has &lt;learn level="2"&gt; 10000001/10000011/10000041 and
/// &lt;learn level="1"&gt; 20000001/20000011; JobTable sets LearnLevel but IsLearn only for level 1 and leaves JobLevel at the
/// sub-job inference. The proven mapping is JobLevel = level - 1, IsLearn = level &lt;= 2. KMS job 1 keeps jobLevel=1,
/// isLearn=1 for 10000001.
/// </summary>
public class RcG13JobLearnLevelTests
{
    [Test]
    public void TheKmsJobKeepsItsLearntSkills()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var j = new GameTables(packs, "en").Skills.Jobs.GetJobData(1);
        Assert.That(j, Is.Not.Null, "kms table/job.xml job 1");
        var s = j.FindSkill(10000001);
        Assert.That(s, Is.Not.Null);
        Assert.That((s.JobLevel, s.IsLearn), Is.EqualTo((1, true)));
    }

    [Test]
    [Ignore("G13: JobTable.LoadGms2 does not map learn level to JobLevel/IsLearn yet")]
    public void TheGms2JobMapsItsLearnLevels()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var j = new GameTables(packs, "en").Skills.Jobs.GetJobData(1);
        Assert.That(j, Is.Not.Null, "gms table/job.xml job code 001");
        var rank = j.FindSkill(10000001);
        Assert.That(rank, Is.Not.Null, "job 001 <learn level=\"2\">");
        Assert.That(rank.LearnLevel, Is.EqualTo(2));
        Assert.That(rank.JobLevel, Is.EqualTo(1), "GMS2 level 2 = KMS jobLevel 1");
        Assert.That(rank.IsLearn, Is.True);
        var novice = j.FindSkill(20000001);
        Assert.That(novice, Is.Not.Null, "job 001 <learn level=\"1\">");
        Assert.That(novice.LearnLevel, Is.EqualTo(1));
        Assert.That(novice.JobLevel, Is.EqualTo(0));
        Assert.That(novice.IsLearn, Is.True);
    }
}
