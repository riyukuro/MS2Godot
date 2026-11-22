using System.Xml.Linq;

namespace Ms2.Ui.Items;

/// <summary>
/// CItemEnchantTransformTable (Table/ItemEnchantTransform.xml, itemenchanttransformtable.cpp:97), the part the tooltip
/// reads: per input item id, the lowest inputEnchantLevel of its transform rows (m_mapLimit, GetLowestLimitLevel :254).
/// LoadItemEnchantTransform skips unavailable rows and stops at the first bad one (inputRank 0, an empty inputSlot or
/// inputItemId list, or a missing output1Id, output1Rank or output1Count), keeping the limits set before it; a bad
/// output still comes after that row's limits are set.
/// </summary>
public sealed class ItemEnchantTransformTable
{
    public static readonly ItemEnchantTransformTable Empty = new(null, _ => true);

    readonly Dictionary<uint, uint> lowest = new();

    public ItemEnchantTransformTable(XDocument doc, Func<XElement, bool> available)
    {
        var root = doc?.Root;
        if (root == null || root.Name.LocalName != "ms2") return;
        foreach (var e in root.Elements("transform"))
        {
            if (!available(e)) continue;
            if (U(e, "inputRank") == 0) return;
            uint enchantLevel = U(e, "inputEnchantLevel");
            if (Split(e, "inputSlot").Count == 0) return;
            var ids = Split(e, "inputItemId");
            if (ids.Count == 0) return;
            foreach (uint id in ids)
                if (!lowest.TryGetValue(id, out uint l) || l > enchantLevel) lowest[id] = enchantLevel;
            if (U(e, "output1Id") == 0 || U(e, "output1Rank") == 0 || U(e, "output1Count") == 0) return;
        }
    }

    /// <summary>GetLowestLimitLevel: the item's lowest transform enchant level, 0 for an item with no row.</summary>
    public uint LowestLimitLevel(int itemId) => lowest.TryGetValue((uint)itemId, out uint l) ? l : 0;

    /// <summary>The input item ids that have rows.</summary>
    public IEnumerable<int> ItemIds => lowest.Keys.Select(k => (int)k);

    static uint U(XElement e, string name) => uint.TryParse((string)e.Attribute(name), out uint v) ? v : 0;

    /// <summary>XmlSplitImpl(str, arr, ","): the comma-separated numbers, empty entries dropped.</summary>
    static List<uint> Split(XElement e, string name)
    {
        var list = new List<uint>();
        foreach (var s in ((string)e.Attribute(name) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (uint.TryParse(s, out uint v)) list.Add(v);
        return list;
    }
}
