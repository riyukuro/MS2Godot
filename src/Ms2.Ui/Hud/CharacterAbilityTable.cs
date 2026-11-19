using System.Xml.Linq;
using Ms2.Quest;
using Ms2.Ui.Skills;

namespace Ms2.Ui.Hud;

/// <summary>
/// CCharacterAbilityTable (ms2/commonfiles/characterability.cpp): Table/CharacterAbilityCategory.xml and
/// CharacterAbility.xml (FUN_1401df7b0 :108). Every map is a CAtlMap(17, 0.75, 0.25, 2.25): GetCategories (@0x1401e0350),
/// GetAbilities (@0x1401e0450) and a category's own map walk it in that map's order (<see cref="FieldMeta.AtlMapOrder(IEnumerable{int})"/>).
/// </summary>
public sealed class CharacterAbilityTable
{
    /// <summary>CCharacterAbility: m_nId, m_nCategoryId, m_nRequireLevel (an unsigned short), m_nAdditionalEffectId,
    /// m_nAdditionalEffectLevel (an unsigned short).</summary>
    public sealed record Ability(int Id, int CategoryId, ushort RequireLevel, int AdditionalEffectId, ushort AdditionalEffectLevel);

    /// <summary>CCharacterAbilityCategory: m_nId, m_strName and m_strTooltip (both through GetStringCommonByKey), and its
    /// abilities (m_abilities, by id) in map order.</summary>
    public sealed class Category
    {
        public int Id;
        public string Name = "", Tooltip = "";
        public readonly List<Ability> Abilities = new();
    }

    readonly Dictionary<int, Category> categories = new();       // m_categories
    readonly Dictionary<int, Ability> abilities = new();         // m_abilities

    /// <summary>The categories in m_categories' order (GetCategories).</summary>
    public IReadOnlyList<Category> Categories { get; private set; } = Array.Empty<Category>();
    /// <summary>The abilities in m_abilities' order (GetAbilities).</summary>
    public IReadOnlyList<Ability> Abilities { get; private set; } = Array.Empty<Ability>();

    /// <summary>GetCategory (@0x1401e0560): the map's Lookup; null when none.</summary>
    public Category GetCategory(int id) => categories.TryGetValue(id, out var c) ? c : null;
    /// <summary>GetAbility (@0x1401e0670): the map's Lookup; null when none.</summary>
    public Ability GetAbility(int id) => abilities.TryGetValue(id, out var a) ? a : null;

    /// <summary>
    /// LoadCharacterAbilityCategory (@0x1401df860) then LoadCharacterAbility (@0x1401dfd40). Each &lt;category&gt; and
    /// &lt;ability&gt; under &lt;ms2&gt; that IsElementAvailable and whose locale IsLocaleInList (<paramref name="available"/>
    /// covers both: an available element's locale is none or the client's) is SetAt by id, so a later row of an id
    /// replaces the earlier one in its place. A missing file or root loads nothing more: without the category file no
    /// ability is read. An ability of an unknown category stops the load there (it logs and returns 0); the abilities
    /// before it stay.
    /// </summary>
    public static CharacterAbilityTable Load(XDocument category, XDocument ability, Func<XElement, bool> available, Func<string, string> commonByKey)
    {
        var t = new CharacterAbilityTable();
        if (category?.Root is not { Name.LocalName: "ms2" } cr) return t;
        var catOrder = new List<int>();
        foreach (var e in cr.Elements("category"))
        {
            if (!available(e)) continue;
            var c = new Category
            {
                Id = Xml.Int(e, "id"),
                Name = commonByKey((string)e.Attribute("name") ?? "") ?? "",
                Tooltip = commonByKey((string)e.Attribute("tooltip") ?? "") ?? "",
            };
            t.categories[c.Id] = c;
            catOrder.Add(c.Id);
        }
        t.Categories = FieldMeta.AtlMapOrder(catOrder).Select(id => t.categories[id]).ToList();

        var order = new List<int>();
        var byCategory = new Dictionary<int, (List<int> Order, Dictionary<int, Ability> Rows)>();
        if (ability?.Root is { Name.LocalName: "ms2" } ar)
        {
            foreach (var e in ar.Elements("ability"))
            {
                if (!available(e)) continue;
                var a = new Ability(Xml.Int(e, "id"), Xml.Int(e, "categoryID"), (ushort)Xml.Int(e, "requireLevel"),
                                    Xml.Int(e, "additionalEffectID"), (ushort)Xml.Int(e, "additionalEffectLevel"));
                if (!t.categories.ContainsKey(a.CategoryId)) break;        // "Not Found CategoryID", return 0
                if (!byCategory.TryGetValue(a.CategoryId, out var m)) byCategory[a.CategoryId] = m = (new List<int>(), new Dictionary<int, Ability>());
                m.Order.Add(a.Id);
                m.Rows[a.Id] = a;           // spCategory->m_abilities.SetAt
                t.abilities[a.Id] = a;      // m_abilities.SetAt
                order.Add(a.Id);
            }
        }
        t.Abilities = FieldMeta.AtlMapOrder(order).Select(id => t.abilities[id]).ToList();
        foreach (var (cid, m) in byCategory)
            t.categories[cid].Abilities.AddRange(FieldMeta.AtlMapOrder(m.Order).Select(id => m.Rows[id]));
        return t;
    }
}
