#if MS2_DEVTOOLS
using System.Text.RegularExpressions;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui;
using Ms2.Ui.ChatEmoticons;
using Ms2.Ui.Items;
using Ms2.Devtools;
using NUnit.Framework;

namespace Ms2Godot.Tests;

/// <summary>
/// The debug item browser's catalog (<see cref="ItemCatalog"/>) and spawn line (<see cref="ItemSpawnCommand"/>): every bag
/// item of the item tables (no beauty-salon item), the name/tab/rank/stack/icon the tables give, the bag's order, search by
/// name and id, and the server command's amount/rarity. Needs MS2_DATA (the KMS2 data); the GMS2 per-item layout has its
/// own test.
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
        packs = new PackSet(d);
        tables = new GameTables(packs, "en");
        catalog = ItemCatalog.Shared(packs, tables);
        return catalog;
    }

    /// <summary>True for the KMS layout (itemdata/NNN.xml), false for the per-item data (GMS2).</summary>
    static bool KmsLayout(ItemDataTable table) => packs.List("Xml").Any(n => n.StartsWith("itemdata/", StringComparison.OrdinalIgnoreCase));

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
        foreach (int id in Ms2.Formats.World.PerItemFiles.Index(entries).Keys)
            if (table.Get(id) != null) ids.Add(id);
        return ids;
    }

    [Test]
    public void TheCatalogHasEveryBagItemAndLeavesOutOnlyTheBeautyKinds()
    {
        var c = Catalog();
        var all = DataIds(tables.Items);
        var kept = c.Entries.Select(e => e.Id).ToList();
        var left = c.Removed.Select(r => r.Id).ToList();
        TestContext.Out.WriteLine($"catalog {c.Count} bag items, {c.Removed.Count} beauty-only left out, {all.Count} items in the tables; {c.WithoutName} without a name");
        Assert.That(c.Count, Is.GreaterThan(0));
        Assert.That(c.Unfiltered, Is.EqualTo(all.Count), "the unfiltered count is every item of the tables");
        Assert.That(c.Count + c.Removed.Count, Is.EqualTo(c.Unfiltered), "every item is kept or left out, none twice");
        Assert.That(kept.Count, Is.EqualTo(kept.Distinct().Count()), "one entry per id");
        Assert.That(left.Count, Is.EqualTo(left.Distinct().Count()), "one removed row per id");
        for (int i = 1; i < c.Count; i++)
        {
            var a = c.Entries[i - 1]; var b = c.Entries[i];
            bool same = a.Tab == b.Tab && a.Kind == b.Kind && a.Sub == b.Sub
                && (a.Kind != 2 || (a.LevelLimit == b.LevelLimit && a.Rank == b.Rank));   // a consumable's level and rank lead the id
            if (same) Assert.That(b.Id, Is.GreaterThan(a.Id), $"ids rise inside a tab/kind/subtype group ({i})");
        }
        Assert.That(new HashSet<int>(kept).Overlaps(left), Is.False, "a left-out item is not in the catalog");
        Assert.That(new HashSet<int>(kept).Union(left), Is.EquivalentTo(all), "every table item, and no other id");
    }

    /// <summary>
    /// The left-out ids are exactly the items itemname.xml's own class calls hair, face, face mark or skin (a second data
    /// source for the same set: the model's slot group), each kind's count and samples, and an equip kept.
    /// </summary>
    [Test]
    public void TheBeautyKindsAreLeftOut()
    {
        var c = Catalog();
        bool kms = KmsLayout(tables.Items);
        foreach (var kind in new[] { BeautyKind.Hair, BeautyKind.Face, BeautyKind.FaceMark, BeautyKind.Ear, BeautyKind.Skin })
        {
            var rows = c.Removed.Where(r => r.Kind == kind).ToList();
            TestContext.Out.WriteLine($"{ItemCatalog.KindName(kind)}: {rows.Count}, e.g. {string.Join(", ", rows.Take(4).Select(r => r.Id + " '" + r.Name + "'"))}");
            if (rows.Count == 0)
            {
                // the per-item data has no skin colour item; every other kind is in both data sets
                Assert.That(kind == BeautyKind.Skin && !kms, Is.True, $"{ItemCatalog.KindName(kind)} items are in the tables");
                continue;
            }
            Assert.That(c.Entries.Any(e => rows.Any(r => r.Id == e.Id)), Is.False, $"{ItemCatalog.KindName(kind)} left out");
        }
        // a normal equip stays: an entry of the gear tab with an equip subtype
        var equip = c.Entries.FirstOrDefault(e => e.Tab == 0 && e.Sub != 0);
        Assert.That(equip.Id, Is.GreaterThan(0), "a normal equip is in the catalog");
        Assert.That(tables.Items.Get(equip.Id).Type, Is.EqualTo(1));

        if (!KmsLayout(tables.Items)) return;
        var classes = nameClasses();
        if (classes.Count == 0) return;     // no itemname file to cross-check against
        var byClass = DataIds(tables.Items).Where(id => classes.TryGetValue(id, out var cl) && cl is "hair" or "face" or "face mark" or "ear" or "skin").ToHashSet();
        Assert.That(c.Removed.Select(r => r.Id).ToHashSet(), Is.EquivalentTo(byClass), "itemname's class names the same beauty items");
    }

    static Dictionary<int, string> nameClasses()
    {
        // every locale's file: a develop row can be missing from one language (six hair ids are in en only)
        var map = new Dictionary<int, string>();
        foreach (var entry in packs.List("Xml"))
        {
            string n = entry.Replace('\\', '/');
            if (!n.StartsWith("string/", StringComparison.OrdinalIgnoreCase) || !n.EndsWith("/itemname.xml", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var e in GameStrings.Parse(packs.Read("Xml", entry)).Root.Elements("key"))
                if (int.TryParse((string)e.Attribute("id"), out int id) && (string)e.Attribute("class") is { Length: > 0 } cl) map[id] = cl;
        }
        return map;
    }

    /// <summary>
    /// The order: CompareItems' keys (item.cpp:2484) with the bag tab first, so every tab's items are together in the tab
    /// order, then the item's kind and equip subtype, then the consumable's level and rank, then the id. The sample takes
    /// two tabs and two equip subtypes.
    /// </summary>
    [Test]
    public void TheOrderIsTheBagsTabKindSubtypeAndId()
    {
        var c = Catalog();
        static (int, int, int, int, int, int) Key(ItemCatalog.Entry e) => (e.Tab, e.Kind, e.Sub, e.Kind == 2 ? e.LevelLimit : 0, e.Kind == 2 ? e.Rank : 0, e.Id);
        for (int i = 1; i < c.Count; i++)
            Assert.That(Key(c.Entries[i]), Is.GreaterThanOrEqualTo(Key(c.Entries[i - 1])), $"entry {i} ({c.Entries[i].Id}) after {c.Entries[i - 1].Id}");

        var tabs = c.Entries.Select(e => e.Tab).Distinct().OrderBy(t => t).ToList();
        Assert.That(tabs.Count, Is.GreaterThanOrEqualTo(2), "at least two tabs have items");
        int firstA = c.Entries.ToList().FindIndex(e => e.Tab == tabs[0]), firstB = c.Entries.ToList().FindIndex(e => e.Tab == tabs[1]);
        TestContext.Out.WriteLine($"tab {tabs[0]} starts at {firstA}, tab {tabs[1]} at {firstB}");
        Assert.That(firstA, Is.LessThan(firstB), "the lower tab's items come first");
        Assert.That(c.Entries.Skip(firstB).All(e => e.Tab >= tabs[1]), "no lower tab's item after a higher tab's");

        // two equip subtypes of the same tab and kind, e.g. a cap before a coat, keep the bag's order
        var group = c.Entries.Where(e => e.Tab == 0 && e.Kind == 3 && e.Sub != 0).ToList();
        var subs = group.Select(e => e.Sub).Distinct().OrderBy(s => s).ToList();
        Assert.That(subs.Count, Is.GreaterThanOrEqualTo(2), "the gear tab has two equip subtypes");
        int low = group.FindIndex(e => e.Sub == subs[0]), high = group.FindIndex(e => e.Sub == subs[^1]);
        TestContext.Out.WriteLine($"tab 0 kind 3: subtype {subs[0]} starts at {low}, subtype {subs[^1]} at {high}");
        Assert.That(low, Is.LessThan(high), "the lower subtype's items come first");
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
            Assert.That(e.Tab, Is.EqualTo(ItemCatalog.TabOf(d)), $"{e.Id} tab");
            Assert.That(e.Rank, Is.EqualTo(d.Rank), $"{e.Id} rank");
            Assert.That(e.SlotMax, Is.EqualTo((int)d.SlotMax), $"{e.Id} slot max");
            Assert.That(e.IconEntry, Is.EqualTo(ChatEmoticonTable.ResourceImageEntry(d.IconPath)), $"{e.Id} icon entry");
            Assert.That(e.Sub, Is.EqualTo(d.EquipSubtype), $"{e.Id} equip subtype");
        }
        TestContext.Out.WriteLine($"{tabs} of 15 tabs have items, {empty} are empty");
        Assert.That(tabs, Is.GreaterThanOrEqualTo(10), "the KMS2 item tables fill most tabs");
    }

    /// <summary>CItem::GetItemKind (item.cpp:2585), every arm and its precedence, over hand-made data rows.</summary>
    [Test]
    public void TheKindIsTheClients()
    {
        static int Kind(ItemData d) => ItemCatalog.KindOf(d);
        Assert.That(Kind(new ItemData { PCBang = true }), Is.EqualTo(6), "a PC bang item");
        Assert.That(Kind(new ItemData { PCBang = true, Vip = true }), Is.EqualTo(6), "PC bang before VIP");
        Assert.That(Kind(new ItemData { Vip = true }), Is.EqualTo(5), "a VIP item");
        Assert.That(Kind(new ItemData { ItemGroup = 4 }), Is.EqualTo(0), "a cash item");
        Assert.That(Kind(new ItemData { Vip = true, ItemGroup = 4 }), Is.EqualTo(5), "VIP before cash");
        Assert.That(Kind(new ItemData { Type = 1, SkinType = 2 }), Is.EqualTo(1), "an item with a skin type");
        Assert.That(Kind(new ItemData { Type = 2 }), Is.EqualTo(2), "a consumable");
        Assert.That(Kind(new ItemData { Type = 4 }), Is.EqualTo(4), "a type 4 item");
        Assert.That(Kind(new ItemData { Type = 7 }), Is.EqualTo(3), "any other");
        Assert.That(Kind(new ItemData { Type = 2, SkinType = 2 }), Is.EqualTo(1), "the skin type before the type");
        Assert.That(Kind(new ItemData { Type = 1, ItemGroup = 4 }), Is.EqualTo(0), "cash before an equip");
    }

    /// <summary>CItemData::FUN_14053e530 (itemdata.cpp:1381), every arm of the switch, over hand-made data rows.</summary>
    [Test]
    public void TheTabRuleIsTheClients()
    {
        static int Tab(int type, int skin = 0) => ItemCatalog.TabOf(new ItemData { Type = type, SkinType = skin });
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
        string text = name.Substring(name.Length / 2, Math.Min(2, name.Length - name.Length / 2)).Trim();   // the filter trims too
        if (text.Length == 0) text = name.Trim();
        c.Filter(text, -1, found);
        Assert.That(found, Does.Contain(sample), $"'{text}' finds {c.Entries[sample].Id}");
        Assert.That(found.All(i => c.Entries[i].Name.Contains(text, StringComparison.OrdinalIgnoreCase)
            || (text.All(char.IsAsciiDigit) && c.Entries[i].Id.ToString().StartsWith(text, StringComparison.Ordinal))), "name matches (or an id prefix for digits)");

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
    public void TheSpawnLineClampsAStackAndKeepsOtherAmounts()
    {
        Assert.That(ItemSpawnCommand.Line(20000000, 1, 0, 1), Is.EqualTo("/item 20000000 --amount 1"));
        Assert.That(ItemSpawnCommand.Line(20000000, 99, 0, 50), Is.EqualTo("/item 20000000 --amount 50"), "a stack clamps to the slot max");
        Assert.That(ItemSpawnCommand.Line(20000000, 20000, 0, 9999), Is.EqualTo("/item 20000000 --amount 9999"), "a stack clamps to the slot max");
        Assert.That(ItemSpawnCommand.Line(20000000, 0, 0, 10), Is.EqualTo("/item 20000000 --amount 1"), "at least one");
        Assert.That(ItemSpawnCommand.Line(20000000, -3, 0, 10), Is.EqualTo("/item 20000000 --amount 1"), "no negative amount");
        Assert.That(ItemSpawnCommand.Line(20000000, 7, 0, 0), Is.EqualTo("/item 20000000 --amount 7"), "no slot max is not a stack cap");
        // a slot max of 1 is a non-stackable: that many copies, the server caps them by free slots (ItemCommand.cs:84-100)
        Assert.That(ItemSpawnCommand.Line(11200003, 5, 0, 1), Is.EqualTo("/item 11200003 --amount 5"));
        Assert.That(ItemSpawnCommand.Line(11200003, 5, 0, 1), Does.Not.Contain("--rarity"));
        // a currency keeps its amount uncapped (ItemCommand.cs:53-57, the id range of Item.IsCurrency)
        Assert.That(ItemSpawnCommand.Line(90000001, 1000000, 0, 1), Is.EqualTo("/item 90000001 --amount 1000000"));
        Assert.That(ItemSpawnCommand.Line(20000000, 3, 4, 99), Is.EqualTo("/item 20000000 --amount 3 --rarity 4"));
        Assert.That(ItemSpawnCommand.Line(20000000, 3, 0, 99), Is.EqualTo("/item 20000000 --amount 3"), "rarity 0 is the item's own, sent as no flag");
        Assert.That(ItemSpawnCommand.Line(20000000, 3, -1, 99), Is.EqualTo("/item 20000000 --amount 3"));
        Assert.That(ItemSpawnCommand.Line(20000000, 3, 9, 99), Is.EqualTo("/item 20000000 --amount 3 --rarity 6"), "the server's cap");
    }

    /// <summary>
    /// The GMS2 data keeps one file per item: the catalog's ids are the per-item index's ids with an available environment,
    /// in the bag's order, and the catalog reads them through the same table. Nothing is dropped there unless the data
    /// marks it a beauty kind (the left-out rows are counted).
    /// </summary>
    [Test]
    public void TheCatalogBuildsOnThePerItemData()
    {
        var d = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_GMS2_DATA is not set");
        var gpack = new PackSet(d);
        var gtables = new GameTables(gpack, "en");
        var entries = gpack.List("Xml").ToList();
        if (entries.Any(e => e.StartsWith("itemdata/", StringComparison.OrdinalIgnoreCase))) Assert.Ignore("not the per-item layout");
        var expected = Ms2.Formats.World.PerItemFiles.Index(entries).Keys.Where(id => gtables.Items.Get(id) != null).ToHashSet();
        var cat = ItemCatalog.Build(gpack, gtables);
        TestContext.Out.WriteLine($"GMS2 catalog {cat.Count} bag items of {cat.Unfiltered}, index {expected.Count}, left out {cat.Removed.Count} ({cat.RemovedSummary()})");
        Assert.That(cat.Count, Is.GreaterThan(0));
        Assert.That(cat.Count + cat.Removed.Count, Is.EqualTo(expected.Count));
        Assert.That(cat.Entries.Select(e => e.Id).Concat(cat.Removed.Select(r => r.Id)).ToHashSet(), Is.EquivalentTo(expected));
        Assert.That(cat.Entries.All(e => e.Tab is >= 0 and <= 14), "every entry has a client tab");
        for (int i = 1; i < cat.Count; i++)
            Assert.That(cat.Entries[i].Tab, Is.GreaterThanOrEqualTo(cat.Entries[i - 1].Tab), "the tabs are together and in tab order");
    }
}
#endif
