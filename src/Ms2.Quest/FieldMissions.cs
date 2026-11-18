using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Quest;

/// <summary>
/// A field's exploration goals, the field mission panel of the quest guide (UIQuestGuideDialog::FUN_141aaddb0): the
/// field mission quests (questType 4) of the field, or of its parent when it has one (CFieldMetaTable::GetParentField),
/// and each condition's text. Without the quest packet state every goal is unmet and its count is 0 (FUN_1416e6900 with
/// no CQuest).
/// </summary>
public sealed class FieldMissions
{
    /// <summary>A field mission quest: its id and each condition's type name and goal (m_nGoal, the condition's value).</summary>
    public sealed record Mission(int QuestId, (string Type, long Goal)[] Conditions);

    /// <summary>A quest's description row (FUN_1416e4590): the manual text per condition and the count flag per condition.</summary>
    public sealed record Description(string[] Manual, int[] Counts);

    readonly SortedDictionary<int, Mission> quests = new();
    readonly Dictionary<int, List<int>> byMap = new();          // QuestDataTable m_1f8: the first progressMap id -> quest ids
    readonly Dictionary<int, Description> descriptions = new();
    public ReservedWords Words = new();
    /// <summary>stringcommon s_quest_desc_with_count ("{0} ({1}/{2})"); null leaves the counts off.</summary>
    public string CountFormat;

    readonly string locale;
    readonly Func<string, bool> featureOn;

    public FieldMissions(string locale, Func<string, bool> featureOn)
    {
        this.locale = locale;
        this.featureOn = featureOn ?? (_ => true);
    }

    /// <summary>
    /// Everything from the client data: the quests (quest/*.xml), the description rows and string tables of the first
    /// language that has them (string/&lt;lang&gt;/questdescription*.xml), and the count format (stringcommon).
    /// </summary>
    public static FieldMissions Load(PackSet packs, string[] languages, string locale, Func<string, bool> featureOn)
    {
        var m = new FieldMissions(locale, featureOn);
        var names = packs.List("Xml").ToList();
        foreach (var entry in names.Where(n => n.StartsWith("quest/", StringComparison.OrdinalIgnoreCase)
                                            && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            string text = Encoding.UTF8.GetString(packs.Read("Xml", entry));
            if (!text.Contains("questType=\"4\"", StringComparison.Ordinal)) continue;     // only field missions are indexed
            try { m.AddQuests(Parse(text)); } catch (System.Xml.XmlException) { }
        }
        m.BuildIndex();

        var wanted = new HashSet<int>(m.quests.Keys);
        foreach (var lang in languages)
        {
            var rows = new Dictionary<int, Description>();
            foreach (var entry in names.Where(n => n.StartsWith($"string/{lang}/questdescription", StringComparison.OrdinalIgnoreCase)
                                                && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n, StringComparer.Ordinal))
            {
                XDocument doc;
                try { doc = Parse(Encoding.UTF8.GetString(packs.Read("Xml", entry))); } catch (System.Xml.XmlException) { continue; }
                m.AddDescriptions(doc, rows, wanted);
            }
            foreach (var (id, d) in rows) if (!m.descriptions.ContainsKey(id)) m.descriptions[id] = d;
        }

        foreach (var (table, file) in ReservedWords.Tables)
            foreach (var lang in languages)
            {
                string entry = $"string/{lang}/{file}.xml";
                if (!packs.Exists("Xml", entry)) continue;
                m.Words.AddTable(table, packs.Read("Xml", entry), m.Available, fillOnly: true);
            }

        foreach (var lang in languages)
        {
            string entry = $"string/{lang}/stringcommon.xml";
            if (!packs.Exists("Xml", entry)) continue;
            var key = Parse(Encoding.UTF8.GetString(packs.Read("Xml", entry))).Descendants("key")
                .FirstOrDefault(k => (string)k.Attribute("id") == "s_quest_desc_with_count");
            if (key != null) { m.CountFormat = ((string)key.Attribute("value"))?.Replace("\\n", "\n"); break; }
        }
        return m;
    }

    /// <summary>
    /// IsElementAvailable (commonutil.cpp): a non-empty locale other than the client's skips the element, as does a
    /// feature that is off.
    /// </summary>
    public bool Available(XElement e) => Available((string)e.Attribute("locale"), (string)e.Attribute("feature"));

    /// <summary><see cref="Available(XElement)"/> on the element's locale and feature attributes (null when missing).</summary>
    public bool Available(string loc, string feature)
    {
        if (!string.IsNullOrEmpty(loc) && !string.Equals(loc, locale, StringComparison.OrdinalIgnoreCase)) return false;
        return string.IsNullOrEmpty(feature) || featureOn(feature);
    }

    /// <summary>
    /// One quest file: its &lt;environment&gt; blocks that pass (FUN_14028f8d0: locale and feature) and their &lt;quest&gt;
    /// elements. A quest already read keeps its first definition (m_quests.insert). Conditions whose type the client does
    /// not know would be dropped (CConditionCommonData::Load); every type the field missions use is a known one.
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
                var basic = q.Element("basic");
                int id = Int((string)basic?.Attribute("questID"));
                if (id == 0 || Int((string)basic.Attribute("questType")) != 4 || quests.ContainsKey(id)) continue;
                var conds = q.Elements("condition")
                    .Select(c => ((string)c.Attribute("type") ?? "", Long((string)c.Attribute("value"))))
                    .Where(c => c.Item1.Length > 0).ToArray();
                var fields = Tokens((string)q.Element("progressMap")?.Attribute("progressMap"), ",").Select(Int).ToArray();
                quests[id] = new Mission(id, conds);
                firstField[id] = fields.Length > 0 ? fields[0] : 0;
                hasField[id] = fields.Length > 0;
            }
    }

    readonly Dictionary<int, int> firstField = new();
    readonly Dictionary<int, bool> hasField = new();

    /// <summary>BuildIndexes (questdata.cpp): a questType 4 quest goes under its first progressMap field, in quest id order.</summary>
    public void BuildIndex()
    {
        byMap.Clear();
        foreach (var id in quests.Keys)
        {
            if (!hasField[id]) continue;
            if (!byMap.TryGetValue(firstField[id], out var l)) byMap[firstField[id]] = l = new List<int>();
            l.Add(id);
        }
    }

    /// <summary>
    /// The &lt;quest&gt; rows of one description file (FUN_1416e4590): a row whose feature is off is skipped and the last
    /// row of a quest wins. manual and count are split on "|" (empty parts dropped, as CString::Tokenize), each manual
    /// trimmed on the right, and the object words replaced (FUN_1416e43d0). The KR client's file holds the rows of its own
    /// locale (string/kr/questdescription_final.xml has no other locale's rows), so the split files' rows of another
    /// locale are skipped too.
    /// </summary>
    public void AddDescriptions(XDocument doc, Dictionary<int, Description> into, ISet<int> wanted = null)
    {
        foreach (var q in doc.Root?.Elements("quest") ?? Enumerable.Empty<XElement>())
        {
            if (!Available(q)) continue;
            int id = Int((string)q.Attribute("questID"));
            if (wanted != null && !wanted.Contains(id)) continue;
            var manual = Tokens((string)q.Attribute("manual"), "|").Select(s => s.TrimEnd()).ToArray();
            var counts = Tokens((string)q.Attribute("count"), "|").Select(Int).ToArray();
            into[id] = new Description(manual, counts);
        }
    }

    public void AddDescriptions(XDocument doc) => AddDescriptions(doc, descriptions);

    /// <summary>The quest ids of a field's missions (GetQuestIdsByMap), empty when none.</summary>
    public IReadOnlyList<int> QuestIds(int mapId) => byMap.TryGetValue(mapId, out var l) ? l : Array.Empty<int>();

    /// <summary>
    /// FUN_141aaddb0 (uiquesteventlistener.cpp:1096): the parent field's missions when the field has a parent, every
    /// condition of each quest in order; a condition is met when the character's record of the quest is rewarded or its
    /// progress reaches the goal (none without a record). Empty when the field has none (the panel is not added).
    /// </summary>
    public List<(string Text, bool Satisfied)> Goals(int fieldId, Func<int, int?> parentField = null, IQuestState state = null)
    {
        int mapId = parentField?.Invoke(fieldId) ?? fieldId;
        var goals = new List<(string, bool)>();
        foreach (int id in QuestIds(mapId))
        {
            var r = state?.Get(id);
            for (int j = 0; j < quests[id].Conditions.Length; j++)
            {
                bool met = r != null && (r.IsRewarded || (r.Progress != null && j < r.Progress.Length && r.Progress[j] >= quests[id].Conditions[j].Goal));
                goals.Add((ConditionText(id, j), met));
            }
        }
        return goals;
    }

    /// <summary>
    /// CQuestUIData::GetConditionText: the condition's manual text (object words already replaced) with
    /// FUN_1416e6900's count suffix, "{text} (0/goal)" with the stringcommon format. A manual whose count is 0 has no
    /// suffix. Every field mission has a manual for each condition, so the per-type texts (s_condTexts) are not needed:
    /// a condition without one comes back empty.
    /// </summary>
    public string ConditionText(int questId, int index)
    {
        if (!quests.TryGetValue(questId, out var q) || index >= q.Conditions.Length) return "";
        descriptions.TryGetValue(questId, out var d);
        string manual = d != null && index < d.Manual.Length ? Words.ReplaceObjectWords(d.Manual[index]) : "";
        if (manual.Length == 0) return "";
        int count = d != null && index < d.Counts.Length ? d.Counts[index] : 0;
        if (count == 0 || CountFormat == null) return manual;
        long goal = q.Conditions[index].Goal;
        return CountFormat.Replace("{0}", manual).Replace("{1}", "0").Replace("{2}", goal.ToString(CultureInfo.InvariantCulture));
    }

    // ---- helpers --------------------------------------------------------------------------------------------------

    static XDocument Parse(string text) => XDocument.Parse(text.TrimStart('﻿'));

    /// <summary>CString::Tokenize: the parts between any of the separator characters, empty parts dropped.</summary>
    public static string[] Tokens(string s, string separators) =>
        (s ?? "").Split(separators.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

    /// <summary>atoi: leading spaces, a sign and the digits, 0 when none.</summary>
    public static int Int(string s) => (int)Long(s);

    public static long Long(string s)
    {
        if (s == null) return 0;
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) neg = s[i++] == '-';
        long v = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') v = v * 10 + (s[i++] - '0');
        return neg ? -v : v;
    }
}

/// <summary>
/// CReservedWordTable's object words (ReplaceObjectWords, reservedwordtable.cpp) that quest descriptions use, over the
/// CStringTable tables (Table/string.xml: 1 itemName, 2 npcName, 3 mapName, 19 npcTitle, 61 npcNamePlural). The
/// language is English (GetLanguageCodeW 2), so the plural words read the plural tables.
/// </summary>
public sealed partial class ReservedWords
{
    public const int ItemName = 1, NpcName = 2, MapName = 3, InteractObjectAction = 11, LiftableTargetBox = 12, RegionName = 18, NpcTitle = 19, FieldEnterance = 20,
                     AchieveName = 24, ContinentName = 25, IslandName = 26, NpcNamePlural = 61, ItemNamePlural = 62,
                     PetCollectGroupName = 65;

    public static readonly (int Table, string File)[] Tables =
    {
        (ItemName, "itemname"), (NpcName, "npcname"), (MapName, "mapname"), (NpcTitle, "npctitle"),
        (NpcNamePlural, "npcnameplural"), (RegionName, "regionname"), (FieldEnterance, "stringfieldenterance"),
        (AchieveName, "achievename"), (ContinentName, "continentname"), (IslandName, "islandname"),
        (ItemNamePlural, "itemnameplural"), (PetCollectGroupName, "petcollectgroupname"),
        (InteractObjectAction, "stringinteractobjectaction"), (LiftableTargetBox, "stringliftabletargetbox"),
    };

    /// <summary>GetQuestName for the $quest word (Rw_Quest); empty when unset.</summary>
    public Func<int, string> QuestName;

    readonly Dictionary<int, Dictionary<long, string>> tables = new();

    /// <summary>
    /// One string file (LoadNames): every available key's name by id, "\n" written as backslash-n turned into a line
    /// break; a later key of the same id wins. With <paramref name="fillOnly"/> an id an earlier language had is kept.
    /// </summary>
    public void AddTable(int table, XDocument doc, Func<XElement, bool> available, bool fillOnly = false)
    {
        if (!tables.TryGetValue(table, out var d)) tables[table] = d = new Dictionary<long, string>();
        var file = new Dictionary<long, string>();
        foreach (var k in doc.Root?.Elements("key") ?? Enumerable.Empty<XElement>())
        {
            if (available != null && !available(k)) continue;
            file[(uint)FieldMissions.Long((string)k.Attribute("id"))] = ((string)k.Attribute("name") ?? "").Replace("\\n", "\n");
        }
        foreach (var (id, s) in file) if (!fillOnly || !d.ContainsKey(id)) d[id] = s;
    }

    /// <summary>
    /// <see cref="AddTable(int, XDocument, Func{XElement, bool}, bool)"/> on the file's bytes, read as rows (XmlRows) without
    /// building the document; <paramref name="available"/> is IsElementAvailable on a key's locale and feature.
    /// </summary>
    public void AddTable(int table, byte[] xml, Func<string, string, bool> available, bool fillOnly = false)
    {
        if (!tables.TryGetValue(table, out var d)) tables[table] = d = new Dictionary<long, string>();
        var file = new Dictionary<long, string>();
        foreach (var k in Ms2.Formats.Text.XmlRows.Elements(xml, "key", rootChildren: true))
        {
            if (available != null && !available(k.GetAttribute("locale"), k.GetAttribute("feature"))) continue;
            file[(uint)FieldMissions.Long(k.GetAttribute("id"))] = (k.GetAttribute("name") ?? "").Replace("\\n", "\n");
        }
        foreach (var (id, s) in file) if (!fillOnly || !d.ContainsKey(id)) d[id] = s;
    }

    /// <summary>CStringTable::FUN_141811980: the text, empty when the table or id has none.</summary>
    public string Get(int table, long id) => tables.TryGetValue(table, out var d) && d.TryGetValue((uint)id, out var s) ? s : "";

    /// <summary>
    /// FUN_1416fc320 over the object words: each "$name:arg$" whose name is a known word is replaced and the scan goes on
    /// from the same place; an unknown one is left and skipped.
    /// </summary>
    public string ReplaceObjectWords(string str) => Scan(str, ObjectWord);

    /// <summary>The scan of FUN_1416fc320 shared by the word maps: <paramref name="word"/> gets (token, name, argument, whole text) and returns the replacement, or null for an unknown word.</summary>
    string Scan(string str, Func<string, string, string, string, string> word)
    {
        var s = new StringBuilder(str ?? "");
        int pos = 0;
        while ((pos = IndexOf(s, '$', pos)) != -1)
        {
            int end = IndexOf(s, '$', pos + 1);
            if (end == -1) break;
            string token = s.ToString(pos, end - pos + 1);
            string outText = word(token, TokenName(token), TokenArgument(token), s.ToString());
            if (outText == null) { pos = end + 1; continue; }
            s.Remove(pos, end - pos + 1).Insert(pos, outText);
        }
        return s.ToString();
    }

    /// <summary>m_mapObject: the word's replacement, or null when it is not an object word.</summary>
    string ObjectWord(string token, string name, string arg, string whole) => name switch
            {
                "$item" => Get(ItemName, FieldMissions.Int(arg)),
                "$map" => Get(MapName, FieldMissions.Int(arg)),
                "$npc" => NpcFullName(FieldMissions.Int(arg)),
                "$npcName" => Get(NpcName, FieldMissions.Int(arg)),
                "$npcNamePlural" => Get(NpcNamePlural, FieldMissions.Int(arg)),
                "$itemPlural" => Get(ItemNamePlural, FieldMissions.Int(arg)),
                "$npcPlural" => NpcPluralName(FieldMissions.Int(arg)),
                "$achieve" => FieldMissions.Int(arg) == 0 ? "" : Get(AchieveName, FieldMissions.Int(arg)),
                "$dungeonTitle" => FieldMissions.Int(arg) == 0 ? "" : Get(FieldEnterance, FieldMissions.Int(arg)),
                "$quest" => FieldMissions.Int(arg) == 0 ? "" : QuestName?.Invoke(FieldMissions.Int(arg)) ?? "",
                "$petCollectGroup" => Get(PetCollectGroupName, FieldMissions.Int(arg)),
                "$pp" => Ppr(token, arg, whole),
                _ => null,
            };

    /// <summary>
    /// ReplaceValueWords (FUN_1416fc320 over m_mapValue) for the words that need no player or dialog state: "$pp" and
    /// "$ppr" (Rw_Ppr). The others ($MyPCHp, $MyPCName, $MyPCLevel, $paneltyPrice, $pp2, the roulette words, $male,
    /// $female, $key) are left as they are; no quest or chapter name uses them.
    /// </summary>
    public string ReplaceValueWords(string str)
    {
        var s = new StringBuilder(str ?? "");
        int pos = 0;
        while ((pos = IndexOf(s, '$', pos)) != -1)
        {
            int end = IndexOf(s, '$', pos + 1);
            if (end == -1) break;
            string token = s.ToString(pos, end - pos + 1);
            string name = TokenName(token);
            if (name != "$pp" && name != "$ppr") { pos = end + 1; continue; }
            s.Remove(pos, end - pos + 1).Insert(pos, Ppr(token, TokenArgument(token), s.ToString()));
        }
        return s.ToString();
    }

    /// <summary>FUN_141700820 ($npcPlural in English): "&lt;title&gt; &lt;plural name&gt;", or the plural name alone without a title.</summary>
    public string NpcPluralName(long id)
    {
        string title = Get(NpcTitle, id), name = Get(NpcNamePlural, id);
        return title.Length != 0 ? title + " " + name : name;
    }

    /// <summary>FUN_1401aa6a0: "&lt;title&gt; &lt;name&gt;", or the name alone when the npc has no title.</summary>
    public string NpcFullName(long id)
    {
        string title = Get(NpcTitle, id), name = Get(NpcName, id);
        return title.Length != 0 ? title + " " + name : name;
    }

    /// <summary>GetTokenName: up to the colon, else the token without its closing "$".</summary>
    public static string TokenName(string t)
    {
        int c = t.IndexOf(':');
        return c != -1 ? t[..c] : t[..^1];
    }

    /// <summary>GetTokenArgument: between the colon and the closing "$", empty without a colon.</summary>
    public static string TokenArgument(string t)
    {
        int c = t.IndexOf(':');
        return c != -1 ? t.Substring(c + 1, t.Length - c - 2) : "";
    }

    /// <summary>
    /// Rw_Ppr, the Korean particle: the text is split on "$" and space, and the part before this token's part picks the
    /// first form when its last letter has no final consonant (GetFinalConsonantType != 1), else the second. Without
    /// such a part the token stays, without its "$".
    /// </summary>
    static string Ppr(string token, string arg, string whole)
    {
        string outText = token.Replace("$", "");
        var words = FieldMissions.Tokens(whole, "$ ");
        for (int i = 1; i < words.Length; i++)
        {
            if (words[i] != outText) continue;
            string prev = StripTags(words[i - 1]).Replace("]", "").Replace(")", "");
            if (prev.Length < 1) continue;
            var forms = FieldMissions.Tokens(arg, ", ");
            if (forms.Length != 2) return outText;
            return FinalConsonantType(prev[^1]) != 1 ? forms[0] : forms[1];
        }
        return outText;
    }

    /// <summary>GetFinalConsonantType: 0 no final consonant, 1 a final consonant (Hangul syllables and digits), 2 other.</summary>
    public static int FinalConsonantType(char c)
    {
        if (c >= 0xac00 && c <= 0xd7a3) return (c - 0xac00) % 28 == 0 ? 0 : 1;
        if (c >= '0' && c <= '9') return c is '2' or '4' or '5' or '9' ? 0 : 1;
        return 2;
    }

    /// <summary>StripTags (localizedtexthelpers): each "&lt;" up to the next "&gt;" taken out, until one has no "&gt;".</summary>
    static string StripTags(string s)
    {
        int open;
        while ((open = s.IndexOf('<')) != -1)
        {
            int close = s.IndexOf('>', open + 1);
            if (close == -1) break;
            s = s.Remove(open, close - open + 1);
        }
        return s;
    }

    static int IndexOf(StringBuilder s, char c, int from)
    {
        for (int i = Math.Max(0, from); i < s.Length; i++) if (s[i] == c) return i;
        return -1;
    }
}
