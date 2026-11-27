using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Ui;

namespace Ms2.Formats.World;

/// <summary>
/// One sub tab of the dungeon finder, UIDungeonCategory (client/ms2/uidungeoncategory.cpp @0x140c08990): the level range of its
/// "level" attribute (Unk_DcLevelRange, placeholder/dclevelrange.h) and the group types of its dungeonCategory children, in file order.
/// </summary>
public sealed class DungeonCategory
{
    /// <summary>m_range.m_nLow and m_nHigh: both 0 when the attribute is missing or not "low-high" (GetLevelRangeAttr leaves the range as it was).</summary>
    public ushort Low, High;
    /// <summary>m_groupTypes: the DungeonGroupType numbers (<see cref="DungeonCategoryTable.GroupTypeByName"/>).</summary>
    public readonly List<int> GroupTypes = new();

    /// <summary>Unk_DcLevelRange::IsValid (@0x140c0e770): false when low is above high or both are 0.</summary>
    public bool IsValid => Low <= High && !(Low == 0 && High == 0);

    /// <summary>Unk_DcLevelRange::FUN_1407790e0: low &lt;= level &lt;= high.</summary>
    public bool Contains(ushort level) => Low <= level && level <= High;
}

/// <summary>
/// The sub tabs of the dungeon finder windows, UIDungeonCategoryTable (uidungeoncategory.cpp @0x140c08a30; Table/dungeonCategory.xml
/// through GetXmlPath("Table", ...), <see cref="TablePath"/>). Load (@0x140c08b50) reads each dungeonMainTab whose index is 0 or 1 and,
/// in it, each dungeonSubTab that IsElementAvailable keeps (locale and feature), with its level range and group types. The tabs of a main
/// tab sit in a map keyed by the range's low end (SetAt: a later sub tab of the same low replaces the earlier), so they list in order of low.
/// STILL-OPEN: a groupType name the client's parser (Parse_140218c20, commonenum.cpp:448) does not know asserts and gives 0 there; here it gives 0 too.
/// </summary>
public sealed class DungeonCategoryTable
{
    /// <summary>The main tabs (m_maps has two).</summary>
    public const int MainTabs = 2;

    readonly SortedDictionary<ushort, DungeonCategory>[] maps = { new(), new() };

    /// <summary>The table from one file's root (the &lt;ms2&gt; element, null for a missing file) and the locale and feature pick.</summary>
    public DungeonCategoryTable(XElement root, Func<XElement, bool> available)
    {
        if (root is not { Name.LocalName: "ms2" }) return;             // a missing file or root: no tabs
        available ??= _ => true;
        foreach (var tab in root.Elements("dungeonMainTab"))
        {
            int index = 0;
            if (int.TryParse((string)tab.Attribute("index"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) index = i;
            if (index < 0 || index >= MainTabs) continue;
            foreach (var sub in tab.Elements("dungeonSubTab"))
            {
                if (!available(sub)) continue;
                var category = new DungeonCategory();
                ReadLevelRange(sub, category);
                foreach (var node in sub.Elements("dungeonCategory"))
                    category.GroupTypes.Add(GroupTypeByName((string)node.Attribute("groupType")));
                maps[index][category.Low] = category;
            }
        }
    }

    /// <summary>The table of a data set; empty without the file.</summary>
    public static DungeonCategoryTable Load(PackSet packs)
    {
        if (packs == null) return new DungeonCategoryTable(null, null);
        string entry = TablePath.Resolve(packs, "dungeonCategory.xml");
        var root = entry == null ? null : Ms2.Formats.Text.PackXml.Parse(packs.Read("Xml", entry))?.Root;
        return new DungeonCategoryTable(root, e => FeatureElements.IsAvailable(packs, e));
    }

    /// <summary>GetLevelRangeAttr (@0x140c09130): "low-high" through XmlSplit(str, values, "-"); anything that does not split into two numbers leaves the range at 0.</summary>
    static void ReadLevelRange(XElement e, DungeonCategory c)
    {
        string s = (string)e.Attribute("level");
        if (s == null) return;
        var parts = s.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return;
        c.Low = (ushort)ParseNumber(parts[0]);
        c.High = (ushort)ParseNumber(parts[1]);
    }

    static int ParseNumber(string s) =>
        int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    /// <summary>
    /// Parse_140218c20 (commonenum.cpp:448): the DungeonGroupType of a groupType name; "" is 0 and so is a name the client does not know.
    /// </summary>
    public static int GroupTypeByName(string name) => name switch
    {
        "normal" => 1, "raid" => 2, "chaosRaid" => 3, "reverseRaid" => 4, "lapenta" => 5, "guildRaid" => 6, "darkStream" => 7,
        "worldBossDungeon" => 8, "item" => 9, "vip" => 10, "event" => 0xb, "fameChallenge" => 0xc, "colosseum" => 0xd, "turka" => 0xe,
        _ => 0,
    };

    /// <summary>GetSubTabs (@0x140c08ec0): the sub tabs of a main tab in order of their low level; none for a tab outside 0..1.</summary>
    public List<DungeonCategory> GetSubTabs(int tab) =>
        tab < 0 || tab >= MainTabs ? new List<DungeonCategory>() : new List<DungeonCategory>(maps[tab].Values);

    /// <summary>GetMaxSubTabLevel (@0x140c08f50): the largest low level of the tab's sub tabs, 0 for none or a tab outside 0..1.</summary>
    public ushort GetMaxSubTabLevel(int tab)
    {
        if (tab < 0 || tab >= MainTabs) return 0;
        ushort max = 0;
        foreach (var c in maps[tab].Values)
            if (c.Low >= max) max = c.Low;
        return max;
    }

    /// <summary>FUN_140c09030 (@0x140c09030): the index of the first sub tab whose range holds the level, else the last one (count - 1: -1 for a tab without sub tabs); 0 for a tab outside 0..1.</summary>
    public int SubTabOfLevel(int tab, ushort level)
    {
        if (tab < 0 || tab >= MainTabs) return 0;
        int index = 0;
        foreach (var c in maps[tab].Values)
        {
            if (c.Contains(level)) return index;
            index++;
        }
        return maps[tab].Count - 1;
    }
}
