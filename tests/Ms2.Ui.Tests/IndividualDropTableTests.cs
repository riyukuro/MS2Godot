using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The individual drop boxes (individualItemDrop_Final.xml) as the select-item-box dialog lists them (needs MS2_DATA):
/// structure and counts only, no language's strings.</summary>
public class IndividualDropTableTests
{
    static PackSet packs;

    static PackSet Packs()
    {
        if (packs != null) return packs;
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        return packs = new PackSet(d);
    }

    static int FirstBox(PackSet p)
    {
        foreach (var b in GameStrings.Parse(p.Read("Xml", "table/individualitemdrop_final.xml")).Root.Elements("dropBox"))
            if (FeatureElements.IsAvailable(p, b) && b.Elements("group").Any(g => FeatureElements.IsAvailable(p, g)
                && g.Elements("v").Any(v => FeatureElements.IsAvailable(p, v))))
                return ItemData.Atoi((string)b.Attribute("dropBoxID") ?? "0");
        return 0;
    }

    [Test]
    public void AMissingBoxListsNothing()
    {
        var p = Packs();
        var table = new IndividualDropTable(p);
        var list = new List<ItemDropEntry>();
        Assert.That(table.GetBoxItems(new ItemDataTable(p), -1, 1, 0, false, list), Is.False);
        Assert.That(list, Is.Empty);
        Assert.That(IndividualDropTable.Empty.GetBoxItems(ItemDataTable.Empty, 1, 1, 0, false, list), Is.False);
    }

    [Test]
    public void ALoadedBoxListsItsItemsWithKnownData()
    {
        var p = Packs();
        if (!p.Exists("Xml", "table/individualitemdrop_final.xml")) Assert.Ignore("no individual drop table");
        int id = FirstBox(p);
        Assume.That(id, Is.Not.EqualTo(0));
        var items = new ItemDataTable(p);
        var table = new IndividualDropTable(p);
        Assert.That(table.Has(id));
        var list = new List<ItemDropEntry>();
        Assert.That(table.GetBoxItems(items, id, 1, 2, false, list), Is.True);
        foreach (var e in list) Assert.That(items.Get(e.ItemId), Is.Not.Null);
        // hiding the tooltip-2 entries never adds any
        var shown = new List<ItemDropEntry>();
        table.GetBoxItems(items, id, 1, 2, true, shown);
        Assert.That(shown.Count, Is.LessThanOrEqualTo(list.Count));
        Assert.That(shown.All(e => e.Tooltip != 2));
    }
}
