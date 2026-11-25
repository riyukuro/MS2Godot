using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Ui;

namespace Ms2.Formats.World;

/// <summary>
/// Npc talk scripts (Xml.m2d : script/npc/npcscript*.xml, the client's LoadNpcTalks). Per &lt;npc id&gt;: the first &lt;select&gt;
/// (the greeting the server opens a plain talk with, talk flag 8), &lt;script id&gt; pages chosen by the server, &lt;job&gt; and
/// &lt;monologue&gt; balloons. Content text is a "$script:&lt;id&gt;$" reference into the script strings
/// (string/&lt;lang&gt;/scriptnpc*.xml; the ids are the same number with or without a leading zero).
/// </summary>
public sealed class NpcScriptTable
{
    /// <summary>
    /// A &lt;content&gt; (CCinematicContent::vfunc_2, scriptcontent.cpp:250, after CScriptContent's text / voiceID / illust :62).
    /// </summary>
    public sealed class Content
    {
        public string Text, VoiceId, Illust;
        /// <summary>The &lt;distractor&gt; choices: the text and the scripts its goto lists ("51", or "61,62,...").</summary>
        public List<(string Text, int[] Goto)> Distractors = new();
        /// <summary>m_buttonSet ("buttonSet", :253): the page's own button set, which GetTalkButtonSet takes first.</summary>
        public int ButtonSet;
        /// <summary>m_openTalkReward ("openTalkReward", :255): the page shows the talk rewards (FillTalkReward).</summary>
        public bool OpenTalkReward;
        public int FunctionId, OtherNpcTalk, SpeakerNpc, Redirect;
        public string LeftIllust, RightIllust, SpeakerIllust, ImagePlate, Movie;
        /// <summary>
        /// m_screenEffectMovie / m_screenEffectAction / m_screenEffectValue ("screenEffectMovie", "screenEffectAction",
        /// "screenEffectValue", scriptcontent.cpp:268-270): a screen effect the page plays, such as "mask_black" "fadeout" 2000.
        /// CCinematicComponent::vfunc_5 (cinematiccomponent.cpp:323) hands it to the page as "screenEffect" when the movie is set.
        /// </summary>
        public string ScreenEffectMovie, ScreenEffectAction;
        public int ScreenEffectValue;
        /// <summary>m_questId: the quest of a quest script's page (FUN_140859160, talkscript.cpp:595), 0 for npc scripts.</summary>
        public int QuestId;

        public static Content Parse(XElement c, int questId = 0)
        {
            var ct = new Content
            {
                Text = (string)c.Attribute("text"), VoiceId = (string)c.Attribute("voiceID"), Illust = (string)c.Attribute("illust"),
                ButtonSet = Int(c, "buttonSet"), OpenTalkReward = Bool(c, "openTalkReward"), FunctionId = Int(c, "functionID"),
                OtherNpcTalk = Int(c, "otherNpcTalk"), LeftIllust = (string)c.Attribute("leftIllust"),
                RightIllust = (string)c.Attribute("rightIllust"), SpeakerIllust = (string)c.Attribute("speakerIllust"),
                SpeakerNpc = Int(c, "speakerNpc"), ImagePlate = (string)c.Attribute("imagePlate"), Movie = (string)c.Attribute("movie"),
                Redirect = Int(c, "redirect"), QuestId = questId,
                ScreenEffectMovie = (string)c.Attribute("screenEffectMovie"), ScreenEffectAction = (string)c.Attribute("screenEffectAction"),
                ScreenEffectValue = Int(c, "screenEffectValue"),
            };
            foreach (var d in c.Elements("distractor")) ct.Distractors.Add(((string)d.Attribute("text"), Ints(d, "goto")));
            return ct;
        }
    }

    public sealed class Script
    {
        public string Kind;          // select, script, job, monologue
        public int Id;
        public bool RandomPick;
        /// <summary>m_questId of a quest script (LoadQuestTalks, talkscript.cpp:660), 0 for npc scripts.</summary>
        public int QuestId;
        /// <summary>
        /// m_popupState / m_popupProp of a &lt;monologue&gt; (FUN_140859320, talkscript.cpp:603; the CTalkScript constructor's
        /// 9 and 100, :187): the balloon kind it is picked for (9 a plain npc, 7 / 8 an affinity npc's) and its weight.
        /// </summary>
        public int PopupState = 9, PopupProp = 100;
        public List<Content> Contents = new();
    }

    public sealed class Npc
    {
        public int Id;
        public List<Script> Scripts = new();
        public Script Select => Scripts.FirstOrDefault(s => s.Kind == "select");
        public Script ById(int id) => Scripts.FirstOrDefault(s => s.Kind == "script" && s.Id == id);
    }

    readonly Dictionary<int, Npc> npcs = new();
    readonly Dictionary<int, List<Script>> monologues = new();     // s_npcAlt
    readonly Dictionary<long, string> strings = new();
    public int Count => npcs.Count;
    public IEnumerable<Npc> AllNpcs() => npcs.Values;
    public int StringCount => strings.Count;
    readonly GameStrings gameText;
    ReservedWords words;

    /// <param name="available">
    /// IsElementAvailable (the locale and feature test, Ms2.Ui FeatureElements.IsAvailable), which LoadNpcTalks
    /// (talkscript.cpp:704) runs on each &lt;monologue&gt;; null keeps them all.
    /// </param>
    public NpcScriptTable(PackSet packs, GameStrings text, Func<XElement, bool> available = null)
    {
        gameText = text;
        var all = packs.List("Xml").Where(n => n.StartsWith("script/npc/", StringComparison.OrdinalIgnoreCase)
                                               && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n).ToList();
        var tables = all.Where(n => n.StartsWith("script/npc/npcscript", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var entry in tables)
        {
            XDocument x;
            try { x = GameStrings.Parse(packs.Read("Xml", entry)); } catch { continue; }
            foreach (var n in x.Root?.Elements("npc") ?? Enumerable.Empty<XElement>())
                if (int.TryParse((string)n.Attribute("id"), out int id)) AddNpc(packs, id, n, available);
        }
        // GMS2 has no npcscript table: script/npc/<id>.xml per npc, the scripts directly under <ms2>, the id the file's number.
        // A data-shape adapter; the GMS2 client's reader is not in the restored source (STILL-OPEN)
        if (tables.Count == 0)
            foreach (var entry in all)
            {
                string stem = entry[(entry.LastIndexOf('/') + 1)..^4];
                if (!int.TryParse(stem, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id)) continue;
                XDocument x;
                try { x = GameStrings.Parse(packs.Read("Xml", entry)); } catch { continue; }
                if (x.Root != null) AddNpc(packs, id, x.Root, available);
            }
        // the $script:N$ words: npc scripts' text in scriptnpc*.xml, quest scripts' (QuestScriptTable) in scriptquest_*.xml,
        // one id space (the ids are unique stamps, e.g. 0831180104000004 for quest 90000000's complete talk)
        foreach (var prefix in new[] { "scriptnpc", "scriptquest" })
            foreach (var k in text.Rows(prefix))
                if (long.TryParse((string)k.Attribute("id"), out long sid) && !strings.ContainsKey(sid)) strings[sid] = (string)k.Attribute("name");
    }

    /// <summary>
    /// One &lt;npc&gt;'s scripts (LoadNpcTalks, talkscript.cpp:694..739): the &lt;select&gt;, &lt;script&gt; and &lt;job&gt; elements the client
    /// takes one of per id among the available ones (FeatureElements.Collect, FUN_14028faa0: a later element of the same id replaces
    /// the kept one when its locale and feature win), and each available &lt;monologue&gt;. GMS2 writes a locale and a feature on every
    /// one of them, with up to 628 script ids repeated by locale or feature.
    /// </summary>
    void AddNpc(PackSet packs, int id, XElement n, Func<XElement, bool> available)
    {
        if (!npcs.TryGetValue(id, out var npc)) npcs[id] = npc = new Npc { Id = id };
        var kept = new HashSet<XElement>();
        foreach (var kind in new[] { "select", "script", "job" })
            foreach (var e in FeatureElements.Collect(packs, n, kind, "id")) kept.Add(e);
        List<Script> alt = new();
        foreach (var s in n.Elements())
        {
            bool mono = s.Name.LocalName == "monologue";
            if (mono ? available != null && !available(s) : s.Name.LocalName is "select" or "script" or "job" && !kept.Contains(s)) continue;
            var sc = new Script { Kind = s.Name.LocalName, Id = Int(s, "id"), RandomPick = Int(s, "randomPick") != 0 };
            if (mono)
            {
                // FUN_140859320: QueryIntAttribute only when the attribute is there
                if (int.TryParse((string)s.Attribute("popupProp"), out int prop)) sc.PopupProp = prop;
                if (int.TryParse((string)s.Attribute("popupState"), out int state)) sc.PopupState = state;
                alt.Add(sc);
            }
            foreach (var c in s.Elements("content")) sc.Contents.Add(Content.Parse(c));
            npc.Scripts.Add(sc);
        }
        // s_npcAlt.SetAt per <npc>, when it has any (talkscript.cpp:714): a later file's npc replaces the list
        if (alt.Count > 0) monologues[id] = alt;
    }

    static int Int(XElement e, string a) => int.TryParse((string)e.Attribute(a), out int v) ? v : 0;

    /// <summary>XmlGetBool: "1" or "true".</summary>
    static bool Bool(XElement e, string a) => (string)e.Attribute(a) is { } v && (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));

    static int[] Ints(XElement e, string a) => ((string)e.Attribute(a) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => int.TryParse(x, out int v) ? v : 0).Where(v => v != 0).ToArray();

    public Npc Find(int npcId) => npcs.GetValueOrDefault(npcId);

    /// <summary>GetNpcMonologues (s_npcAlt): the npc's available &lt;monologue&gt; scripts, null when it has none.</summary>
    public IReadOnlyList<Script> Monologues(int npcId) => monologues.GetValueOrDefault(npcId);

    /// <summary>
    /// FUN_140856fa0 (talkscript.cpp:445): the npc's monologues of popup state <paramref name="state"/>, weighted by their
    /// popupProp. With total the weights' sum (none or 0: null), a roll of <paramref name="rand"/>() % 100 under total
    /// picks one by rand() % total; at or over it nothing shows. <paramref name="rand"/> is FUN_140767ff0 (s_random).
    /// </summary>
    public Script PickMonologue(int npcId, int state, Func<uint> rand)
    {
        var list = Monologues(npcId);
        if (list == null) return null;
        int total = 0;
        var candidates = new List<Script>();
        foreach (var s in list)
        {
            if (s.PopupState != state) continue;
            candidates.Add(s);
            total += s.PopupProp;
        }
        if (candidates.Count == 0 || total == 0) return null;
        int roll = (int)(rand() % 100);
        if (roll < total)
        {
            roll = (int)(rand() % (uint)total);
            int accumulated = 0;
            foreach (var s in candidates)
            {
                accumulated += s.PopupProp;
                if (accumulated > roll) return s;
            }
        }
        return null;
    }

    /// <summary>
    /// The text of a content as the talk window shows it: the object words (CReservedWordTable::ReplaceObjectWords, which
    /// the client runs when it loads the content, CScriptContent::vfunc_2), so "$script:0831180407000001$" becomes its
    /// string and the $map, $npcName, $item ... words in that string their names; then the value words
    /// (ReplaceValueWords, CCinematicComponent::vfunc_5 when the page is shown) with <paramref name="playerName"/> for
    /// $MyPCName and <paramref name="playerGender"/> (0 male, 1 female) for $male and $female. Both depend only on the tables and the name, so running them together here gives the same text.
    /// </summary>
    ReservedWords Words => words ??= new ReservedWords(gameText, id => strings.TryGetValue(unchecked((long)id), out var v) ? v : "");

    /// <summary>FUN_1401aa6a0: the npc's "title name" (string tables 19 and 2), or the name alone when it has no title.</summary>
    public string NpcDisplayName(int npcId) => Words.NpcDisplayName(npcId);

    public string Resolve(string text, string playerName, int? playerGender = null)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return Words.ReplaceValueWords(Words.ReplaceObjectWords(text), playerName, playerGender).Replace("\\n", "\n");
    }
}
