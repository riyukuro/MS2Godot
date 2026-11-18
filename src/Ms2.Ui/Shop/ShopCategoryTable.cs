using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Ui.Shop;

/// <summary>
/// One tab row of a shop's tab tree (CShopCategoryTable::TabInfo, shopcategorytable.cpp:102): its name id (string table 13),
/// symbol, the product categories it lists (comma separated; a parent's grows by each sub tab's), priority, static flag, id
/// and sub tabs. The sort tabs' gender / job flags (SortTabInfo) are not kept: the npc shop does not read them.
/// </summary>
public sealed class ShopTab
{
    public int NameId;
    public string Symbol = "";
    public string Category = "";
    public int Priority;
    /// <summary>m_nStatic: the tab is listed with no product in it.</summary>
    public int Static;
    /// <summary>m_nId (-1 when the row has none).</summary>
    public int Id = -1;
    public bool AlwaysOpen;
    public readonly List<ShopTab> SubTabs = new();
}

/// <summary>
/// CShopCategoryTable (shopcategorytable.cpp): the shop tab trees of table/shopcategory.xml and table/meratmarketcategory.xml,
/// by category id.
/// </summary>
public sealed class ShopCategoryTable
{
    readonly Dictionary<int, List<ShopTab>> tabs = new();
    readonly PackSet packs;
    readonly Func<string, bool> featureEnabled;

    public static readonly ShopCategoryTable Empty = new(null);

    /// <summary>The ctor (:182) and Load (:203): both files, each &lt;ms2&gt; root and the environment FUN_14028f8d0 selects
    /// under it.</summary>
    public ShopCategoryTable(PackSet packs)
    {
        this.packs = packs;
        featureEnabled = f => LobbyFeatures.IsEnabledByName(packs, f);
        if (packs == null) return;
        foreach (var path in new[] { "table/shopcategory.xml", "table/meratmarketcategory.xml" })
        {
            if (!packs.Exists("Xml", path)) continue;
            var root = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", path)).TrimStart('\uFEFF')).Root;
            if (root == null || root.Name != "ms2") continue;
            LoadCategories(root);
            if (FeatureElements.SelectEnvironment(packs, root) is { } env) LoadCategories(env);
        }
    }

    /// <summary>LoadCategories (:229): each available &lt;category&gt; with an id above 0; a later one with the same id
    /// replaces the list (CAtlMap::SetAt).</summary>
    void LoadCategories(XElement root)
    {
        foreach (var c in root.Elements("category"))
        {
            uint.TryParse((string)c.Attribute("id"), out uint id);
            if (id == 0) continue;
            if (!FeatureElements.IsAvailable(packs, c)) continue;
            var list = new List<ShopTab>();
            LoadTabs(c, list, null);
            tabs[(int)id] = list;
        }
    }

    /// <summary>
    /// LoadTabs (:269): each available &lt;tab&gt;. One with a name is loaded (a failed Load still adds the tab), its sub
    /// tabs read into it, and its category appended to the parent's; one without copies the tabs of the category its
    /// copyid names.
    /// </summary>
    void LoadTabs(XElement parent, List<ShopTab> list, ShopTab parentTab)
    {
        foreach (var e in parent.Elements("tab"))
        {
            if (!FeatureElements.IsAvailable(packs, e)) continue;
            int.TryParse((string)e.Attribute("name"), out int name);
            if (name > 0)
            {
                var tab = new ShopTab();
                Load(tab, e);
                LoadTabs(e, tab.SubTabs, tab);
                if (parentTab != null && tab.Category.Length > 0)
                    parentTab.Category += (parentTab.Category.Length == 0 ? "" : ",") + tab.Category;
                list.Add(tab);
            }
            else
            {
                int.TryParse((string)e.Attribute("copyid"), out int copy);
                if (tabs.TryGetValue(copy, out var copied)) list.AddRange(copied);
            }
        }
    }

    /// <summary>TabInfo::Load (:114): the name (0 fails); a feature that is off fails before the rest is read; a symbol with
    /// a '/' is dropped.</summary>
    bool Load(ShopTab tab, XElement e)
    {
        uint.TryParse((string)e.Attribute("name"), out uint name);
        if (name == 0) return false;
        tab.NameId = (int)name;
        if (!featureEnabled((string)e.Attribute("feature") ?? "")) return false;
        tab.Symbol = (string)e.Attribute("symbol") ?? "";
        if (tab.Symbol.Contains('/')) tab.Symbol = "";
        tab.Category = (string)e.Attribute("category") ?? "";
        tab.Priority = Int(e, "priority", 0);
        tab.Static = Int(e, "static", 0);
        tab.Id = Int(e, "id", -1);
        tab.AlwaysOpen = (string)e.Attribute("alwaysOpen") is { } a && (a == "1" || a.Equals("true", StringComparison.OrdinalIgnoreCase));
        return true;
    }

    static int Int(XElement e, string attr, int fallback) => int.TryParse((string)e.Attribute(attr), out int v) ? v : fallback;

    /// <summary>GetTabs (:367): the tab list of a category, null when there is none.</summary>
    public IReadOnlyList<ShopTab> GetTabs(int category) => tabs.TryGetValue(category, out var l) ? l : null;
}
