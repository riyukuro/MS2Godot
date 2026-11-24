using System.Xml.Linq;

namespace Ms2.Ui.Items;

/// <summary>
/// CItemMergeOptionTable::LoadItemMergeOptionBase (GetXmlPath("Table", "itemMergeOptionBase.xml"),
/// itemmergeoptiontable.cpp:394): per available &lt;mergeOption&gt; (FUN_14028faa0, by id) with a non-zero id, its &lt;slot&gt;s
/// keyed by (id, part). A slot (CItemMergeOptionData::Load, :291) is its &lt;option&gt;s: a stat (FindStatByName) or else a
/// special (GetSATypeByName) by optionName, whose steps idx0_max..idx9_max (up to the first 0) cut min+1..max; the first
/// range is the whole span, the second from the step picked by the step count to the end. A slot with an unknown option
/// name is left out. Only what the tooltip reads (FUN_1405c0870 / FUN_1405c0a70 / FUN_1405c0c70 / FUN_1405c0e80) is kept.
/// <para>The GMS2 data's shape, INFERRED from the data (the GMS2 client is packed, so unread): a &lt;mergeOption&gt; holds
/// &lt;mergeStep step&gt; elements around its slots (step 1 is the only one in the data); an option is named
/// "&lt;stat&gt;_value_base" or "&lt;stat&gt;_rate_base" and gives each step as idxN="min,max" (empty after the last), a rate as
/// a fraction, so 0.012 is the KMS data's 12. The merge materials carry no param1 and are subtype 21 (keyed by part, as the
/// KMS subtype 1) or 22 (one slot without a part, as the KMS subtype 2); each one's own id names its option, the 21s in
/// itemMergeOptionBase.xml and the 22s in itemMergeOptionMaterial.xml. For every KMS material the GMS2 option of its id
/// has the ranges of the KMS option its param1 names (1972 of 1972 options), with the rate fractions times 1000.
/// STILL-OPEN: the GMS2 slot's partLimit and consumeMeso, which the tooltip does not read.</para>
/// </summary>
public sealed class ItemMergeOptionTable
{
    public static readonly ItemMergeOptionTable Empty = new(null, null, null, null);

    /// <summary>The GMS2 data's material table (subtype 22 materials).</summary>
    public const string MaterialFile = "itemmergeoptionmaterial.xml";

    /// <summary>CItemMergeOption: the stat (-1 for none) or special (0 for none), the whole range and the top range
    /// (Unk_MergeRange, min..max).</summary>
    public sealed record Option(int Stat, int Special, int FirstMin, int FirstMax, int SecondMin, int SecondMax);

    readonly Dictionary<(int Id, int Part), List<Option>> data = new();
    /// <summary>The GMS2 material table's slots (<see cref="MaterialFile"/>).</summary>
    readonly Dictionary<(int Id, int Part), List<Option>> materials = new();

    public ItemMergeOptionTable(XDocument doc, Func<XElement, string, string, IEnumerable<XElement>> collect,
        Func<string, int> statByName, Func<string, int> specialByName, XDocument materialDoc = null)
    {
        Read(doc, data, collect, statByName, specialByName);
        Read(materialDoc, materials, collect, statByName, specialByName);
    }

    static void Read(XDocument doc, Dictionary<(int, int), List<Option>> into, Func<XElement, string, string, IEnumerable<XElement>> collect,
        Func<string, int> statByName, Func<string, int> specialByName)
    {
        var root = doc?.Root;
        if (root == null || root.Name.LocalName != "ms2" || collect == null) return;
        foreach (var node in collect(root, "mergeOption", "id"))
        {
            int id = I(node, "id");
            if (id == 0) continue;
            // the KMS slots, then (GMS2) the slots of each merge step in order
            foreach (var slot in node.Elements("slot").Concat(node.Elements("mergeStep").Elements("slot")))
            {
                var options = Load(slot, statByName, specialByName);
                if (options == null) continue;
                into.TryAdd((id, I(slot, "part")), options);                   // std::map::insert keeps the first
            }
        }
    }

    static List<Option> Load(XElement slot, Func<string, int> statByName, Func<string, int> specialByName)
    {
        var list = new List<Option>();
        foreach (var e in slot.Elements("option"))
        {
            string name = (string)e.Attribute("optionName") ?? "";
            bool gms2 = e.Attribute("idx0") != null, rate = false;
            if (gms2)
            {
                rate = name.EndsWith(RateSuffix, StringComparison.Ordinal);
                if (rate) name = name[..^RateSuffix.Length];
                else if (name.EndsWith(ValueSuffix, StringComparison.Ordinal)) name = name[..^ValueSuffix.Length];
            }
            int stat = statByName(name), special = 0;
            if (stat == -1) special = specialByName(name);
            if (stat == -1 && special == 0) return null;
            var steps = gms2 ? Gms2Steps(e, rate) : new List<(int Min, int Max)>();
            uint prev = (uint)I(e, "min");
            for (int i = 0; !gms2 && i < 10; i++)
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

    const string ValueSuffix = "_value_base", RateSuffix = "_rate_base";

    /// <summary>The GMS2 option's steps: idx0..idx9 as "min,max" up to the first empty one, a rate's fractions in
    /// thousandths (the KMS data's unit).</summary>
    static List<(int Min, int Max)> Gms2Steps(XElement e, bool rate)
    {
        var steps = new List<(int Min, int Max)>();
        for (int i = 0; i < 10; i++)
        {
            string v = (string)e.Attribute("idx" + i) ?? "";
            int comma = v.IndexOf(',');
            if (comma < 0 || !Number(v[..comma], rate, out int min) || !Number(v[(comma + 1)..], rate, out int max)) break;
            steps.Add((min, max));
        }
        return steps;
    }

    static bool Number(string s, bool rate, out int v)
    {
        v = 0;
        if (!decimal.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) return false;
        v = (int)Math.Round(rate ? d * 1000 : d, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>FindMergeData (:674): the material (the merge id's item data) must be type 0x13; its part is the item's
    /// equip subtype when the material's subtype is 1, else 0; the key is (GetMergeOptionId = param1, part). GMS2 data
    /// (see the class notes): a subtype 21 material is keyed as a subtype 1 one and a subtype 22 one in the material
    /// table, both by the material's own id, as they have no param1.</summary>
    public IReadOnlyList<Option> Find(ItemData item, ItemData material)
    {
        if (material == null || material.Type != 0x13) return null;
        bool gms2 = material.Param1 == 0 && material.Subtype is Gms2PartSubtype or Gms2MaterialSubtype;
        int part = 0;
        if (material.Subtype == 1 || gms2 && material.Subtype == Gms2PartSubtype)
        {
            if (item == null) return null;
            part = item.EquipSubtype;
        }
        var table = gms2 && material.Subtype == Gms2MaterialSubtype ? materials : data;
        return table.TryGetValue((gms2 ? material.Id : material.Param1, part), out var l) ? l : null;
    }

    /// <summary>The GMS2 data's merge material subtypes (INFERRED, see the class notes).</summary>
    const int Gms2PartSubtype = 21, Gms2MaterialSubtype = 22;

    /// <summary>IsRangeSet (:704): min &lt;= max and not both 0.</summary>
    public static bool IsSet(int min, int max) => min <= max && !(min == 0 && max == 0);

    static int I(XElement e, string name) => int.TryParse((string)e.Attribute(name), out int v) ? v : 0;
}
