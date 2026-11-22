using System.Xml.Linq;

namespace Ms2.Ui.Items;

/// <summary>
/// CItemMergeOptionTable::LoadItemMergeOptionBase (GetXmlPath("Table", "itemMergeOptionBase.xml"),
/// itemmergeoptiontable.cpp:394): per available &lt;mergeOption&gt; (FUN_14028faa0, by id) with a non-zero id, its &lt;slot&gt;s
/// keyed by (id, part). A slot (CItemMergeOptionData::Load, :291) is its &lt;option&gt;s: a stat (FindStatByName) or else a
/// special (GetSATypeByName) by optionName, whose steps idx0_max..idx9_max (up to the first 0) cut min+1..max; the first
/// range is the whole span, the second from the step picked by the step count to the end. A slot with an unknown option
/// name is left out. Only what the tooltip reads (FUN_1405c0870 / FUN_1405c0a70 / FUN_1405c0c70 / FUN_1405c0e80) is kept.
/// </summary>
public sealed class ItemMergeOptionTable
{
    public static readonly ItemMergeOptionTable Empty = new(null, null, null, null);

    /// <summary>CItemMergeOption: the stat (-1 for none) or special (0 for none), the whole range and the top range
    /// (Unk_MergeRange, min..max).</summary>
    public sealed record Option(int Stat, int Special, int FirstMin, int FirstMax, int SecondMin, int SecondMax);

    readonly Dictionary<(int Id, int Part), List<Option>> data = new();

    public ItemMergeOptionTable(XDocument doc, Func<XElement, string, string, IEnumerable<XElement>> collect,
        Func<string, int> statByName, Func<string, int> specialByName)
    {
        var root = doc?.Root;
        if (root == null || root.Name.LocalName != "ms2" || collect == null) return;
        foreach (var node in collect(root, "mergeOption", "id"))
        {
            int id = I(node, "id");
            if (id == 0) continue;
            foreach (var slot in node.Elements("slot"))
            {
                var options = Load(slot, statByName, specialByName);
                if (options == null) continue;
                data.TryAdd((id, I(slot, "part")), options);                   // std::map::insert keeps the first
            }
        }
    }

    static List<Option> Load(XElement slot, Func<string, int> statByName, Func<string, int> specialByName)
    {
        var list = new List<Option>();
        foreach (var e in slot.Elements("option"))
        {
            string name = (string)e.Attribute("optionName") ?? "";
            int stat = statByName(name), special = 0;
            if (stat == -1) special = specialByName(name);
            if (stat == -1 && special == 0) return null;
            var steps = new List<(int Min, int Max)>();
            uint prev = (uint)I(e, "min");
            for (int i = 0; i < 10; i++)
            {
                uint max = (uint)I(e, "idx" + i + "_max");
                if (max == 0) break;
                steps.Add(((int)(prev + 1), (int)max));
                prev = max;
            }
            if (steps.Count == 0)
            {
                list.Add(new Option(stat, special, 0, 0, 0, 0));
                continue;
            }
            int n = steps.Count;
            int pick = n switch { <= 4 => n - 1, <= 6 => n - 2, <= 8 => n - 3, 9 => n - 4, _ => n - 5 };
            list.Add(new Option(stat, special, steps[0].Min, steps[n - 1].Max, steps[pick].Min, steps[n - 1].Max));
        }
        return list;
    }

    /// <summary>FindMergeData (:674): the material (the merge id's item data) must be type 0x13; its part is the item's
    /// equip subtype when the material's subtype is 1, else 0; the key is (GetMergeOptionId = param1, part).</summary>
    public IReadOnlyList<Option> Find(ItemData item, ItemData material)
    {
        if (material == null || material.Type != 0x13) return null;
        int part = 0;
        if (material.Subtype == 1)
        {
            if (item == null) return null;
            part = item.EquipSubtype;
        }
        return data.TryGetValue((material.Param1, part), out var l) ? l : null;
    }

    /// <summary>IsRangeSet (:704): min &lt;= max and not both 0.</summary>
    public static bool IsSet(int min, int max) => min <= max && !(min == 0 && max == 0);

    static int I(XElement e, string name) => int.TryParse((string)e.Attribute(name), out int v) ? v : 0;
}
