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
    /// <summary>basic alliance (m_4c, Parse_14021a330): 0 none, 1 darkwind .. 5 greenhood, 6 .. 8 the expedition ones, 9 georg.</summary>
    public int Alliance;
    /// <summary>basic rank (m_50, Parse_14021a570): S 0, A 1, B 2, C 3, D 4; 5 when absent or another value.</summary>
    public int Rank = 5;
    /// <summary>The &lt;completeReward&gt; block (m_complete).</summary>
    public readonly CompleteReward Complete = new();
    /// <summary>m_conditions: the &lt;condition&gt; rows that load (CConditionCommonData::Load), in order.</summary>
    public readonly List<QuestCondition> Conditions = new();

    /// <summary>HasProperty (questdata.cpp).</summary>
    public bool HasProperty(int bit) => (Properties & (1 << bit)) != 0;
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
}

/// <summary>
/// QuestDataTable's quest and chapter indexes (LoadQuestData, BuildIndexes, FUN_1407503f0, BuildChapterFieldCodes) and
/// the quest and chapter texts (CQuestUIData FUN_1416e4590, CChapterUIData FUN_1416e7c00), from the client data.
/// </summary>
public sealed partial class QuestTable
{
    /// <summary>A chapter's text (CChapterUIData): its name and the isTutorial flag.</summary>
    public sealed record Chapter(int Id, string Name, bool Tutorial);

    readonly SortedDictionary<uint, QuestData> quests = new();          // m_quests, a std::map: quest id order
    readonly Dictionary<int, List<uint>> chapterQuests = new();         // m_chapter
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
    /// Everything from the client data: every quest file (quest/*.xml), the chapter and quest texts and the string tables
    /// of the first language that has them, and stringcommon.
    /// </summary>
    public static QuestTable Load(PackSet packs, string[] languages, string locale, Func<string, bool> featureOn)
    {
        var t = new QuestTable(locale, featureOn);
        var entries = packs.List("Xml").ToList();
        foreach (var entry in entries.Where(n => n.StartsWith("quest/", StringComparison.OrdinalIgnoreCase)
                                               && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            try { t.AddQuests(Parse(Encoding.UTF8.GetString(packs.Read("Xml", entry)))); } catch (System.Xml.XmlException) { }
        }
        t.BuildIndexes();
        t.Rewards = QuestRewards.Load(packs, locale, t.Available);

        foreach (var (table, file) in ReservedWords.Tables)
            foreach (var lang in languages)
            {
                string entry = $"string/{lang}/{file}.xml";
                if (!packs.Exists("Xml", entry)) continue;
                t.Words.AddTable(table, Parse(Encoding.UTF8.GetString(packs.Read("Xml", entry))), t.Available, fillOnly: true);
            }

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

    /// <summary>IsElementAvailable (commonutil.cpp): the client's locale (or none) and an enabled feature (or none).</summary>
    public bool Available(XElement e)
    {
        var loc = (string)e.Attribute("locale");
        if (!string.IsNullOrEmpty(loc) && !string.Equals(loc, locale, StringComparison.OrdinalIgnoreCase)) return false;
        var feature = (string)e.Attribute("feature");
        return string.IsNullOrEmpty(feature) || featureOn(feature);
    }

    /// <summary>
    /// One quest file: the quests of its environment that passes (FUN_14028f8d0; every quest file has exactly one) and of
    /// the root when it has none. A quest already read keeps its first definition (m_quests.insert).
    /// </summary>
    public void AddQuests(XDocument doc)
    {
        var root = doc.Root;
        if (root == null) return;
        var blocks = root.Elements("environment").Any() ? root.Elements("environment").Where(Available) : new[] { root };
        foreach (var env in blocks)
            foreach (var q in env.Elements("quest"))
            {
                if (!Available(q)) continue;
                var d = Load(q);
                if (d != null) quests.TryAdd((uint)d.Id, d);
            }
    }

    /// <summary>CQuestData::Load: the &lt;basic&gt;, &lt;require&gt; and &lt;start&gt; attributes the log reads.</summary>
    static QuestData Load(XElement q)
    {
        var basic = q.Element("basic");
        if (basic == null) return null;
        var d = new QuestData();
        QueryUInt(basic, "questID", ref d.Id);
        QueryInt(basic, "chapterID", ref d.Chapter);
        QueryInt(basic, "standardLevel", ref d.Level);
        QueryBool(basic, "exceptChapterClear", ref d.ExceptChapterClear);
        QueryInt(basic, "repeatable", ref d.Repeat);
        QueryInt(basic, "questType", ref d.Type);
        QueryBool(basic, "locking", ref d.Locking);
        QueryInt(basic, "tabIndex", ref d.TabIndex);
        QueryBool(basic, "forceRegistGuide", ref d.ForceRegist);
        foreach (var p in FieldMissions.Tokens((string)basic.Attribute("property"), ","))
        {
            int bit = ParseProperty(p.Trim());
            if (bit < 4) d.Properties |= 1 << bit;
        }
        var require = q.Element("require");
        if (require != null)
        {
            d.RequireQuest = Split((string)require.Attribute("quest"));
            d.SelectableQuest = Split((string)require.Attribute("selectableQuest"));
        }
        var start = q.Element("start");
        if (start != null) QueryUInt(start, "npc", ref d.StartNpc);
        var complete = q.Element("complete");
        if (complete != null) QueryUInt(complete, "npc", ref d.CompleteNpc);
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
        }
        return d;
    }

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

    static XDocument Parse(string text) => XDocument.Parse(text.TrimStart('﻿'));
}
