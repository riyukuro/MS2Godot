using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// G19 — region spawns (D20): GMS2 has no table/npcregionspawndata.xml; the npc's mainTags/subTags/rareDegree/difficulty
/// are on npc/&lt;..&gt;/&lt;id&gt;.xml environment/basic. Fixture gms/npc/33/00/33000005.xml (mainTags="\uD558\uBC14\uB9AC\uCE74",
/// rareDegree 100, difficulty 29) against gms/table/mapspawntag.xml map 02000039 spawn 80 (tag="\uD558\uBC14\uB9AC\uCE74,\uC774\uBCA4\uD2B8",
/// difficultyMin 28, difficulty 29), so the weight is (1+1)*20 + 1 + 100*0.01 + (12-0) = 54. KMS loads its own
/// table/npcregionspawndata.xml and mapspawntag.xml.
/// </summary>
public class RcG19RegionSpawnTests
{
    [Test]
    public void TheKmsRegionSpawnTablesStillLoad()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var t = new RegionSpawnTable(packs);
        var r = t.Find(2000145, 1);
        Assert.That(r, Is.Not.Null, "kms table/mapspawntag.xml map 02000145 spawn 1");
        Assert.That(r.Tags, Does.Contain("\uBC84\uC12F"));
        Assert.That(t.HasRegions(2000145), Is.True);
    }

    [Test]
    [Ignore("G19: RegionSpawnTable reads the absent table/npcregionspawndata.xml only")]
    public void TheGms2NpcTagsGiveTheRegionCandidate()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var t = new RegionSpawnTable(packs);
        var r = t.Find(2000039, 80);
        Assert.That(r, Is.Not.Null, "gms/table/mapspawntag.xml map 02000039 spawn 80");
        var c = t.Candidates(r);
        Assert.That(c, Does.Contain((33000005, 54)), "gms/npc/33/00/33000005.xml on tag \uD558\uBC14\uB9AC\uCE74");
    }
}
