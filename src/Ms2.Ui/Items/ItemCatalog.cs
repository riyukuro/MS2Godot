using System.Globalization;
using Ms2.Formats.Pack;

namespace Ms2.Ui.Items;

/// <summary>
/// The debug item browser's catalog: every item of the item tables with what a browser cell shows (the name, the bag
/// tab of <see cref="ItemData.InventoryTab"/>, the rank, the slot max and the icon), read through the table the inventory
/// and tooltips use (<see cref="ItemDataTable"/>, <see cref="GameTables.ItemName"/>), not a second item reader.
/// The client reads items by id only, so there is no client list to port: this is a tool's index, built once per pack
/// set and kept (the item rows stay in the table's file cache either way, as the client keeps all CItemData).
/// </summary>
public sealed class ItemCatalog
{
    /// <summary>One item's cell data. <see cref="IconEntry"/> is the icon's Resource/Image entry ("item/icon/12345.png").</summary>
    public readonly record struct Entry(int Id, string Name, int Tab, int Rank, int SlotMax, string IconEntry);

    /// <summary>Every item of the item tables, ascending id (the client's CItemDataTable maps by id).</summary>
    public readonly IReadOnlyList<Entry> Entries;
    /// <summary>Items whose itemname.xml row is empty or missing (the client shows an empty name too).</summary>
    public readonly int WithoutName;

    ItemCatalog(List<Entry> entries, int withoutName) { Entries = entries; WithoutName = withoutName; }

    public int Count => Entries.Count;

    /// <summary>
    /// Builds the catalog from the data's item tables. Every id <see cref="ItemDataTable.Ids"/> lists has a row there;
    /// one that still gives no data is left out (no selected environment).
    /// </summary>
    public static ItemCatalog Build(PackSet packs, GameTables tables)
    {
        var table = tables.Items;
        var ids = table.Ids();
        var entries = new List<Entry>(ids.Count);
        int withoutName = 0;
        foreach (int id in ids)
        {
            if (table.Get(id) is not { } d) continue;
            string name = tables.ItemName(id);
            if (name.Length == 0) withoutName++;
            var icon = ChatEmoticons.ChatEmoticonTable.ResourceImageEntry(d.IconPath);
            entries.Add(new Entry(id, name, d.InventoryTab, d.Rank, (int)d.SlotMax, icon));
        }
        return new ItemCatalog(entries, withoutName);
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, ItemCatalog> shared = new();

    /// <summary>The catalog of a pack set, built on the first call and kept for the run (main thread).</summary>
    public static ItemCatalog Shared(PackSet packs, GameTables tables) => shared.GetValue(packs, _ => Build(packs, tables));

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
