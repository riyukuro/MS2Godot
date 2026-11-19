using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui;

/// <summary>
/// CAchieveCategory (client/ms2/achievemanager.cpp:1279-1552): a row of the trophy dialog's category tree, from
/// Table/achieveCategory.xml. The top-level rows are the tabs (s_aCategory); each has its &lt;child&gt; rows.
/// </summary>
public sealed class AchieveCategory
{
    /// <summary>m_nId.</summary>
    public int Id;
    /// <summary>m_nTab: ParseTab (@0x14093f8a0) of "tab": trophy 0, summary 1, emblem 2 (0 when absent or unknown). A
    /// child row reads its parent's "tab" (Load reads it off the parent element).</summary>
    public int Tab;
    /// <summary>m_nTree: ParseTree (@0x14093f910) of "tree": expand 0, collapse 1, fixed 2 (0 when absent; never read for a
    /// child row).</summary>
    public int Tree;
    public string CategoryTag = "", ContentsTag = "";
    /// <summary>m_strEventFilter (a top-level row only).</summary>
    public string EventFilter = "";
    public readonly List<AchieveCategory> Children = new();

    /// <summary>
    /// IsOpen (@0x14093f240): a row without an event filter is open; one with a filter is open while
    /// GameEventManagerCli::IsEnabled(filter) holds. STILL-OPEN: the game events are not ported;
    /// <paramref name="eventEnabled"/> answers for them (the client data has no row with a filter).
    /// </summary>
    public bool IsOpen(Func<string, bool> eventEnabled) => EventFilter.Length == 0 || eventEnabled(EventFilter);

    /// <summary>Matches (@0x14093f2d0): whether a trophy's data fits the row: none for the summary tab, only type 9 for
    /// the emblem tab, none of type 9 for the trophy tab, and the row's tags each found in the trophy's.</summary>
    public bool Matches(AchieveData d)
    {
        if (d == null) return false;
        switch (Tab)
        {
            case 1: return false;
            case 2: if (d.Type != 9) return false; break;
            case 0: if (d.Type == 9) return false; break;
        }
        if (CategoryTag.Length != 0 && !d.CategoryTag.Contains(CategoryTag, StringComparison.Ordinal)) return false;
        if (ContentsTag.Length != 0 && !d.ContentsTag.Contains(ContentsTag, StringComparison.Ordinal)) return false;
        return true;
    }

    static int ParseTab(string s) =>
        string.Equals(s, "summary", StringComparison.OrdinalIgnoreCase) ? 1 : string.Equals(s, "emblem", StringComparison.OrdinalIgnoreCase) ? 2 : 0;

    static int ParseTree(string s) =>
        string.Equals(s, "collapse", StringComparison.OrdinalIgnoreCase) ? 1 : string.Equals(s, "fixed", StringComparison.OrdinalIgnoreCase) ? 2 : 0;

    /// <summary>
    /// Load (@0x14093e610): the &lt;category&gt; rows FUN_14028faa0 keeps (one per id, by locale and feature), each with its
    /// &lt;child&gt; rows (every child, with no locale or feature test).
    /// </summary>
    public static AchieveCategoryTable Load(PackSet packs)
    {
        var list = new List<AchieveCategory>();
        const string path = "table/achievecategory.xml";
        if (packs == null || !packs.Exists("Xml", path)) return new AchieveCategoryTable(list);
        XElement root;
        try { root = GameStrings.Parse(packs.Read("Xml", path)).Root; } catch (System.Xml.XmlException) { return new AchieveCategoryTable(list); }
        if (root == null || root.Name != "ms2") return new AchieveCategoryTable(list);
        foreach (var e in FeatureElements.Collect(packs, root, "category", "id"))
        {
            var cat = new AchieveCategory
            {
                Id = Atoi(e, "id"),
                Tab = e.Attribute("tab") is { } t ? ParseTab((string)t) : 0,
                Tree = e.Attribute("tree") is { } tr ? ParseTree((string)tr) : 0,
                CategoryTag = (string)e.Attribute("categoryTag") ?? "",
                ContentsTag = (string)e.Attribute("contentsTag") ?? "",
                EventFilter = (string)e.Attribute("eventFilter") ?? "",
            };
            foreach (var c in e.Elements("child"))
                cat.Children.Add(new AchieveCategory
                {
                    Id = Atoi(c, "id"),
                    Tab = cat.Tab,                                             // FUN_140940370(pElem, "tab"): the parent's
                    CategoryTag = (string)c.Attribute("categoryTag") ?? "",
                    ContentsTag = (string)c.Attribute("contentsTag") ?? "",
                });
            list.Add(cat);
        }
        return new AchieveCategoryTable(list);
    }

    static int Atoi(XElement e, string attr) => Items.ItemData.Atoi((string)e.Attribute(attr) ?? "0");
}

/// <summary>s_aCategory (0x143ab3658) and CAchieveCategory's static readers over it.</summary>
public sealed class AchieveCategoryTable
{
    public readonly IReadOnlyList<AchieveCategory> Tabs;

    /// <summary>The event test of <see cref="AchieveCategory.IsOpen"/> (STILL-OPEN: no game events; every filter is off).</summary>
    public Func<string, bool> EventEnabled = _ => false;

    public AchieveCategoryTable(IReadOnlyList<AchieveCategory> tabs) => Tabs = tabs;

    /// <summary>GetTabs (@0x14093eae0): the open top-level rows.</summary>
    public List<AchieveCategory> GetTabs() => Tabs.Where(t => t.IsOpen(EventEnabled)).ToList();

    /// <summary>GetRows (@0x14093eb90): the open top-level rows, each followed by its open children.</summary>
    public List<AchieveCategory> GetRows()
    {
        var rows = new List<AchieveCategory>();
        foreach (var t in Tabs)
        {
            if (!t.IsOpen(EventEnabled)) continue;
            rows.Add(t);
            foreach (var c in t.Children)
                if (c.IsOpen(EventEnabled)) rows.Add(c);
        }
        return rows;
    }

    /// <summary>GetRow (@0x14093ece0): the <paramref name="index"/>-th of <see cref="GetRows"/>, null past the end.</summary>
    public AchieveCategory GetRow(int index)
    {
        var rows = GetRows();
        return index >= 0 && index < rows.Count ? rows[index] : null;
    }

    /// <summary>FindById (@0x14093ef10): a top-level row or child of the id, open or not; null when none.</summary>
    public AchieveCategory FindById(int id)
    {
        foreach (var t in Tabs)
        {
            if (t.Id == id) return t;
            foreach (var c in t.Children)
                if (c.Id == id) return c;
        }
        return null;
    }

    /// <summary>FindIdByTab (@0x14093f0d0): the id of the first row (a top-level row before its children) of the tab, 0
    /// when none.</summary>
    public int FindIdByTab(int tab)
    {
        foreach (var t in Tabs)
        {
            if (t.Tab == tab) return t.Id;
            foreach (var c in t.Children)
                if (c.Tab == tab) return c.Id;
        }
        return 0;
    }
}
