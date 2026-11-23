using Ms2.Formats.Pack;
using Ms2.Ui.Shop;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// G21 — shop category in table/na/ (D27): GMS2 keeps meratmarketcategory.xml only under table/na/, with category 100000
/// (tabs 10, 11, 40000, ...); ShopCategoryTable looks for table/meratmarketcategory.xml with packs.Exists, so it never
/// finds it. The ordinary shopcategory.xml tree is unchanged (KMS category 1 has one tab, name 1003).
/// </summary>
public class RcG21ShopCategoryTests
{
    [Test]
    public void TheKmsOrdinaryShopTreeIsUnchanged()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var tabs = new ShopCategoryTable(packs).GetTabs(1);
        Assert.That(tabs, Is.Not.Null, "kms table/shopcategory.xml category 1");
        Assert.That(tabs.Select(t => t.NameId), Is.EqualTo(new[] { 1003 }));
    }

    [Test]
    [Ignore("G21: ShopCategoryTable looks for table/meratmarketcategory.xml only; GMS2 keeps it under table/na/")]
    public void TheGms2MeratMarketTreeLoads()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var tabs = new ShopCategoryTable(packs).GetTabs(100000);
        Assert.That(tabs, Is.Not.Null, "gms table/na/meratmarketcategory.xml category 100000");
        Assert.That(tabs.Select(t => t.Id), Does.Contain(10));
        Assert.That(tabs.Select(t => t.Id), Does.Contain(11));
        Assert.That(tabs.Select(t => t.Id), Does.Contain(40000));
    }
}
