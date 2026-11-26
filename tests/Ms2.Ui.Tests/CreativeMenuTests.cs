using System.Text.RegularExpressions;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui;
using Ms2.Ui.ChatEmoticons;
using Ms2.Ui.Items;
using Ms2.Ui.Tests;
using NUnit.Framework;

namespace Ms2Godot.Tests;

/// <summary>
/// The debug item browser's catalog (<see cref="ItemCatalog"/>) and spawn line (<see cref="ItemSpawnCommand"/>): every item
/// of the item tables, the name/tab/rank/stack/icon the tables give, search by name and id, and the server command's
/// amount/rarity. Needs MS2_DATA (the KMS2 data); the GMS2 per-item layout has its own test.
/// </summary>
[NonParallelizable]
public class CreativeMenuTests
{
    static PackSet packs;
    static GameTables tables;
    static ItemCatalog catalog;

    static ItemCatalog Catalog()
    {
        if (catalog != null) return catalog;
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs = TestData.Packs(d);
        tables = new GameTables(packs, "en");
        catalog = ItemCatalog.Shared(packs, tables);
        return catalog;
    }

    /// <summary>
    /// Every item id the item tables hold, counted the way ItemNameTests walks the pack (each itemdata/NNN.xml row whose
    /// env is selected, else the per-item files), independently of <see cref="ItemDataTable.Ids"/>.
    /// </summary>
    static HashSet<int> DataIds(ItemDataTable table)
    {
        var ids = new HashSet<int>();
        var file = new Regex(@"^itemdata/\d+\.xml$", RegexOptions.IgnoreCase);
        var entries = packs.List("Xml").ToList();
        foreach (var entry in entries.Where(e => file.IsMatch(e.Replace('\\', '/'))))
            foreach (var e in GameStrings.Parse(packs.Read("Xml", entry)).Root.Elements("item"))
                if (int.TryParse((string)e.Attribute("id"), out int id) && table.Get(id) != null) ids.Add(id);
        foreach (int id in ItemDataTable.PerItemIndex(entries).Keys)
            if (table.Get(id) != null) ids.Add(id);
        return ids;
    }

    [Test]
    public void TheCatalogHasEveryItemOfTheItemTables()
    {
        var c = Catalog();
        var expected = DataIds(tables.Items);
        var got = c.Entries.Select(e => e.Id).ToList();
        TestContext.Out.WriteLine($"catalog {c.Count} items ({c.WithoutName} without a name), data {expected.Count}");
        Assert.That(c.Count, Is.GreaterThan(0));
        Assert.That(got.Count, Is.EqualTo(got.Distinct().Count()), "one entry per id");
        Assert.That(got, Is.Ordered, "ascending id, as the client's id map");
        Assert.That(new HashSet<int>(got), Is.EquivalentTo(expected), "every item of the tables, and no other id");
        Assert.That(c.WithoutName, Is.LessThan(c.Count), "the data has named items");
    }

    [Test]
    public void TheNameTabRankStackAndIconAreTheTables()
    {
        var c = Catalog();
        int tabs = 0, empty = 0;
        for (int tab = 0; tab < 15; tab++)
        {
            int at = -1;
            for (int i = 0; i < c.Count; i++) if (c.Entries[i].Tab == tab) { at = i; break; }
            if (at < 0) { empty++; continue; }
            tabs++;
            var e = c.Entries[at];
            var d = tables.Items.Get(e.Id);
            Assert.That(d, Is.Not.Null, $"{e.Id} has data");
            Assert.That(e.Name, Is.EqualTo(tables.ItemName(e.Id)), $"{e.Id} name");
            Assert.That(e.Tab, Is.EqualTo(d.InventoryTab), $"{e.Id} tab");
            Assert.That(e.Rank, Is.EqualTo(d.Rank), $"{e.Id} rank");
            Assert.That(e.SlotMax, Is.EqualTo((int)d.SlotMax), $"{e.Id} slot max");
            Assert.That(e.IconEntry, Is.EqualTo(ChatEmoticonTable.ResourceImageEntry(d.IconPath)), $"{e.Id} icon entry");
        }
        TestContext.Out.WriteLine($"{tabs} of 15 tabs have items, {empty} are empty");
        Assert.That(tabs, Is.GreaterThanOrEqualTo(10), "the KMS2 item tables fill most tabs");
    }

    /// <summary>CItemData::FUN_14053e530 (itemdata.cpp:1381), every arm of the switch, over hand-made data rows.</summary>
    [Test]
    public void TheTabRuleIsTheClients()
    {
        static int Tab(int type, int skin = 0) => new ItemData { Type = type, SkinType = skin }.InventoryTab;
        Assert.That(Tab(1), Is.EqualTo(0), "an equip");
        Assert.That(Tab(1, 2), Is.EqualTo(1), "an equip with a skin type is the outfit tab");
        foreach (int type in new[] { 6, 8, 0xc, 0x17 }) Assert.That(Tab(type), Is.EqualTo(4), $"type {type}");
        foreach (int type in new[] { 5, 9 }) Assert.That(Tab(type), Is.EqualTo(2), $"type {type}");
        foreach (int type in new[] { 0xd, 0xe }) Assert.That(Tab(type), Is.EqualTo(6), $"type {type}");
        Assert.That(Tab(3), Is.EqualTo(5));
        foreach (int type in new[] { 0xf, 0x13 }) Assert.That(Tab(type), Is.EqualTo(3), $"type {type}");
        Assert.That(Tab(0x10), Is.EqualTo(8));
        Assert.That(Tab(0xb), Is.EqualTo(9));
        Assert.That(Tab(2), Is.EqualTo(10));
        Assert.That(Tab(0x14), Is.EqualTo(11));
        Assert.That(Tab(7), Is.EqualTo(12));
        Assert.That(Tab(0x15), Is.EqualTo(13));
        Assert.That(Tab(0x1b), Is.EqualTo(14));
        foreach (int type in new[] { 0, 4, 10, 0x16, 0x18 }) Assert.That(Tab(type), Is.EqualTo(7), $"type {type} is etc");
    }

    [Test]
    public void SearchFindsByNameAndByIdAndFiltersByTab()
    {
        var c = Catalog();
        var found = new List<int>();

        // a name substring of an item whose name is not empty: every result holds it (or has a matching id when numeric)
        int sample = c.Entries.ToList().FindIndex(e => e.Name.Length >= 3);
        Assert.That(sample, Is.GreaterThanOrEqualTo(0), "a named item");
        string name = c.Entries[sample].Name;
        string text = name.Substring(name.Length / 2, Math.Min(2, name.Length - name.Length / 2));
        c.Filter(text, -1, found);
        Assert.That(found, Does.Contain(sample), $"'{text}' finds {c.Entries[sample].Id}");
        Assert.That(found.All(i => c.Entries[i].Name.Contains(text, StringComparison.OrdinalIgnoreCase)), "name matches only");

        // an id prefix: the item is found and every result matches the name or the id prefix
        var byId = c.Entries[c.Count / 2];
        string prefix = byId.Id.ToString().Substring(0, 4);
        c.Filter(prefix, -1, found);
        Assert.That(found, Does.Contain(c.Count / 2), $"id prefix {prefix}");
        Assert.That(found.All(i => c.Entries[i].Name.Contains(prefix, StringComparison.OrdinalIgnoreCase)
            || c.Entries[i].Id.ToString().StartsWith(prefix, StringComparison.Ordinal)), "name or id prefix");

        // the tab filter: only that tab's items, and the same count as the entries of the tab
        int tab = c.Entries[c.Count / 3].Tab;
        c.Filter("", tab, found);
        Assert.That(found.All(i => c.Entries[i].Tab == tab));
        Assert.That(found.Count, Is.EqualTo(c.Entries.Count(e => e.Tab == tab)));

        // an empty search with no tab lists everything
        c.Filter("", -1, found);
        Assert.That(found.Count, Is.EqualTo(c.Count));
    }

    [Test]
    public void AKeystrokeOverTheCatalogAllocatesNothing()
    {
        var c = Catalog();
        var found = new List<int>();
        c.Filter("a", -1, found);       // warm: the list's capacity grows here
        long bytes = Alloc.Least(() => c.Filter("a", -1, found));
        TestContext.Out.WriteLine($"filter of {c.Count} entries: {bytes} B over {found.Count} matches");
        Assert.That(bytes, Is.Zero, "no allocation per keystroke once the result list has its capacity");
    }

    [Test]
    public void TheSpawnLineClampsTheAmountAndKeepsTheRarity()
    {
        Assert.That(ItemSpawnCommand.Line(20000000, 1, 0, 1), Is.EqualTo("/item 20000000 --amount 1"));
        Assert.That(ItemSpawnCommand.Line(20000000, 99, 0, 50), Is.EqualTo("/item 20000000 --amount 50"), "a stack clamps to the slot max");
        Assert.That(ItemSpawnCommand.Line(20000000, 0, 0, 10), Is.EqualTo("/item 20000000 --amount 1"), "at least one");
        Assert.That(ItemSpawnCommand.Line(20000000, -3, 0, 10), Is.EqualTo("/item 20000000 --amount 1"), "no negative amount");
        Assert.That(ItemSpawnCommand.Line(20000000, 7, 0, 0), Is.EqualTo("/item 20000000 --amount 1"), "a slot max below 1 counts as 1");
        Assert.That(ItemSpawnCommand.Line(20000000, 3, 4, 99), Is.EqualTo("/item 20000000 --amount 3 --rarity 4"));
        Assert.That(ItemSpawnCommand.Line(20000000, 3, 0, 99), Is.EqualTo("/item 20000000 --amount 3"), "rarity 0 is the item's own, sent as no flag");
        Assert.That(ItemSpawnCommand.Line(20000000, 3, -1, 99), Is.EqualTo("/item 20000000 --amount 3"));
        Assert.That(ItemSpawnCommand.Line(20000000, 3, 9, 99), Is.EqualTo("/item 20000000 --amount 3 --rarity 6"), "the server's cap");
    }

    /// <summary>
    /// The GMS2 data keeps one file per item: the catalog's id list is the per-item index's ids with an available
    /// environment, ascending, and the catalog reads them through the same table.
    /// </summary>
    [Test]
    public void TheCatalogBuildsOnThePerItemData()
    {
        var d = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_GMS2_DATA is not set");
        var packs = TestData.Packs(d);
        var tables = new GameTables(packs, "en");
        var entries = packs.List("Xml").ToList();
        if (entries.Any(e => e.StartsWith("itemdata/", StringComparison.OrdinalIgnoreCase))) Assert.Ignore("not the per-item layout");
        var expected = ItemDataTable.PerItemIndex(entries).Keys.Where(id => tables.Items.Get(id) != null).ToHashSet();
        var catalog = ItemCatalog.Build(packs, tables);
        TestContext.Out.WriteLine($"GMS2 catalog {catalog.Count} items, index {expected.Count}");
        Assert.That(catalog.Count, Is.GreaterThan(0));
        Assert.That(catalog.Entries.Select(e => e.Id).ToHashSet(), Is.EquivalentTo(expected));
        Assert.That(catalog.Entries.Select(e => e.Id), Is.Ordered);
        Assert.That(catalog.Entries.All(e => e.Tab is >= 0 and <= 14), "every entry has a client tab");
    }
}
