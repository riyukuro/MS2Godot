using Ms2.Formats.Pack;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>GMS2 item files write recommendJobs="0" for "any job", so a reward row of the level-up package found no job it suits
/// (quest 70000001, Knight). Needs MS2_GMS2_DATA.</summary>
public class Gms2RecommendJobsTests
{
    static ItemDataTable Items()
    {
        var d = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_GMS2_DATA is not set");
        return new ItemDataTable(new PackSet(d));
    }

    [Test]
    public void ARecommendJobsOfZeroIsAnyJob()
    {
        var item = Items().Get(20301178);   // the package's reward row of quest 70000001
        Assert.That(item, Is.Not.Null);
        foreach (int job in new[] { 1, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110 })
            Assert.That(item.IsJobRecommended(job), Is.True, "job " + job);
    }

    [Test]
    public void AListedJobStaysALimitAndAZeroBesideItIsDropped()
    {
        var env = System.Xml.Linq.XElement.Parse(@"<environment><limit recommendJobs=""0,10"" /></environment>");
        var d = ItemData.Load(1, env);
        Assert.That(d.IsJobRecommended(10), Is.True);
        Assert.That(d.IsJobRecommended(20), Is.False);
        Assert.That(ItemData.Load(1, System.Xml.Linq.XElement.Parse(@"<environment><limit recommendJobs=""0"" /></environment>")).IsJobRecommended(20), Is.True);
    }
}
