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
        Assert.That(HousingTable.EntryOf(packs, HousingTable.HousingFile), Is.Not.Null);
        foreach (var f in HousingTable.PriceFiles) Assert.That(HousingTable.EntryOf(packs, f), Is.Not.Null, f);
        var t = new HousingTable(packs);
        Assert.That(t.Count, Is.GreaterThan(0));
        var priced = PackXml.Parse(packs.Read("Xml", HousingTable.EntryOf(packs, HousingTable.PriceFiles[1]))).Root!.Elements("key")
            .Select(k => uint.Parse((string)k.Attribute("id")!)).First(id => t.Get((int)id) != null);
        Assert.That(t.PriceOf(priced), Is.Not.Null);
        Assert.That(t.Get((int)priced).MoneyType, Is.EqualTo(t.PriceOf(priced)!.Value.MoneyType));
    }
}
