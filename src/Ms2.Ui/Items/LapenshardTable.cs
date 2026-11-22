using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.Items;

/// <summary>
/// CItemLapenshardTable (ms2/commonfiles/itemlapensharddata.cpp), the part the item tooltip reads. Load (:188) reads
/// Table/itemLapenshardUpgrade.xml (the locale folder first) on first use: each available &lt;key&gt; with an ItemId and
/// its EquipPart mask (<see cref="GemstoneUpgradeTable.EquipPartMask"/>, the constructor's 0); a later row of an id
/// replaces an earlier one (SetAt).
/// </summary>
public sealed class LapenshardTable
{
    readonly PackSet packs;
    Dictionary<int, uint> masks;
    Dictionary<int, int> groups;
    Dictionary<int, Row> rows;
    Dictionary<int, int> firstItems;
    Dictionary<int, Dictionary<int, int>> levels;

    /// <summary>CItemLapenshardData (:101-124): the upgrade row of a lapenshard. Ingredients keep the file order, at most
    /// four (IngredientItemID1-4 "id:tagName" with IngredientCount1-4, both attributes needed).</summary>
    public sealed record Row(int Group, int Level, int Next, uint Mask, int MinCount, long Meso,
        IReadOnlyList<MasteryRecipeTable.Ingredient> Ingredients);

    public static readonly LapenshardTable Empty = new(null);

    public LapenshardTable(PackSet packs) { this.packs = packs; }

    /// <summary>Get (:339)'s m_nMask: 1 red, 2 blue, 4 green; null without a row.</summary>
    public uint? Mask(int itemId)
    {
        Load();
        return masks.TryGetValue(itemId, out var m) ? m : null;
    }

    /// <summary>GetGroup: the row's LapenGroupID, 0 without a row.</summary>
    public int Group(int itemId)
    {
        Load();
        return groups.TryGetValue(itemId, out var g) ? g : 0;
    }

    /// <summary>Get (:339): the row of an item, null without one.</summary>
    public Row Get(int itemId)
    {
        Load();
        return rows.TryGetValue(itemId, out var r) ? r : null;
    }

    /// <summary>GetLevel (:358): the row's LapenLevel, 0 without a row.</summary>
    public int Level(int itemId) => Get(itemId)?.Level ?? 0;

    /// <summary>HasUpgrade: the row has a NextItemID.</summary>
    public bool HasUpgrade(int itemId) => Get(itemId) is { } r && r.Next != 0;

    /// <summary>GetFirstItem: the level 1 item of a group, 0 when none.</summary>
    public int FirstItem(int group)
    {
        Load();
        return firstItems.TryGetValue(group, out var i) ? i : 0;
    }

    /// <summary>GetItem: the item of a group at a level, 0 when none.</summary>
    public int Item(int group, int level)
    {
        Load();
        return levels.TryGetValue(group, out var l) && l.TryGetValue(level, out var i) ? i : 0;
    }

    /// <summary>CItemLapenshardData::GetIngredients (@0x1405b41b0): scale 0 the rows as loaded; otherwise the money
    /// tagged rows (0x5d, 0x5e, 0x5f) become max(1, (uint)(count * (1 - scale / 10000) + 1e-06)) (FUN_140482050).</summary>
    public static IReadOnlyList<MasteryRecipeTable.Ingredient> Scaled(Row row, int scale)
    {
        if (row == null) return Array.Empty<MasteryRecipeTable.Ingredient>();
        if (scale == 0) return row.Ingredients;
        var r = new List<MasteryRecipeTable.Ingredient>(row.Ingredients.Count);
        foreach (var i in row.Ingredients)
        {
            uint n = (uint)i.Count;
            if (i.Tag is 0x5d or 0x5e or 0x5f)
            {
                n = (uint)(n * (1.0 - (uint)scale / 10000.0) + 1e-06);
                if (n < 1) n = 1;
            }
            r.Add(i with { Count = (int)n });
        }
        return r;
    }

    /// <summary>The item ids of the rows, for fixtures.</summary>
    public IEnumerable<int> ItemIds() { Load(); return masks.Keys; }

    void Load()
    {
        if (masks == null)
        {
            masks = new();
            groups = new();
            rows = new();
            firstItems = new();
            levels = new();
            foreach (var p in packs != null && TablePath.Resolve(packs, "itemlapenshardupgrade.xml") is { } lp ? new[] { lp } : Array.Empty<string>())   // GetXmlPath("Table", ...)
            {
                var root = GameStrings.Parse(packs.Read("Xml", p)).Root;
                if (root == null) break;
                foreach (var e in root.Elements("key"))
                {
                    if (!FeatureElements.IsAvailable(packs, e)) continue;
                    int id = ItemData.Atoi((string)e.Attribute("ItemId") ?? "0");
                    if (id == 0) continue;
                    masks[id] = GemstoneUpgradeTable.EquipPartMask(e, 0);
                    int group = ItemData.Atoi((string)e.Attribute("LapenGroupID") ?? "0");
                    groups[id] = group;
                    int level = (ushort)ItemData.Atoi((string)e.Attribute("LapenLevel") ?? "0");
                    var ings = new List<MasteryRecipeTable.Ingredient>(4);
                    for (int i = 1; i <= 4; i++)
                    {
                        string v = (string)e.Attribute("IngredientItemID" + i), c = (string)e.Attribute("IngredientCount" + i);
                        if (v == null || c == null) continue;
                        var tok = v.Split(':', StringSplitOptions.RemoveEmptyEntries);
                        ings.Add(new(tok.Length > 0 ? ItemData.Atoi(tok[0]) : 0, ItemData.Atoi(c), tok.Length > 1 ? ItemTags.ByName(tok[1]) : 0));
                    }
                    var row = new Row(group, level, ItemData.Atoi((string)e.Attribute("NextItemID") ?? "0"), masks[id],
                        ItemData.Atoi((string)e.Attribute("GroupLapenshardMinCount") ?? "0"), long.TryParse((string)e.Attribute("meso"), out var meso) ? meso : 0, ings);
                    rows[id] = row;
                    if (level == 1) firstItems[group] = id;
                    if (!levels.TryGetValue(group, out var lv)) levels[group] = lv = new();
                    lv[level] = id;
                }
                break;
            }
        }
    }
}
