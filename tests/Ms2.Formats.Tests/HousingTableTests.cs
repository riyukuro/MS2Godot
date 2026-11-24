using System.Linq;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>CItemDataTable::LoadHousing (@0x14055b030), ItemHousing::LoadElement (@0x140548fe0) and CPriceTable::LoadHousing
/// (@0x140738e60) / GetHousing (@0x140738d40).</summary>
public class HousingTableTests
{
    static XDocument X(string s) => XDocument.Parse(s);

    [Test]
    public void ARowWithoutAttributesTakesTheClientsDefaults()
    {
        var t = new HousingTable(X("<ms2><housing id=\"50100001\"/></ms2>"));
        var r = t.Get(50100001);
        Assert.That(r, Is.Not.Null);
        Assert.That((r.Filter, r.CategoryIndex, r.MoneyType, r.Price), Is.EqualTo((100, 10000, 1, 0L)));
        Assert.That((r.TrophyId, r.TrophyLevel, r.InteriorLevel, r.SetItemId), Is.EqualTo((0u, 1u, (ushort)1, 0u)));
        Assert.That(t.Get(50100002), Is.Null);
    }

    [Test]
    public void TheAttributesAreRead()
    {
        var t = new HousingTable(X("<ms2><housing id=\"7\" filter=\"3\" categoryIndex=\"12\" trophyID=\"2300\" trophyLevel=\"4\" interiorLevel=\"6\" setItemID=\"9\"/></ms2>"));
        var r = t.Get(7);
        Assert.That((r.Filter, r.CategoryIndex, r.TrophyId, r.TrophyLevel, r.InteriorLevel, r.SetItemId),
            Is.EqualTo((3, 12, 2300u, 4u, (ushort)6, 9u)));
    }

    [Test]
    public void TheFirstHousingRowOfAnIdStays()
    {
        var t = new HousingTable(X("<ms2><housing id=\"7\" interiorLevel=\"2\"/><housing id=\"7\" interiorLevel=\"5\"/></ms2>"));
        Assert.That(t.Get(7).InteriorLevel, Is.EqualTo(2));
        Assert.That(t.Count, Is.EqualTo(1));
    }

    [Test]
    public void TheMoneyTypeComesFromTheLastPriceRow()
    {
        var maid = X("<ms2><key id=\"7\" ugcHousingDefaultPrice=\"100\" ugcHousingMoneyType=\"2\"/><key id=\"8\" ugcHousingDefaultPrice=\"5\"/></ms2>");
        var all = X("<ms2><key id=\"7\" ugcHousingDefaultPrice=\"300\" ugcHousingMoneyType=\"3\"/></ms2>");
        var t = new HousingTable(X("<ms2><housing id=\"7\"/><housing id=\"8\"/><housing id=\"9\"/></ms2>"), maid, all);
        Assert.That((t.Get(7).MoneyType, t.Get(7).Price), Is.EqualTo((3, 300L)));
        Assert.That((t.Get(8).MoneyType, t.Get(8).Price), Is.EqualTo((1, 5L)), "ugcHousingMoneyType defaults to 1");
        Assert.That((t.Get(9).MoneyType, t.Get(9).Price), Is.EqualTo((1, 0L)), "no price row keeps the row's defaults");
        Assert.That(t.PriceOf(9), Is.Null);
    }

    [Test]
    public void AFileWithoutTheMs2RootIsSkipped()
    {
        var t = new HousingTable(X("<root><housing id=\"7\"/></root>"), X("<root><key id=\"7\" ugcHousingMoneyType=\"3\"/></root>"));
        Assert.That(t.Count, Is.Zero);
        Assert.That(t.PriceOf(7), Is.Null);
    }

    [Test]
    public void TheDataHasHousingRowsAndPrices()
    {
        var data = System.Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data) || !System.IO.Directory.Exists(data)) Assert.Ignore("MS2_DATA not set");
        using var packs = new PackSet(data);
        // the KMS data's housing table, or (GMS2) one file per item with its own <housing>
        Assert.That(HousingTable.EntryOf(packs, HousingTable.HousingFile) != null || PerItemFiles.Index(packs).Count > 0, Is.True);
        foreach (var f in HousingTable.PriceFiles) Assert.That(HousingTable.EntryOf(packs, f), Is.Not.Null, f);
        var t = new HousingTable(packs);
        var priced = PackXml.Parse(packs.Read("Xml", HousingTable.EntryOf(packs, HousingTable.PriceFiles[1]))).Root!.Elements("key")
            .Select(k => uint.Parse((string)k.Attribute("id")!)).First(id => t.Get((int)id) != null);
        Assert.That(t.PriceOf(priced), Is.Not.Null);
        Assert.That(t.Get((int)priced).MoneyType, Is.EqualTo(t.PriceOf(priced)!.Value.MoneyType));
        Assert.That(t.Count, Is.GreaterThan(0));
    }

    /// <summary>
    /// The GMS2 data has no table/housing.xml; each item/&lt;a&gt;/&lt;bb&gt;/&lt;id&gt;.xml holds a &lt;housing&gt; in its environment.
    /// Every item file's row is that element's (with the defaults for a missing attribute) and its price row's money type
    /// and price; an id without a file has none. Before, the table was empty on this data.
    /// </summary>
    [Test]
    public void EveryGms2ItemFileGivesItsHousingRow()
    {
        var data = System.Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(data) || !System.IO.Directory.Exists(data)) Assert.Ignore("MS2_GMS2_DATA not set");
        using var packs = new PackSet(data);
        Assert.That(HousingTable.EntryOf(packs, HousingTable.HousingFile), Is.Null, "the GMS2 data has no housing table");
        var t = new HousingTable(packs);
        var index = PerItemFiles.Index(packs);
        Assert.That(index.Count, Is.GreaterThan(30000));
        int rows = 0, notDefault = 0, priced = 0;
        foreach (var (id, entry) in index)
        {
            var root = PackXml.Parse(packs.Read("Xml", entry)).Root!;
            var h = Ms2.Ui.FeatureElements.SelectEnvironment(packs, root)?.Element("housing");
            var r = t.Get(id);
            if (h == null) { Assert.That(r, Is.Null, $"item {id}"); continue; }
            rows++;
            Assert.That(r, Is.Not.Null, $"item {id}");
            int I(string a, int keep) => int.TryParse((string)h.Attribute(a), out int v) ? v : keep;
            Assert.That((r.ItemId, r.Filter, r.CategoryIndex, (int)r.TrophyId, (int)r.TrophyLevel, (int)r.InteriorLevel),
                Is.EqualTo((id, I("filter", 100), I("categoryIndex", 10000), I("trophyID", 0), I("trophyLevel", 1), I("interiorLevel", 1))),
                $"item {id}");
            var p = t.PriceOf((uint)id);
            Assert.That((r.MoneyType, r.Price), Is.EqualTo((p?.MoneyType ?? 1, p?.DefaultPrice ?? 0L)), $"item {id}");
            if (p != null) priced++;
            if (r.CategoryIndex != 10000 || r.Filter != 100) notDefault++;
        }
        TestContext.Out.WriteLine($"{index.Count} item files, {rows} housing rows, {notDefault} not the default category, {priced} priced");
        Assert.That(rows, Is.GreaterThan(30000));
        Assert.That(notDefault, Is.GreaterThan(1000), "the install items' own categories");
        Assert.That(priced, Is.GreaterThan(0));
        Assert.That(t.Count, Is.EqualTo(rows));
        Assert.That(t.Get(int.MaxValue), Is.Null, "no file, no row");
    }
}
