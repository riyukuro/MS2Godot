using Ms2.Formats.Pack;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// G01 — item rank (D2): GMS2 item/1/12/11200001.xml has no property@rank (KMS itemdata/112.xml says rank 3), and the
/// gaps document concludes no GMS2 field decides rank. The reader is unchanged: GMS Rank 0 (data, not a parse miss) and
/// ItemTooltip.ItemRank falls back to 1; KMS stays 3.
/// </summary>
public class RcG01ItemRankTests
{
    [Test]
    public void TheKmsItemKeepsItsRank()
    {
        using var packs = new PackSet(RcGap.KmsData());
        var items = new ItemDataTable(packs);
        Assert.That(items.Get(11200001)?.Rank, Is.EqualTo(3), "kms itemdata/112.xml property@rank=3");
        Assert.That(ItemTooltip.ItemRank(items, 11200001), Is.EqualTo(3));
    }

    [Test]
    public void TheGms2ItemHasNoRankAndTooltipsAsGradeOne()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var items = new ItemDataTable(packs);
        var d = items.Get(11200001);
        Assert.That(d, Is.Not.Null, "gms/item/1/12/11200001.xml");
        Assert.That(d.Rank, Is.EqualTo(0), "GMS2 has no property@rank");
        Assert.That(ItemTooltip.ItemRank(items, 11200001), Is.EqualTo(1), "the KMS client's fallback for rank 0");
    }
}
