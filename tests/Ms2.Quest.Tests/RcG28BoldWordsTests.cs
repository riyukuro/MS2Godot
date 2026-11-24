using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Quest.Tests;

/// <summary>
/// G28 — BoldWords item ids and NPC friendly (D23): GMS2 has no itemdata/ (items are item/&lt;..&gt;/&lt;id&gt;.xml) and its
/// NPCs write friendly on environment/basic (npc/23/50/23503021.xml friendly="1", npc/22/00/22000052.xml friendly="0").
/// LoadBold scans itemdata/ only and reads environment@friendly, so no item is bold and every NPC is a monster. KMS
/// itemdata/112.xml and npcdata/220.xml are unchanged.
/// </summary>
public class RcG28BoldWordsTests
{
    static ReservedWords Load(PackSet packs)
    {
        var w = new ReservedWords();
        w.LoadBold(packs, (_, _) => true, new Dictionary<int, int>());
        return w;
    }

    [Test]
    public void TheKmsBoldTablesAreUnchanged()
    {
        using var packs = new PackSet(RcGap.KmsData());
        var w = Load(packs);
        Assert.That(RcGap.Field<HashSet<long>>(w, "itemIds"), Does.Contain(11200001L), "kms itemdata/112.xml");
        Assert.That(RcGap.Field<Dictionary<long, int>>(w, "npcFriendly")[22000052], Is.EqualTo(0), "kms npcdata/220.xml environment has no friendly");
    }

    [Test]
    [Ignore("G28: BoldWords scans itemdata/ only and reads environment@friendly; GMS2 item/ files and environment/basic@friendly are unread")]
    public void TheGms2ItemsAndNpcFriendlyLoad()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var w = Load(packs);
        Assert.That(RcGap.Field<HashSet<long>>(w, "itemIds"), Does.Contain(11200001L), "gms/item/1/12/11200001.xml");
        var friendly = RcGap.Field<Dictionary<long, int>>(w, "npcFriendly");
        Assert.That(friendly[22000052], Is.EqualTo(0), "npc/22/00/22000052.xml basic friendly=0");
        Assert.That(friendly[23503021], Is.EqualTo(1), "npc/23/50/23503021.xml basic friendly=1");
    }
}
