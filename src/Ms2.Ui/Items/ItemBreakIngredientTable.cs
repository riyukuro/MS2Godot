using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.Items;

/// <summary>
/// CItemBreakIngredientTable (ms2/commonfiles/item.cpp): what breaking an item gives. LoadBreakItems (@0x14052e010) reads
/// Table/itemBreakIngredient.xml's available &lt;item&gt; rows by ItemID (not 0), each with its IngredientItemID{n} /
/// IngredientCount{n} pairs from 1 up to the first pair with a 0; LoadGachaBoxes (@0x14052e490) gives every gacha box its
/// coin item and amount. Read on first use.
/// </summary>
public sealed class ItemBreakIngredientTable
{
    readonly PackSet packs;
    readonly GachaBoxTable gacha;
    Dictionary<int, IReadOnlyList<(int ItemId, int Count)>> items;

    public static readonly ItemBreakIngredientTable Empty = new(null, GachaBoxTable.Empty);

    public ItemBreakIngredientTable(PackSet packs, GachaBoxTable gacha) { this.packs = packs; this.gacha = gacha; }

    /// <summary>GetBreakItem (@0x14052e650): the item's row, null for none.</summary>
    public IReadOnlyList<(int ItemId, int Count)> BreakItem(int itemId) => Items().TryGetValue(itemId, out var r) ? r : null;

    /// <summary>GetGachaBox (@0x14052e730): the box's coin item and amount, null for no box.</summary>
    public IReadOnlyList<(int ItemId, int Count)> GachaBox(int boxId) =>
        gacha.Coin(boxId) is { } c ? new[] { c } : null;

    /// <summary>
    /// GetIngredients (@0x14052e810): the item's row (<see cref="BreakItem"/>). Without a row the client gives null for an item
    /// with no data or EnableBreak 0 or 2, and for a skin item the skin calculation (FUN_140530c90 with the grade and socket
    /// count). STILL-OPEN: that calculation is not ported, so every item without a row gives null here.
    /// </summary>
    public IReadOnlyList<(int ItemId, int Count)> Ingredients(int itemId, int grade, int sockets) => BreakItem(itemId);

    Dictionary<int, IReadOnlyList<(int, int)>> Items()
    {
        if (items != null) return items;
        items = new();
        string path = packs == null ? null : TablePath.Resolve(packs, "itembreakingredient.xml");   // GetXmlPath("Table", ...)
        XDocument doc = path == null ? null : GameStrings.Parse(packs.Read("Xml", path));
        if (doc?.Root is not { Name.LocalName: "ms2" } root) return items;
        foreach (var e in root.Elements("item"))
        {
            if (!FeatureElements.IsAvailable(packs, e)) continue;
            int id = ItemData.Atoi((string)e.Attribute("ItemID") ?? "0");
            if (id == 0) continue;
            var list = new List<(int, int)>();
            for (int n = 1; ; n++)
            {
                int ing = ItemData.Atoi((string)e.Attribute("IngredientItemID" + n) ?? "0");
                int count = ItemData.Atoi((string)e.Attribute("IngredientCount" + n) ?? "0");
                if (ing == 0 || count == 0) break;
                list.Add((ing, count));                                          // CIngredientContainer::FUN_14026bbf0
            }
            items[id] = list;                                                    // m_mapBreak.SetAt
        }
        return items;
    }
}
