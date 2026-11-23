using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// G18 — housing (D18): GMS2 has no table/housing.xml; every item carries &lt;housing filter="100" categoryIndex="10000"
/// trophyLevel="1" interiorLevel="1"&gt; (gms/item/0/00/00000001.xml). KMS table/housing.xml row 50100000 has
/// categoryIndex="14".
/// </summary>
public class RcG18HousingTests
{
    [Test]
    public void TheKmsHousingTableKeepsItsRow()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var r = new HousingTable(packs).Get(50100000);
        Assert.That(r, Is.Not.Null, "kms table/housing.xml");
        Assert.That(r.CategoryIndex, Is.EqualTo(14));
    }

    [Test]
    [Ignore("G18: GMS2 has no housing.xml and HousingTable does not build rows from the per-item <housing> yet")]
    public void TheGms2ItemReadsItsOwnHousingRow()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var r = new HousingTable(packs).Get(1);
        Assert.That(r, Is.Not.Null, "gms/item/0/00/00000001.xml <housing>");
        Assert.That(r.Filter, Is.EqualTo(100));
        Assert.That(r.CategoryIndex, Is.EqualTo(10000));
        Assert.That(r.InteriorLevel, Is.EqualTo(1));
        Assert.That(r.TrophyLevel, Is.EqualTo(1));
    }
}
