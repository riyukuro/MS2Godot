using Ms2.Formats.Pack;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>Where the item bodies come from: itemdata/NNN.xml where the pack has it (KMS), else the entries named item/.../&lt;id&gt;.xml (GMS2).</summary>
public class ItemPerFileTests
{
    [Test]
    public void TheIndexTakesTheIdFromTheFileName()
    {
        var idx = ItemDataTable.PerItemIndex(new[] { "item/1/14/11400016.xml", "item/0/00/00000001.xml", "item/1/14/readme.txt", "effect/item/x.xml", "table/itemdata.xml" });
        Assert.That(idx, Is.EquivalentTo(new Dictionary<int, string> { [11400016] = "item/1/14/11400016.xml", [1] = "item/0/00/00000001.xml" }));
    }

    [Test]
    public void APackWithItemdataFilesIndexesNothing()
    {
        Assert.That(ItemDataTable.PerItemIndex(new[] { "itemdata/114.xml", "item/1/14/11400016.xml" }), Is.Empty);
    }

    [Test]
    public void KmsItemdataStillWinsAndBuildsNoIndex()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = new PackSet(d);
        if (!packs.Exists("Xml", "itemdata/150.xml")) Assert.Ignore("no itemdata/ files in this data");
        var t = new ItemDataTable(packs);
        Assert.That(t.Get(15000307), Is.Not.Null);
        Assert.That(t.PerItemIndexCount, Is.Zero);
        Assert.That(t.PerItemIndexCount, Is.Zero, "the index stays empty where itemdata/ exists");
    }

    [Test]
    public void AKmsMissBuildsNoIndex()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = new PackSet(d);
        if (!packs.Exists("Xml", "itemdata/150.xml")) Assert.Ignore("no itemdata/ files in this data");
        var t = new ItemDataTable(packs);
        Assert.That(t.Get(15099999), Is.Null, "an id that its itemdata file does not hold");
        Assert.That(t.Get(99900001), Is.Null, "an id whose itemdata file does not exist");
        Assert.That(t.PerItemIndexCount, Is.Zero);
    }

    [Test]
    public void Gms2ItemsLoadFromTheirOwnFiles()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = new PackSet(d);
        if (packs.Exists("Xml", "itemdata/114.xml") || !packs.Exists("Xml", "item/1/14/11400016.xml")) Assert.Ignore("not GMS2-layout data");
        var item = new ItemDataTable(packs).Get(11400016);
        Assert.That(item, Is.Not.Null);
        Assert.That(item.GearScore, Is.EqualTo(23));
    }
}
