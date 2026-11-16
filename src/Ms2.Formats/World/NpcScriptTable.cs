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
    public sealed class Content
    {
        public string Text, VoiceId, Illust;
        public List<(string Text, int Goto)> Distractors = new();
    }

    public sealed class Script
    {
        public string Kind;          // select, script, job, monologue
        public int Id;
        public bool RandomPick;
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

    public NpcScriptTable(PackSet packs, GameStrings text)
    {
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
                    foreach (var c in s.Elements("content"))
                    {
                        var ct = new Content
                        {
                            Text = (string)c.Attribute("text"), VoiceId = (string)c.Attribute("voiceID"),
                            Illust = (string)c.Attribute("illust"),
                        };
                        foreach (var d in c.Elements("distractor")) ct.Distractors.Add(((string)d.Attribute("text"), Int(d, "goto")));
                        sc.Contents.Add(ct);
                    }
                    npc.Scripts.Add(sc);
                }
            }
        }
        foreach (var k in text.Rows("scriptnpc"))
            if (long.TryParse((string)k.Attribute("id"), out long sid) && !strings.ContainsKey(sid)) strings[sid] = (string)k.Attribute("name");
    }

    static int Int(XElement e, string a) => int.TryParse((string)e.Attribute(a), out int v) ? v : 0;

    public Npc Find(int npcId) => npcs.GetValueOrDefault(npcId);

    /// <summary>"$script:0831180407000001$" (and plain text) -> the string, with $MyPCName$ filled in.</summary>
    public string Resolve(string text, string playerName)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string s = text;
        const string tag = "$script:";
        int i;
        while ((i = s.IndexOf(tag, StringComparison.Ordinal)) >= 0)
        {
            int j = s.IndexOf('$', i + tag.Length);
            if (j < 0) break;
            var id = s[(i + tag.Length)..j];
            s = s[..i] + (long.TryParse(id, out long n) && strings.TryGetValue(n, out var v) ? v : "") + s[(j + 1)..];
        }
        return s.Replace("$MyPCName$", playerName).Replace("\\n", "\n");
    }
}
