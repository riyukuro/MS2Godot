using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.Items;

/// <summary>One entry GetBoxItems emits (ItemDropResult, itemdroptable.cpp:945).</summary>
public sealed record ItemDropEntry(int ItemId, int Count, int Rank, int Enchant, int Tooltip);

/// <summary>
/// ItemDropTable's individual boxes (ms2/commonfiles/itemdroptable.cpp), the part the select-item-box dialog lists.
/// LoadIndividualBoxes (@0x1402ed0a0) reads table/individualItemDrop_Final.xml: each available &lt;dropBox&gt; keeps its
/// available &lt;group&gt;s (ItemDropIndividualBox::Load @0x1402e8f80; a box without a group is dropped) and each group keeps
/// its available &lt;v&gt; items (ItemDropIndividualGroup::Load @0x1402e8a20; a group without an item is dropped).
/// STILL-OPEN: ShutdownDropBox (CGameEventValueManager) is not ported, so no box is ever shut down; the count is the
/// minimum of minCount/maxCount where the client's m_count.GetValue() draws from the range.
/// </summary>
public sealed class IndividualDropTable
{
    sealed record Item(int ItemId, int ItemId2, int Rank, int Enchant, int Count, int Tooltip);
    sealed record Group(bool SmartGender, int SmartDropRate, List<Item> Items);

    readonly PackSet packs;
    Dictionary<int, List<Group>> boxes;

    public static readonly IndividualDropTable Empty = new(null);

    public IndividualDropTable(PackSet packs) { this.packs = packs; }

    Dictionary<int, List<Group>> Boxes()
    {
        if (boxes != null) return boxes;
        boxes = new();
        if (packs == null || !packs.Exists("Xml", "table/individualitemdrop_final.xml")) return boxes;
        foreach (var b in GameStrings.Parse(packs.Read("Xml", "table/individualitemdrop_final.xml")).Root.Elements("dropBox"))
        {
            if (!FeatureElements.IsAvailable(packs, b)) continue;
            var groups = new List<Group>();
            foreach (var g in b.Elements("group"))
            {
                if (!FeatureElements.IsAvailable(packs, g)) continue;
                var items = new List<Item>();
                foreach (var v in g.Elements("v"))
                {
                    if (!FeatureElements.IsAvailable(packs, v)) continue;
                    int min = v.Attribute("minCount") != null ? ItemData.Atoi((string)v.Attribute("minCount")) : 1;
                    int max = Math.Max(min, ItemData.Atoi((string)v.Attribute("maxCount") ?? "0"));
                    items.Add(new Item(
                        ItemData.Atoi((string)v.Attribute("itemID") ?? "0"),
                        ItemData.Atoi((string)v.Attribute("itemID2") ?? "0"),
                        ItemData.Atoi((string)v.Attribute("uiItemRank") ?? "0"),
                        ItemData.Atoi((string)v.Attribute("enchantLevel") ?? "0"),
                        min,
                        ItemData.Atoi((string)v.Attribute("showTooltip") ?? "0")));
                }
                if (items.Count == 0) continue;
                groups.Add(new Group(ItemData.Atoi((string)g.Attribute("isApplySmartGenderDrop") ?? "0") != 0,
                    ItemData.Atoi((string)g.Attribute("smartDropRate") ?? "0"), items));
            }
            if (groups.Count == 0) continue;
            boxes[ItemData.Atoi((string)b.Attribute("dropBoxID") ?? "0")] = groups;
        }
        return boxes;
    }

    /// <summary>True when the table holds the box.</summary>
    public bool Has(int boxId) => Boxes().ContainsKey(boxId);

    /// <summary>
    /// GetBoxItems (@0x1402eaf10): false for a missing box. Per item: skipped when its data is unknown; when
    /// <paramref name="skipHidden"/> and showTooltip is 2; when the group applies the smart gender drop and the item's
    /// gender does not allow <paramref name="gender"/>; when the group's smartDropRate is above 0 and the item does not
    /// recommend <paramref name="job"/>. An itemID2 adds a second entry of the same count, rank and enchant.
    /// </summary>
    public bool GetBoxItems(ItemDataTable items, int boxId, int job, int gender, bool skipHidden, List<ItemDropEntry> output)
    {
        if (!Boxes().TryGetValue(boxId, out var groups)) return false;
        foreach (var g in groups)
            foreach (var i in g.Items)
            {
                var data = items.Get(i.ItemId);
                if (data == null) continue;
                if (skipHidden && i.Tooltip == 2) continue;
                if (g.SmartGender && !data.IsGenderAllowed(gender)) continue;
                if (g.SmartDropRate > 0 && !data.IsJobRecommended(job)) continue;
                output.Add(new ItemDropEntry(i.ItemId, i.Count, i.Rank, i.Enchant, i.Tooltip));
                if (i.ItemId2 != 0) output.Add(new ItemDropEntry(i.ItemId2, i.Count, i.Rank, i.Enchant, i.Tooltip));
            }
        return true;
    }
}
