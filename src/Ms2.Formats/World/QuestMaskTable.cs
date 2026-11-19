using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

/// <summary>
/// Quest masks (CQuestMaskTable, questmasktable.cpp): which portals and npcs of a field the client shows only within a stretch
/// of the quest line. Masks come from Xml.m2d : table/questmaskportal.xml and table/questmasknpc.xml
/// (&lt;field fieldID&gt;&lt;key id enableInMask&gt;&lt;mask startQuestID startQuestState endQuestID endQuestState&gt;), and for a
/// portal also from its own entity (MaskQuestID "start,end" and MaskQuestState "startState,endState", ranges split by '|',
/// ParseQuestMask @0x140764310). When a portal has a mask the client sets its shown and glow flags from CheckPortal
/// (CPortal::FUN_14169d0e0), so a portal that fails is hidden and cannot be used; an npc failing CheckNpc is not shown.
/// </summary>
public sealed class QuestMaskTable
{
    /// <summary>Unk_QuestMaskRange {a, b, c, d}: a = the start quest, b = the end quest, c = the start quest's mode, d = the
    /// end quest's mode (the "state" attributes are <see cref="Eval"/> modes).</summary>
    public readonly record struct Range(int StartQuest, int StartState, int EndQuest, int EndState);

    public sealed class Mask
    {
        /// <summary>QuestMask::m_nEnable (enableInMask, 1 unless the row sets it): what the check gives inside the stretch.</summary>
        public int Enable = 1;
        public List<Range> Ranges = new();
        /// <summary>QuestMask::IsSatisfied @0x140266970 (the mask itself is usable): it has ranges and every range has a start quest.</summary>
        public bool IsSatisfied => Ranges.Count > 0 && Ranges.All(r => r.StartQuest != 0);
    }

    /// <summary>A quest's standing for <see cref="Eval"/>: no record, taken (state 1, not complete), complete (state 1) or
    /// rewarded (state 2).</summary>
    public enum QuestStatus { Absent, Active, Completed, Rewarded }

    readonly Dictionary<(int field, int key), Mask> portals = new(), npcs = new();

    /// <param name="available">IsElementAvailable for a &lt;mask&gt; row (its locale and feature); every row when null.</param>
    public QuestMaskTable(PackSet packs, Func<XElement, bool> available = null)
    {
        Load(packs, "table/questmaskportal.xml", portals, available);
        Load(packs, "table/questmasknpc.xml", npcs, available);
    }

    /// <summary>A table from already parsed documents (tests).</summary>
    public QuestMaskTable(XDocument portalXml, XDocument npcXml, Func<XElement, bool> available = null)
    {
        Load(portalXml, portals, available);
        Load(npcXml, npcs, available);
    }

    public int PortalMasks => portals.Count;
    public int NpcMasks => npcs.Count;

    static void Load(PackSet packs, string entry, Dictionary<(int, int), Mask> into, Func<XElement, bool> available)
    {
        if (!packs.Exists("Xml", entry)) return;
        Load(GameStrings.Parse(packs.Read("Xml", entry)), into, available);
    }

    /// <summary>CQuestMaskTable::LoadFile @0x140765080: field 0 and key 0 are skipped; a key's mask is kept only when IsSatisfied.</summary>
    static void Load(XDocument x, Dictionary<(int, int), Mask> into, Func<XElement, bool> available)
    {
        if (x == null) return;
        foreach (var f in x.Root?.Elements("field") ?? Enumerable.Empty<XElement>())
        {
            int field = Int(f, "fieldID");
            if (field == 0) continue;
            foreach (var k in f.Elements("key"))
            {
                int key = Int(k, "id");
                if (key == 0) continue;
                var m = new Mask();
                if (int.TryParse((string)k.Attribute("enableInMask"), out int en)) m.Enable = en;
                foreach (var r in k.Elements("mask"))
                {
                    if (available != null && !available(r)) continue;
                    m.Ranges.Add(new Range(Int(r, "startQuestID"), Int(r, "startQuestState"), Int(r, "endQuestID"), Int(r, "endQuestState")));
                }
                if (m.IsSatisfied) into[(field, key)] = m;
            }
        }
    }

    static int Int(XElement e, string a) => int.TryParse((string)e.Attribute(a), out int v) ? v : 0;

    /// <summary>
    /// ParseQuestMask @0x140764310 of an entity's two strings: both split on '|' (a count mismatch gives no ranges); each pair
    /// split on ',' (FUN_140764760: a count mismatch skips that range; the first values are the start quest and mode, the
    /// second the end quest and mode). The mask may not be <see cref="Mask.IsSatisfied"/>; the callers test that.
    /// </summary>
    public static Mask FromEntity(string questIds, string questStates)
    {
        var m = new Mask();
        var ids = (questIds ?? "").Split('|'); var states = (questStates ?? "").Split('|');
        if (ids.Length != states.Length) return m;
        for (int i = 0; i < ids.Length; i++)
        {
            var a = Ints(ids[i]); var b = Ints(states[i]);
            if (a.Length != b.Length) continue;
            m.Ranges.Add(new Range(a.Length > 0 ? a[0] : 0, b.Length > 0 ? b[0] : 0, a.Length > 1 ? a[1] : 0, b.Length > 1 ? b[1] : 0));
        }
        return m;
    }

    static int[] Ints(string s) => s.Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(x => int.TryParse(x.Trim(), out int v) ? v : 0).ToArray();

    public Mask Portal(int field, int portalId) => portals.GetValueOrDefault((field, portalId));
    public Mask Npc(int field, int npcId) => npcs.GetValueOrDefault((field, npcId));

    /// <summary>
    /// FUN_140764480: one quest against a range mode. 0 = the quest is in the mode's stage, 1 = past it, 2 = not yet (or an
    /// unknown mode). Mode 1: taken; mode 2: complete; mode 3: rewarded.
    /// </summary>
    public static int Eval(QuestStatus s, int mode) => mode switch
    {
        1 => s == QuestStatus.Absent ? 2 : s == QuestStatus.Active ? 0 : 1,
        2 => s is QuestStatus.Absent or QuestStatus.Active ? 2 : s == QuestStatus.Completed ? 0 : 1,
        3 => s == QuestStatus.Rewarded ? 0 : 2,
        _ => 2,
    };

    /// <summary>
    /// CQuestManagerCommon::vfunc_o10_23 @0x1407470f0 (the player's mask check): an unusable mask passes; a range whose start
    /// quest is not yet in its stage, or whose end quest is past its stage, is skipped; the first other range gives Enable;
    /// with none, the check gives Enable == 0.
    /// </summary>
    public static bool Check(Mask m, Func<int, QuestStatus> quest)
    {
        if (!m.IsSatisfied) return true;
        foreach (var r in m.Ranges)
        {
            if (Eval(quest(r.StartQuest), r.StartState) == 2) continue;
            if (Eval(quest(r.EndQuest), r.EndState) == 1) continue;
            return m.Enable != 0;
        }
        return m.Enable == 0;
    }

    /// <summary>CQuestMaskTable::HasPortalMask @0x140764d50: the portal's own mask is usable, or the table has one for it.</summary>
    public bool HasPortalMask(int field, int portalId, Mask own) => own is { IsSatisfied: true } || Portal(field, portalId) != null;

    /// <summary>CQuestMaskTable::CheckPortal @0x140764f30: no mask passes; else the portal's own mask, then the table's.</summary>
    public bool CheckPortal(int field, int portalId, Mask own, Func<int, QuestStatus> quest)
    {
        if (!HasPortalMask(field, portalId, own)) return true;
        if (own is { IsSatisfied: true } && Check(own, quest)) return true;
        return Portal(field, portalId) is { } t && Check(t, quest);
    }

    /// <summary>CQuestMaskTable::CheckNpc @0x140764e40: the table's mask for the npc, or pass.</summary>
    public bool CheckNpc(int field, int npcId, Func<int, QuestStatus> quest) => Npc(field, npcId) is not { } m || Check(m, quest);

    /// <summary>A new character's quests: none taken.</summary>
    public static QuestStatus NoQuests(int id) => QuestStatus.Absent;
}
