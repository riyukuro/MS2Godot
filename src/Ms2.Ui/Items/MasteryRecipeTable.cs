using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.Items;

/// <summary>
/// CMasteryTable (ms2/commonfiles/masterytable.cpp). Load (:154) reads, on first use:
/// <list type="bullet">
/// <item>LoadReceipes (:224): Table/masteryReceipe.xml (the locale folder first). Each available &lt;receipe&gt;
/// (IsElementAvailable) is a CMasteryReceipe (Load, :97) kept by id, a later row of an id replacing an earlier one
/// (m_receipes.SetAt). Every loaded row adds (its id, the ingredient count) to the list of each of its ingredients' item
/// ids (m_ingredientReceipes), in file order, so an id loaded twice is listed twice.</item>
/// <item>LoadMastery (:173): Table/mastery.xml, each &lt;mastery type&gt; with 0 &lt;= type &lt; 12 and its available
/// &lt;v&gt; rows (value, grade and the job reward lists), keyed by grade in grade order (CRBMap). Reward lists of unequal
/// lengths fail the load.</item>
/// <item>LoadDifferentials (:262): Table/masteryDifferentialFactor.xml, each &lt;v differential factor icon&gt; (no
/// availability check), keyed by the differential.</item>
/// </list>
/// </summary>
public sealed class MasteryRecipeTable
{
    /// <summary>CMasteryReceipe (masterytable.h:21). <see cref="Habitats"/> are the habitatMapId map ids, which Load turns
    /// into m_strHabitat (<see cref="HabitatText"/>).</summary>
    public sealed record Recipe(int Id, int Type)
    {
        public ushort Level { get; init; }
        public uint RequireMastery { get; init; }
        public long RequireMeso { get; init; }
        public IReadOnlyList<uint> RequireQuests { get; init; } = Array.Empty<uint>();
        public IReadOnlyList<Ingredient> Ingredients { get; init; } = Array.Empty<Ingredient>();
        public uint RewardMastery { get; init; }
        public uint GatheringTime { get; init; }
        public uint HighPropLimit { get; init; }
        public uint NormalPropLimit { get; init; }
        public IReadOnlyList<uint> Habitats { get; init; } = Array.Empty<uint>();
        public IReadOnlyList<Reward> Rewards { get; init; } = Array.Empty<Reward>();
        public int ExceptRewardExp { get; init; }

        /// <summary>IconPath (:141): JoinResourcePath("MasteryReceipe", "{id}.png").</summary>
        public string IconPath => "MasteryReceipe/" + Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".png";

        /// <summary>m_strHabitat (Load, :114-119): CReservedWordTable::GetWorldMapLink(id, "#909090") of each habitat
        /// map, each later one after ", " only when the text already has something (an empty first link keeps no
        /// separator).</summary>
        public string HabitatText(Func<long, string, string> worldMapLink)
        {
            var s = "";
            foreach (var h in Habitats)
            {
                if (s.Length != 0) s += ", ";
                s += worldMapLink?.Invoke(h, "#909090") ?? "";
            }
            return s;
        }
    }

    /// <summary>CIngredientInfo: m_a (the item id), m_c (the count) and m_b (the item tag after the id's ':',
    /// CItemDataTable::GetTagByName; 0 when none).</summary>
    public readonly record struct Ingredient(int ItemId, int Count, int Tag = 0);

    /// <summary>ItemEntity: the item id, rank and count.</summary>
    public readonly record struct Reward(int ItemId, int Rank, int Count);

    /// <summary>CMasteryTable::Mastery: a grade row of a type.</summary>
    sealed record GradeRow(int Grade, uint Value, IReadOnlyList<Reward> Rewards);

    readonly PackSet packs;
    readonly XDocument doc, masteryDoc, differentialDoc;
    Dictionary<int, Recipe> recipes;
    Dictionary<int, List<(int RecipeId, int Count)>> byIngredient;
    SortedDictionary<int, GradeRow>[] mastery;
    Dictionary<int, (uint Factor, string Icon)> differentials;

    public static readonly MasteryRecipeTable Empty = new((PackSet)null);

    public MasteryRecipeTable(PackSet packs) { this.packs = packs; }

    /// <summary>A table read from documents (tests); every row is available.</summary>
    public MasteryRecipeTable(XDocument doc, XDocument masteryDoc = null, XDocument differentialDoc = null)
    {
        this.doc = doc ?? new XDocument();
        this.masteryDoc = masteryDoc;
        this.differentialDoc = differentialDoc;
    }

    /// <summary>CMasteryTable::Find: the recipe of an id, null when none.</summary>
    public Recipe Find(int id) { Load(); return recipes.TryGetValue(id, out var r) ? r : null; }

    /// <summary>IngredientReceipes (:701): the (recipe id, count) pairs that use the item, null when none.</summary>
    public IReadOnlyList<(int RecipeId, int Count)> IngredientRecipes(int itemId)
    {
        Load();
        return byIngredient.TryGetValue(itemId, out var l) ? l : null;
    }

    /// <summary>Receipes (:301): the recipes of a type sorted by requireMastery, then id (CompareReceipes, :291).</summary>
    public List<Recipe> Recipes(int type)
    {
        Load();
        var l = recipes.Values.Where(r => r.Type == type).ToList();
        l.Sort((a, b) => a.RequireMastery == b.RequireMastery ? (a.Id > b.Id ? 1 : -1) : (a.RequireMastery > b.RequireMastery ? 1 : -1));
        return l;
    }

    /// <summary>
    /// Factor (:314): the mastery a craft of the recipe rewards at the mastery value. 0 without the recipe; an Other
    /// recipe's rewardMastery; else rewardMastery * factor / 10000 (rounded) of the differential between the grade of the
    /// value and the grade of requireMastery, 0 when the differential is negative or has no row.
    /// </summary>
    public int Factor(int id, uint value)
    {
        var r = Find(id);
        if (r == null) return 0;
        if (IsOther(r)) return (int)r.RewardMastery;
        int diff = Grade(r.Type, value) - Grade(r.Type, r.RequireMastery);
        if (diff >= 0 && differentials.TryGetValue(diff, out var f))
        {
            float reward = (float)(uint)(r.RewardMastery * f.Factor) / 10000.0f;
            return (int)(uint)(reward + 0.5f);
        }
        return 0;
    }

    /// <summary>DifferentialName (:337): the differential's icon the same way as <see cref="Factor"/>, empty when none.</summary>
    public string DifferentialName(int id, uint value)
    {
        var r = Find(id);
        if (r == null) return "";
        int diff = Grade(r.Type, value) - Grade(r.Type, r.RequireMastery);
        return diff >= 0 && differentials.TryGetValue(diff, out var f) ? f.Icon : "";
    }

    /// <summary>Grade (:355): one below the first grade (in grade order) whose value is above the value, else MaxGrade; 0
    /// for a type outside 0..11.</summary>
    public int Grade(int type, uint value)
    {
        Load();
        if (type < 0 || type >= 12) return 0;
        foreach (var g in mastery[type].Values)
            if (g.Value > value) return g.Grade - 1;
        return MaxGrade(type);
    }

    /// <summary>MaxGrade (:369): the last grade of the type, 0 when none or for a type outside 0..11.</summary>
    public int MaxGrade(int type)
    {
        Load();
        if (type < 0 || type >= 12 || mastery[type].Count == 0) return 0;
        return mastery[type].Values.Last().Grade;
    }

    /// <summary>
    /// Rewards (:384): the grade rewards of a reward id (type * 1000 + grade) for the job: none for a type outside 0..12
    /// (12 has no grades), a grade below 1 or above MaxGrade; else each reward with an item id whose item data exists and
    /// is recommended for the job (ItemLimit::IsJobRecommended).
    /// </summary>
    public List<Reward> Rewards(int id, int job, Func<int, ItemData> item)
    {
        Load();
        var l = new List<Reward>();
        int grade = GradeFromID((uint)id), type = TypeFromID((uint)id);
        if (type < 0 || type > 12 || grade < 1 || grade > MaxGrade(type)) return l;
        if (!mastery[type].TryGetValue(grade, out var row)) return l;
        foreach (var r in row.Rewards)
            if (r.ItemId != 0 && item?.Invoke(r.ItemId) is { } d && d.IsJobRecommended(job))
                l.Add(r);
        return l;
    }

    /// <summary>Value (:413): the value a grade starts at; -1u for a grade below 1, above MaxGrade or without a row.</summary>
    public uint Value(int type, int grade)
    {
        Load();
        if (grade < 1 || grade > MaxGrade(type)) return uint.MaxValue;
        return mastery[type].TryGetValue(grade, out var g) ? g.Value : uint.MaxValue;
    }

    /// <summary>MaxValue (:426): Value of MaxGrade; -1u for a type outside 0..11 or without grades.</summary>
    public uint MaxValue(int type)
    {
        if (type < 0 || type >= 12) return uint.MaxValue;
        int max = MaxGrade(type);
        return max == 0 ? uint.MaxValue : Value(type, max);
    }

    /// <summary>Type (:437): the recipe's mastery type, 12 without the recipe.</summary>
    public int Type(int id) => Find(id)?.Type ?? 12;

    public static int TypeFromID(uint id) => (int)(id / 1000);
    public static int GradeFromID(uint id) => (int)(id % 1000);
    public static int MakeID(int type, int grade) => type * 1000 + grade;

    /// <summary>FirstItem (:513): the recipe's first reward, null when none.</summary>
    public Reward? FirstItem(int id) => Find(id) is { } r && r.Rewards.Count > 0 ? r.Rewards[0] : null;

    /// <summary>HasIngredient (:527): the item has data and an ingredient of the recipe has its tag or its id.</summary>
    public bool HasIngredient(int id, int itemId, Func<int, ItemData> item)
    {
        var r = Find(id);
        if (r == null || item?.Invoke(itemId) is not { } d) return false;
        int tag = ItemTags.ByName(d.StringTag);
        foreach (var i in r.Ingredients)
        {
            if (i.Tag != 0 && tag == i.Tag) return true;           // CItemData::HasStringTag
            if (i.ItemId == itemId) return true;
        }
        return false;
    }

    /// <summary>HasTypeIngredient (:547): a recipe of the type has the item as an ingredient.</summary>
    public bool HasTypeIngredient(int type, int itemId, Func<int, ItemData> item)
    {
        Load();
        foreach (var r in recipes.Values)
            if (r.Type == type && HasIngredient(r.Id, itemId, item)) return true;
        return false;
    }

    /// <summary>IsProductionType (:561): types 3..6 (mining, gathering, breeding, farming).</summary>
    public static bool IsProductionType(int type) => type is >= 3 and <= 6;
    /// <summary>IsProduction (:592): a recipe of a production type (false for none).</summary>
    public static bool IsProduction(Recipe r) => r != null && IsProductionType(r.Type);
    /// <summary>IsOther (:605): not IsProduction, so a missing recipe counts as Other.</summary>
    public static bool IsOther(Recipe r) => !IsProduction(r);

    /// <summary>
    /// LimitState (:617): 3 without the recipe or when the Lua success chance of the times gathered today (g_143ab3150,
    /// <paramref name="gathered"/>) is 0; else 0 below highPropLimitCount, 1 below normalPropLimitCount, 2 otherwise.
    /// </summary>
    public int LimitState(int id, IReadOnlyDictionary<int, int> gathered)
    {
        var r = Find(id);
        if (r == null) return 3;
        int held = gathered != null && gathered.TryGetValue(id, out int h) ? h : 0;
        double p = LuaGatheringSuccessProp((uint)held, r.HighPropLimit, r.NormalPropLimit);
        if (Math.Abs((float)p - 0.0f) < 1e-06f) return 3;              // IsNearlyEqual
        if ((uint)held < r.HighPropLimit) return 0;
        return (uint)held < r.NormalPropLimit ? 1 : 2;
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

    /// <summary>TypeName (:632) through the common strings; empty for a type without a name.</summary>
    public static string TypeName(int type, Func<string, string> common) => TypeKey(type) is { } k ? common(k) ?? "" : "";

    /// <summary>GradeName (:690): the common string s_mastery_grade_{grade}.</summary>
    public static string GradeName(int grade, Func<string, string> common) =>
        common("s_mastery_grade_" + grade.ToString(System.Globalization.CultureInfo.InvariantCulture)) ?? "";

    /// <summary>
    /// SuccessDescription (:660): by LimitState 0..3 the common string s_mastery_success_prop_high / _normal / _low / _zero
    /// formatted with TypeName; when coloured, that text formatted into s_mastery_success_prop_green (0), _yellow (1) or
    /// _red (2, 3).
    /// </summary>
    public string SuccessDescription(int type, int id, bool colored, IReadOnlyDictionary<int, int> gathered, Func<string, string> common)
    {
        string F(string key, string arg) => Ms2.Quest.ClientFormat.Format(common(key) ?? "", arg);
        int state = LimitState(id, gathered);
        string text = state switch
        {
            0 => F("s_mastery_success_prop_high", TypeName(type, common)),
            1 => F("s_mastery_success_prop_normal", TypeName(type, common)),
            2 => F("s_mastery_success_prop_low", TypeName(type, common)),
            3 => F("s_mastery_success_prop_zero", TypeName(type, common)),
            _ => "",
        };
        if (!colored) return text;
        return state switch
        {
            0 => F("s_mastery_success_prop_green", text),
            1 => F("s_mastery_success_prop_yellow", text),
            2 or 3 => F("s_mastery_success_prop_red", text),
            _ => "",
        };
    }

    // ---- the client Lua (CLua, luautil.cpp:1068-1120; the chunk of Data/lua/Precompiled.m2d) ----------------------

    /// <summary>CLua::CalcGatheringReceipeRewardItemCount (luautil.cpp:1068): Lua calcGatheringReceipeRewardItemCount
    /// returns its first argument (the count).</summary>
    public static uint LuaGatheringRewardItemCount(uint count, uint requireMastery, uint mastery) => count;

    /// <summary>CLua::CalcCraftReceipeRequireItemCount (luautil.cpp:1086): Lua calcCraftReceipeRequireItemCount returns its
    /// first argument (the count).</summary>
    public static uint LuaCraftRequireItemCount(uint count, uint requireMastery, uint mastery) => count;

    /// <summary>
    /// CLua::CalcGatheringObjectgSuccessProp (luautil.cpp:1104) calls Lua calcGatheringObjectgSuccessProp(held, high,
    /// normal, 1): 100 below <paramref name="high"/>; 0 at floor(1.778093883357 * (normal * 10 / 3 - 5) / 10 /
    /// sqrt(1 / 0.7111) + high) + 1; else with k = (normal / 0.9 * 0.35 - 0.5) / 1.406, (1 - (held - high)^2 / (2.5k)^2 /
    /// 0.7111) * 100, 0 when below 0. (The 4th argument 0 would scale high and normal by 0.2; the client passes 1.)
    /// </summary>
    public static double LuaGatheringSuccessProp(uint held, uint high, uint normal)
    {
        double cur = held, h = high, n = normal;
        double last = Math.Floor(1.778093883357 * (n * 10 / 3 - 5) / 10 / Math.Sqrt(1 / 0.7111) + h) + 1;
        double r;
        if (cur < h) r = 100;
        else if (cur == last) r = 0;
        else
        {
            double k = (n / 0.9 * 0.35 - 0.5) / 1.406;
            double w = (3 * k * 2 - k) / 2;
            r = (1 - 1 / (w * w) / 0.7111 * (cur - h) * (cur - h)) * 100;
        }
        return r < 0 ? 0 : r;
    }

    /// <summary>
    /// CIngredientContainer::FUN_14026b9d0 (commonstruct_wa.cpp:193): the text split on ',' must have exactly three parts;
    /// the first split on ':' gives the item id and the tag (GetTagByName of the part after the ':'), the third the count.
    /// Only an item id other than 0 is added.
    /// </summary>
    public static Ingredient? ParseIngredient(string text)
    {
        var parts = (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) return null;
        var sub = parts[0].Split(':', StringSplitOptions.RemoveEmptyEntries);
        int id = sub.Length > 0 ? ItemData.Atoi(sub[0]) : 0;
        int tag = sub.Length > 1 ? ItemTags.ByName(sub[1]) : 0;
        int count = ItemData.Atoi(parts[2]);
        return id != 0 ? new Ingredient(id, count, tag) : null;
    }

    /// <summary>ItemEntity::vfunc_1 (commonstruct_wa.cpp:52): "id,rank,count", each part only when present (rank and
    /// count default to 1).</summary>
    public static Reward ParseReward(string text)
    {
        var parts = (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        return new Reward(parts.Length > 0 ? ItemData.Atoi(parts[0]) : 0, parts.Length > 1 ? ItemData.Atoi(parts[1]) : 1,
            parts.Length > 2 ? ItemData.Atoi(parts[2]) : 1);
    }

    static List<uint> Tokens(string s) =>
        (s ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => (uint)ItemData.Atoi(t)).ToList();
    static uint UInt(XElement e, string name) => (uint)ItemData.Atoi((string)e.Attribute(name) ?? "0");
    /// <summary>XmlGetAttr of an __int64: the leading sign and digits, 0 when none.</summary>
    static long Long(XElement e, string name)
    {
        var s = ((string)e.Attribute(name) ?? "").TrimStart();
        int i = 0;
        bool neg = false;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) neg = s[i++] == '-';
        long v = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i])) v = v * 10 + (s[i++] - '0');
        return neg ? -v : v;
    }

    /// <summary>CMasteryReceipe::Load (:97).</summary>
    static Recipe ReadRecipe(XElement e)
    {
        int type = ItemData.Atoi((string)e.Attribute("masteryType") ?? "0");
        var ings = new List<Ingredient>();
        for (int i = 1; i <= 5; i++)
            if (ParseIngredient((string)e.Attribute("requireItem" + i)) is { } ing) ings.Add(ing);
        var rewards = new List<Reward>();
        for (int i = 1; i <= 5; i++)
            if (ParseReward((string)e.Attribute("rewardItem" + i)) is { ItemId: not 0 } rw) rewards.Add(rw);
        return new Recipe(ItemData.Atoi((string)e.Attribute("id") ?? "0"), type)
        {
            RequireMastery = UInt(e, "requireMastery"),
            RequireMeso = Long(e, "requireMeso"),
            RequireQuests = Tokens((string)e.Attribute("requireQuest")),
            Level = type == 11 ? (ushort)50 : (ushort)UInt(e, "requireLevel"),
            RewardMastery = UInt(e, "rewardMastery"),
            GatheringTime = UInt(e, "gatheringTime"),
            HighPropLimit = UInt(e, "highPropLimitCount"),
            NormalPropLimit = UInt(e, "normalPropLimitCount"),
            ExceptRewardExp = ItemData.Atoi((string)e.Attribute("exceptRewardExp") ?? "0"),
            Habitats = Tokens((string)e.Attribute("habitatMapId")),
            Ingredients = ings,
            Rewards = rewards,
        };
    }

    XDocument Read(params string[] paths)
    {
        foreach (var p in paths)                                  // GetXmlPath("Table", ...)
            if (packs != null && packs.Exists("Xml", p)) return GameStrings.Parse(packs.Read("Xml", p));
        return null;
    }

    bool Available(XElement e) => doc != null || FeatureElements.IsAvailable(packs, e);

    void Load()
    {
        if (recipes != null) return;
        recipes = new();
        byIngredient = new();
        mastery = new SortedDictionary<int, GradeRow>[12];
        for (int i = 0; i < 12; i++) mastery[i] = new();
        differentials = new();
        var d = doc ?? Read("table/kr/masteryreceipe.xml", "table/masteryreceipe.xml");
        foreach (var e in d?.Root?.Elements("receipe") ?? Enumerable.Empty<XElement>())
        {
            if (!Available(e)) continue;
            var r = ReadRecipe(e);
            recipes[r.Id] = r;
            foreach (var ing in r.Ingredients)
            {
                if (!byIngredient.TryGetValue(ing.ItemId, out var list)) byIngredient[ing.ItemId] = list = new();
                list.Add((r.Id, ing.Count));
            }
        }
        var m = doc != null ? masteryDoc : Read("table/kr/mastery.xml", "table/mastery.xml");
        if (!LoadMastery(m))
            for (int i = 0; i < 12; i++) mastery[i].Clear();        // a failed Load: STILL-OPEN what the client keeps
        var f = doc != null ? differentialDoc : Read("table/kr/masterydifferentialfactor.xml", "table/masterydifferentialfactor.xml");
        foreach (var e in f?.Root?.Elements("v") ?? Enumerable.Empty<XElement>())
            differentials[ItemData.Atoi((string)e.Attribute("differential") ?? "0")] = (UInt(e, "factor"), (string)e.Attribute("icon") ?? "");
    }

    bool LoadMastery(XDocument m)
    {
        foreach (var t in m?.Root?.Elements("mastery") ?? Enumerable.Empty<XElement>())
        {
            int type = ItemData.Atoi((string)t.Attribute("type") ?? "0");
            if (type < 0 || type >= 12) continue;
            foreach (var v in t.Elements("v"))
            {
                if (!Available(v)) continue;
                var ids = Tokens((string)v.Attribute("rewardJobItemID"));
                var ranks = Tokens((string)v.Attribute("rewardJobItemRank"));
                var counts = Tokens((string)v.Attribute("rewardJobItemCount"));
                if (ids.Count != ranks.Count || ranks.Count != counts.Count) return false;
                var rw = new List<Reward>();
                for (int i = 0; i < ids.Count; i++) rw.Add(new Reward((int)ids[i], (int)ranks[i], (int)counts[i]));
                int grade = ItemData.Atoi((string)v.Attribute("grade") ?? "0");
                mastery[type][grade] = new GradeRow(grade, UInt(v, "value"), rw);
            }
        }
        return true;
    }
}
