using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.Items;

/// <summary>
/// CMasteryTable's recipes (ms2/commonfiles/masterytable.cpp), the parts the item tooltip reads. LoadReceipes (:224) reads
/// Table/masteryReceipe.xml (the locale folder first) on first use: each available &lt;receipe&gt; (IsElementAvailable)
/// is a CMasteryReceipe (Load, :97: id, masteryType and the ingredients requireItem1..5) kept by id, a later row of an id
/// replacing an earlier one (m_receipes.SetAt); every loaded row adds (its id, the ingredient count) to the list of each
/// of its ingredients' item ids (m_ingredientReceipes), in file order, so an id loaded twice is listed twice.
/// </summary>
public sealed class MasteryRecipeTable
{
    /// <summary>CMasteryReceipe: m_nId and m_nType (masteryType).</summary>
    public sealed record Recipe(int Id, int Type);

    /// <summary>CIngredientInfo: m_a (the item id) and m_c (the count).</summary>
    public readonly record struct Ingredient(int ItemId, int Count);

    readonly PackSet packs;
    readonly XDocument doc;
    Dictionary<int, Recipe> recipes;
    Dictionary<int, List<(int RecipeId, int Count)>> byIngredient;

    public static readonly MasteryRecipeTable Empty = new((PackSet)null);

    public MasteryRecipeTable(PackSet packs) { this.packs = packs; }

    /// <summary>A table read from a document (tests); every row is available.</summary>
    public MasteryRecipeTable(XDocument doc) { this.doc = doc; }

    /// <summary>CMasteryTable::Find: the recipe of an id, null when none.</summary>
    public Recipe Find(int id) { Load(); return recipes.TryGetValue(id, out var r) ? r : null; }

    /// <summary>IngredientReceipes (:701): the (recipe id, count) pairs that use the item, null when none.</summary>
    public IReadOnlyList<(int RecipeId, int Count)> IngredientRecipes(int itemId)
    {
        Load();
        return byIngredient.TryGetValue(itemId, out var l) ? l : null;
    }

    /// <summary>TypeName (:632): the common string of a mastery type 3..11, else empty.</summary>
    public static string TypeKey(int type) => type switch
    {
        3 => "s_mastery_mining",
        4 => "s_mastery_gathering",
        5 => "s_mastery_breeding",
        6 => "s_mastery_farming",
        7 => "s_mastery_blacksmithing",
        8 => "s_mastery_engraving",
        9 => "s_mastery_alchemist",
        10 => "s_mastery_cooking",
        11 => "s_mastery_pettaming",
        _ => null,
    };

    /// <summary>
    /// CIngredientContainer::FUN_14026b9d0 (commonstruct_wa.cpp:193): the text split on ',' must have exactly three parts;
    /// the first split on ':' gives the item id (its tag after the ':' is not needed here), the third the count. Only an
    /// item id other than 0 is added.
    /// </summary>
    public static Ingredient? ParseIngredient(string text)
    {
        var parts = (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) return null;
        var sub = parts[0].Split(':', StringSplitOptions.RemoveEmptyEntries);
        int id = sub.Length > 0 ? ItemData.Atoi(sub[0]) : 0;
        int count = ItemData.Atoi(parts[2]);
        return id != 0 ? new Ingredient(id, count) : null;
    }

    void Load()
    {
        if (recipes != null) return;
        recipes = new();
        byIngredient = new();
        var d = doc;
        if (d == null)
            foreach (var p in new[] { "table/kr/masteryreceipe.xml", "table/masteryreceipe.xml" })   // GetXmlPath("Table", ...)
                if (packs != null && packs.Exists("Xml", p)) { d = GameStrings.Parse(packs.Read("Xml", p)); break; }
        if (d?.Root == null) return;
        foreach (var e in d.Root.Elements("receipe"))
        {
            if (doc == null && !FeatureElements.IsAvailable(packs, e)) continue;
            int id = ItemData.Atoi((string)e.Attribute("id") ?? "0");
            var r = new Recipe(id, ItemData.Atoi((string)e.Attribute("masteryType") ?? "0"));
            recipes[id] = r;
            for (int i = 1; i <= 5; i++)
            {
                if (ParseIngredient((string)e.Attribute("requireItem" + i)) is not { } ing) continue;
                if (!byIngredient.TryGetValue(ing.ItemId, out var list)) byIngredient[ing.ItemId] = list = new();
                list.Add((r.Id, ing.Count));
            }
        }
    }
}
