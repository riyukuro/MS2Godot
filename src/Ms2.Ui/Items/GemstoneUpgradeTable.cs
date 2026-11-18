using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.Items;

/// <summary>
/// CItemGemStoneUpgradeTable (ms2/commonfiles/itemgemstonedata.cpp), the parts the item tooltip reads. Load (:105) reads
/// Table/itemGemstoneUpgrade.xml (the locale folder first) on first use: each available &lt;key&gt; with an ItemId, its
/// NextItemID and up to four IngredientItemID&lt;n&gt; ("id:tag") with an IngredientCount&lt;n&gt;; a later row of an id
/// replaces an earlier one. A row whose EquipPart mixes part 4 with others ends the load (the rows before it stay).
/// </summary>
public sealed class GemstoneUpgradeTable
{
    /// <summary>CItemGemStoneData: m_nLinked (NextItemID) and the ingredient item ids in file order.</summary>
    public sealed record Row(int NextItemId, IReadOnlyList<int> Ingredients);

    readonly PackSet packs;
    Dictionary<int, Row> rows;

    public static readonly GemstoneUpgradeTable Empty = new(null);

    public GemstoneUpgradeTable(PackSet packs) { this.packs = packs; }

    /// <summary>FindData (:233): the row of an item id, null when none.</summary>
    public Row Find(int itemId) => Rows().TryGetValue(itemId, out var r) ? r : null;

    /// <summary>FUN_140627020 (itemsocketsystemutil.cpp:223): no next tier, or the next tier has no row.</summary>
    public bool IsLastTier(Row row) => row.NextItemId == 0 || Find(row.NextItemId) == null;

    Dictionary<int, Row> Rows()
    {
        if (rows != null) return rows;
        rows = new();
        XDocument doc = null;
        foreach (var p in new[] { "table/kr/itemgemstoneupgrade.xml", "table/itemgemstoneupgrade.xml" })   // GetXmlPath("Table", ...)
            if (packs != null && packs.Exists("Xml", p)) { doc = GameStrings.Parse(packs.Read("Xml", p)); break; }
        if (doc?.Root == null) return rows;
        foreach (var e in doc.Root.Elements("key"))
        {
            if (!FeatureElements.IsAvailable(packs, e)) continue;
            int id = ItemData.Atoi((string)e.Attribute("ItemId") ?? "0");
            if (id == 0) continue;
            int next = ItemData.Atoi((string)e.Attribute("NextItemID") ?? "0");
            var ingredients = new List<int>();
            for (int i = 1; i <= 4; i++)
            {
                // an ingredient needs both attributes; its id is the text before the first ':'
                if ((string)e.Attribute("IngredientItemID" + i) is not { } value || e.Attribute("IngredientCount" + i) == null) continue;
                var tokens = value.Split(':', StringSplitOptions.RemoveEmptyEntries);
                ingredients.Add(tokens.Length > 0 ? ItemData.Atoi(tokens[0]) : 0);
            }
            string parts = ((string)e.Attribute("EquipPart") ?? "").Trim();
            uint mask = 7;
            if (parts.Length != 0)
            {
                var list = parts.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(ItemData.Atoi).ToList();
                if (list.Count != 0)
                {
                    mask = 0;
                    foreach (int part in list)
                    {
                        if (part - 1 < 0) { mask = 7; break; }
                        mask |= 1u << (part - 1);
                    }
                }
            }
            if ((mask & 8) != 0 && mask != 8) break;
            rows[id] = new Row(next, ingredients);
        }
        return rows;
    }
}
