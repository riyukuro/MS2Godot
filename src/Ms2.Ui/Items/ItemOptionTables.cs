using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.Items;

/// <summary>
/// The option tables the item tooltip's status rows read: the stat and special ability definitions (LoadStatDefinitions,
/// stat.cpp:172; LoadSpecialAbilityStringTable, specialability.cpp:170), CItemOptionTable's random options and variation
/// rows (itemoptiondata_3.cpp:909, :942) and CItemEnchantLimitTable (itemenchantlimittable.cpp:150). Each is read on
/// first use.
/// </summary>
public sealed class ItemOptionTables
{
    /// <summary>A stat or special ability definition: its option name, valueType and value format text (the name string of
    /// its valueStringID).</summary>
    sealed record Def(string Name, int ValueType, string ValueText);

    /// <summary>ItemOptionValueRandom (itemoptiondata_2.cpp:1378): the stat (-1 when none) or special (0 when none) and
    /// its "statID" (the variation row).</summary>
    sealed record RandomValue(int Stat, int Special, int StatId);

    /// <summary>ItemOptionRandom (itemoptiondata_2.cpp:1737): "pickCount", "isRarePickOne" and the values. A GMS2 option
    /// (<see cref="Gms2Randoms"/>) gives its pick count per grade in <paramref name="PickByGrade"/> instead.</summary>
    sealed record Random(int PickCount, bool RarePickOne, List<RandomValue> Values, Dictionary<int, int> PickByGrade = null);

    /// <summary>ItemOptionVariationData (itemoptiondata_2.cpp:1633): "highStat" and the &lt;v stat&gt; values in file order.</summary>
    sealed record Variation(int HighStat, List<int> Stats);

    /// <summary>ItemOptionConstant (itemoptiondata_2.cpp:1493): its "grade" and the &lt;v&gt; values (a stat, or -1 and a
    /// special); Values is null for a createType 3 rank (LoadLuaValues, not ported).</summary>
    sealed record Constant(int Grade, List<(int Stat, int Special, int Value)> Values);

    /// <summary>EnchantLimit (itemenchantlimittable.cpp:150): "minLv", "maxLv", "maxGrade".</summary>
    sealed record EnchantLimit(int MinLv, int MaxLv, int MaxGrade);

    readonly PackSet packs;
    readonly Dictionary<string, string> statValueNames, specialValueNames;
    Def[] stats, specials;
    Dictionary<int, Random> randoms;
    Dictionary<int, Variation> variations;
    Dictionary<int, EnchantLimit> limits;
    Dictionary<int, List<Constant>> constants;

    public static readonly ItemOptionTables Empty = new(null, new(), new());

    /// <param name="statValueNames">string table stringstatname (by id).</param>
    /// <param name="specialValueNames">string table stringspecialabilityname (by id).</param>
    public ItemOptionTables(PackSet packs, Dictionary<string, string> statValueNames, Dictionary<string, string> specialValueNames)
    {
        this.packs = packs;
        this.statValueNames = statValueNames;
        this.specialValueNames = specialValueNames;
    }

    XElement Root(string path) => packs != null && packs.Exists("Xml", path) ? GameStrings.Parse(packs.Read("Xml", path)).Root : null;

    static int Int(XElement e, string name) => ItemData.Atoi((string)e.Attribute(name) ?? "0");

    /// <summary>The definitions of a key table, by &lt;key id&gt; into <paramref name="slots"/> slots (a later key replaces an
    /// earlier one; an id outside the slots is dropped).</summary>
    Def[] Defs(string path, int slots, Dictionary<string, string> valueNames)
    {
        var d = new Def[slots];
        var root = Root(path);
        if (root == null) return d;
        foreach (var e in root.Elements("key"))
        {
            if (!int.TryParse((string)e.Attribute("id"), out int id) || (uint)id >= (uint)slots) continue;
            string text = valueNames.TryGetValue((string)e.Attribute("valueStringID") ?? "", out var t) && t != null ? t : "";
            d[id] = new Def((string)e.Attribute("option") ?? "", Int(e, "valueType"), text);
        }
        return d;
    }

    /// <summary>
    /// STAND-IN (STILL-OPEN): the stat definitions of a pack that has no statStringTable.xml (the GMS2 data has none, in table/,
    /// table/na/ or any other entry; LoadStatDefinitions stat.cpp:180 would find none either). These are the 33 rows of
    /// KMS2_DEV's table/statstringtable.xml (stat id, option name, valueType) copied here, since the pack gives no table of its
    /// own to read. The texts are inferred from the option name: the GMS2 stringcommon.xml holds the client's own common-string keys
    /// s_word_stat_&lt;option&gt; (name) and s_word_stat_&lt;option&gt;_v (value format), which the client lists in its CommonStringTable
    /// (commonstringtable.cpp:2781-2880); <see cref="FallbackStatTexts"/> files them under the ids the table would give.
    /// The pack also has s_word_stat_&lt;option&gt;_r rate texts; LoadStatDefinitions (stat.cpp:180-198) reads only valueStringID,
    /// so no stat row takes a rate text and none is read here.
    /// </summary>
    public static readonly (string Option, int ValueType)[] DefaultStats =
    {
        ("str", 1), ("dex", 1), ("int", 1), ("luk", 1), ("hp", 1), ("hp_rgp", 1), ("hp_inv", 1), ("sp", 1), ("sp_rgp", 1), ("sp_inv", 1),
        ("ep", 1), ("ep_rgp", 1), ("ep_inv", 1), ("asp", 1), ("msp", 1), ("atp", 1), ("evp", 1), ("cap", 1), ("cad", 1), ("car", 1),
        ("ndd", 1), ("abp", 2), ("jmp", 2), ("pap", 1), ("map", 1), ("par", 1), ("mar", 1), ("wapmin", 1), ("wapmax", 1), ("bap", 1),
        ("bap_pet", 1), ("pen", 2), ("rmsp", 1),
    };

    /// <summary>The common-string key stem of a stat option: the weapon's min and max attack are s_word_stat_wap_d_v and
    /// s_word_stat_wap_u_v ("Min Attack {0}", "Max Attack {0}", commonstringtable.cpp:2841-2842); the rest are the option's name.</summary>
    static string TextKey(string option) => option switch { "wapmin" => "wap_d", "wapmax" => "wap_u", _ => option };

    /// <summary>The value format texts of <see cref="DefaultStats"/> by valueStringID (1000 + stat id), from the common strings.
    /// The name texts (s_word_stat_&lt;option&gt;) of wapmin and wapmax have no key of their own and stay empty (STILL-OPEN).</summary>
    public static void FallbackStatTexts(Dictionary<string, string> common, Dictionary<string, string> valueNames)
    {
        for (int i = 0; i < DefaultStats.Length; i++)
            if (common.TryGetValue("s_word_stat_" + TextKey(DefaultStats[i].Option) + "_v", out var v)) valueNames.TryAdd((1000 + i).ToString(), v);
    }

    Def[] Stats()
    {
        if (stats != null) return stats;
        var path = packs == null ? null : TablePath.Resolve(packs, "statstringtable.xml");
        if (path != null) return stats = Defs(path, 0x23, statValueNames);
        var d = new Def[0x23];
        for (int i = 0; i < DefaultStats.Length; i++)
            d[i] = new Def(DefaultStats[i].Option, DefaultStats[i].ValueType, statValueNames.TryGetValue((1000 + i).ToString(), out var t) ? t : "");
        return stats = d;
    }
    Def[] Specials() => specials ??= Defs("table/specialabilitystringtable.xml", 0xcf, specialValueNames);

    /// <summary>FindStatByName (stat.cpp): the stat whose option name equals <paramref name="name"/> (case-insensitive),
    /// -1 when none.</summary>
    public int FindStatByName(string name)
    {
        var s = Stats();
        for (int i = 0; i < s.Length; i++)
            if (s[i] != null && string.Equals(s[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>FUN_14081d260 (specialability.cpp): the special ability whose option name equals <paramref name="name"/>
    /// (case-insensitive), 0 when none.</summary>
    public int FindSpecialByName(string name)
    {
        var s = Specials();
        for (int i = 0; i < s.Length; i++)
            if (s[i] != null && string.Equals(s[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        return 0;
    }

    /// <summary>FUN_14081d380 (specialability.cpp): the special ability's option name, "" when none.</summary>
    public string SpecialOption(int special) => (uint)special < 0xcf ? Specials()[special]?.Name ?? "" : "";

    /// <summary>IsStatKindTwo (stat.cpp): the stat's valueType is 2 (a rate, in tenths).</summary>
    public bool IsStatRate(int stat) => (uint)stat < 0x23 && Stats()[stat]?.ValueType == 2;
    /// <summary>GetStatValueText (stat.cpp): the stat's value format text, "" when none.</summary>
    public string StatValueText(int stat) => (uint)stat < 0x23 ? Stats()[stat]?.ValueText ?? "" : "";
    /// <summary>FUN_14081dcd0 (specialability.cpp): the special ability's valueType is 2 (a rate, in tenths).</summary>
    public bool IsSpecialRate(int special) => (uint)special < 0xcf && Specials()[special]?.ValueType == 2;
    /// <summary>The special ability's value format text (SpecialAbilityInfo, valueStringID), "" when none.</summary>
    public string SpecialValueText(int special) => (uint)special < 0xcf ? Specials()[special]?.ValueText ?? "" : "";

    /// <summary>
    /// LoadItemOptionRandom (itemoptiondata_3.cpp:909): every &lt;option&gt; of Table/ItemOptionRandom.xml whose feature is
    /// empty or on (FUN_14028f5d0, commonutil.cpp:1022), keyed by "code"; code 0 is dropped and the first of a code is kept
    /// (unordered_map insert; the client loads them in parallel, so which duplicate wins there is not fixed). Each
    /// option's &lt;v&gt; children pass the same filter; a v without a name, or whose name is neither a stat nor a special
    /// ability, is dropped.
    /// </summary>
    Dictionary<int, Random> Randoms()
    {
        if (randoms != null) return randoms;
        randoms = new();
        var root = Root("table/itemoptionrandom.xml");
        if (root == null) return Gms2Randoms(randoms);
        foreach (var e in root.Elements("option"))
        {
            if (!FeatureOn(e)) continue;
            int code = Int(e, "code");
            if (code == 0 || randoms.ContainsKey(code)) continue;
            var values = new List<RandomValue>();
            foreach (var v in e.Elements("v"))
            {
                if (!FeatureOn(v)) continue;
                string name = (string)v.Attribute("name") ?? "";
                if (name.Length == 0) continue;
                int statId = Int(v, "statID");
                int stat = FindStatByName(name);
                if (stat != -1) { values.Add(new RandomValue(stat, 0, statId)); continue; }
                int special = FindSpecialByName(name);
                if (special != 0) values.Add(new RandomValue(-1, special, statId));
            }
            randoms[code] = new Random(Int(e, "pickCount"), Int(e, "isRarePickOne") != 0, values);
        }
        return randoms;
    }

    /// <summary>
    /// GMS2 has no Table/ItemOptionRandom.xml: its random options are itemoption/option/random/itemoptionrandom_&lt;x&gt;.xml, one
    /// &lt;option code grade optionNumPick="min,max"&gt; per code and grade with the values as &lt;name&gt;_value_base /
    /// &lt;name&gt;_rate_base attributes (<see cref="Gms2Values"/>). The pick count of a grade is optionNumPick's max (min and max
    /// are equal in every row of the data). No row carries a statID, so these values link no variation row (RandomOptionRange
    /// gives (0, 0) and IsHighRoll false). INFERRED from the data and the Maple2 server's reader of the same files (its
    /// ItemOptionParser reads these files by name; the GMS2 client's reader is not in the restored source). STILL-OPEN: the
    /// GMS2 variation tables (table/itemoptionvariation*.xml, by option name) are not read.
    /// </summary>
    Dictionary<int, Random> Gms2Randoms(Dictionary<int, Random> into)
    {
        foreach (var e in Gms2Rows("itemoption/option/random/"))
        {
            if (!FeatureOn(e)) continue;
            int code = Int(e, "code");
            if (code == 0) continue;
            if (!into.TryGetValue(code, out var r))
                into[code] = r = new Random(0, false, Gms2Values(e).Select(v => new RandomValue(v.Stat, v.Special, 0)).ToList(), new());
            var pick = ((string)e.Attribute("optionNumPick") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            r.PickByGrade.TryAdd(Int(e, "grade"), pick.Length == 0 ? 0 : ItemData.Atoi(pick[^1]));
        }
        return into;
    }

    /// <summary>The &lt;option&gt; rows of the GMS2 per-kind option files under <paramref name="folder"/>, the files in name order.</summary>
    IEnumerable<XElement> Gms2Rows(string folder)
    {
        if (packs == null) yield break;
        foreach (var name in packs.List("Xml").Where(n => n.StartsWith(folder, StringComparison.Ordinal) && n.EndsWith(".xml", StringComparison.Ordinal))
                     .OrderBy(n => n, StringComparer.Ordinal))
            if (Root(name) is { } root)
                foreach (var e in root.Elements("option")) yield return e;
    }

    /// <summary>
    /// The values of a GMS2 option row, in attribute order: &lt;name&gt;_value_base for a stat or special whose valueType is not
    /// 2, and &lt;name&gt;_rate_base for one whose valueType is 2, the rate read in the KMS rows' unit (thousandths: GMS2's
    /// parpen_rate_base 0.054 is the KMS rows' parpen 54), rounded. A name that is neither a stat nor a special ability (the
    /// GMS2-only calibration factors and hidden adds), or whose kind is the other one (78 wapmin/wapmax_rate_base and 6
    /// pvpdamage*_rate_base rows), and a value of 0 are dropped. INFERRED from the data (the GMS2 client's reader is not in
    /// the restored source). STILL-OPEN: GMS2 has no specialabilitystringtable.xml, so <see cref="FindSpecialByName"/> finds no
    /// special there and the special values are dropped too.
    /// </summary>
    internal List<(int Stat, int Special, int Value)> Gms2Values(XElement e)
    {
        var values = new List<(int, int, int)>();
        foreach (var a in e.Attributes())
        {
            string n = a.Name.LocalName;
            bool rate = n.EndsWith("_rate_base", StringComparison.Ordinal);
            if (!rate && !n.EndsWith("_value_base", StringComparison.Ordinal)) continue;
            string name = n[..^(rate ? "_rate_base".Length : "_value_base".Length)];
            if (!double.TryParse(a.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double x)) continue;
            double scaled = rate ? Math.Round(x * 1000) : x;
            if (scaled == 0 || scaled > int.MaxValue || scaled < int.MinValue) continue;
            int value = (int)scaled;
            int stat = FindStatByName(name);
            if (stat != -1) { if (IsStatRate(stat) == rate) values.Add((stat, 0, value)); continue; }
            int special = FindSpecialByName(name);
            if (special != 0 && IsSpecialRate(special) == rate) values.Add((-1, special, value));
        }
        return values;
    }

    /// <summary>IsElementFeatureEnabled (commonutil.cpp:1035): the "feature" attribute is empty or on.</summary>
    bool FeatureOn(XElement e)
    {
        string feature = (string)e.Attribute("feature") ?? "";
        return feature.Length == 0 || LobbyFeatures.IsEnabledByName(packs, feature);
    }

    /// <summary>LoadItemOptionVariation (itemoptiondata_3.cpp:942): every &lt;option&gt; of Table/itemOptionVariation.xml by
    /// "id" (std::map insert keeps the first), no feature filter.</summary>
    Dictionary<int, Variation> Variations()
    {
        if (variations != null) return variations;
        variations = new();
        var root = Root("table/itemoptionvariation.xml");
        if (root == null) return variations;
        foreach (var e in root.Elements("option"))
        {
            int id = Int(e, "id");
            if (variations.ContainsKey(id)) continue;
            variations[id] = new Variation(Int(e, "highStat"), e.Elements("v").Select(v => Int(v, "stat")).ToList());
        }
        return variations;
    }

    /// <summary>CItemData::FUN_14053eff0 (itemdata.cpp:1633) for a non-pet item: its randomID.</summary>
    static int RandomCode(ItemData d) => d.RandomId;

    /// <summary>ItemOptionRandom::GetPickCount (itemoptiondata_2.cpp:2021) of the item's random option at
    /// <paramref name="rank"/>; null without the option (GetRandom, a map find). Not for pets.</summary>
    public int? PickCount(ItemData d, int rank)
    {
        if (!Randoms().TryGetValue(RandomCode(d), out var r)) return null;
        if (r.PickByGrade != null) return r.PickByGrade.TryGetValue(rank, out int n) ? n : null;     // GMS2 (Gms2Randoms)
        if (r.RarePickOne)
        {
            if (rank == 2) return 1;
            if (rank == 1) return 0;
        }
        return r.PickCount;
    }

    /// <summary>GetStatIdByStat / GetStatIdBySpecial (itemoptiondata_2.cpp:2198, :2207): the statID of the first value of
    /// the stat or special; 0 when none. <paramref name="stat"/> -1 matches by <paramref name="special"/>.</summary>
    int StatId(Random r, int stat, int special)
    {
        foreach (var v in r.Values)
            if (stat != -1 ? v.Stat == stat : v.Special == special) return v.StatId;
        return 0;
    }

    /// <summary>GetVariationRange (itemoptiondata_3.cpp:562): the first and last stat of the row, (0, 0) when none.</summary>
    (int First, int Last) VariationRange(int id) =>
        Variations().TryGetValue(id, out var v) && v.Stats.Count > 0 ? (v.Stats[0], v.Stats[^1]) : (0, 0);

    /// <summary>GetRandomOptionRange / GetRandomSpecialOptionRange (itemoptiondata_3.cpp:575, :590): the variation range of
    /// the stat (or, with <paramref name="stat"/> -1, the special) in the item's random option at
    /// <paramref name="rank"/>; (0, 0) when none.</summary>
    public (int First, int Last) RandomOptionRange(ItemData d, int rank, int stat, int special)
    {
        if (d == null || !Randoms().TryGetValue(RandomCode(d), out var r)) return (0, 0);
        return VariationRange(StatId(r, stat, special));
    }

    /// <summary>IsHighRollValue / IsHighRollSpecialValue (itemoptiondata_3.cpp:606, :631): the value is in the top band of
    /// its variation row; the item must be level 50 or more.</summary>
    public bool IsHighRoll(ItemData d, int stat, int special, int value)
    {
        if (d == null || (ushort)d.LevelLimit < 50) return false;
        if (!Randoms().TryGetValue(RandomCode(d), out var r)) return false;
        int id = StatId(r, stat, special);
        int high = Variations().TryGetValue(id, out var v) ? v.HighStat : 0;
        if (high == 0) return false;
        var range = VariationRange(id);
        if (range.First == range.Last) return true;
        if (range.First >= value) return false;
        return value >= high;
    }

    /// <summary>CItemEnchantLimitTable::Load (itemenchantlimittable.cpp:150): the &lt;limit&gt; elements of
    /// Table/enchantLimitTable.xml picked per "slot" (FUN_14028faa0), by slot.</summary>
    Dictionary<int, EnchantLimit> Limits()
    {
        if (limits != null) return limits;
        limits = new();
        var root = Root("table/enchantlimittable.xml");
        if (root == null) return limits;
        foreach (var e in FeatureElements.Collect(packs, root, "limit", "slot"))
            limits[Int(e, "slot")] = new EnchantLimit((ushort)Int(e, "minLv"), (ushort)Int(e, "maxLv"), Int(e, "maxGrade"));
        return limits;
    }

    /// <summary>
    /// CItemEnchantLimitTable::CanEnchant (itemenchantlimittable.cpp:185): an equip item that has not expired, has no
    /// unlimited enchant level, whose slot has a limit with the item's level limit inside it and the enchant level below
    /// its max grade, and whose data does not set exceptEnchant.
    /// </summary>
    public bool CanEnchant(Item item)
    {
        var d = item?.Data;
        if (d == null || d.Type != 1) return false;
        if (item.ExpiryTime > 0) return false;                              // FUN_140525c60: 0 < FUN_140528910
        if (item.UnlimitedEnchantLevel > 0) return false;                   // GetPlusLevel
        if (!Limits().TryGetValue(d.EquipSubtype, out var l)) return false;
        int level = (ushort)d.LevelLimit;                                   // GetRequiredLevel
        if (level < l.MinLv || level > l.MaxLv) return false;
        if (item.EnchantLevel >= l.MaxGrade) return false;
        return !d.ExceptEnchant;
    }

    /// <summary>
    /// CItemEnchantLimitTable::FUN_14058e230 @0x14058e230 (itemenchantlimittable.cpp:218), the limit test of the enchant
    /// dialog's list (UIItemEnchantDialog::GetEnchantableItemUids): the item's slot has a limit with the required level inside
    /// it and the item data does not set exceptEnchant. Unlike <see cref="CanEnchant"/> it asks neither the item type, the lock,
    /// the plus level nor the enchant level against the max grade.
    /// </summary>
    public bool InEnchantLimit(Item item)
    {
        var d = item?.Data;
        if (d == null) return false;
        if (!Limits().TryGetValue(d.EquipSubtype, out var l)) return false;
        int level = (ushort)d.LevelLimit;
        if (level < l.MinLv || level > l.MaxLv) return false;
        return !d.ExceptEnchant;
    }

    /// <summary>
    /// LoadItemOptionConstant (itemoptiondata_3.cpp:872): every &lt;option&gt; of Table/ItemOptionConstant.xml whose feature is
    /// on (FUN_14028f5d0), by "code" (code 0 is dropped); each of its &lt;rank&gt; children that pass the same filter is one
    /// ItemOptionConstant of the code (a multimap, the ranks in file order; the client loads the options in parallel, so the
    /// order of two options with one code is not fixed). ItemOptionConstant::Load (itemoptiondata_2.cpp:1493): "grade",
    /// then for createType 3 the Lua values (LoadLuaValues, STILL-OPEN: not ported), else the &lt;v&gt; children that pass
    /// the filter: an empty name is skipped, a stat name (FindStatByName) or else a special name (FUN_14081d260) keeps its
    /// "value" when it is not 0.
    /// </summary>
    Dictionary<int, List<Constant>> Constants()
    {
        if (constants != null) return constants;
        constants = new();
        var root = Root("table/itemoptionconstant.xml");
        if (root == null)
        {
            // GMS2 has no Table/ItemOptionConstant.xml: itemoption/constant/itemoptionconstant_<x>.xml holds one <option code grade>
            // per code and grade, its values as attributes (Gms2Values), each row one rank of the code. The files the Maple2
            // server's ItemOptionParser reads are the nine of that folder; itemoption/rank/ (other values for the same codes) is
            // read by neither. INFERRED from the data and that reader; STILL-OPEN: the GMS2 client's reader is not in the restored source.
            foreach (var e in Gms2Rows("itemoption/constant/"))
            {
                if (!FeatureOn(e)) continue;
                int code = Int(e, "code");
                if (code != 0) Add(code, new Constant(Int(e, "grade"), Gms2Values(e)));
            }
            return constants;
        }
        foreach (var e in root.Elements("option"))
        {
            if (!FeatureOn(e)) continue;
            int code = Int(e, "code");
            if (code == 0) continue;
            foreach (var r in e.Elements("rank"))
            {
                if (!FeatureOn(r)) continue;
                int grade = Int(r, "grade");
                if (Int(r, "createType") == 3) { Add(code, new Constant(grade, null)); continue; }
                var values = new List<(int, int, int)>();
                foreach (var v in r.Elements("v"))
                {
                    if (!FeatureOn(v)) continue;
                    string name = (string)v.Attribute("name") ?? "";
                    if (name.Length == 0) continue;
                    int value = Int(v, "value");
                    int stat = FindStatByName(name);
                    if (stat != -1) { if (value != 0) values.Add((stat, 0, value)); continue; }
                    int special = FindSpecialByName(name);
                    if (special != 0 && value != 0) values.Add((-1, special, value));
                }
                Add(code, new Constant(grade, values));
            }
        }
        return constants;

        void Add(int code, Constant c)
        {
            if (!constants.TryGetValue(code, out var list)) constants[code] = list = new();
            list.Add(c);
        }
    }

    /// <summary>
    /// ApplyConstantOptions (itemoptiondata_3.cpp:413) on a fresh option: the values FindConstantOption (:485) picks for
    /// the item's constantId at <paramref name="rank"/>, the last rank of the code whose grade is not above it, as
    /// FUN_1405d2e70 (itemoptiondata_2.cpp:1536) sets them in option group 0: a value above 0, the last of a stat or
    /// special winning (SetAt). Empty without a rank; null for a Lua rank (STILL-OPEN).
    /// </summary>
    public (Dictionary<int, int> Stat, Dictionary<int, int> Special)? ConstantOptions(ItemData d, int rank)
    {
        var stat = new Dictionary<int, int>();
        var special = new Dictionary<int, int>();
        if (d == null || !Constants().TryGetValue(d.ConstantId, out var list)) return (stat, special);
        Constant pick = null;
        int best = 0;
        foreach (var c in list)
            if (rank >= c.Grade && rank >= best) { pick = c; best = c.Grade; }
        if (pick == null) return (stat, special);
        if (pick.Values == null) return null;
        foreach (var (s, sp, v) in pick.Values)
        {
            if (v <= 0) continue;                           // vfunc_1: the base ItemOptionValue returns its value
            if (s != -1) stat[s] = v; else special[sp] = v;
        }
        return (stat, special);
    }

    static readonly int[] Rates20 = { 0, 10000, 10000, 10000, 5000, 5000, 5000, 2500, 2500, 2500, 2000, 2000, 1000, 500, 200, 100 };
    static readonly int[] Rates38 = { 0, 10000, 10000, 10000, 5000, 5000, 5000, 2500, 2500, 2500, 2000, 3334, 2000, 2000, 1250, 1250 };

    /// <summary>FUN_140528f90 (item.cpp): the enchant gauge step of the next level (<paramref name="level"/> + 1): with a
    /// charge, version 0 reads the first rate table and version 1 the second (any other version 0); without a charge the
    /// second. 0 past level 15.</summary>
    public static int EnchantStep(int level, int charge, int version)
    {
        int lv = level + 1;
        if ((uint)(lv - 1) >= 15) return 0;
        if (charge != 0) return version == 0 ? Rates20[lv] : version == 1 ? Rates38[lv] : 0;
        return Rates38[lv];
    }

}
