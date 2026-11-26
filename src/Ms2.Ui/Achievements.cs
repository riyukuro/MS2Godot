using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Quest;

namespace Ms2.Ui;

/// <summary>Unk_AchieveReward (achievedata.cpp:672): a grade's reward. The ctor's defaults: type, code and value 0, rank 1,
/// sub job 1.</summary>
public sealed class AchieveReward
{
    /// <summary>ParseRewardType of the type name (1 item .. 13 etc, 0 unknown).</summary>
    public int Type;
    public uint Code, Value;
    public int Rank = 1, SubJob = 1;
    /// <summary>m_mapExtra: Unk_ParseExtra of "extra" ("{key:value,...}").</summary>
    public IReadOnlyDictionary<string, string> Extra = new Dictionary<string, string>();
}

/// <summary>CAchieveGradeData: a grade's value, its condition (type 0 when the grade has none) and its reward.</summary>
public sealed record AchieveGrade(int Grade, QuestCondition Condition, AchieveReward Reward);

/// <summary>
/// CAchieveData (commonfiles/achievedata.cpp): the id's type (id / 10000000) and category ((id / 1000000) % 10), the lowest
/// grade and each grade's condition type (0: no condition, which no CAchieve can start on). The init members are the rest
/// of the data Load (@0x14015c270) reads, which the trophy dialog shows.
/// </summary>
public sealed record AchieveData(int Id, int Type, int Category, int FirstGrade, IReadOnlyDictionary<int, int> GradeConditions)
{
    /// <summary>m_nMaxGrade (ctor 0): the highest grade value.</summary>
    public int MaxGrade { get; init; }
    /// <summary>m_nAccount ("account", XmlGetBool, achievedata.cpp:900), CAchieve::GetData30: an account trophy.</summary>
    public bool Account { get; init; }
    /// <summary>m_nLocking ("locking"), GetData68.</summary>
    public int Locking { get; init; }
    /// <summary>m_nRequireType: ParseRequireType of "uiFilterType" ("event" 1, "eventID" 2, else 0).</summary>
    public int RequireType { get; init; }
    /// <summary>m_aRequire: "uiFilterValue" split on ",".</summary>
    public IReadOnlyList<string> Require { get; init; } = Array.Empty<string>();
    public string CategoryTag { get; init; } = "";
    public string ContentsTag { get; init; } = "";
    /// <summary>m_nSurvival: the contents tag holds "Survival".</summary>
    public bool Survival { get; init; }
    /// <summary>m_strIcon ("icon").</summary>
    public string Icon { get; init; } = "";
    /// <summary>m_nNoticePercent ("noticePercent", ctor 1, achievedata.cpp:729/899): the step of the progress lines
    /// (UIMainPlayerInfo::FUN_1419f0bb0).</summary>
    public int NoticePercent { get; init; } = 1;
    /// <summary>m_mapGrade's full rows (conditions and rewards); empty for a record built from the condition types only.</summary>
    public IReadOnlyDictionary<int, AchieveGrade> Grades { get; init; } = new Dictionary<int, AchieveGrade>();

    /// <summary>GetGrade (@0x14015bc40) is not null.</summary>
    public bool HasGrade(int grade) => GradeConditions.ContainsKey(grade);

    /// <summary>GetGrade (@0x14015bc40): the grade's row, null when absent.</summary>
    public AchieveGrade Grade(int grade) => Grades.TryGetValue(grade, out var g) ? g : null;

    /// <summary>GetName (@0x14015bcb0): the icon's path under the medal icons.</summary>
    public string IconPath => ".\\Data\\Resource\\Image\\Emblem\\Medal\\Icon\\" + Icon;

    /// <summary>ParseRewardType (@0x14015dd20, case-insensitive): item 1 .. etc 13, else 0.</summary>
    public static int ParseRewardType(string s)
    {
        for (int i = 1; i < RewardTypeNames.Length; i++)
            if (string.Equals(s, RewardTypeNames[i], StringComparison.OrdinalIgnoreCase)) return i;
        return 0;
    }

    /// <summary>GetRewardTypeName (@0x14015df70): the name of a reward type, "" for 0 or an unknown one.</summary>
    public static string RewardTypeName(int type) => type >= 1 && type < RewardTypeNames.Length ? RewardTypeNames[type] : "";

    static readonly string[] RewardTypeNames =
    {
        "", "item", "title", "statpoint", "skillpoint", "shop_weapon", "shop_build", "shop_ride", "itemcoloring",
        "beauty_makeup", "beauty_skin", "beauty_hair", "dynamicaction", "etc",
    };

    /// <summary>ParseRequireType (@0x14015dcc0, case-insensitive): "event" 1, "eventID" 2, else 0.</summary>
    static int ParseRequireType(string s) =>
        string.Equals(s, "event", StringComparison.OrdinalIgnoreCase) ? 1 : string.Equals(s, "eventID", StringComparison.OrdinalIgnoreCase) ? 2 : 0;

    /// <summary>LoadAll in load order: see <see cref="LoadAll(PackSet, List{AchieveData})"/>.</summary>
    public static Dictionary<int, AchieveData> LoadAll(PackSet packs) => LoadAll(packs, null);

    /// <summary>
    /// LoadAll (@0x14015c050): Load (@0x14015c270) of each Xml achieve/*.xml file. A file's &lt;achieves&gt; elements are
    /// FUN_14028faa0's (one per id, by locale and feature; so CAchieveData::IsAvailable @0x14015bd60, the same feature and
    /// locale test, holds for every loaded record); one of type 0 is skipped, and a grade condition
    /// CConditionCommonData::Load (conditionutil.cpp:322) rejects (an unknown type name) stops the file there.
    /// <paramref name="order"/> gets DAT_143aab428, the records in load order (AddTail: an id loaded twice is there twice).
    /// STILL-OPEN: Load's Validate (@0x14029edc0) also rejects a type 0x11f condition whose target names a key
    /// CQuestConditionCheckerUserFind has no handler for; that check is not ported, so such a condition is kept.
    /// </summary>
    public static Dictionary<int, AchieveData> LoadAll(PackSet packs, List<AchieveData> order)
    {
        var all = new Dictionary<int, AchieveData>();
        if (packs == null) return all;
        bool rejected = false;
        var files = packs.List("Xml").Where(n => n.StartsWith("achieve/", StringComparison.OrdinalIgnoreCase)
                                                 && n.IndexOf('/', 8) < 0 && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(n => n, StringComparer.Ordinal);
        foreach (var file in files)
        {
            XElement root;
            try { root = GameStrings.Parse(packs.Read("Xml", file)).Root; } catch (System.Xml.XmlException) { continue; }
            if (root == null || root.Name != "ms2") continue;
            rejected = false;
            foreach (var e in FeatureElements.Collect(packs, root, "achieves", "id"))
            {
                if (Load(e) is not { } d) { if (rejected) break; continue; }
                all[d.Id] = d;                                                   // s_mapData.SetAt
                order?.Add(d);                                                   // DAT_143aab428.AddTail
            }
        }
        return all;

        AchieveData Load(XElement e)
        {
            int id = (int)Atoi(e, "id");
            int type = id / 10000000, category = id / 1000000 % 10;
            if (type == 0) return null;
            string contents = (string)e.Attribute("contentsTag") ?? "";
            int first = int.MaxValue, max = 0;
            var conds = new Dictionary<int, int>();
            var grades = new Dictionary<int, AchieveGrade>();
            foreach (var g in e.Elements("grade"))
            {
                int grade = (int)Atoi(g, "value");
                if (grade > max) max = grade;
                if (grade < first) first = grade;
                var cond = new QuestCondition();
                if (g.Element("condition") is { } c)
                {
                    cond = QuestCondition.Load(c);
                    if (cond == null) { rejected = true; return null; }          // Load returned 0: the whole file stops
                }
                var reward = new AchieveReward();
                if (g.Element("reward") is { } r)
                {
                    if (r.Attribute("type") is { } t) reward.Type = ParseRewardType((string)t);   // Unk_ReadRewardType
                    reward.Code = (uint)Atoi(r, "code");
                    reward.Value = (uint)Atoi(r, "value");
                    if (r.Attribute("rank") != null) reward.Rank = (int)Atoi(r, "rank");        // Unk_ReadRank
                    int subJob = (int)Atoi(r, "subJobLevel");
                    if (subJob == 0) reward.SubJob = 1;
                    else if (subJob == 1) reward.SubJob = 2;
                    reward.Extra = ParseExtra((string)r.Attribute("extra") ?? "");
                }
                conds[grade] = cond.Type;                                       // m_mapGrade.SetAt (a later duplicate wins)
                grades[grade] = new AchieveGrade(grade, cond, reward);
            }
            return new AchieveData(id, type, category, first, conds)
            {
                MaxGrade = max,
                Account = Skills.Xml.GetBool(e, "account", false),
                Locking = (int)Atoi(e, "locking"),
                RequireType = e.Attribute("uiFilterType") is { } ft ? ParseRequireType((string)ft) : 0,
                Require = ((string)e.Attribute("uiFilterValue") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries),
                CategoryTag = (string)e.Attribute("categoryTag") ?? "",
                ContentsTag = contents,
                Survival = contents.Contains("Survival", StringComparison.Ordinal),
                Icon = (string)e.Attribute("icon") ?? "",
                NoticePercent = e.Attribute("noticePercent") != null ? (int)Atoi(e, "noticePercent") : 1,
                Grades = grades,
            };
        }
    }

    /// <summary>Unk_ParseExtra (@0x14015f2b0): "{key:value,key:value}" trimmed of "{} ", split on ",", each part with a ":"
    /// after its first character a trimmed key and value (a later key wins).</summary>
    static Dictionary<string, string> ParseExtra(string s)
    {
        var map = new Dictionary<string, string>();
        foreach (var part in s.Trim('{', '}', ' ').Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = part.IndexOf(':');
            if (colon <= 0) continue;
            map[part[..colon].Trim()] = part[(colon + 1)..].Trim();
        }
        return map;
    }

    static long Atoi(XElement e, string attr) => Items.ItemData.Atoi((string)e.Attribute(attr) ?? "0");
}

/// <summary>
/// CAchieve (commonfiles/achievecondition.cpp): one trophy the player has: its data, the grade its condition was set up
/// on (Init), and the progress Decode (@0x140158a90) reads.
/// </summary>
public sealed class Achieve
{
    public AchieveData Data { get; internal set; }
    public int Id => Data.Id;
    public byte State { get; internal set; }
    /// <summary>m_80 (GetValue80); its meaning is STILL-OPEN.</summary>
    public int Value80 { get; internal set; }
    public int CurGrade { get; internal set; }
    public int StartGrade { get; internal set; }
    /// <summary>m_d8 (GetBookmark / SetBookmark).</summary>
    public bool Bookmark { get; set; }
    /// <summary>m_18 (ctor 0, achievecondition.cpp:55): the percentage of the last progress line
    /// (UIMainPlayerInfo::FUN_1419f0bb0 sets it).</summary>
    public float LastNotifiedPercent { get; set; }
    /// <summary>m_cond.m_nCount: the condition's count (IsStarted when not 0; the dialog's progress).</summary>
    public long Count { get; internal set; }
    /// <summary>m_cond's data: the condition of the grade Init set the trophy up on.</summary>
    public QuestCondition Condition { get; internal set; } = new();
    internal AchieveProgress Progress;

    /// <summary>GetValue (@0x140159040): the grade's value (a clear time), 0 when absent.</summary>
    public long GetValue(int grade) => Progress?.GetValue(grade) ?? 0;

    internal void Decode(AchieveProgress p)
    {
        State = p.State; Value80 = p.Value80; CurGrade = p.CurGrade; StartGrade = p.StartGrade; Bookmark = p.Bookmark;
        Count = p.Count; Progress = p;
    }

    public bool IsStarted => Count != 0;
    /// <summary>IsGradeReached (@0x140158730).</summary>
    public bool IsGradeReached => State == 3 ? StartGrade <= CurGrade : StartGrade < CurGrade;
    /// <summary>IsCompleted (@0x1401587c0).</summary>
    public bool IsCompleted => Data.FirstGrade == Data.MaxGrade ? State == 3 : CurGrade > Data.FirstGrade;

    /// <summary>GetReward (@0x140158d90): the start grade's reward, null without that grade.</summary>
    public AchieveReward Reward => Data.Grade(StartGrade)?.Reward;

    /// <summary>GetGradeGoal (@0x140158e30): the grade's condition value, 0 without the grade.</summary>
    public long GradeGoal(int grade) => Data.Grade(grade)?.Condition.Goal ?? 0;

    /// <summary>GetRewardCount (@0x140158f20): the grades from the start grade to the current one (the one before it unless
    /// state 3) whose reward has a type.</summary>
    public int RewardCount
    {
        get
        {
            int last = State == 3 ? CurGrade : CurGrade - 1, n = 0;
            for (int i = StartGrade; i <= last; i++)
                if (Data.Grade(i) is { } g && g.Reward.Type != 0) n++;
            return n;
        }
    }

    /// <summary>
    /// CheckRequire (@0x140159290): false with no require type; with one, false when a require is an enabled game event
    /// (type 1, which falls through to the type 2 test) or a found event id (type 2), else true. The game events
    /// (GameEventManagerCli) are not ported: <paramref name="eventEnabled"/> and <paramref name="eventFound"/> answer for them.
    /// </summary>
    public bool CheckRequire(Func<string, bool> eventEnabled, Func<long, bool> eventFound)
    {
        int type = Data.RequireType;
        if (type == 0) return false;
        foreach (var r in Data.Require)
        {
            switch (type)
            {
                case 1:
                    if (eventEnabled(r)) return false;
                    goto case 2;
                case 2:
                    if (eventFound(Items.ItemData.Atoi(r))) return false;
                    break;
            }
        }
        return true;
    }

    /// <summary>CanRewardRequire (@0x140159350).</summary>
    public bool CanRewardRequire(Func<string, bool> eventEnabled, Func<long, bool> eventFound) =>
        CheckRequire(eventEnabled, eventFound) && CurGrade > Data.FirstGrade;

    /// <summary>CanRewardData68 (@0x1401593c0): a locking trophy past its lowest grade.</summary>
    public bool CanRewardLocking => Data.Locking != 0 && CurGrade > Data.FirstGrade;
}

/// <summary>
/// CAchieveManager (client/ms2/achievemanager.cpp): the trophies the player has (m_map, kept in id order) from the Achieve
/// packet, and the tally of trophy grades reached per category (m_tally, GetTally), whose Sum is the character window's
/// trophy count (UIMyInfoDialog::vfunc_104 @0x141a5ae90).
/// </summary>
public sealed class AchieveBook
{
    readonly Func<int, AchieveData> data;
    readonly Func<IEnumerable<AchieveData>> allData;
    readonly SortedDictionary<int, Achieve> map = new();
    readonly int[] tally = new int[3];

    /// <summary><paramref name="data"/>: GetAchieveData (@0x14015d9e0); <paramref name="allData"/>: GetAll (Reload's
    /// list), also DAT_143aab428 (GetListFromTable's load order).</summary>
    public AchieveBook(Func<int, AchieveData> data, Func<IEnumerable<AchieveData>> allData)
    {
        this.data = data;
        this.allData = allData;
    }

    /// <summary>A book over a loaded data table (its order is the table's).</summary>
    public AchieveBook(IReadOnlyDictionary<int, AchieveData> table)
        : this(id => table.TryGetValue(id, out var d) ? d : null, () => table.Values) { }

    /// <summary>A book over a loaded data table and its load order.</summary>
    public AchieveBook(IReadOnlyDictionary<int, AchieveData> table, IReadOnlyList<AchieveData> order)
        : this(id => table.TryGetValue(id, out var d) ? d : null, () => order) { }

    /// <summary>GetAchieveData (@0x14015d9e0): the data of an id, null when none.</summary>
    public AchieveData DataOf(int id) => data(id);

    /// <summary>The tally's three category slots (category 1, 2, 3).</summary>
    public IReadOnlyList<int> Tally => tally;

    /// <summary>GetTally()->Sum().</summary>
    public uint Sum => (uint)(tally[0] + tally[1] + tally[2]);

    /// <summary>Whether the player has trophy <paramref name="id"/> (Find @0x140935e60).</summary>
    public bool Has(int id) => map.ContainsKey(id);

    /// <summary>
    /// HasCompletedOfType (@0x140936740, achievemanager.cpp:430): a held trophy that is not an account one
    /// (GetData30), of the type when one is given, and IsCompleted.
    /// </summary>
    public bool HasCompletedOfType(int? type)
    {
        foreach (var a in map.Values)
        {
            if (a.Data.Account) continue;
            if (type is int t && a.Data.Type != t) continue;
            if (a.IsCompleted) return true;
        }
        return false;
    }

    /// <summary>Find (@0x140935e60): the trophy, null when the player lacks it.</summary>
    public Achieve Find(int id) => map.TryGetValue(id, out var a) ? a : null;

    /// <summary>DAT_143af0f30: the ids an Achieve mode 2 named (marked whether or not the player has the trophy); the trophy
    /// dialog clears it at load and redraws each of them per frame.</summary>
    public readonly HashSet<int> Pending = new();

    /// <summary>GetList (@0x140935f80): the trophies in id order of a type (0 any) and category (0 any), the started ones
    /// only and the ones whose grade is reached only when asked.</summary>
    public List<Achieve> GetList(int type, int category, bool started, bool reached) =>
        map.Values.Where(a => Passes(a, type, category, started, reached)).ToList();

    /// <summary>GetListFromTable (@0x1409361f0): the same in the data's load order.</summary>
    public List<Achieve> GetListFromTable(int type, int category, bool started, bool reached)
    {
        var list = new List<Achieve>();
        foreach (var d in allData())
            if (Find(d.Id) is { } a && Passes(a, type, category, started, reached)) list.Add(a);
        return list;
    }

    static bool Passes(Achieve a, int type, int category, bool started, bool reached) =>
        (type == 0 || a.Data.Type == type) && (category == 0 || a.Data.Category == category)
        && (!started || a.IsStarted) && (!reached || a.IsGradeReached);

    /// <summary>GetFirstList (@0x140935ee0) = GetList(2, 0, 0, 0).</summary>
    public List<Achieve> GetFirstList() => GetList(2, 0, false, false);

    /// <summary>GetRewardTotal (@0x1409364d0): the reward counts of the type's trophies whose grade is reached.</summary>
    public int GetRewardTotal(int type) => GetList(type, 0, false, true).Sum(a => a.RewardCount);

    /// <summary>IsGradeCleared (@0x140936640).</summary>
    public bool IsGradeCleared(int id, int grade) =>
        Find(id) is { } a && (a.State == 3 ? a.CurGrade >= grade : a.CurGrade > grade);

    /// <summary>
    /// The notices of a mode 2 update (achievemanager.cpp:525-556), in the client's order: <see cref="AchieveNotices.NotifyClear"/>
    /// on a new grade or the last one completed (before RebuildTally), then for a trophy not of type 8
    /// <see cref="AchieveNotices.NotifyReward"/> of the old start grade when the start grade moved (the reward taken) and
    /// <see cref="AchieveNotices.Progress"/> (UIMainPlayerInfo::FUN_1419f0bb0). Null sends none.
    /// </summary>
    public AchieveNotices Notices;

    /// <summary>
    /// OnPacket (@0x140936a50). True when the client refreshes the character window and the game menu's count (mode 2 on a
    /// trophy the player has whose type is not 8: UIMyInfoDialog::FUN_141a5a560, UIGameMenuDialog::FUN_141937ea0). Mode 2
    /// marks the id in <see cref="Pending"/> and sends its <see cref="Notices"/>. STILL-OPEN: NotifyClear's
    /// CMeratMarketMan::PushAdProducts (the merat market's ad products) is not ported.
    /// </summary>
    public bool Apply(AchieveMessage m)
    {
        switch (m)
        {
            case AchieveMessage.Reload:
                Reload();
                return false;
            case AchieveMessage.Rows rows:
                foreach (var (id, grade, progress) in rows.List)
                {
                    if (Init(id, grade, 0) is not { } a) continue;              // decoded and dropped
                    a.Decode(progress);
                    map[id] = a;                                                // m_map.SetAt
                    Add(a);                                                     // Tally: started or not
                }
                return false;
            case AchieveMessage.Update u:
            {
                Pending.Add(u.Id);                                              // DAT_143af0f30.SetAt(nId, 1)
                if (!map.TryGetValue(u.Id, out var a)) return false;
                int oldStart = a.StartGrade, oldGrade = a.CurGrade, oldState = a.State;
                a.Decode(u.Progress);
                if (oldGrade != a.CurGrade || (oldState != 3 && a.State == 3))
                {
                    Notices?.NotifyClear(a);
                    RebuildTally();
                }
                if (a.Data.Type == 8) return false;
                if (oldStart != a.StartGrade) Notices?.NotifyReward(u.Id, oldStart);
                Notices?.Progress(a);                                           // UIMainPlayerInfo::FUN_1419f0bb0
                return true;
            }
        }
        return false;
    }

    /// <summary>Reload (@0x140935cd0): every trophy of the data at its lowest grade, state 2, not started; the tally
    /// cleared.</summary>
    void Reload()
    {
        map.Clear();
        Array.Clear(tally);
        foreach (var d in allData())
            if (Init(d.Id, d.FirstGrade, 2) is { } a) map[d.Id] = a;
    }

    /// <summary>
    /// CAchieve::Init (@0x140158350): the data, the grade (or the lowest grade when the data has none of that value) and
    /// CAchieveCondition::Init (@0x140158130 → CConditionCommon::vfunc_3 @0x14029f090: the grade's condition has a type);
    /// null when one is missing. The current grade is the grade, the lowest when 0; the start grade the lowest.
    /// </summary>
    Achieve Init(int id, int grade, byte state)
    {
        if (id == 0 || data(id) is not { } d) return null;
        if (!d.HasGrade(grade))
        {
            if (!d.HasGrade(d.FirstGrade)) return null;
            grade = d.FirstGrade;
        }
        if (d.GradeConditions[grade] == 0) return null;
        return new Achieve
        {
            Data = d, State = state, CurGrade = grade != 0 ? grade : d.FirstGrade, StartGrade = d.FirstGrade,
            Condition = d.Grade(grade)?.Condition ?? new QuestCondition { Type = d.GradeConditions[grade] },
        };
    }

    /// <summary>Tally (@0x1409388a0): a type 2 trophy adds 1 to its category's slot for each grade from the lowest to the
    /// current one whose value is not 0.</summary>
    void Add(Achieve a)
    {
        if (a.Data.Type != 2) return;
        for (int g = a.Data.FirstGrade; g <= a.CurGrade; g++)
        {
            if (a.GetValue(g) == 0) continue;
            if (a.Data.Category is >= 1 and <= 3) tally[a.Data.Category - 1]++;
        }
    }

    /// <summary>RebuildTally (@0x1409389f0): the tally of the started trophies (IsStarted: a count other than 0).</summary>
    void RebuildTally()
    {
        Array.Clear(tally);
        foreach (var a in map.Values)
            if (a.Count != 0) Add(a);
    }
}
