using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Ms2.Quest;

namespace Ms2.Ui;

/// <summary>
/// CBlackMarketTable (blackmarketitemoption.cpp:1063): Table/blackMarketTable.xml, the Black Market's search tabs, the
/// option stat tables and the option rows that name three stat tables each.
/// </summary>
public sealed class BlackMarketTable
{
    /// <summary>CBlackMarketTable::StatInfo: Kind 0 a stat (Id its stat), 1 a special ability (Id its type).</summary>
    public sealed class Stat
    {
        public int Id = -1;
        public int Kind;
        public bool Percent;
        public float Default;
        public string Name = "";
    }

    /// <summary>CBlackMarketTable::TabInfo: Category is the tab's own category plus, comma-joined, its children's.</summary>
    public sealed class Tab
    {
        public string Name = "";
        public string Category = "";
        public int Priority;
        public int Id = -1;
        public int OptionId = -1;
        public bool AutoCompleteName;
        public bool ShowIncludeLowTab;
        public readonly List<Tab> Children = new();
    }

    public static readonly BlackMarketTable Empty = new(null, _ => true, _ => "", _ => -1, _ => 0, _ => "", _ => "");

    readonly List<Tab> tabs = new();
    readonly Dictionary<int, Tab> tabById = new();
    readonly Dictionary<uint, uint[]> options = new();
    readonly Dictionary<uint, List<Stat>> statTables = new();
    readonly Func<string, bool> featureEnabled;
    readonly Func<int, string> categoryName;

    /// <summary>
    /// Load (@0x1401c3da0) from the &lt;ms2&gt; root: every &lt;category&gt; reads its tabs (the last one's list is kept, the
    /// id map gathers all), then the &lt;stat_table&gt; and &lt;option&gt; rows.
    /// </summary>
    /// <param name="featureEnabled">CFeatureManager::IsFeatureEnabledByName.</param>
    /// <param name="categoryName">CStringTable::FUN_141811980(13, id).</param>
    /// <param name="findStat">FindStatByName: -1 when none.</param>
    /// <param name="findSpecial">FUN_14081d260 (FindSpecialByName).</param>
    /// <param name="statName">GetStatCommonText.</param>
    /// <param name="specialName">GetSATypeName.</param>
    public BlackMarketTable(XDocument doc, Func<string, bool> featureEnabled, Func<int, string> categoryName,
        Func<string, int> findStat, Func<string, int> findSpecial, Func<int, string> statName, Func<int, string> specialName)
    {
        this.featureEnabled = featureEnabled;
        this.categoryName = categoryName;
        var root = doc?.Root;
        if (root == null || root.Name.LocalName != "ms2") return;
        foreach (var cat in root.Elements("category"))
        {
            var list = new List<Tab>();
            ReadTabs(cat, list, null);
            tabs.Clear();
            tabs.AddRange(list);
        }
        foreach (var table in root.Elements("stat_table"))
        {
            uint id = 0;
            QuestTable.QueryUInt(table, "id", ref id);
            var stats = new List<Stat>();
            foreach (var e in table.Elements("stat"))
            {
                var stat = new Stat();
                string name = (string)e.Attribute("name") ?? "";
                QuestTable.QueryBool(e, "percent", ref stat.Percent);
                QueryFloat(e, "default", ref stat.Default);
                int n = findStat(name);
                if (n == -1)
                {
                    n = findSpecial(name);
                    stat.Kind = 1;
                    stat.Name = specialName(n) ?? "";
                }
                else
                {
                    stat.Kind = 0;
                    stat.Name = statName(n) ?? "";
                }
                stat.Id = n;
                stats.Add(stat);
            }
            statTables[id] = stats;
        }
        foreach (var option in root.Elements("option"))
        {
            uint id = 0;
            QuestTable.QueryUInt(option, "id", ref id);
            // XmlSplit: the comma-separated ids, each read as an unsigned number
            var ids = new List<uint>();
            foreach (var part in ((string)option.Attribute("stat_tables") ?? "").Split(','))
                if (uint.TryParse(part.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out uint v)) ids.Add(v);
            options[id] = ids.ToArray();
        }
    }

    /// <summary>
    /// ReadTabs (@0x1401c4670): each &lt;tab&gt; with a name id above 0 and its feature on; its children are read into it
    /// and its category is appended to the parent's.
    /// </summary>
    void ReadTabs(XElement parent, List<Tab> output, Tab owner)
    {
        foreach (var e in parent.Elements("tab"))
        {
            int name = 0;
            QuestTable.QueryInt(e, "name", ref name);
            string text = categoryName(name) ?? "";
            if (name <= 0) continue;
            if (!featureEnabled((string)e.Attribute("feature") ?? "")) continue;
            var tab = new Tab { Name = text, Category = (string)e.Attribute("category") ?? "" };
            QuestTable.QueryInt(e, "priority", ref tab.Priority);
            QuestTable.QueryInt(e, "id", ref tab.Id);
            QuestTable.QueryInt(e, "option_id", ref tab.OptionId);
            QuestTable.QueryBool(e, "auto_complete_name", ref tab.AutoCompleteName);
            QuestTable.QueryBool(e, "showIncludeLowTab", ref tab.ShowIncludeLowTab);
            tabById[tab.Id] = tab;
            ReadTabs(e, tab.Children, tab);
            if (owner != null && tab.Category.Length > 0)
                owner.Category += (owner.Category.Length == 0 ? "" : ",") + tab.Category;
            output.Add(tab);
        }
    }

    /// <summary>QueryFloatAttribute: a value that does not read as a number leaves the field as it was.</summary>
    static void QueryFloat(XElement e, string name, ref float v)
    {
        var s = (string)e.Attribute(name);
        if (s != null && float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) v = f;
    }

    /// <summary>GetTabs (@0x1401c43e0).</summary>
    public IReadOnlyList<Tab> Tabs => tabs;

    /// <summary>GetOptionStats (@0x1401c4430): the option row's stat table ids, or null.</summary>
    public uint[] OptionStats(uint option) => options.TryGetValue(option, out var ids) ? ids : null;

    /// <summary>GetStatTable (@0x1401c44f0), or null.</summary>
    public IReadOnlyList<Stat> StatTable(uint id) => statTables.TryGetValue(id, out var s) ? s : null;

    /// <summary>GetTab (@0x1401c45b0), or null.</summary>
    public Tab GetTab(int id) => tabById.TryGetValue(id, out var t) ? t : null;
}
