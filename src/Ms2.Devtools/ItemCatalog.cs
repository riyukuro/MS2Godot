#if MS2_DEVTOOLS
using System.Globalization;
using Ms2.Formats.Pack;
using Ms2.Ui;
using Ms2.Ui.Items;

namespace Ms2.Devtools;

/// <summary>
/// The look item kinds a bag never holds: a hair style, a face, a face decoration, the ear shape and the skin colour. The
/// client keeps them in the item tables but wears them through the beauty shop; the data marks them by the item model's
/// first &lt;slots&gt;&lt;slot&gt; group (ModelGroup::Load, itemdatastruct.cpp:439), whose name Parse_14021bdc0
/// (commonenum.cpp:1401) maps to the client's equip slot types: "HR" 1, "FA" 2, "FD" 3, "ER" 18. The server's look slots
/// are the same four plus SK (GenderChangeRules.LookSlots; ValidEquipSlot, EquipManager.cs:492, refuses SK, OH, ER and
/// Unknown), and a skin colour's model has no slot group at all (item 10100001's itemmodel is a bare &lt;customize&gt;).
/// </summary>
public enum BeautyKind
{
    /// <summary>&lt;slot name="HR"&gt; (the client's equip slot 1).</summary>
    Hair = 1,
    /// <summary>&lt;slot name="FA"&gt; (2).</summary>
    Face = 2,
    /// <summary>&lt;slot name="FD"&gt; (3).</summary>
    FaceMark = 3,
    /// <summary>&lt;slot name="ER"&gt; (18, the ear shape; the server refuses it as an equip too).</summary>
    Ear = 18,
    /// <summary>An equip whose model has no slot group: the skin colour (e.g. item 10100001 "Skin").</summary>
    Skin = 0,
}

/// <summary>
/// The debug item browser's catalog: every *bag* item of the item tables with what a browser cell shows (the name, the bag
/// tab of CItemData::FUN_14053e530 (<see cref="TabOf"/>), the rank, the slot max and the icon), read through the table the
/// inventory and tooltips use (<see cref="ItemDataTable"/>, <see cref="GameTables.ItemName"/>), not a second item reader.
/// The beauty-salon items are left out (<see cref="Removed"/>), because the server's item command cannot put them in a bag;
/// nothing else is dropped. The order is the bag's own: the tab, then CItem::GetItemKind and GetEquipSubtype (the
/// CompareItems comparator, item.cpp:2484-2545), then the id. The client reads items by id only, so there is no client
/// list to port: this is a tool's index, built once per pack set and kept (the item rows stay in the table's file cache
/// either way, as the client keeps all CItemData).
/// </summary>
public sealed class ItemCatalog
{
    /// <summary>
    /// One item's cell data. <see cref="Kind"/> is CItem::GetItemKind (item.cpp:2585): 6 PCBang, 5 VIP, 0 cash, 1 an item
    /// with a skin type, 2 a consumable, 4 type 4, else 3. <see cref="Sub"/> is GetEquipSubtype (itemdata.cpp:509), 0 for
    /// a non-equip. <see cref="IconEntry"/> is the icon's Resource/Image entry ("item/icon/12345.png").
    /// </summary>
    public readonly record struct Entry(int Id, string Name, int Tab, int Kind, int Sub, int Rank, int SlotMax, int LevelLimit, string IconEntry);

    /// <summary>An item the browser leaves out, with its kind (the beauty kinds only).</summary>
    public readonly record struct RemovedItem(BeautyKind Kind, int Id, string Name);

    /// <summary>Every bag item, in the bag's order: tab, kind, equip subtype, the consumable's level and rank, id.</summary>
    public readonly IReadOnlyList<Entry> Entries;
    /// <summary>Items whose itemname.xml row is empty or missing (the client shows an empty name too).</summary>
    public readonly int WithoutName;
    /// <summary>The beauty-salon items left out, in the tables' id order; never a bag item.</summary>
    public readonly IReadOnlyList<RemovedItem> Removed;
    /// <summary>How many items the tables hold before the beauty kinds are left out.</summary>
    public readonly int Unfiltered;

    ItemCatalog(List<Entry> entries, int withoutName, List<RemovedItem> removed, int unfiltered)
    {
        Entries = entries; WithoutName = withoutName; Removed = removed; Unfiltered = unfiltered;
    }

    public int Count => Entries.Count;

    /// <summary>
    /// Builds the catalog from the data's item tables. <see cref="ItemIds"/> lists every id the tables hold (one whose
    /// files give no data is left out, no selected environment); the beauty kinds go to <see cref="Removed"/>.
    /// </summary>
    public static ItemCatalog Build(PackSet packs, GameTables tables)
    {
        var table = tables.Items;
        var ids = ItemIds(packs, table);
        var entries = new List<Entry>(ids.Count);
        var removed = new List<RemovedItem>();
        int withoutName = 0;
        foreach (int id in ids)
        {
            if (table.Get(id) is not { } d) continue;
            string name = tables.ItemName(id);
            var model = tables.ItemModels.Get(d.ItemPreset);
            var beauty = BeautyKindOf(d, model);
            if (beauty is { } kind) { removed.Add(new RemovedItem(kind, id, name)); continue; }
            if (name.Length == 0) withoutName++;
            var icon = Ms2.Ui.ChatEmoticons.ChatEmoticonTable.ResourceImageEntry(d.IconPath);
            entries.Add(new Entry(id, name, TabOf(d), KindOf(d), d.EquipSubtype, d.Rank, (int)d.SlotMax, d.LevelLimit, icon));
        }
        entries.Sort(Compare);
        return new ItemCatalog(entries, withoutName, removed, entries.Count + removed.Count);
    }

    /// <summary>
    /// Every item id the tables hold, ascending: the ids of the itemdata/NNN.xml files (KMS, streaming each file's
    /// &lt;item id&gt; attributes) or of the per-item entries (the other data layout, <see cref="Ms2.Formats.World.PerItemFiles"/>),
    /// with <see cref="ItemDataTable.Get"/> asked for each so only items with an available environment stay. The client
    /// reads items by id only (CItemDataTable maps them), so there is no client list to port; this is the tool's index.
    /// The id pass reads the itemdata files' item attributes; the rows themselves then come from the table's own cache.
    /// </summary>
    static List<int> ItemIds(PackSet packs, ItemDataTable table)
    {
        var ids = new List<int>();
        var names = packs.List("Xml");
        var files = new List<int>();
        foreach (var name in names)
        {
            string n = name.Replace('\\', '/');
            if (!n.StartsWith("itemdata/", StringComparison.OrdinalIgnoreCase) || !n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
            string number = n["itemdata/".Length..^".xml".Length];
            if (number.Length > 0 && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int file)) files.Add(file);
        }
        if (files.Count > 0)
        {
            files.Sort();
            foreach (int file in files)
            {
                using var r = Ms2.Formats.Text.XmlRows.Open(packs.Read("Xml", $"itemdata/{file:000}.xml"));
                r.MoveToContent();
                if (r.IsEmptyElement) continue;
                r.Read();
                while (r.NodeType != System.Xml.XmlNodeType.EndElement && !r.EOF)
                {
                    if (r.NodeType != System.Xml.XmlNodeType.Element) { r.Read(); continue; }
                    if (r.LocalName != "item" || r.NamespaceURI.Length != 0) { r.Skip(); continue; }
                    var e = (System.Xml.Linq.XElement)System.Xml.Linq.XNode.ReadFrom(r);
                    if (int.TryParse((string)e.Attribute("id"), out int id) && table.Get(id) != null) ids.Add(id);
                }
            }
        }
        else
        {
            foreach (int id in Ms2.Formats.World.PerItemFiles.Index(names).Keys)
                if (table.Get(id) != null) ids.Add(id);
        }
        ids.Sort();
        return ids;
    }

    /// <summary>
    /// CItemData::FUN_14053e530 (itemdata.cpp:1381), the item's bag tab 0-14, as Unk_Inventory::FUN_140a61870
    /// (inventory.cpp:1148) takes it: the type picks the tab, an equip with a skin type is the outfit tab (1) and any
    /// other equip the gear tab (0), and a type the switch does not name is the etc tab (7). The tab names are
    /// CStringTable::GetInventoryTabName (stringtable.cpp:931, common strings s_inventory_tab_*). The GMS2 client numbers
    /// its tabs differently (<see cref="ItemData.Gms2InventoryTabOf"/>); this is the KMS rule.
    /// </summary>
    internal static int TabOf(ItemData d) => d.Type switch
    {
        1 => d.HasSkinType ? 1 : 0,
        6 or 8 or 0xc or 0x17 => 4,
        5 or 9 => 2,
        0xd or 0xe => 6,
        3 => 5,
        0xf or 0x13 => 3,
        0x10 => 8,
        0xb => 9,
        2 => 10,
        0x14 => 0xb,
        7 => 0xc,
        0x15 => 0xd,
        0x1b => 0xe,
        _ => 7,
    };

    /// <summary>
    /// CItem::GetItemKind (item.cpp:2585) for an item's data: 6 a PC bang item, 5 a VIP one, 0 a cash item, 1 an item with
    /// a skin type, 2 a consumable (type 2), 4 a type 4 item, else 3. The order is the client's: the first test that holds
    /// decides.
    /// </summary>
    internal static int KindOf(ItemData d) =>
        d.PCBang ? 6 : d.Vip ? 5 : d.IsCashItem ? 0 : d.HasSkinType ? 1 : d.Type == 2 ? 2 : d.Type == 4 ? 4 : 3;

    /// <summary>
    /// The beauty kind of an item, null for a bag item: the item model's first &lt;slot&gt; group type 1, 2 or 3
    /// (<see cref="ItemModelTable.Model.FirstSlotType"/>), and for an equip whose model has no slot group at all the skin
    /// colour (the model file of item 10100001 is a bare &lt;customize&gt;). A model the data lacks is not judged here: an
    /// equip without a model is not a beauty item but a hole in the data (the GMS2 data has three).
    /// </summary>
    static BeautyKind? BeautyKindOf(ItemData d, ItemModelTable.Model model)
    {
        if (model == null) return null;
        switch (model.FirstSlotType)
        {
            case 1: return BeautyKind.Hair;
            case 2: return BeautyKind.Face;
            case 3: return BeautyKind.FaceMark;
            case 18: return BeautyKind.Ear;
        }
        return d.Type == 1 && model.SlotTypes.Count == 0 ? BeautyKind.Skin : null;
    }

    /// <summary>
    /// CompareItems (@0x14052a7e0, item.cpp:2484): the bag tab here, then GetItemKind (item.cpp:2585), then GetEquipSubtype
    /// (itemdata.cpp:509), then for a consumable its level limit and rarity, then the id. The tab leads because the catalog
    /// lists all tabs at once; the client compares inside one bag.
    /// </summary>
    static int Compare(Entry a, Entry b)
    {
        int c = a.Tab.CompareTo(b.Tab);
        if (c != 0) return c;
        c = a.Kind.CompareTo(b.Kind);
        if (c != 0) return c;
        c = a.Sub.CompareTo(b.Sub);
        if (c != 0) return c;
        if (a.Kind == 2)
        {
            c = a.LevelLimit.CompareTo(b.LevelLimit);
            if (c != 0) return c;
            c = a.Rank.CompareTo(b.Rank);
            if (c != 0) return c;
        }
        return a.Id.CompareTo(b.Id);
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, ItemCatalog> shared = new();

    /// <summary>The catalog of a pack set, built on the first call and kept for the run (main thread).</summary>
    public static ItemCatalog Shared(PackSet packs, GameTables tables) => shared.GetValue(packs, _ => Build(packs, tables));

    /// <summary>How many items of <paramref name="kind"/> the build left out.</summary>
    public int RemovedCount(BeautyKind kind)
    {
        int n = 0;
        foreach (var r in Removed) if (r.Kind == kind) n++;
        return n;
    }

    /// <summary>The left-out counts as "hair 259, face 191, ...", for the log and the window's status line.</summary>
    public string RemovedSummary()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var kind in new[] { BeautyKind.Hair, BeautyKind.Face, BeautyKind.FaceMark, BeautyKind.Ear, BeautyKind.Skin })
        {
            int n = RemovedCount(kind);
            if (n == 0) continue;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(KindName(kind)).Append(' ').Append(n);
        }
        return sb.ToString();
    }

    /// <summary>The kind's name ("hair", "face", "face mark", "skin").</summary>
    public static string KindName(BeautyKind kind) => kind switch
    {
        BeautyKind.Hair => "hair",
        BeautyKind.Face => "face",
        BeautyKind.FaceMark => "face mark",
        BeautyKind.Ear => "ear",
        _ => "skin",
    };

    /// <summary>
    /// CStringTable::GetInventoryTabName's common string keys (stringtable.cpp:931): the 15 bag tabs, in tab order.
    /// </summary>
    public static readonly string[] TabKeys =
    {
        "s_inventory_tab_equip", "s_inventory_tab_skin", "s_inventory_tab_summon", "s_inventory_tab_material",
        "s_inventory_tab_life", "s_inventory_tab_quest", "s_inventory_tab_gem", "s_inventory_tab_etc",
        "s_inventory_tab_mastery", "s_inventory_tab_pet", "s_inventory_tab_activeskill", "s_inventory_tab_coin",
        "s_inventory_tab_badge", "s_inventory_tab_lapenshard", "s_inventory_tab_piece",
    };

    /// <summary>The tab's name in the data's language, "" for a number outside the 15 tabs.</summary>
    public static string TabName(GameTables tables, int tab) => tab >= 0 && tab < TabKeys.Length ? tables.Common(TabKeys[tab]) : "";

    /// <summary>
    /// The entries a search shows: a case-insensitive name substring, and for an all-digit text also the ids that start
    /// with it (the client's item name is string table 1, itemname.xml). <paramref name="tab"/> -1 shows every tab.
    /// Indices into <see cref="Entries"/>, appended to <paramref name="into"/> (cleared first), so a keystroke reuses
    /// one list; the entries themselves are never copied.
    /// </summary>
    public void Filter(string text, int tab, List<int> into)
    {
        into.Clear();
        text = text?.Trim() ?? "";
        bool numeric = text.Length > 0 && IsDigits(text);
        for (int i = 0; i < Entries.Count; i++)
        {
            var e = Entries[i];
            if (tab >= 0 && e.Tab != tab) continue;
            if (text.Length > 0 && !e.Name.Contains(text, StringComparison.OrdinalIgnoreCase) && !(numeric && IdStartsWith(e.Id, text))) continue;
            into.Add(i);
        }
    }

    static bool IsDigits(string s)
    {
        foreach (char c in s) if (!char.IsAsciiDigit(c)) return false;
        return true;
    }

    /// <summary>The id's decimal text (InvariantCulture, as the command line writes it) starts with <paramref name="prefix"/>.</summary>
    static bool IdStartsWith(int id, string prefix)
    {
        Span<char> digits = stackalloc char[11];
        int len = 0;
        for (int v = id; v > 0; v /= 10) digits[len++] = (char)('0' + v % 10);
        if (prefix.Length > len) return false;
        for (int i = 0; i < prefix.Length; i++) if (digits[len - 1 - i] != prefix[i]) return false;
        return true;
    }

    /// <summary>The entry's id as the client's text (decimal), for logs and tooltips.</summary>
    public static string IdText(int id) => id.ToString(CultureInfo.InvariantCulture);
}
#endif
