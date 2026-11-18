using System.Xml.Linq;
using Ms2.Formats.Pack;

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
    readonly Dictionary<long, string> strings = new();
    public int Count => npcs.Count;
    public int StringCount => strings.Count;
    readonly GameStrings gameText;
    ReservedWords words;

    public NpcScriptTable(PackSet packs, GameStrings text)
    {
        gameText = text;
        foreach (var entry in packs.List("Xml").Where(n => n.StartsWith("script/npc/npcscript", StringComparison.OrdinalIgnoreCase)
                                                             && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n))
        {
            XDocument x;
            try { x = GameStrings.Parse(packs.Read("Xml", entry)); } catch { continue; }
            foreach (var n in x.Root?.Elements("npc") ?? Enumerable.Empty<XElement>())
            {
                if (!int.TryParse((string)n.Attribute("id"), out int id)) continue;
                if (!npcs.TryGetValue(id, out var npc)) npcs[id] = npc = new Npc { Id = id };
                foreach (var s in n.Elements())
                {
                    var sc = new Script { Kind = s.Name.LocalName, Id = Int(s, "id"), RandomPick = Int(s, "randomPick") != 0 };
                    foreach (var c in s.Elements("content")) sc.Contents.Add(Content.Parse(c));
                    npc.Scripts.Add(sc);
                }
            }
        }
        // the $script:N$ words: npc scripts' text in scriptnpc*.xml, quest scripts' (QuestScriptTable) in scriptquest_*.xml,
        // one id space (the ids are unique stamps, e.g. 0831180104000004 for quest 90000000's complete talk)
        foreach (var prefix in new[] { "scriptnpc", "scriptquest" })
            foreach (var k in text.Rows(prefix))
                if (long.TryParse((string)k.Attribute("id"), out long sid) && !strings.ContainsKey(sid)) strings[sid] = (string)k.Attribute("name");
    }

    static int Int(XElement e, string a) => int.TryParse((string)e.Attribute(a), out int v) ? v : 0;

    /// <summary>XmlGetBool: "1" or "true".</summary>
    static bool Bool(XElement e, string a) => (string)e.Attribute(a) is { } v && (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));

    static int[] Ints(XElement e, string a) => ((string)e.Attribute(a) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => int.TryParse(x, out int v) ? v : 0).Where(v => v != 0).ToArray();

    public Npc Find(int npcId) => npcs.GetValueOrDefault(npcId);

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
