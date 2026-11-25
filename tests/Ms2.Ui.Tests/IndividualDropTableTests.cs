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

    static int FirstBox(PackSet p, IndividualDropTable table, ItemDataTable items)
    {
        var probe = new List<ItemDropEntry>();
        bool HasKnownItems(int id)
        {
            if (id == 0) return false;
            probe.Clear();
            return table.GetBoxItems(items, id, 1, 2, false, probe) && probe.Count > 0;
        }
        if (p.Exists("Xml", "table/individualitemdrop_final.xml"))
        {
            foreach (var b in GameStrings.Parse(p.Read("Xml", "table/individualitemdrop_final.xml")).Root.Elements("dropBox"))
                if (FeatureElements.IsAvailable(p, b) && b.Elements("group").Any(g => FeatureElements.IsAvailable(p, g)
                    && g.Elements("v").Any(v => FeatureElements.IsAvailable(p, v))))
                {
                    int id = ItemData.Atoi((string)b.Attribute("dropBoxID") ?? "0");
                    if (HasKnownItems(id)) return id;
                }
            return 0;
        }
        string region = TablePath.Region(e => p.Exists("Xml", e));
        static bool IsDropFile(string name, string folder) =>
            name.StartsWith(folder + "individualitemdrop", StringComparison.Ordinal) && name.IndexOf('/', folder.Length) < 0;
        var names = p.List("Xml").Where(n => n.EndsWith(".xml", StringComparison.Ordinal)
                && (IsDropFile(n, "table/") || IsDropFile(n, "table/" + region + "/")))
            .OrderBy(n => n.Count(c => c == '/')).ThenBy(n => n, StringComparer.Ordinal);
        var seen = new HashSet<int>();
        foreach (var name in names)
            foreach (var e in GameStrings.Parse(p.Read("Xml", name)).Root.Elements("individualDropBox"))
                if (FeatureElements.IsAvailable(p, e))
                {
                    int id = ItemData.Atoi((string)e.Attribute("individualDropBoxID") ?? "0");
                    if (seen.Add(id) && HasKnownItems(id)) return id;
                }
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
        var items = new ItemDataTable(p);
        var table = new IndividualDropTable(p);
        int id = FirstBox(p, table, items);
        Assert.That(id, Is.Not.EqualTo(0), "no individual drop box with known items in this data");
        Assert.That(table.Has(id));
        var list = new List<ItemDropEntry>();
        Assert.That(table.GetBoxItems(items, id, 1, 2, false, list), Is.True);
        Assert.That(list, Is.Not.Empty);
        foreach (var e in list) Assert.That(items.Get(e.ItemId), Is.Not.Null);
        // hiding the tooltip-2 entries never adds any
        var shown = new List<ItemDropEntry>();
        table.GetBoxItems(items, id, 1, 2, true, shown);
        Assert.That(shown.Count, Is.LessThanOrEqualTo(list.Count));
        Assert.That(shown.All(e => e.Tooltip != 2));
    }
}
