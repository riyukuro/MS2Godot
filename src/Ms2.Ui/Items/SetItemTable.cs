using System.Xml.Linq;

namespace Ms2.Ui.Items;

/// <summary>
/// SetItemTable (GetXmlPath("Table", "SetItemInfo.xml"), setitemtable.cpp:104): the available &lt;set&gt; rows, one per id
/// (FUN_14028faa0), each a SetItemInfo (setiteminfo.cpp:40): id, showEffectIfItsSetItemMotion, isDisableTooltip, the
/// itemIDs list and the available &lt;part&gt;s by count (SetItemOptionPart::Load, setitemoptionpart.cpp:33). LoadSets stops at
/// the first bad row (a set id of 0 or one already read, a part count of 0 or one already read, unequal additionalEffectID
/// and additionalEffectLevel lists), keeping the sets read before it. Per item id the sets that list it, in file order
/// (the std::multimap m_byItem).
/// </summary>
public sealed class SetItemTable
{
    public static readonly SetItemTable Empty = new(null, null, null, null, null);

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
    public SetItemTable(XDocument doc, Func<XElement, string, string, IEnumerable<XElement>> collect, Func<XElement, bool> available,
        Func<int, string> statName, Func<int, string> specialName)
    {
        var root = doc?.Root;
        if (root == null || root.Name.LocalName != "ms2" || collect == null) return;
        foreach (var e in collect(root, "set", "id"))
        {
            var s = new Set { Id = I(e, "id"), ShowEffect = I(e, "showEffectIfItsSetItemMotion") != 0, DisableTooltip = I(e, "isDisableTooltip") != 0 };
            s.ItemIds.AddRange(Tokens(e, "itemIDs"));
            foreach (var pe in e.Elements("part"))
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
