using System.Text;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Quest;

/// <summary>
/// One quest's static data, the parts of CQuestData (CQuestData::Load, questdata.cpp) the quest log reads.
/// </summary>
public sealed class QuestData
{
    public int Id;
    /// <summary>basic questType (FUN_14074b050): 0 epic, 1 world, 2 event, 4 field mission, 6, 8 field, 9 alliance.</summary>
    public int Type;
    /// <summary>basic repeatable (m_20, FUN_14074b070): 1 repeatable, 2 daily, 3 weekly.</summary>
    public int Repeat;
    /// <summary>basic standardLevel (m_level).</summary>
    public int Level;
    /// <summary>basic chapterID (m_28).</summary>
    public int Chapter;
    /// <summary>basic locking (m_30).</summary>
    public bool Locking;
    /// <summary>basic tabIndex (m_48), -1 when absent.</summary>
    public int TabIndex = -1;
    /// <summary>basic forceRegistGuide (m_31).</summary>
    public bool ForceRegist;
    /// <summary>basic exceptChapterClear (m_2f).</summary>
    public bool ExceptChapterClear;
    /// <summary>basic property bits (m_54, ParseProperty): 0 special, 1 hidebeginablelist, 2 availablecompletionticket, 3 HideWorldMap.</summary>
    public int Properties;
    /// <summary>start npc (m_2e8).</summary>
    public int StartNpc;
    /// <summary>m_2ec: &lt;complete npc&gt;, the npc the quest is completed at.</summary>
    public int CompleteNpc;
    /// <summary>require quest / selectableQuest (m_require.m_quest / m_selectableQuest).</summary>
    public uint[] RequireQuest = Array.Empty<uint>(), SelectableQuest = Array.Empty<uint>();
    /// <summary>
    /// m_require.m_followingQuests (FUN_1407503f0): the quests that require this one, then those that may select it, each
    /// added once per list in quest id order.
    /// </summary>
    public readonly List<uint> Following = new();
    internal readonly List<uint> RequiredBy = new(), SelectableBy = new();
    /// <summary>&lt;summonPortal&gt; fieldID / portalID (m_318.m_0 / m_4): a progress talk on such a quest sends Quest mode 0x12.</summary>
    public int SummonPortalField, SummonPortalId;
    /// <summary>basic alliance (m_4c, Parse_14021a330): 0 none, 1 darkwind .. 5 greenhood, 6 .. 8 the expedition ones, 9 georg.</summary>
    public int Alliance;
    /// <summary>basic rank (m_50, Parse_14021a570): S 0, A 1, B 2, C 3, D 4; 5 when absent or another value.</summary>
    public int Rank = 5;
    /// <summary>The &lt;completeReward&gt; block (m_complete).</summary>
    public readonly CompleteReward Complete = new();
    /// <summary>m_conditions: the &lt;condition&gt; rows that load (CConditionCommonData::Load), in order.</summary>
    public readonly List<QuestCondition> Conditions = new();
    /// <summary>basic autoStart (m_2d).</summary>
    public bool AutoStart;
    /// <summary>basic eventTag (m_schedule): the term tag the quest runs in, empty for none.</summary>
    public string Schedule = "";
    /// <summary>
    /// basic usePeriod: a day name (FUN_14028e160: sun 1 .. sat 7) is the weekday the quest resets on (m_resetType); any
    /// other text is its duration in minutes (atoi, m_durationMinutes).
    /// </summary>
    public int ResetType, DurationMinutes;
    /// <summary>progressMap progressMap (m_fields): the fields of the quest, the first one a field mission's field.</summary>
    public uint[] Fields = Array.Empty<uint>();
    /// <summary>guide guideType (m_338.m_value, Parse_140217a00 commonenum.cpp:187): 1 level, 2 level_boost, 3
    /// daily_pick_once, 4 daily_pick_repeat, 5 mission_point, 0 none.</summary>
    public int GuideType;
    /// <summary>eventMission event (m_350, Parse_140217bd0): 1 levelup_package, 2 stamp, 3 timerun, 4 gallery.</summary>
    public int EventMission;
    /// <summary>The &lt;require&gt; block (m_require).</summary>
    public readonly QuestRequire Require = new();
    /// <summary>basic disableGiveup (m_2e, questdata.cpp:1285): no "giveup" button.</summary>
    public bool DisableGiveup;
    /// <summary>basic useReEntryHiddenMap (m_34, questdata.cpp:1298): the "gotoReEntry" button is possible.</summary>
    public bool UseReEntryHiddenMap;
    /// <summary>
    /// &lt;gotoNpc&gt; (m_300, questdata.cpp:1481-1486; read only with feature 0x42 EnableGoToNpc): enable (m_0), gotoField
    /// (m_4), gotoPortal (m_8).
    /// </summary>
    public int GotoNpcEnable, GotoNpcField, GotoNpcPortal;
    /// <summary>
    /// &lt;gotoDungeon&gt; (m_30c, questdata.cpp:1488-1494): state (m_0, a plain int: 1 active and not complete, 2 complete,
    /// 3 active), gotoDungeon (m_roomId, a DungeonRoom id), gotoInstanceID (m_fieldId).
    /// </summary>
    public int GotoDungeonState, GotoDungeonRoom, GotoDungeonField;
    /// <summary>
    /// &lt;remoteAccept&gt; (m_320, questdata.cpp:1495-1500): useRemote (m_0, FUN_1407513c0 @0x1407513c0: cinematic 1, popup 2,
    /// system 3, any other text 0, any case) and requireField (m_4).
    /// </summary>
    public int RemoteAccept, RemoteRequireField;
    /// <summary>
    /// &lt;remoteComplete&gt; (m_32c, questdata.cpp:1501-1507): useRemote (m_0, as RemoteAccept), requireField (m_4),
    /// requireDungeonClear (m_8).
    /// </summary>
    public int RemoteComplete, RemoteCompleteField, RemoteCompleteDungeonClear;

    /// <summary>HasProperty (questdata.cpp).</summary>
    public bool HasProperty(int bit) => (Properties & (1 << bit)) != 0;
}

/// <summary>CQuestData::Require (questdata.h; ctor 0x140747910): the &lt;require&gt; attributes.</summary>
public sealed class QuestRequire
{
    public int Level, MaxLevel, AchievementGrade, UnreqAchievementGrade, GroupId, FameGrade, MasteryType, MasteryGrade, Mentor, Mentee, Vip;
    public uint Achievement, GearScore, Dungeon, UnrequireDungeon, EnterDungeon;
    public int UnreqAchievement;
    /// <summary>m_guildLevel: -1 when absent (no guild rule), 0 not in a guild, else the lowest guild level.</summary>
    public int GuildLevel = -1;
    public int GuildNpcLevel;
    /// <summary>alliance (Parse_14021a330).</summary>
    public int Alliance;
    public int[] Job = Array.Empty<int>(), SubJob = Array.Empty<int>(), UnrequireGroup = Array.Empty<int>(), DayOfWeek = Array.Empty<int>();
    public uint[] Unrequire = Array.Empty<uint>(), ProgressQuest = Array.Empty<uint>(), Equipment = Array.Empty<uint>(),
                  DungeonBeginner = Array.Empty<uint>();
}

/// <summary>CQuestData::CompleteReward (questdata.h; ctor 0x140748100): the &lt;completeReward&gt; attributes.</summary>
public sealed class CompleteReward
{
    public int Money, Karma, Lu, Exp, GuildFund, GuildExp, GuildCoin, MenteeCoin, UseMainFamePoint, RewardExpExtra;
    /// <summary>m_38 / m_40: the global exp and meso rates applied to the quest (FUN_14074ee30), 1 until the server sends them.</summary>
    public float ExpRate = 1, MesoRate = 1;
    /// <summary>relativeExp (m_3c): the exp type (Parse_14021af40), -1 when absent.</summary>
    public int RelativeExp = -1;
    /// <summary>relativeMeso (m_44): the reward contents id (Parse_140217cd0), 0 when absent.</summary>
    public int RelativeMeso;
    /// <summary>m_choiceRewards: the &lt;v&gt; rows that load (QuestRewardItem::LoadXml, questdata.cpp:895).</summary>
    public readonly List<QuestRewardItem> ChoiceRewards = new();
}

/// <summary>
/// CQuestData::QuestRewardItem (questdata.cpp:895): code, rank and count of a &lt;v&gt; row whose feature (if any) is
/// enabled (IsElementFeatureEnabled, commonutil.cpp:1034; no locale test).
/// </summary>
public sealed record QuestRewardItem(int ItemId, int Rank, int Count);

/// <summary>
/// QuestDataTable's quest and chapter indexes (LoadQuestData over questdata/QuestData.xml, BuildIndexes, FUN_1407503f0, BuildChapterFieldCodes) and
/// the quest and chapter texts (CQuestUIData FUN_1416e4590, CChapterUIData FUN_1416e7c00), from the client data.
/// </summary>
public sealed partial class QuestTable
{
    /// <summary>A chapter's text (CChapterUIData): its name and the isTutorial flag.</summary>
    public sealed record Chapter(int Id, string Name, bool Tutorial);

    readonly SortedDictionary<uint, QuestData> quests = new();          // m_quests, a std::map: quest id order
    readonly Dictionary<int, List<uint>> chapterQuests = new();         // m_chapter
    readonly Dictionary<int, List<uint>> groupQuests = new();           // m_390: group id -> its quests in id order
    readonly Dictionary<int, (int Count, int Kind)> groupPicks = new(); // m_3d8: QuestGroupTable.xml
    List<int> chapterOrder = new();                                     // m_chapter's CAtlMap order
    readonly Dictionary<int, Chapter> chapters = new();                 // s_chapters
    readonly Dictionary<uint, QuestDescription> descriptions = new();   // s_quests: the CQuestUIData rows
    public ReservedWords Words = new();
    /// <summary>stringcommon text of a key (GetStringCommonByKey), empty when it has none.</summary>
    public Func<string, string> Common = _ => "";
    /// <summary>The reward tables and readers (questmasktable.cpp); empty tables until loaded.</summary>
    public QuestRewards Rewards = new();

    readonly string locale;
    readonly Func<string, bool> featureOn;

    public QuestTable(string locale, Func<string, bool> featureOn)
    {
        this.locale = locale;
        this.featureOn = featureOn ?? (_ => true);
        Words.QuestName = id => QuestName(id);
    }

    /// <summary>
    /// Everything from the client data: QuestData.xml (the quests), the chapter and quest texts and the string tables
    /// of the first language that has them, and stringcommon.
    /// </summary>
    public static QuestTable Load(PackSet packs, string[] languages, string locale, Func<string, bool> featureOn)
    {
        var t = new QuestTable(locale, featureOn);
        var entries = packs.List("Xml").ToList();
        if (packs.Exists("Xml", QuestDataFile))
            try { t.AddQuests(Parse(Encoding.UTF8.GetString(packs.Read("Xml", QuestDataFile)))); } catch (System.Xml.XmlException) { }
        t.BuildIndexes();
        if (packs.Exists("Xml", "table/questgrouptable.xml"))
            try { t.AddGroups(Parse(Encoding.UTF8.GetString(packs.Read("Xml", "table/questgrouptable.xml")))); } catch (System.Xml.XmlException) { }
        t.Rewards = QuestRewards.Load(packs, locale, t.Available);

        foreach (var (table, file) in ReservedWords.Tables)
            foreach (var lang in languages)
            {
                string entry = $"string/{lang}/{file}.xml";
                if (!packs.Exists("Xml", entry)) continue;
                t.Words.AddTable(table, Parse(Encoding.UTF8.GetString(packs.Read("Xml", entry))), t.Available, fillOnly: true);
            }

        t.Words.LoadBold(packs, t.Available, packs.Exists(FieldMeta.Archive, FieldMeta.Entry) ? FieldMeta.Load(packs).ParentFields : null);

        var common = new Dictionary<string, string>();
        foreach (var lang in languages)
        {
            string entry = $"string/{lang}/stringcommon.xml";
            if (!packs.Exists("Xml", entry)) continue;
            foreach (var k in Parse(Encoding.UTF8.GetString(packs.Read("Xml", entry))).Descendants("key"))
                common.TryAdd((string)k.Attribute("id") ?? "", ((string)k.Attribute("value") ?? "").Replace("\\n", "\n"));
        }
        t.Common = key => common.TryGetValue(key, out var v) ? v : "";

        // the texts of the client's language: the first language that has a file kind fills the ids it holds
        foreach (var lang in languages)
        {
            var files = entries.Where(n => n.StartsWith($"string/{lang}/", StringComparison.OrdinalIgnoreCase)
                                        && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n, StringComparer.Ordinal).ToList();
            var chapterRows = new Dictionary<int, Chapter>();
            var nameRows = new Dictionary<uint, QuestDescription>();
            foreach (var entry in files)
            {
                string file = entry[(entry.LastIndexOf('/') + 1)..].ToLowerInvariant();
                XDocument doc;
                if (file.Contains("chapterdescription", StringComparison.Ordinal))
                {
                    try { doc = Parse(Encoding.UTF8.GetString(packs.Read("Xml", entry))); } catch (System.Xml.XmlException) { continue; }
                    t.AddChapters(doc, chapterRows);
                }
                else if (file.StartsWith("questdescription", StringComparison.Ordinal))
                {
                    try { doc = Parse(Encoding.UTF8.GetString(packs.Read("Xml", entry))); } catch (System.Xml.XmlException) { continue; }
                    t.AddDescriptions(doc, nameRows);
                }
            }
            foreach (var (id, c) in chapterRows) t.chapters.TryAdd(id, c);
            foreach (var (id, n) in nameRows) t.descriptions.TryAdd(id, n);
        }
        return t;
    }

    /// <summary>QuestDataTable::LoadQuestData's file (GetXmlPath("QuestData", "QuestData.xml"), questdata.cpp:1800).</summary>
    const string QuestDataFile = "questdata/questdata.xml";

    /// <summary>IsElementFeatureEnabled (commonutil.cpp:1034): no feature, or an enabled one.</summary>
    bool FeatureEnabled(XElement e) => (string)e.Attribute("feature") is not { Length: > 0 } f || featureOn(f);

    /// <summary>IsElementAvailable (commonutil.cpp): the client's locale (or none) and an enabled feature (or none).</summary>
    public bool Available(XElement e)
    {
        var loc = (string)e.Attribute("locale");
        if (!string.IsNullOrEmpty(loc) && !string.Equals(loc, locale, StringComparison.OrdinalIgnoreCase)) return false;
        var feature = (string)e.Attribute("feature");
        return string.IsNullOrEmpty(feature) || featureOn(feature);
    }

    /// <summary>
    /// QuestDataTable::LoadQuestData (@0x14074c320, questdata.cpp:1797): each &lt;quest&gt; of &lt;ms2&gt; whose feature is on
    /// (IsElementFeatureEnabled; no locale test), loaded by CQuestData::Load. A quest id already read keeps its first row
    /// (m_quests.insert).
    /// </summary>
    public void AddQuests(XDocument doc)
    {
        if (doc.Root is not { Name.LocalName: "ms2" } root) return;
        foreach (var q in root.Elements("quest"))
        {
            if (!FeatureEnabled(q)) continue;
            var d = Load(q, featureOn);
            quests.TryAdd((uint)d.Id, d);
        }
    }

    /// <summary>
    /// CQuestData::Load (@0x1407495d0, questdata.cpp:1276): the &lt;quest&gt;'s id, then each child it reads when present; it
    /// always succeeds.
    /// </summary>
    static QuestData Load(XElement q, Func<string, bool> featureOn)
    {
        var d = new QuestData();
        QueryUInt(q, "id", ref d.Id);
        var basic = q.Element("basic") ?? new XElement("basic");
        QueryInt(basic, "chapterID", ref d.Chapter);
        QueryInt(basic, "standardLevel", ref d.Level);
        QueryBool(basic, "exceptChapterClear", ref d.ExceptChapterClear);
        QueryInt(basic, "repeatable", ref d.Repeat);
        QueryInt(basic, "questType", ref d.Type);
        QueryBool(basic, "locking", ref d.Locking);
        QueryInt(basic, "tabIndex", ref d.TabIndex);
        QueryBool(basic, "forceRegistGuide", ref d.ForceRegist);
        QueryBool(basic, "disableGiveup", ref d.DisableGiveup);
        QueryBool(basic, "useReEntryHiddenMap", ref d.UseReEntryHiddenMap);
        foreach (var p in FieldMissions.Tokens((string)basic.Attribute("property"), ","))
        {
            int bit = ParseProperty(p.Trim());
            if (bit < 4) d.Properties |= 1 << bit;
        }
        QueryBool(basic, "autoStart", ref d.AutoStart);
        d.Schedule = (string)basic.Attribute("eventTag") ?? "";
        string period = (string)basic.Attribute("usePeriod") ?? "";
        int reset = DayNumber(period);
        if (reset != 0) d.ResetType = reset;
        else d.DurationMinutes = FieldMissions.Int(period);
        var require = q.Element("require");
        if (require != null) LoadRequire(require, d);
        if (q.Element("progressMap") is { } progressMap) d.Fields = Split((string)progressMap.Attribute("progressMap"));
        if (q.Element("guide") is { } guide) d.GuideType = ParseGuideType((string)guide.Attribute("guideType"));
        if (q.Element("eventMission") is { } eventMission) d.EventMission = ParseEventMission((string)eventMission.Attribute("event"));
        var start = q.Element("start");
        if (start != null) QueryUInt(start, "npc", ref d.StartNpc);
        var complete = q.Element("complete");
        if (complete != null) QueryUInt(complete, "npc", ref d.CompleteNpc);
        // 0x42 = EnableGoToNpc (featuremanager.cpp:261-262); questdata.cpp:1479-1487
        if (featureOn("EnableGoToNpc") && q.Element("gotoNpc") is { } gotoNpc)
        {
            QueryInt(gotoNpc, "enable", ref d.GotoNpcEnable);
            QueryUInt(gotoNpc, "gotoField", ref d.GotoNpcField);
            QueryInt(gotoNpc, "gotoPortal", ref d.GotoNpcPortal);
        }
        if (q.Element("gotoDungeon") is { } gotoDungeon)
        {
            QueryInt(gotoDungeon, "state", ref d.GotoDungeonState);
            QueryUInt(gotoDungeon, "gotoDungeon", ref d.GotoDungeonRoom);
            QueryUInt(gotoDungeon, "gotoInstanceID", ref d.GotoDungeonField);
        }
        if (q.Element("remoteAccept") is { } remoteAccept)
        {
            d.RemoteAccept = ParseRemote((string)remoteAccept.Attribute("useRemote"), d.RemoteAccept);
            QueryUInt(remoteAccept, "requireField", ref d.RemoteRequireField);
        }
        if (q.Element("remoteComplete") is { } remoteComplete)
        {
            d.RemoteComplete = ParseRemote((string)remoteComplete.Attribute("useRemote"), d.RemoteComplete);
            QueryUInt(remoteComplete, "requireField", ref d.RemoteCompleteField);
            QueryInt(remoteComplete, "requireDungeonClear", ref d.RemoteCompleteDungeonClear);
        }
        if (q.Element("summonPortal") is { } summonPortal)
        {
            QueryInt(summonPortal, "fieldID", ref d.SummonPortalField);
            QueryInt(summonPortal, "portalID", ref d.SummonPortalId);
        }
        foreach (var c in q.Elements("condition"))
            if (QuestCondition.Load(c) is { } cond) d.Conditions.Add(cond);
        if ((string)basic.Attribute("alliance") is { } alliance) d.Alliance = QuestRewards.ParseAlliance(alliance);
        if ((string)basic.Attribute("rank") is { } rank) d.Rank = QuestRewards.ParseRank(rank);
        var reward = q.Element("completeReward");
        if (reward != null)
        {
            var c = d.Complete;
            QueryInt(reward, "money", ref c.Money);
            QueryInt(reward, "exp", ref c.Exp);
            QueryInt(reward, "guildFund", ref c.GuildFund);
            QueryInt(reward, "guildExp", ref c.GuildExp);
            QueryInt(reward, "guildCoin", ref c.GuildCoin);
            QueryInt(reward, "menteeCoin", ref c.MenteeCoin);
            QueryInt(reward, "useMainFamePoint", ref c.UseMainFamePoint);
            QueryInt(reward, "rewardExpExtra", ref c.RewardExpExtra);
            if ((string)reward.Attribute("relativeExp") is { } relExp) c.RelativeExp = QuestRewards.ParseExpType(relExp);
            if ((string)reward.Attribute("relativeMeso") is { } relMeso) c.RelativeMeso = QuestRewards.ParseRewardContents(relMeso);
            QueryInt(reward, "karma", ref c.Karma);
            QueryInt(reward, "lu", ref c.Lu);
            // the <v> rows QuestRewardItem::LoadXml (:895) takes: one whose feature is on, code, rank and count
            foreach (var v in reward.Elements("v"))
            {
                if ((string)v.Attribute("feature") is { Length: > 0 } f && !featureOn(f)) continue;
                int code = 0, itemRank = 0, count = 0;
                QueryInt(v, "code", ref code);
                QueryInt(v, "rank", ref itemRank);
                QueryInt(v, "count", ref count);
                c.ChoiceRewards.Add(new QuestRewardItem(code, itemRank, count));
            }
        }
        return d;
    }

    /// <summary>The &lt;require&gt; attributes (CQuestData::Load); groupID is kept only for a quest that is not locked.</summary>
    static void LoadRequire(XElement e, QuestData d)
    {
        var r = d.Require;
        QueryUShort(e, "level", ref r.Level);
        QueryUShort(e, "maxLevel", ref r.MaxLevel);
        QueryUInt(e, "achievement", ref r.Achievement);
        QueryInt(e, "achievementGrade", ref r.AchievementGrade);
        QueryUInt(e, "gearScore", ref r.GearScore);
        QueryUShort(e, "guildNpcLevel", ref r.GuildNpcLevel);
        QueryUInt(e, "dungeon", ref r.Dungeon);
        QueryUInt(e, "unrequireDungeon", ref r.UnrequireDungeon);
        QueryUInt(e, "enterDungeon", ref r.EnterDungeon);
        if ((string)e.Attribute("alliance") is { } alliance) r.Alliance = QuestRewards.ParseAlliance(alliance);
        QueryInt(e, "fameGrade", ref r.FameGrade);
        QueryInt(e, "masteryType", ref r.MasteryType);
        QueryInt(e, "masteryGrade", ref r.MasteryGrade);
        r.DungeonBeginner = Split((string)e.Attribute("dungeonBeginner"));
        QueryInt(e, "mentor", ref r.Mentor);
        QueryInt(e, "mentee", ref r.Mentee);
        QueryInt(e, "vip", ref r.Vip);
        r.Equipment = Split((string)e.Attribute("equipment"));
        r.SubJob = SplitInt((string)e.Attribute("subJob"));
        r.ProgressQuest = Split((string)e.Attribute("progressQuest"));
        string guildLevel = (string)e.Attribute("guildLevel") ?? "";
        if (guildLevel.Length > 0) r.GuildLevel = FieldMissions.Int(guildLevel);
        var unreq = ((string)e.Attribute("unreqAchievement") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (unreq.Length == 2)
        {
            r.UnreqAchievement = FieldMissions.Int(unreq[0]);
            r.UnreqAchievementGrade = FieldMissions.Int(unreq[1]);
        }
        d.RequireQuest = Split((string)e.Attribute("quest"));
        d.SelectableQuest = Split((string)e.Attribute("selectableQuest"));
        r.Job = SplitInt((string)e.Attribute("job"));
        r.Unrequire = Split((string)e.Attribute("unrequire"));
        r.UnrequireGroup = SplitInt((string)e.Attribute("unrequireGroup"));
        r.DayOfWeek = SplitInt((string)e.Attribute("dayOfWeek"));
        int group = 0;
        QueryInt(e, "groupID", ref group);
        if (group != 0 && !d.Locking) r.GroupId = group;
    }

    /// <summary>FUN_1407513c0 (questdata.cpp:2772): cinematic 1, popup 2, system 3, other 0; an absent attribute keeps the value.</summary>
    static int ParseRemote(string s, int keep) => s == null ? keep : s.ToLowerInvariant() switch
    {
        "cinematic" => 1, "popup" => 2, "system" => 3, _ => 0,
    };

    /// <summary>FUN_14028e160: the day number of a three letter day name (sun 1 .. sat 7, any case), 0 for anything else.</summary>
    public static int DayNumber(string s) => s.ToLowerInvariant() switch
    {
        "sun" => 1, "mon" => 2, "tue" => 3, "wed" => 4, "thu" => 5, "fri" => 6, "sat" => 7, _ => 0,
    };

    /// <summary>Parse_140217a00 (commonenum.cpp:187), case-insensitive: the guide type.</summary>
    static int ParseGuideType(string s) => (s ?? "").ToLowerInvariant() switch
    {
        "level" => 1, "level_boost" => 2, "mission_point" => 5, "daily_pick_once" => 3, "daily_pick_repeat" => 4, _ => 0,
    };

    /// <summary>Parse_140217bd0 (commonenum.cpp): the event mission kind.</summary>
    static int ParseEventMission(string s) => (s ?? "").ToLowerInvariant() switch
    {
        "levelup_package" => 1, "stamp" => 2, "timerun" => 3, "gallery" => 4, _ => 0,
    };

    /// <summary>
    /// QuestDataTable::LoadQuestGroupTable (@0x140750120): each available &lt;group&gt; of &lt;ms2&gt;: id, pickCount (1 when
    /// absent) and dependency (ParseScope: global 0, character 1, account 2). A later row of an id replaces it.
    /// </summary>
    public void AddGroups(XDocument doc)
    {
        if (doc.Root is not { Name.LocalName: "ms2" } root) return;
        foreach (var g in root.Elements("group"))
        {
            if (!Available(g)) continue;
            int id = 0, count = 1;
            QueryInt(g, "id", ref id);
            QueryInt(g, "pickCount", ref count);
            int kind = (string)g.Attribute("dependency") is { } dep ? ParseScope(dep) : 0;
            groupPicks[id] = (count, kind);
        }
    }

    /// <summary>ParseScope (@0x140751440).</summary>
    static int ParseScope(string s) => s.ToLowerInvariant() switch { "character" => 1, "account" => 2, _ => 0 };

    /// <summary>GetGroupPickKind (@0x14074e390): 0 when the group has no row.</summary>
    public int GroupPickKind(int groupId) => groupPicks.TryGetValue(groupId, out var g) ? g.Kind : 0;

    /// <summary>GetGroupPickCount (@0x14074e450): 1 when the group has no row.</summary>
    public int GroupPickCount(int groupId) => groupPicks.TryGetValue(groupId, out var g) ? g.Count : 1;

    /// <summary>CollectType3QuestsByGuideType (questdata.cpp:1899): the type 3 quests in quest id order, with the guide
    /// type when it is not 0.</summary>
    public List<QuestData> CollectType3QuestsByGuideType(int guideType) =>
        quests.Values.Where(q => q.Type == 3 && (guideType == 0 || q.GuideType == guideType)).ToList();

    /// <summary>m_390: the quests of a group in quest id order, null when none.</summary>
    public IReadOnlyList<uint> GroupQuests(int groupId) => groupQuests.TryGetValue(groupId, out var l) ? l : null;

    /// <summary>ParseProperty (questdata.cpp): the bit of a property name, 4 for an unknown one.</summary>
    static int ParseProperty(string s) => s.ToLowerInvariant() switch
    {
        "special" => 0,
        "hidebeginablelist" => 1,
        "availablecompletionticket" => 2,
        "hideworldmap" => 3,
        _ => 4,
    };

    /// <summary>
    /// BuildIndexes then FUN_1407503f0: each quest, in id order, under its chapter (m_chapter, a CAtlMap); each quest's
    /// require and selectable quests get it as a following quest; then each chapter of two or more quests whose quests
    /// are all non-repeat, non-event quests is reordered along its chain from its head quest, when the chain reaches all
    /// of them.
    /// </summary>
    public void BuildIndexes()
    {
        chapterQuests.Clear();
        var insertOrder = new List<int>();
        foreach (var (id, d) in quests)
        {
            if (!chapterQuests.TryGetValue(d.Chapter, out var l)) { chapterQuests[d.Chapter] = l = new List<uint>(); insertOrder.Add(d.Chapter); }
            l.Add(id);
        }
        chapterOrder = FieldMeta.AtlMapOrder(insertOrder);
        groupQuests.Clear();
        foreach (var (id, d) in quests)
            if (d.Require.GroupId != 0)
            {
                if (!groupQuests.TryGetValue(d.Require.GroupId, out var g)) groupQuests[d.Require.GroupId] = g = new List<uint>();
                g.Add(id);
            }

        foreach (var (id, d) in quests)
        {
            foreach (uint r in d.RequireQuest)
                if (quests.TryGetValue(r, out var other) && !other.RequiredBy.Contains(id)) { other.RequiredBy.Add(id); other.Following.Add(id); }
            foreach (uint r in d.SelectableQuest)
                if (quests.TryGetValue(r, out var other) && !other.SelectableBy.Contains(id)) { other.SelectableBy.Add(id); other.Following.Add(id); }
        }

        foreach (int chain in chapterOrder)
        {
            var ids = chapterQuests[chain];
            if (ids.Count < 2) continue;
            QuestData head = null;
            bool invalid = false;
            foreach (uint id in ids)
            {
                if (!quests.TryGetValue(id, out var q)) continue;
                if (q.Repeat != 0 || q.Type == 2 || q.Type == 3) { invalid = true; break; }
                var prev = q.RequireQuest.Concat(q.SelectableQuest).ToArray();
                if (prev.Length == 0) { head = q; break; }
                // the first previous quest that exists decides: one of this chapter means q is not the head
                foreach (uint p in prev)
                {
                    if (!quests.TryGetValue(p, out var pq)) continue;
                    if (pq.Chapter == chain) break;
                    head = q;
                    break;
                }
                if (head != null) break;
            }
            if (invalid || head == null) continue;
            var ordered = new List<uint>();
            CollectChain(chain, ordered, head, 0);
            if (ordered.Count != ids.Count) continue;
            chapterQuests[chain] = ordered;
        }
    }

    /// <summary>CollectChainQuestIds: depth first along the following quests that stay in the chapter (the depth counts every visit).</summary>
    void CollectChain(int chain, List<uint> into, QuestData q, int depth)
    {
        if (depth > 1000 || q == null || q.Chapter != chain) return;
        if (into.Contains((uint)q.Id)) return;
        into.Add((uint)q.Id);
        foreach (uint f in q.Following)
            CollectChain(chain, into, Quest(f), ++depth);
    }

    /// <summary>
    /// FUN_1416e7c00: one chapter text file. Each available chapter's name with the object words replaced, and isTutorial
    /// read as an int (TinyXML QueryIntAttribute: "1" is a tutorial, a value that is not a number such as "OK" leaves it 0).
    /// A later row of the same id wins.
    /// </summary>
    public void AddChapters(XDocument doc, Dictionary<int, Chapter> into)
    {
        foreach (var c in doc.Root?.Elements("chapter") ?? Enumerable.Empty<XElement>())
        {
            if (!Available(c)) continue;
            int id = 0, tutorial = 0;
            QueryInt(c, "id", ref id);
            QueryInt(c, "isTutorial", ref tutorial);
            into[id] = new Chapter(id, Words.ReplaceObjectWords((string)c.Attribute("name") ?? ""), tutorial != 0);
        }
    }

    public void AddChapters(XDocument doc) => AddChapters(doc, chapters);

    /// <summary>
    /// FUN_1416e4590: each quest's description row (the last available row of a quest wins): name and summary (both the
    /// name attribute), desc, complete and descFameUI with backslash-n turned into line breaks, manual split on "|" (each
    /// part trimmed on the right) and count parsed on "|". FUN_1416e43d0's object words are replaced when read. The rows
    /// of another locale are skipped, as the field mission texts are.
    /// </summary>
    public void AddDescriptions(XDocument doc, Dictionary<uint, QuestDescription> into)
    {
        var rows = new Dictionary<uint, QuestDescription>();
        foreach (var q in doc.Root?.Elements("quest") ?? Enumerable.Empty<XElement>())
        {
            if (!Available(q)) continue;
            rows[(uint)FieldMissions.Int((string)q.Attribute("questID"))] = QuestDescription.Read(q);
        }
        foreach (var (id, n) in rows) into[id] = n;
    }

    public void AddDescriptions(XDocument doc) => AddDescriptions(doc, descriptions);

    // ---- QuestDataTable / CQuestUIData lookups -------------------------------------------------------------------

    /// <summary>GetQuestData, null when the quest is not in the table.</summary>
    public QuestData Quest(uint id) => quests.TryGetValue(id, out var q) ? q : null;
    public QuestData Quest(int id) => Quest((uint)id);

    /// <summary>Every quest of the table, in quest id order.</summary>
    public IEnumerable<QuestData> Quests => quests.Values;

    /// <summary>GetChapterIds: the chapter ids in m_chapter's order.</summary>
    public IReadOnlyList<int> ChapterIds => chapterOrder;

    /// <summary>GetQuestIdsByChapter: the chapter's quest ids, chain order when it was reordered, else id order.</summary>
    public IReadOnlyList<uint> QuestIdsByChapter(int chapter) =>
        chapterQuests.TryGetValue(chapter, out var l) ? l : Array.Empty<uint>();

    /// <summary>FUN_1416e7f30: the chapter's text, null when it has none.</summary>
    public Chapter ChapterText(int chapter) => chapters.TryGetValue(chapter, out var c) ? c : null;

    /// <summary>FUN_1416e8060: the chapter is a tutorial chapter.</summary>
    public bool IsTutorial(int chapter) => ChapterText(chapter)?.Tutorial ?? false;

    /// <summary>
    /// GetQuestName (questuidata.cpp): the quest's name, the object words replaced, after the repeat kind's word for a
    /// repeatable (s_quest_repeat_infinite, common string 1504), daily (s_quest_repeat_daily, 1505) or weekly
    /// (s_quest_repeat_week) quest, then the value words replaced. Empty when the quest has no text or no data.
    /// </summary>
    public string QuestName(int id)
    {
        if (!descriptions.TryGetValue((uint)id, out var row)) return "";
        var d = Quest(id);
        if (d == null) return "";
        string name = Words.ReplaceObjectWords(row.Name);
        name = d.Repeat switch
        {
            1 => Common("s_quest_repeat_infinite") + " " + name,
            2 => Common("s_quest_repeat_daily") + " " + name,
            3 => Common("s_quest_repeat_week") + " " + name,
            _ => name,
        };
        return Words.ReplaceValueWords(name);
    }

    /// <summary>FUN_14074b790: a field quest (type 8) or one with forceRegistGuide is always registered.</summary>
    public bool ForceRegist(int id)
    {
        var d = Quest(id);
        return d != null && (d.Type == 8 || d.ForceRegist);
    }

    // ---- chapter field codes (BuildChapterFieldCodes) -------------------------------------------------------------

    /// <summary>
    /// BuildChapterFieldCodes: each chapter's region and continent code, those of the field its first quest's start npc
    /// stands in (CFieldMetaTable::FindNpcField: the first of the npc's fields whose time window, if it has one, holds the
    /// server time, FUN_1406fbb50) per table/fielddata.xml (CFieldData continentCode / regionCode). The client builds them
    /// when the table loads; here they are built on the first call, the time window checked by the caller (it needs the
    /// server time). FieldMetaData's npcs without npc data are skipped by the client (FindNpcData); every quest start npc
    /// has npc data.
    /// </summary>
    public void BuildChapterFieldCodes(FieldMeta meta, IReadOnlyDictionary<int, (int Continent, int Region)> fields,
                                       Func<int, int, bool> npcFieldOpen)
    {
        regionCodes.Clear();
        continentCodes.Clear();
        foreach (int chapter in chapterOrder)
        {
            var ids = chapterQuests[chapter];
            if (ids.Count == 0) continue;
            var d = Quest(ids[0]);
            if (d == null) continue;
            if (!meta.NpcFields.TryGetValue(d.StartNpc, out var npcFields)) continue;
            int? field = null;
            foreach (int f in npcFields)
                if (!meta.TimedNpcFields.Contains((d.StartNpc, f)) || npcFieldOpen(d.StartNpc, f)) { field = f; break; }
            if (field == null || !fields.TryGetValue(field.Value, out var codes)) continue;
            regionCodes[chapter] = codes.Region;
            continentCodes[chapter] = codes.Continent;
        }
        FieldCodesBuilt = true;
    }

    readonly Dictionary<int, int> regionCodes = new(), continentCodes = new();
    public bool FieldCodesBuilt { get; private set; }

    /// <summary>GetChapterRegionCode, 0 when the chapter has none.</summary>
    public int ChapterRegionCode(int chapter) => regionCodes.TryGetValue(chapter, out var v) ? v : 0;
    /// <summary>GetChapterContinentCode, 0 when the chapter has none.</summary>
    public int ChapterContinentCode(int chapter) => continentCodes.TryGetValue(chapter, out var v) ? v : 0;

    /// <summary>
    /// CFieldData::LoadFieldData over table/fielddata.xml: each fieldData's continentCode and regionCode from its
    /// environment that passes (FUN_14028f8d0). In the client data every field whose environments pass gives the same
    /// codes from each, so the first that passes is taken; a field with none is not loaded.
    /// </summary>
    public static Dictionary<int, (int Continent, int Region)> FieldCodes(XDocument doc, Func<XElement, bool> available)
    {
        var codes = new Dictionary<int, (int, int)>();
        foreach (var fd in doc.Root?.Elements("fieldData") ?? Enumerable.Empty<XElement>())
        {
            int id = 0;
            QueryUInt(fd, "id", ref id);
            var env = fd.Elements("environment").FirstOrDefault(available);
            if (env == null) continue;
            int continent = 0, region = 0;
            var p = env.Element("property");
            if (p != null) { QueryInt(p, "continentCode", ref continent); QueryInt(p, "regionCode", ref region); }
            codes[id] = (continent, region);
        }
        return codes;
    }

    // ---- TinyXML attribute queries ---------------------------------------------------------------------------------

    /// <summary>QueryIntAttribute: sscanf "%d"; a value that does not start with a number leaves the field as it was.</summary>
    public static void QueryInt(XElement e, string name, ref int v)
    {
        var s = (string)e.Attribute(name);
        if (s != null && ScanInt(s, out long n)) v = (int)n;
    }

    /// <summary>QueryUnsignedAttribute: sscanf "%u".</summary>
    public static void QueryUInt(XElement e, string name, ref int v)
    {
        var s = (string)e.Attribute(name);
        if (s != null && ScanInt(s, out long n)) v = (int)(uint)n;
    }

    /// <summary>QueryUnsignedAttribute into an unsigned field.</summary>
    public static void QueryUInt(XElement e, string name, ref uint v)
    {
        var s = (string)e.Attribute(name);
        if (s != null && ScanInt(s, out long n)) v = (uint)n;
    }

    /// <summary>QueryBoolAttribute (MS2's TinyXML 2.6): true / yes / 1 and false / no / 0, any case; anything else leaves it.</summary>
    public static void QueryBool(XElement e, string name, ref bool v)
    {
        var s = (string)e.Attribute(name);
        if (string.IsNullOrEmpty(s)) return;
        switch (s.ToLowerInvariant())
        {
            case "true": case "yes": case "1": v = true; break;
            case "false": case "no": case "0": v = false; break;
        }
    }

    static bool ScanInt(string s, out long v)
    {
        v = 0;
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) neg = s[i++] == '-';
        int start = i;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') v = v * 10 + (s[i++] - '0');
        if (i == start) return false;
        if (neg) v = -v;
        return true;
    }

    /// <summary>XmlSplit over ",": each part's unsigned value.</summary>
    static uint[] Split(string s) =>
        FieldMissions.Tokens(s, ",").Select(p => (uint)FieldMissions.Long(p)).ToArray();

    /// <summary>XmlSplit over "," into an int array: each part's atoi.</summary>
    static int[] SplitInt(string s) => FieldMissions.Tokens(s, ",").Select(FieldMissions.Int).ToArray();

    /// <summary>QueryUnsignedAttribute into an unsigned short (XmlGetAttr's unsigned short form keeps the low 16 bits).</summary>
    static void QueryUShort(XElement e, string name, ref int v)
    {
        int n = v;
        QueryUInt(e, name, ref n);
        v = (ushort)n;
    }

    static XDocument Parse(string text) => XDocument.Parse(text.TrimStart('﻿'));
}
