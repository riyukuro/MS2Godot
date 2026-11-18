using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

/// <summary>
/// Quest talk scripts (Xml.m2d : script/quest/questscript*.xml), the client's LoadQuestTalks (talkscript.cpp @0x140857f20):
/// per &lt;quest id&gt; its &lt;script id&gt;s, the available one per id (FUN_14028faa0, the feature and locale pick), each
/// &lt;content&gt; a page that carries the quest id (FUN_140859160 :595). The script id says the step: 100-199 the accept talk,
/// 200-299 the progress talk, 300 and up the complete talk (GetTalkButtonSet, talkscript.cpp:866).
/// </summary>
public sealed class QuestScriptTable
{
    readonly Dictionary<int, Dictionary<int, NpcScriptTable.Script>> quests = new();

    public int Count => quests.Count;

    /// <param name="collect">FUN_14028faa0 (commonutil.cpp:1748): the available child elements of a name, one per key value.</param>
    public QuestScriptTable(PackSet packs, Func<XElement, string, string, IEnumerable<XElement>> collect)
    {
        foreach (var entry in packs.List("Xml").Where(n => n.StartsWith("script/quest/questscript", StringComparison.OrdinalIgnoreCase)
                                                             && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n))
        {
            XDocument x;
            try { x = GameStrings.Parse(packs.Read("Xml", entry)); } catch { continue; }
            foreach (var q in x.Root?.Elements("quest") ?? Enumerable.Empty<XElement>())
            {
                if (!int.TryParse((string)q.Attribute("id"), out int questId) || questId == 0) continue;   // "the value is 0"
                if (!quests.TryGetValue(questId, out var talks)) quests[questId] = talks = new();
                foreach (var s in collect(q, "script", "id"))
                {
                    var sc = new NpcScriptTable.Script { Kind = "script", QuestId = questId };
                    int.TryParse((string)s.Attribute("id"), out sc.Id);
                    foreach (var c in s.Elements("content")) sc.Contents.Add(NpcScriptTable.Content.Parse(c, questId));
                    talks[sc.Id] = sc;
                }
            }
        }
    }

    /// <summary>FindQuestTalk (talkscript.cpp:510): the quest's script of that id, null when it has none.</summary>
    public NpcScriptTable.Script Find(int questId, int scriptId) =>
        quests.TryGetValue(questId, out var t) && t.TryGetValue(scriptId, out var s) ? s : null;

    /// <summary>The quest's script ids, in order (what the server's GetFirstStateScript picks from).</summary>
    public IEnumerable<int> ScriptIds(int questId) => quests.TryGetValue(questId, out var t) ? t.Keys.OrderBy(k => k) : Enumerable.Empty<int>();
}
