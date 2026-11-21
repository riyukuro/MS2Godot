using Ms2.Net.Packets;

namespace Ms2.Ui.Skills;

/// <summary>
/// The skill system's compact auto-cast (feature 0x10, CompactControl): holding a compact control tab's key action casts the
/// tab's skills one after another. CSkillSystem's m_lstCompact, m_spCompact, m_lstLapenSkills, m_lstLapenChains, m_nLapenJob,
/// m_nLapenTick and m_nLapenDirty with FUN_141766ec0 @0x141766ec0 (skillslot.cpp:2384-2442, <see cref="Update"/>),
/// FUN_1417674a0 @0x1417674a0 (:2446-2508, <see cref="Pick"/>), FUN_141767990 @0x141767990 (:2512-2526, <see cref="Used"/>),
/// SetCompactConfigs @0x141767d90 (:2551-2556, <see cref="SetConfigs"/>) and FUN_141767df0 @0x141767df0 (:2560-2567,
/// <see cref="HotKeyCapture"/>). 0 B per call once the lists have grown.
/// STILL-OPEN: the refusal notice (FUN_141769440) goes to the caller; IsShortKey (:2457) is always false here:
/// the only adder of short keys is UICartoonDialog (uicameraeditdialog.cpp:621-640), which is not ported; the state flag 0x80 of the capture has no reader here.
/// </summary>
public sealed class CompactCast
{
    IReadOnlyList<SkillMacroTab> configs = Array.Empty<SkillMacroTab>();   // m_lstCompact
    SkillMacroTab tab;                                                     // m_spCompact, kept once picked
    readonly List<uint> skills = new();                                    // m_lstLapenSkills: the useCoolDownShowUI skills
    readonly List<uint> chains = new();                                    // m_lstLapenChains: the others, each repeatCount + 1 times
    int job;                                                               // m_nLapenJob
    uint lapenTick;                                                        // m_nLapenTick
    bool dirty;                                                            // m_nLapenDirty

    /// <summary>m_spCompact: the tab whose key was down last, null before any.</summary>
    public SkillMacroTab Tab => tab;
    /// <summary>m_lstLapenSkills and m_lstLapenChains, for a test.</summary>
    public IReadOnlyList<uint> Skills => skills;
    public IReadOnlyList<uint> Chains => chains;

    /// <summary>
    /// The tabs replace the old ones. With <paramref name="force"/> (SetCompactConfigs @0x141767d90, :2551-2556, the compact
    /// control window's close) <see cref="Update"/> follows with force; without (SkillMacro mode 2, CSkillSystem::FUN_141767ae0
    /// :2530-2547) only the tabs change and the next update reads them.
    /// </summary>
    public void SetConfigs(IReadOnlyList<SkillMacroTab> tabs, bool force, bool featureOn, Func<long, bool> actionDown,
                           int jobGroup, SlotWorld w, SkillCompactControlTable table)
    {
        configs = tabs ?? Array.Empty<SkillMacroTab>();
        if (force) Update(true, featureOn, actionDown, jobGroup, w, table);
    }

    /// <summary>
    /// FUN_141766ec0 (:2384-2442), each skill system update (CSkillSystem::Update, :2339): the first tab with a key action
    /// whose key is down (<paramref name="actionDown"/>, CKeyTable::IsActionKeyDown) becomes the tab; a new job group
    /// (<paramref name="jobGroup"/>, GetJobGroupId), <paramref name="force"/> or a new tab empties both lists. Then, with a
    /// tab that has skills and the job's table rows (CSkillCompactControlTable::GetJobSkills), each row learnt
    /// (JobSystemCli::vfunc_2) and ticked in the tab with level data at the own level goes to the skills list (a
    /// useCoolDownShowUI skill, only while that list was empty) or to the chains list once plus repeatCount more (only while
    /// that list was empty and the skill is not in it yet). An empty list refills at the next update.
    /// The rows are not read while neither list was empty (nothing is added then: the outcome is the client's).
    /// </summary>
    public void Update(bool force, bool featureOn, Func<long, bool> actionDown, int jobGroup, SlotWorld w,
                       SkillCompactControlTable table)
    {
        if (!featureOn) return;
        var old = tab;
        for (int i = 0; i < configs.Count; i++)
        {
            var c = configs[i];
            if (c.KeyAction == 0) continue;
            if (actionDown(c.KeyAction)) { tab = c; break; }
        }
        if (job != jobGroup || force || (object)old != tab)
        {
            skills.Clear();
            chains.Clear();
            job = jobGroup;
        }
        if (tab == null || tab.Skills.Count == 0) return;
        var rows = table?.GetJobSkills(jobGroup);
        if (rows == null || rows.Count == 0) return;
        bool skillsEmpty = skills.Count == 0, chainsEmpty = chains.Count == 0;
        if (!skillsEmpty && !chainsEmpty) return;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            uint id = row.SkillId;
            if (w.Jobs == null || !w.Jobs.IsLearned(id)) continue;
            if (!Ticked(tab, id)) continue;
            // SkillDataTable::GetSkillLevelData(id, MyPC::GetSkillLevel(id))
            var lv = w.Tables.GetSkillData(id)?.GetLevelData(w.Jobs.SkillLevel(id));
            if (lv == null) continue;
            if (lv.Skill.Condition.UseCoolDownShowUI)
            {
                if (skillsEmpty) skills.Add(id);
            }
            else if (chainsEmpty && !chains.Contains(id))
            {
                chains.Add(id);
                for (int n = 0; n < row.RepeatCount; n++) chains.Add(id);
            }
        }
    }

    static bool Ticked(SkillMacroTab t, uint id)
    {
        var s = t.Skills;
        for (int i = 0; i < s.Count; i++)
            if ((uint)s[i] == id) return true;
        return false;
    }

    /// <summary>
    /// FUN_1417674a0 (:2446-2508), MyPC::vfunc_104's first ask (mypc.cpp:1682) and the continue skill's hold
    /// (stateskill.cpp:665): 0 with the feature off, before the capture's tick (<paramref name="now"/>, GetTickCountMs2) or
    /// without a tab; with the tab's key not down it marks the next refusal for a notice and gives 0. Else the first id of the
    /// skills list, then the chains list, whose level data (FUN_141764c80, <see cref="CastGate.LevelData"/>) is there, which
    /// without <paramref name="any"/> is not cooling (FUN_141765fe0, or no charge left for a recharge skill; a cooling skill
    /// is skipped with useCoolDownShowUI or without <paramref name="castAllowed"/>) and whose cast check (FUN_14176a380,
    /// on the level data of the skill MyPC::FUN_141526c80 swaps it to, <see cref="SkillCast.ChangeSkill"/>,
    /// <see cref="CastGate.Why"/> with <paramref name="castAllowed"/>) passes. <paramref name="castAllowed"/> is
    /// m_nCastAllowed: 1 inside the skill state (StateSkill::vfunc_8 stateskill.cpp:639-697 and the end step :2906-2928 set
    /// it around the poll and the hold check), 0 for the idle poll and a key press. The first refusal after the key went
    /// down goes to <paramref name="notice"/> (FUN_141769440) once.
    /// </summary>
    public uint Pick(bool any, bool castAllowed, bool featureOn, Func<long, bool> actionDown, SlotWorld w, uint now, Action<int> notice)
    {
        if (!featureOn) return 0;
        if (lapenTick > now) return 0;
        if (tab == null) return 0;
        if (!actionDown(tab.KeyAction)) { dirty = true; return 0; }
        // :2457: IsShortKey(FindKeyByActionAndPriority(m_30, 0)), always false here (short keys come only from the unported cartoon dialog)
        for (int i = 0; i < skills.Count + chains.Count; i++)
        {
            uint id = i < skills.Count ? skills[i] : chains[i - skills.Count];
            var lv = CastGate.LevelData(w, id);
            if (lv == null) continue;
            var s = lv.Skill;
            if (!any)
            {
                float cool = w.CooldownRemain(s.Id, 0);
                bool recharge = s.RechargeMaxCount > 0;
                if (((!recharge && cool > 0f) || (recharge && w.RechargeRemain(s) == 0)) && (s.Condition.UseCoolDownShowUI || !castAllowed))
                    continue;
            }
            // :2482-2490: the swap (MyPC::FUN_141526c80); a changed id or level takes its own level data
            uint cid = s.Id;
            ushort clv = lv.Level;
            SkillCast.ChangeSkill(w, ref cid, ref clv);
            if ((cid != s.Id || clv != lv.Level) && (lv = CastGate.LevelData(w, cid)) == null) continue;
            int why = CastGate.Why(w, lv, castAllowed);
            if (why != 0)
            {
                if (dirty)
                {
                    notice?.Invoke(why);
                    dirty = false;
                }
                continue;
            }
            return id;
        }
        return 0;
    }

    /// <summary>
    /// FUN_141767990 (:2512-2526), after a cast went (MyPC::vfunc_102 mypc.cpp:1229, a continue round stateskill.cpp:926, the
    /// queued round :1594): one entry of the skill's origin id leaves the chains list (with the feature on and the skill's
    /// level data there).
    /// </summary>
    public void Used(bool featureOn, SkillData skill, int level)
    {
        if (!featureOn || skill?.GetLevelData(level) == null) return;
        int i = chains.IndexOf(skill.OriginId);
        if (i >= 0) chains.RemoveAt(i);
    }

    /// <summary>FUN_141767df0 (:2560-2567), the compact control window's key capture: off holds the picks for 1000 ms from
    /// <paramref name="now"/> (m_nLapenTick). The state flag 0x80 it sets while on has no reader here.</summary>
    public void HotKeyCapture(bool on, uint now)
    {
        if (!on) lapenTick = now + 1000;
    }
}
