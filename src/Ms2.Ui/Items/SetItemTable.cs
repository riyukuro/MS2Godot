using System.Xml.Linq;

namespace Ms2.Ui.Items;

/// <summary>
/// SetItemTable (GetXmlPath("Table", "SetItemInfo.xml"), setitemtable.cpp:104): the available &lt;set&gt; rows, one per id
/// (FUN_14028faa0), each a SetItemInfo (setiteminfo.cpp:40): id, showEffectIfItsSetItemMotion, isDisableTooltip, the
/// itemIDs list and the available &lt;part&gt;s by count (SetItemOptionPart::Load, setitemoptionpart.cpp:33). LoadSets stops at
/// the first bad row (a set id of 0 or one already read, a part count of 0 or one already read, unequal additionalEffectID
/// and additionalEffectLevel lists), keeping the sets read before it. Per item id the sets that list it, in file order
/// (the std::multimap m_byItem). GMS2 has no &lt;part&gt; in a set: its optionID names an &lt;option id&gt; row of
/// table/setitemoption.xml that holds the parts, their values as &lt;name&gt;_value_base and &lt;name&gt;_rate_base (the rate in
/// the KMS rows' thousandths: pen_rate_base 0.02 is KMS's pen 20). INFERRED from the data, the GMS2 client's reader is not in
/// the restored source; KMS has no table/setitemoption.xml. STILL-OPEN: GMS2 has no specialabilitystringtable.xml, so the
/// specials (sgi, sgi_boss, finaladditionaldamage) have no option name there and are not read, nor sgi_target.
/// </summary>
public sealed class SetItemTable
{
    public static readonly SetItemTable Empty = new(null, null, null, null, null);
    const string ValueBase = "_value_base", RateBase = "_rate_base";

    /// <summary>The 16 stats SetItemOptionPart::Load reads by their option name (DAT_1439f48d0).</summary>
    static readonly int[] StatKeys = { 0, 1, 2, 3, 4, 13, 14, 15, 16, 17, 20, 23, 24, 25, 26, 31 };
    /// <summary>The 4 specials it reads by their option name (DAT_1439f4910).</summary>
    static readonly int[] SpecialKeys = { 7, 11, 12, 25 };

    /// <summary>SetItemOptionPart: the bonus at a piece count.</summary>
    public sealed class Part
    {
        public int Count;
        /// <summary>additionalEffectID with additionalEffectLevel.</summary>
        public readonly List<(int Id, ushort Level)> Effects = new();
        /// <summary>m_stats / m_specials (std::map, so walked in key order): the non-zero values.</summary>
        public readonly SortedDictionary<int, int> Stats = new(), Specials = new();
    }

    /// <summary>SetItemInfo.</summary>
    public sealed class Set
    {
        public int Id;
        public bool ShowEffect, DisableTooltip;
        public readonly List<int> ItemIds = new();
        /// <summary>m_parts by count (GetOptionPart).</summary>
        public readonly Dictionary<int, Part> Parts = new();
    }

    readonly Dictionary<int, Set> sets = new();
    readonly Dictionary<int, List<Set>> byItem = new();

    /// <param name="collect">FUN_14028faa0: the available child rows of a name, one per key value.</param>
    /// <param name="available">IsElementAvailable, for the parts.</param>
    /// <param name="statName">GetStatName: a stat's option name.</param>
    /// <param name="specialName">FUN_14081d380: a special's option name.</param>
    /// <param name="optionDoc">GMS2's table/setitemoption.xml, null when the data has none.</param>
    public SetItemTable(XDocument doc, Func<XElement, string, string, IEnumerable<XElement>> collect, Func<XElement, bool> available,
        Func<int, string> statName, Func<int, string> specialName, XDocument optionDoc = null)
    {
        var root = doc?.Root;
        if (root == null || root.Name.LocalName != "ms2" || collect == null) return;
        var options = new Dictionary<int, XElement>();
        if (optionDoc?.Root is { } optionRoot && optionRoot.Name.LocalName == "ms2")
            foreach (var o in collect(optionRoot, "option", "id"))
                options.TryAdd(I(o, "id"), o);
        foreach (var e in collect(root, "set", "id"))
        {
            var s = new Set { Id = I(e, "id"), ShowEffect = I(e, "showEffectIfItsSetItemMotion") != 0, DisableTooltip = I(e, "isDisableTooltip") != 0 };
            s.ItemIds.AddRange(Tokens(e, "itemIDs"));
            var partRows = e.Elements("part").ToList();
            if (partRows.Count == 0 && options.TryGetValue(I(e, "optionID"), out var option))
                partRows = option.Elements("part").Select(KmsPart).ToList();
            foreach (var pe in partRows)
            {
                if (!available(pe)) continue;
                var p = new Part { Count = I(pe, "count") };
                if (p.Count == 0) return;
                var ids = Tokens(pe, "additionalEffectID");
                var levels = Tokens(pe, "additionalEffectLevel");
                if (ids.Count != levels.Count) return;
                for (int i = 0; i < ids.Count; i++) p.Effects.Add((ids[i], (ushort)levels[i]));
                foreach (int k in StatKeys)
                    if (I(pe, statName(k)) is var v && v != 0) p.Stats[k] = v;
                foreach (int k in SpecialKeys)
                    if (I(pe, specialName(k)) is var v && v != 0) p.Specials[k] = v;
                if (p.Count < 1 || s.Parts.ContainsKey(p.Count)) return;    // AddOptionPart
                s.Parts[p.Count] = p;
            }
            if (s.Id == 0 || sets.ContainsKey(s.Id)) return;
            foreach (int id in s.ItemIds)
            {
                if (!byItem.TryGetValue(id, out var list)) byItem[id] = list = new();
                list.Add(s);
            }
            sets[s.Id] = s;
        }
    }

    /// <summary>HasSetItem: some set lists the item.</summary>
    public bool HasSetItem(int itemId) => byItem.ContainsKey(itemId);

    /// <summary>GetSetItemInfos: the sets that list the item, in file order.</summary>
    public IReadOnlyList<Set> SetsOf(int itemId) => byItem.TryGetValue(itemId, out var l) ? l : Array.Empty<Set>();

    /// <summary>GetSetItemInfo: the set of an id, null when none.</summary>
    public Set Get(int setId) => sets.TryGetValue(setId, out var s) ? s : null;

    /// <summary>A GMS2 option part in the KMS part's shape: &lt;name&gt;_value_base as &lt;name&gt;, &lt;name&gt;_rate_base as
    /// &lt;name&gt; in thousandths, rounded; the other attributes as they are.</summary>
    static XElement KmsPart(XElement pe)
    {
        var part = new XElement("part");
        foreach (var a in pe.Attributes())
        {
            string n = a.Name.LocalName;
            bool rate = n.EndsWith(RateBase, StringComparison.Ordinal);
            if (!rate && !n.EndsWith(ValueBase, StringComparison.Ordinal)) { part.SetAttributeValue(n, a.Value); continue; }
            if (!double.TryParse(a.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double x)) continue;
            double v = Math.Round(rate ? x * 1000 : x);
            if (v <= int.MaxValue && v >= int.MinValue)
                part.SetAttributeValue(n[..^(rate ? RateBase.Length : ValueBase.Length)], ((int)v).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        return part;
    }

    /// <summary>XmlGetAttr into an int: 0 without the attribute.</summary>
    static int I(XElement e, string name) => !string.IsNullOrEmpty(name) && int.TryParse((string)e.Attribute(name), out int v) ? v : 0;

    /// <summary>XmlTokenList: the comma separated numbers.</summary>
    static List<int> Tokens(XElement e, string name)
    {
        var list = new List<int>();
        foreach (var s in ((string)e.Attribute(name) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (int.TryParse(s, out int v)) list.Add(v);
        return list;
    }
}
