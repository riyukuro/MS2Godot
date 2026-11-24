using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// G19 — region spawns (D20): GMS2 has no table/npcregionspawndata.xml; the npc's mainTags/subTags/rareDegree/difficulty
/// are on npc/&lt;..&gt;/&lt;id&gt;.xml environment/basic. GMS2 table/mapspawntag.xml map 02000003 spawn 1 (two tags, difficultyMin 33,
/// difficulty 35) names npc 21000339 by its one main tag (difficulty 34, rareDegree 100): (1+1)*20 + 1 + 100*0.01 + (12-1) = 53,
/// and npc 21000153 (difficulty 33): 52, the weights KMS's table gives there too. KMS loads its own
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
    public void TheGms2NpcTagsGiveTheRegionCandidate()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var t = new RegionSpawnTable(packs);
        var r = t.Find(2000003, 1);
        Assert.That(r, Is.Not.Null, "gms table/mapspawntag.xml map 02000003 spawn 1");
        Assert.That(t.Candidates(r), Is.EqualTo(new[] { (21000153, 52), (21000339, 53) }), "gms npc/21/00/21000153.xml and 21000339.xml");
    }
}
