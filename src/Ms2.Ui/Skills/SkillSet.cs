using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;

namespace Ms2.Ui.Skills;

/// <summary>
/// A skill set row (CSkillSet, commonfiles/skillset.cpp): the skills a skill set gives the quick slots for a while (a
/// mount's skillSetID, MyPC::vfunc_120, mypc.cpp:3295-3317). The ctor (@0x1408092d0) sets version 1 and slotLock 1.
/// </summary>
public sealed class SkillSet
{
    public int Id;
    /// <summary>m_nVersion: part of the key setting name "SkillSet{id}_{version}".</summary>
    public int Version = 1;
    /// <summary>m_nSlotLock: the set's quick slots are fixed (SkillSlot::SetFixed); without it the player's own layout of
    /// the set is saved and loaded (CSkillSystem::AddSkillSet / ApplySkillSet).</summary>
    public bool SlotLock = true;
    public List<int> SkillIds = new(), SkillLevels = new(), LearnSkillIds = new(), LearnSkillLevels = new(), ItemIds = new();

    /// <summary>The key setting file's name part (Unk_SocketFormat("SkillSet{0}_{1}", id, version), skillslot.cpp:2588).</summary>
    public string KeySettingName => $"SkillSet{Id}_{Version}";

    /// <summary>CSkillSet::HasSkill @0x140809410 (skillset.cpp:57): the skill in skillIDs or learnSkillIDs, at
    /// <paramref name="level"/> unless it is 0.</summary>
    public bool HasSkill(int skillId, int level = 0) =>
        Has(SkillIds, SkillLevels, skillId, level) || Has(LearnSkillIds, LearnSkillLevels, skillId, level);

    static bool Has(List<int> ids, List<int> levels, int id, int level)
    {
        for (int i = 0; i < ids.Count; i++)
            if (ids[i] == id && (level == 0 || level == At(levels, i))) return true;
        return false;
    }

    /// <summary>CSkillSet::GetSkillLevel @0x140809560 (skillset.cpp:75): the level of the skill in skillIDs, else in
    /// learnSkillIDs, else 0.</summary>
    public int GetSkillLevel(int skillId)
    {
        int i = SkillIds.IndexOf(skillId);
        if (i >= 0) return At(SkillLevels, i);
        i = LearnSkillIds.IndexOf(skillId);
        return i >= 0 ? At(LearnSkillLevels, i) : 0;
    }

    // a levels list shorter than its ids (the client indexes the vector past its end; the data has none)
    static int At(List<int> l, int i) => i < l.Count ? l[i] : 0;
}

/// <summary>
/// CSkillSetTable (commonfiles/skillset.cpp): Table/skillSet.xml, &lt;ms2&gt;&lt;skillSet id version slotLock skillIDs
/// skillLevels learnSkillIDs learnSkillLevels itemIDs&gt;, each available element (IsElementAvailable: its feature on and
/// its locale the client's) a row by id (CSkillSetTable::Load @0x140809930, :132-170). The lists are XmlSplit by ",": atoi of
/// each non-empty token, so "0" items stay in place.
/// </summary>
public sealed class SkillSetTable
{
    public const string Entry = "table/skillset.xml";
    readonly Dictionary<int, SkillSet> rows = new();

    public IReadOnlyDictionary<int, SkillSet> Rows => rows;

    /// <summary>CSkillSetTable::GetSkillSet @0x140809840: the row, null without one.</summary>
    public SkillSet Get(int id) => rows.GetValueOrDefault(id);

    public static SkillSetTable Load(PackSet packs)
    {
        if (!packs.Exists("Xml", Entry)) return new SkillSetTable();
        return Load(PackXml.Parse(packs.Read("Xml", Entry)), e => FeatureElements.IsAvailable(packs, e));
    }

    public static SkillSetTable Load(XDocument doc, Func<XElement, bool> available)
    {
        var t = new SkillSetTable();
        foreach (var e in doc.Root?.Elements("skillSet") ?? Enumerable.Empty<XElement>())
        {
            if (!available(e)) continue;
            var s = new SkillSet { Id = Xml.Int(e, "id") };
            if (e.Attribute("version") != null) s.Version = Xml.Int(e, "version");
            s.SlotLock = Xml.GetBool(e, "slotLock", s.SlotLock);
            s.SkillIds = Ints(e, "skillIDs");
            s.SkillLevels = Ints(e, "skillLevels");
            s.LearnSkillIds = Ints(e, "learnSkillIDs");
            s.LearnSkillLevels = Ints(e, "learnSkillLevels");
            s.ItemIds = Ints(e, "itemIDs");
            t.rows[s.Id] = s;      // CAtlMap::SetAt: a later row of the same id replaces the earlier
        }
        return t;
    }

    static List<int> Ints(XElement e, string name) => Xml.Tokens((string)e.Attribute(name)).Select(Xml.Atoi).ToList();
}

/// <summary>
/// The skill sets of the own skill system (CSkillSystem m_lstSkillSets / m_spSkillSet, skillslot.cpp:2582-2621): the
/// ids in the order they were added, the current set the last one's row. Before the current set changes, a set without
/// slotLock saves the player's quick slots under its key setting name (<see cref="SaveKeySetting"/>).
/// </summary>
public sealed class SkillSetList
{
    readonly List<int> ids = new();

    /// <summary>m_spSkillSet (GetSkillSet @0x141768380): null without a set.</summary>
    public SkillSet Current { get; private set; }

    public IReadOnlyList<int> Ids => ids;

    /// <summary>CKeyTable::SaveKeySetting of the current set's name.</summary>
    public Action<string> SaveKeySetting;

    /// <summary>CSkillSystem::AddSkillSet @0x141767f80 (:2582): 0 does nothing; the current set without slotLock saves its
    /// layout; the id goes to the end and its row (null when the table has none) becomes the current set.</summary>
    public void Add(int id, SkillSetTable table)
    {
        if (id == 0) return;
        if (Current is { SlotLock: false } cur) SaveKeySetting?.Invoke(cur.KeySettingName);
        ids.Add(id);
        Current = table?.Get(id);
    }

    /// <summary>CSkillSystem::RemoveSkillSet @0x141768100 (:2596): 0 does nothing; the current set of this id without
    /// slotLock saves its layout; the first entry of the id goes; the current set is the last id's row, or none.</summary>
    public void Remove(int id, SkillSetTable table)
    {
        if (id == 0) return;
        if (Current is { SlotLock: false } cur && cur.Id == id) SaveKeySetting?.Invoke(cur.KeySettingName);
        ids.Remove(id);
        Current = ids.Count == 0 ? null : table?.Get(ids[^1]);
    }

    /// <summary>
    /// ~CSkillSystem @0x141764900 (skillslot.cpp:1829-1836) with the old MyPC at a field change or a character's leave: each
    /// listed set is removed (an open set's layout saved), so the next MyPC's skill system starts with none. With a set
    /// listed, <paramref name="applied"/> (the HUD's ApplySkillSet, which without a set leaves skill set mode) runs and the
    /// key table's lock is lifted.
    /// <para>The client lifts the lock one step later: the new MyPC's CSkillSystem::Init (@0x141764a80, :1839-1846) applies
    /// no set, and CKeyTable::SendKeyTableMode7 (@0x14142f700, keytable.cpp:2021-2034) empties the slots, unlocks and sends
    /// KeyTable mode 7, which the server answers with the pages. Both servers here send the pages (mode 7) before the new
    /// field's FieldAddUser, which a locked table skips (keytable.cpp:355-356), and do not answer the request, so a lock
    /// kept until then would leave the quick slots empty until the next ride off.</para>
    /// </summary>
    public void Drop(SkillSetTable table, Ms2.Net.Packets.ServerKeyTable keys, Action applied = null)
    {
        if (ids.Count == 0) return;
        while (ids.Count > 0) Remove(ids[0], table);
        applied?.Invoke();
        if (keys != null) keys.Locked = false;
    }
}
