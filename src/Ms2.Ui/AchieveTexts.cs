using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Quest;
using Ms2.Ui.Items;
using ReservedWords = Ms2.Formats.World.ReservedWords;

namespace Ms2.Ui;

/// <summary>
/// CAchieveUIData (client/ms2/achievemanager.cpp:792-900, 1265-1276) and the trophy texts over it (:902-1262): the
/// description rows of string/&lt;lang&gt;/achieveDescription.xml, the special rewards of
/// achieveSpecialRewardDescription.xml, and the name, grade, requirement, progress and reward lines the trophy dialog,
/// its tooltip and the notices show.
/// </summary>
public sealed class AchieveTexts
{
    /// <summary>One CAchieveUIData row: desc and manualDesc with bold words replaced, complete and manualComplete with
    /// object words replaced (LoadDesc, :812).</summary>
    public sealed record Desc(int Id, string Text, string Complete, string ManualDesc, string ManualComplete);

    /// <summary>CAchieveUIData::SpecialRewardDesc: specialDesc and specialNotifyString with object words replaced
    /// (LoadSpecialReward, :854).</summary>
    public sealed record SpecialReward(string SpecialDesc, string NotifyIcon, string NotifyString);

    readonly Dictionary<int, Desc> descs;
    readonly Dictionary<ulong, SpecialReward> specials;

    /// <summary>GetAchieveData.</summary>
    public Func<int, AchieveData> Data = _ => null;
    /// <summary>CAchieveManager::Find (the player's trophies).</summary>
    public Func<int, Achieve> Find = _ => null;
    /// <summary>GetStringCommonByKey.</summary>
    public Func<string, string> CommonByKey = _ => "";
    /// <summary>CStringTable::FUN_141811980(table, id): 1 item names, 4 skill names, 0x18 trophy names, 0x48 palette
    /// colour names.</summary>
    public Func<int, long, string> TableString = (_, _) => "";
    /// <summary>FUN_141b720b0(code, 0), the name of a title reward (STILL-OPEN: the client reads it through
    /// FUN_141b720b0, whose table is not confirmed; HudScene answers with the title name table).</summary>
    public Func<int, string> TitleName = _ => "";
    /// <summary>CFeatureManager::IsFeatureEnabled.</summary>
    public Func<int, bool> FeatureOn = _ => false;
    /// <summary>The local player's PC::GetGender (0 male), null when there is no local player.</summary>
    public Func<int?> Gender = () => null;
    /// <summary>CItemDataTable::GetPaletteColors (itemdatatable.cpp:820): a palette's colours in file order.</summary>
    public Func<int, IReadOnlyList<ColorPaletteTable.Color>> PaletteColors = _ => Array.Empty<ColorPaletteTable.Color>();

    public AchieveTexts(Dictionary<int, Desc> descs, Dictionary<ulong, SpecialReward> specials)
    {
        this.descs = descs;
        this.specials = specials;
    }

    /// <summary>
    /// LoadAll (@0x14093af30): LoadDesc (@0x14093af50) keeps the &lt;achieve&gt; rows FUN_14028faa0 picks by id (locale and
    /// feature), LoadSpecialReward (@0x14093b440) every &lt;achieve&gt; row keyed by id and grade (a later row replaces an
    /// earlier one). FUN_14071ae20 reads the client's one string language; the first of <paramref name="languages"/>
    /// whose file exists stands for it.
    /// </summary>
    public static AchieveTexts Load(PackSet packs, IEnumerable<string> languages, Func<string, string> objectWords, Func<string, string> boldWords)
    {
        var descs = new Dictionary<int, Desc>();
        var specials = new Dictionary<ulong, SpecialReward>();
        var langs = languages?.ToArray() ?? Array.Empty<string>();
        if (packs != null && Root(packs, langs, "achievedescription.xml") is { } root)
            foreach (var e in FeatureElements.Collect(packs, root, "achieve", "id"))
            {
                int id = ReservedWords.Atoi((string)e.Attribute("id") ?? "");
                descs[id] = new Desc(id, boldWords((string)e.Attribute("desc") ?? ""), objectWords((string)e.Attribute("complete") ?? ""),
                    boldWords((string)e.Attribute("manualDesc") ?? ""), objectWords((string)e.Attribute("manualComplete") ?? ""));
            }
        if (packs != null && Root(packs, langs, "achievespecialrewarddescription.xml") is { } sroot)
            foreach (var e in sroot.Elements("achieve"))
            {
                uint id = (uint)ReservedWords.Atoi((string)e.Attribute("id") ?? "");      // GetUIntAttr
                int grade = ReservedWords.Atoi((string)e.Attribute("grade") ?? "");
                specials[Key((int)id, grade)] = new SpecialReward(objectWords((string)e.Attribute("specialDesc") ?? ""),
                    (string)e.Attribute("specialNotifyIcon") ?? "", objectWords((string)e.Attribute("specialNotifyString") ?? ""));
            }
        return new AchieveTexts(descs, specials);
    }

    static XElement Root(PackSet packs, string[] langs, string file)
    {
        foreach (var lang in langs)
        {
            string entry = $"string/{lang}/{file}";
            if (!packs.Exists("Xml", entry)) continue;
            try
            {
                var root = GameStrings.Parse(packs.Read("Xml", entry)).Root;
                return root != null && root.Name == "ms2" ? root : null;
            }
            catch (System.Xml.XmlException) { return null; }
        }
        return null;
    }

    /// <summary>The s_mapSpecial key: (ULONGLONG)(unsigned)id &lt;&lt; 32 | (__int64)grade.</summary>
    static ulong Key(int id, int grade) => (ulong)(uint)id << 32 | (ulong)(long)grade;

    /// <summary>CAchieveUIData::Find (@0x14093b8c0).</summary>
    public Desc FindDesc(int id) => descs.TryGetValue(id, out var d) ? d : null;

    /// <summary>FindSpecialReward (@0x14093e3e0).</summary>
    public SpecialReward FindSpecialReward(int id, int grade) => specials.TryGetValue(Key(id, grade), out var s) ? s : null;

    // ---- common strings by id (stringcommon ids the trophy texts use; commonstring source m_strings[id]) ----

    static readonly Dictionary<int, string> CommonKeys = new()
    {
        [0x76d] = "s_achieve_adventure", [0x76e] = "s_achieve_combat", [0x76f] = "s_achieve_life",
        [0x770] = "s_achieve_reward_item", [0x774] = "s_achieve_reward_title",
        [0x77c] = "s_achieve_get_achieve_hero_progress", [0x77d] = "s_achieve_get_achieve_hero_complete",
        [0x77f] = "s_achieve_state_progress", [0x780] = "s_achieve_state_complete",
        [0x778] = "s_achieve_meter", [0x779] = "s_achieve_killometer", [0x77a] = "s_achieve_killometer_f",
        [0x781] = "s_achieve_tooltip_comment", [0x784] = "s_achieve_category_reward_count",
        [0x7ff] = "s_achieve_renderer_count", [0x800] = "s_achieve_renderer_count_time",
        [0x801] = "s_achieve_renderer_hour", [0x802] = "s_achieve_renderer_min",
        [0x80d] = "s_notify_achieve_reward_deadfail", [0x8b7] = "s_ban_check_err_all_word",
    };

    /// <summary>GetStringCommon(id) of the ids above.</summary>
    public string Common(int id) => CommonKeys.TryGetValue(id, out var k) ? CommonByKey(k) : "";

    /// <summary>FormatStringId / BlockFormat: the common string of the id formatted with the arguments.</summary>
    public string CommonF(int id, params object[] args) => ClientFormat.Format(Common(id), args);

    /// <summary>FUN_14028dfa0: the common string of the key formatted with the arguments ("" for an empty string).</summary>
    public string CommonKeyF(string key, params object[] args) => ClientFormat.Format(CommonByKey(key), args);

    // ---- texts ----

    /// <summary>FUN_14093b970: the trophy's name (string table 0x18).</summary>
    public string Name(int id) => TableString(0x18, id);

    static readonly char[] Roman = { '0', 'Ⅰ', 'Ⅱ', 'Ⅲ', 'Ⅳ', 'Ⅴ', 'Ⅵ', 'Ⅶ', 'Ⅷ', 'Ⅸ', 'Ⅹ' };

    /// <summary>FUN_14093b9c0: s_achieve_title of the grade's roman numeral (0..10, else "") and the name.</summary>
    public string GradeTitle(int id, int grade) =>
        grade < 0 || grade >= Roman.Length ? "" : CommonKeyF("s_achieve_title", Roman[grade].ToString(), Name(id));

    /// <summary>FUN_14093bad0: the format (default "[{0}]{1}") of the category tag (1 combat, 2 adventure, 3 life, else
    /// "") and <see cref="GradeTitle"/>; "" without the trophy's data.</summary>
    public string TaggedTitle(int id, int grade, string format = "")
    {
        var d = Data(id);
        if (d == null) return "";
        if (string.IsNullOrEmpty(format)) format = "[{0}]{1}";
        string tag = d.Category switch { 1 => Common(0x76e), 2 => Common(0x76d), 3 => Common(0x76f), _ => "" };
        return ClientFormat.Format(format, tag, GradeTitle(id, grade));
    }

    /// <summary>GetRequireAchieveText (@0x14093bd40): the format (default s_skill_require_achieve) of
    /// <see cref="TaggedTitle"/> and the grade's requirement with its tags stripped.</summary>
    public string RequireText(int id, int grade, string format = "")
    {
        string title = TaggedTitle(id, grade);
        if (title.Length == 0) return "";
        string cond = RequirementText(id, grade, false);
        if (string.IsNullOrEmpty(format)) format = CommonByKey("s_skill_require_achieve");
        return ClientFormat.Format(format, title, cond);
    }

    /// <summary>
    /// GetConditionDisplayKind (@0x14093e2b0) over ConditionKindOfType (conditionutil.cpp AddCondition): kind 0x11 (types
    /// 0x41-0x45) shows seconds (1) except stay_cube 0x44 / survive_cube 0x45 in minutes (0); kind 0xf (0x8c-0x92)
    /// distance (2); kind 0xe (0x93-0x99) minutes (0); enchant_result 0x7b a stack (3); else a plain count (4).
    /// </summary>
    public static int DisplayKind(int type)
    {
        if (type >= 0x41 && type <= 0x45) return type == 0x44 || type == 0x45 ? 0 : 1;
        if (type >= 0x8c && type <= 0x92) return 2;
        if (type >= 0x93 && type <= 0x99) return 0;
        return type == 0x7b ? 3 : 4;
    }

    /// <summary>GetAchieveProgressText (@0x14093bf10): the count against the grade's goal (the count capped at it).</summary>
    public string ProgressText(Achieve a, int grade)
    {
        if (a == null) return "";
        long progress = a.Count, value = a.GradeGoal(grade);
        if (progress > value) progress = value;
        return DisplayKind(a.Condition.Type) switch
        {
            1 => CommonF(0x800, FormatDuration((int)progress), FormatDuration((int)value)),
            2 => CommonKeyF("s_achieve_renderer_count_distance", FormatDistance((int)progress), FormatDistance((int)value)),
            0 => CommonF(0x800, FormatDuration((int)(progress / 60)), FormatDuration((int)(value / 60))),
            _ => CommonF(0x7ff, progress, value),
        };
    }

    /// <summary>FUN_14093c320: the complete text of the current grade, its target the first value of the grade's
    /// condition target (a stack counts from the target).</summary>
    public string CurrentText(Achieve a)
    {
        if (a == null) return "";
        var d = Data(a.Id);
        var g = d?.Grade(a.CurGrade);
        if (g == null) return "";
        long progress = a.Count, target = g.Condition.Target.FirstValue;
        switch (DisplayKind(a.Condition.Type))
        {
            case 1: return CompleteText(a.Id, FormatDuration((int)progress), FormatDuration((int)target));
            case 2: return CompleteText(a.Id, FormatDistance((int)progress), FormatDistance((int)target));
            case 0: return CompleteText(a.Id, FormatDuration((int)(progress / 60)), FormatDuration((int)(target / 60)));
            case 3:
                if (target != 0) progress -= target - 1;
                return CompleteText(a.Id, progress, target);
            case 4: return CompleteText(a.Id, progress, target);
        }
        return "";
    }

    /// <summary>FUN_14093c8d0: the description of a grade from its goal and target; the tags are stripped
    /// (FUN_141842290) unless <paramref name="raw"/>.</summary>
    public string RequirementText(int id, int grade, bool raw)
    {
        var g = Data(id)?.Grade(grade);
        if (g == null) return "";
        long goal = g.Condition.Goal, target = g.Condition.Target.FirstValue;
        string text = DisplayKind(g.Condition.Type) switch
        {
            1 => DescText(id, FormatDuration((int)goal), FormatDuration((int)target)),
            2 => DescText(id, FormatDistance((int)goal), FormatDistance((int)target)),
            0 => DescText(id, FormatDuration((int)(goal / 60)), FormatDuration((int)(target / 60))),
            _ => DescText(id, goal, target),
        };
        return raw ? text : ReservedWords.StripTags(text);
    }

    /// <summary>FUN_14093cdb0: <see cref="RewardText"/> of the start grade of the player's trophy.</summary>
    public string StartRewardText(int id) => Find(id) is { } a ? RewardText(id, a.StartGrade) : "";

    /// <summary>
    /// FUN_14093ce70: the reward line of a grade (a description row must exist): the special reward's text, else by
    /// reward type: item 1, title 2, stat point 3, skill point 4, buy authority 5-7; with feature 0xbb also a dynamic action
    /// 12, colouring 8 (s_achieve_reward_coloring of each colour's name, string table 0x48, joined by newlines) and beauty 10/11 (by the local player's gender); with feature 0xbc beauty 9.
    /// </summary>
    public string RewardText(int id, int grade)
    {
        var g = Data(id)?.Grade(grade);
        if (g == null || FindDesc(id) == null) return "";
        if (FindSpecialReward(id, grade) is { } special) return special.SpecialDesc;
        var r = g.Reward;
        int value = (int)r.Value;
        uint code = r.Code;
        switch (r.Type)
        {
            case 1: return CommonF(0x770, TableString(1, code), value);
            case 2: return CommonF(0x774, TitleName((int)code));
            case 3: return CommonKeyF("s_achieve_reward_stat_point", value);
            case 4: return CommonKeyF(r.SubJob == 2 ? "s_achieve_reward_skill_point_sub_1" : "s_achieve_reward_skill_point", value);
            case 5: case 6: case 7: return CommonKeyF("s_achieve_reward_item_buy_auth", TableString(1, code));
        }
        if (FeatureOn(0xbb))
        {
            switch (r.Type)
            {
                case 12: return CommonKeyF("s_achieve_reward_dynamicaction", TableString(4, code));
                case 8:
                    return string.Join("\n", RewardColors(id, grade, code).Select(c => CommonKeyF("s_achieve_reward_coloring", TableString(0x48, c.StringKey))));
                case 10:
                case 11:
                    if (Gender() is { } gender10) return BeautyText(r, gender10);
                    break;
            }
        }
        if (FeatureOn(0xbc) && r.Type == 9)
            return Gender() is { } gender9 ? BeautyText(r, gender9) : "";
        return "";
    }

    /// <summary>
    /// The colours a type 8 reward unlocks (achievemanager.cpp:1114, :654; uiachievedialog.cpp:881): the rows of
    /// CItemDataTable::GetPaletteColors(code) in file order whose achieveID is the trophy and achieveGrade the grade.
    /// </summary>
    public IEnumerable<ColorPaletteTable.Color> RewardColors(int id, int grade, uint code) =>
        PaletteColors((int)code).Where(c => c.AchieveId == id && c.AchieveGrade == grade);

    string BeautyText(AchieveReward r, int gender)
    {
        r.Extra.TryGetValue(gender == 0 ? "male" : "female", out var v);
        return CommonKeyF("s_achieve_reward_beauty", TableString(1, ParseUInt(v ?? "")));
    }

    /// <summary>ParseUInt: strtoul base 10 of the leading digits (0 without).</summary>
    internal static uint ParseUInt(string s)
    {
        s = s.TrimStart();
        ulong n = 0;
        int i = 0;
        if (i < s.Length && s[i] == '+') i++;
        while (i < s.Length && char.IsAsciiDigit(s[i]) && n <= uint.MaxValue) n = n * 10 + (ulong)(s[i++] - '0');
        return n > uint.MaxValue ? uint.MaxValue : (uint)n;
    }

    /// <summary>GetSpecialRewardNotify (@0x14093dc90).</summary>
    public bool SpecialNotify(int id, int grade, out string icon, out string message)
    {
        var s = FindSpecialReward(id, grade);
        icon = s?.NotifyIcon ?? "";
        message = s?.NotifyString ?? "";
        return s != null;
    }

    /// <summary>FUN_14093dd50 / FUN_14093dee0: manualDesc when set, else desc formatted with the two values (numbers
    /// grouped by FUN_1409404f0).</summary>
    public string DescText(int id, object a, object b)
    {
        var d = FindDesc(id);
        if (d == null) return "";
        return d.ManualDesc.Length != 0 ? d.ManualDesc : ClientFormat.Format(d.Text, Arg(a), Arg(b));
    }

    /// <summary>FUN_14093e000 / FUN_14093e190: manualComplete when set, else complete formatted with the two values.</summary>
    public string CompleteText(int id, object a, object b)
    {
        var d = FindDesc(id);
        if (d == null) return "";
        return d.ManualComplete.Length != 0 ? d.ManualComplete : ClientFormat.Format(d.Complete, Arg(a), Arg(b));
    }

    /// <summary>FUN_1409404f0 of a number (the grouped decimal text); a string as given.</summary>
    static object Arg(object v) => v is long n ? Group(n) : v;

    /// <summary>FUN_1409404f0: FUN_14083cb20(FUN_140719a00(n)), the digits grouped.</summary>
    public static string Group(long n) => NumberText.Group(n.ToString(CultureInfo.InvariantCulture));

    /// <summary>GetRecordedDateText (@0x14093e330): the date the trophy recorded for the key, "" when 0.</summary>
    public static string RecordedDate(Achieve a, int key)
    {
        if (a == null) return "";
        long t = a.GetValue(key);
        return t == 0 ? "" : FormatDate(t);
    }

    /// <summary>FormatDuration (@0x14093f4d0): s_achieve_renderer_hour of n/60 and s_achieve_renderer_min of the rest,
    /// joined by a space; the minute string of 0 when both are 0.</summary>
    public string FormatDuration(int n)
    {
        int minutes = n / 60, rest = n % 60;
        string s = "";
        if (minutes != 0) s += CommonF(0x801, minutes);
        if (rest != 0)
        {
            if (s.Length != 0) s += " ";
            s += CommonF(0x802, rest);
        }
        return s.Length == 0 ? CommonF(0x802, 0) : s;
    }

    /// <summary>FormatDistance (@0x14093f670): metres under 1000, else kilometres (a float when not whole).</summary>
    public string FormatDistance(int n)
    {
        if (n < 1000) return CommonF(0x778, n);
        if (n % 1000 != 0) return CommonF(0x77a, (float)n / 1000.0f);
        return CommonF(0x779, n / 1000);
    }

    /// <summary>FormatDate (@0x14093f800): FUN_14083bab0 of CMsTime with "/": yyyy/mm/dd.</summary>
    public static string FormatDate(long t)
    {
        var d = Ms2.Net.MsTime.LocalTm(t);
        return $"{d.Year:D4}/{d.Month:D2}/{d.Day:D2}";
    }
}
