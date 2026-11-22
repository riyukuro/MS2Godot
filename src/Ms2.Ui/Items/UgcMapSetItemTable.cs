using System.Xml.Linq;

namespace Ms2.Ui.Items;

/// <summary>
/// CUGCMapSetItemTable (commonfiles/ugcmapsetiteminfo.cpp, LoadUGCMapSetItem @0x140b9e100: GetXmlPath("Table",
/// "UGCMapSetItem.xml")): per &lt;UGCMapSetItem&gt; row a CUGCMapSetItemData with its group, minCount and the pieces of the
/// comma separated itemID list (std::map m_items, so walked in piece id order; a piece named twice keeps its first entry)
/// with their itemCount, aniIdle, aniWork, effect and effectNode. A row whose itemCount, aniIdle or aniWork list is not as
/// long as its itemID list is left out; a later row of a group replaces the earlier one (CRBMap::SetAt). Group 0 has no
/// data (GetUGCMapSetItem @0x140b9df60).
/// </summary>
public sealed class UgcMapSetItemTable
{
    public static readonly UgcMapSetItemTable Empty = new(null);

    /// <summary>CUGCMapSetItemInfo: one piece.</summary>
    public sealed record Piece(uint Count, string AniIdle, string AniWork, string Effect, string EffectNode);

    /// <summary>CUGCMapSetItemData: one set.</summary>
    public sealed class Set
    {
        public uint Group, MinCount;
        /// <summary>m_items: the pieces by item id, in id order.</summary>
        public readonly SortedDictionary<uint, Piece> Pieces = new();
    }

    readonly Dictionary<uint, Set> sets = new();

    public UgcMapSetItemTable(XDocument doc)
    {
        var root = doc?.Root;
        if (root == null || root.Name.LocalName != "ms2") return;
        foreach (var e in root.Elements("UGCMapSetItem"))
        {
            var s = new Set { Group = Number((string)e.Attribute("group")) };
            var ids = Numbers(e, "itemID");
            var counts = Numbers(e, "itemCount");
            if (ids.Count != counts.Count) continue;
            s.MinCount = Number((string)e.Attribute("minCount"));
            var idle = Words(e, "aniIdle");
            if (ids.Count != idle.Length) continue;
            var work = Words(e, "aniWork");
            if (ids.Count != work.Length) continue;
            var effect = Words(e, "effect");
            var effectNode = Words(e, "effectNode");
            for (int i = 0; i < ids.Count; i++)
                s.Pieces.TryAdd(ids[i], new Piece(counts[i], idle[i], work[i], i < effect.Length ? effect[i] : "",
                    i < effectNode.Length ? effectNode[i] : ""));
            sets[s.Group] = s;
        }
    }

    /// <summary>GetUGCMapSetItem: the set of a group; null for group 0 or no row.</summary>
    public Set Get(uint group) => group != 0 && sets.TryGetValue(group, out var s) ? s : null;

    /// <summary>XmlGetAttr's unsigned read (atoi; 0 for a missing attribute).</summary>
    static uint Number(string s) => (uint)ItemData.Atoi(s ?? "");

    /// <summary>XmlSplit (FUN_140177e10): the non-empty comma separated tokens as numbers.</summary>
    static List<uint> Numbers(XElement e, string name)
    {
        var list = new List<uint>();
        foreach (var t in Words(e, name)) list.Add(Number(t));
        return list;
    }

    /// <summary>Unk_SplitA (achievedata.cpp:491, CString::Tokenize): the non-empty comma separated tokens.</summary>
    static string[] Words(XElement e, string name) =>
        ((string)e.Attribute(name) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
}
