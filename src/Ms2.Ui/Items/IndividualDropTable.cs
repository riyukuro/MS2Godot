using System.Xml.Linq;
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
/// <para>The GMS2 data has no individualItemDrop_Final.xml: it writes one flat &lt;individualDropBox individualDropBoxID
/// dropGroup item item2 minCount maxCount enchantLevel showTooltip smartDropRate isApplySmartGenderDrop&gt; per item over
/// table/individualitemdrop*.xml and the region folder's (table/na/individualitemdrop_*.xml). <see cref="Gms2Boxes"/> reads
/// them as the same boxes: the rows of a box and dropGroup are one group, in the order they come, the group's flags from
/// its first row (isApplySmartGenderDrop is written True/TRUE/0). INFERRED from the data: of the 2785 boxes both data sets
/// have, 2259 list the same items and counts, and showTooltip and the gender flag agree on every row of those. STILL-OPEN:
/// GMS2 has no uiItemRank, and its PackageUIShowGrade does not follow it (KMS rank 3 rows are unset there), so the rank
/// is 0; the GMS2 client's reader is packed, so unread.</para>
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
        if (packs == null) return boxes;
        if (!packs.Exists("Xml", "table/individualitemdrop_final.xml")) return Gms2Boxes(boxes);
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

    /// <summary>The GMS2 data's flat rows as boxes (see the class notes); the plain table files first, then the region
    /// folder's, each set in name order.</summary>
    Dictionary<int, List<Group>> Gms2Boxes(Dictionary<int, List<Group>> into)
    {
        string region = TablePath.Region(e => packs.Exists("Xml", e));
        var names = packs.List("Xml").Where(n => n.EndsWith(".xml", StringComparison.Ordinal)
                && (IsDropFile(n, "table/") || IsDropFile(n, "table/" + region + "/")))
            .OrderBy(n => n.Count(c => c == '/')).ThenBy(n => n, StringComparer.Ordinal).ToList();
        var groupsOf = new Dictionary<int, List<(int Group, Group Value)>>();
        foreach (var name in names)
            foreach (var e in GameStrings.Parse(packs.Read("Xml", name)).Root.Elements("individualDropBox"))
            {
                if (!FeatureElements.IsAvailable(packs, e)) continue;
                int box = Int(e, "individualDropBoxID"), groupId = Int(e, "dropGroup");
                if (!groupsOf.TryGetValue(box, out var list)) groupsOf[box] = list = new();
                int at = list.FindIndex(g => g.Group == groupId);
                if (at < 0)
                {
                    list.Add((groupId, new Group(Bool(e, "isApplySmartGenderDrop"), Int(e, "smartDropRate"), new List<Item>())));
                    at = list.Count - 1;
                }
                int min = e.Attribute("minCount") != null ? Int(e, "minCount") : 1;
                list[at].Value.Items.Add(new Item(Int(e, "item"), Int(e, "item2"), 0, Int(e, "enchantLevel"), min, Int(e, "showTooltip")));
            }
        foreach (var (box, list) in groupsOf) into[box] = list.Select(g => g.Value).ToList();
        return into;
    }

    static bool IsDropFile(string name, string folder) =>
        name.StartsWith(folder + "individualitemdrop", StringComparison.Ordinal) && name.IndexOf('/', folder.Length) < 0;

    static int Int(XElement e, string a) => ItemData.Atoi((string)e.Attribute(a) ?? "0");

    static bool Bool(XElement e, string a) =>
        ((string)e.Attribute(a) ?? "") is var v && (v.Equals("true", StringComparison.OrdinalIgnoreCase) || ItemData.Atoi(v) != 0);

    /// <summary>The ids of the loaded boxes.</summary>
    public IEnumerable<int> BoxIds => Boxes().Keys;

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
