using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

/// <summary>
/// Quest masks (CQuestMaskTable): which portals and npcs of a field the client shows only within a stretch of the quest line.
/// Masks come from Xml.m2d : table/questmaskportal.xml and table/questmasknpc.xml
/// (&lt;field fieldID&gt;&lt;key id enableInMask&gt;&lt;mask startQuestID startQuestState endQuestID endQuestState&gt;), and for a portal also
/// from its own entity (MaskQuestID "start,end" and MaskQuestState "startState,endState", ranges split by '|').
/// When a portal has a mask the client sets its shown and enabled flags from the check (CPortal::FUN_14169d0e0), so a portal that
/// fails is hidden and cannot be used; a masked npc is not shown.
/// </summary>
public sealed class QuestMaskTable
{
    public readonly record struct Range(int StartQuest, int StartState, int EndQuest, int EndState);

    public sealed class Mask
    {
        public bool EnableInMask = true;
        public List<Range> Ranges = new();
        /// <summary>QuestMask::IsSatisfied (the mask itself): it has a range with a quest.</summary>
        public bool IsValid => Ranges.Any(r => r.StartQuest != 0 || r.EndQuest != 0);
    }

    readonly Dictionary<(int field, int key), Mask> portals = new(), npcs = new();

    public QuestMaskTable(PackSet packs)
    {
        Load(packs, "table/questmaskportal.xml", portals);
        Load(packs, "table/questmasknpc.xml", npcs);
    }

    public int PortalMasks => portals.Count;
    public int NpcMasks => npcs.Count;

    static void Load(PackSet packs, string entry, Dictionary<(int, int), Mask> into)
    {
        if (!packs.Exists("Xml", entry)) return;
        var x = GameStrings.Parse(packs.Read("Xml", entry));
        foreach (var f in x.Root?.Elements("field") ?? Enumerable.Empty<XElement>())
        {
            int field = Int(f, "fieldID");
            if (field == 0) continue;
            foreach (var k in f.Elements("key"))
            {
                int key = Int(k, "id");
                if (key == 0) continue;
                var m = new Mask { EnableInMask = (string)k.Attribute("enableInMask") != "0" };
                foreach (var r in k.Elements("mask"))
                    m.Ranges.Add(new Range(Int(r, "startQuestID"), Int(r, "startQuestState"), Int(r, "endQuestID"), Int(r, "endQuestState")));
                if (m.IsValid) into[(field, key)] = m;
            }
        }
    }

    static int Int(XElement e, string a) => int.TryParse((string)e.Attribute(a), out int v) ? v : 0;

    /// <summary>A portal entity's own mask (ParseQuestMask of its two strings), or null.</summary>
    public static Mask FromEntity(string questIds, string questStates)
    {
        if (string.IsNullOrEmpty(questIds) || string.IsNullOrEmpty(questStates)) return null;
        var ids = questIds.Split('|'); var states = questStates.Split('|');
        if (ids.Length != states.Length) return null;
        var m = new Mask();
        for (int i = 0; i < ids.Length; i++)
        {
            var a = ids[i].Split(',').Select(s => int.TryParse(s.Trim(), out int v) ? v : 0).ToArray();
            var b = states[i].Split(',').Select(s => int.TryParse(s.Trim(), out int v) ? v : 0).ToArray();
            if (a.Length != b.Length || a.Length == 0) continue;
            m.Ranges.Add(new Range(a[0], b[0], a.Length > 1 ? a[1] : 0, b.Length > 1 ? b[1] : 0));
        }
        return m.IsValid ? m : null;
    }

    public Mask Portal(int field, int portalId) => portals.GetValueOrDefault((field, portalId));
    public Mask Npc(int field, int npcId) => npcs.GetValueOrDefault((field, npcId));

    /// <summary>
    /// The player's check of one mask: a range holds once its start quest has reached the start state and while its end quest has
    /// not reached the end state; any range holding passes. <paramref name="questState"/> gives a quest's state (0 = never taken).
    /// </summary>
    public static bool Check(Mask m, Func<int, int> questState)
    {
        foreach (var r in m.Ranges)
        {
            bool started = r.StartQuest == 0 || questState(r.StartQuest) >= r.StartState;
            bool ended = r.EndQuest != 0 && questState(r.EndQuest) >= r.EndState;
            if (started && !ended) return true;
        }
        return false;
    }
}
