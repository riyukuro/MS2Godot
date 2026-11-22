using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Skills;

namespace Ms2.Ui.Hud;

/// <summary>
/// UISkillBookTreeDialog (client/ms2/uiskillbookautodistributedialog.cpp; UI id 0x4f2d, action 400005 = K), the C++ side of the
/// skill window movie uiskillbooktreedialog (uiskillbooktreedialog_jobchange01 with feature 0xca, GetTypeName :471). The movie
/// draws the window, the tab bar, the tree (setSkill places a skillbooktree_dialog_skill per skill) and the buttons; this
/// gives it the saved tabs, each skill's state and level text, the slot bindings ("skillTreeSlot.N"), the remaining points
/// and the tooltips, through the client's own skill point preview (<see cref="SkillPointSimulator"/>).
/// <para>The +/- buttons change only the preview, as in the client (SkillPointUp / SkillPointDown, :1355). Everything that
/// sends a request (apply, reset, auto distribute, a tab add / rename / switch to the used tab, the sub job switch, the
/// lapenshard button, the shortcut key registration) is raised as <see cref="Button"/> and left unsent. The close-time
/// JobSystemCli::RequestReset (vfunc_67 :505) is not sent either.</para>
/// </summary>
public sealed class SkillBookTreeController : UiController, IDragSlotOwner
{
    /// <summary>Feature 0xca (JobChange_01): the jobchange01 movie and its rank 1 / rank 2 trees.</summary>
    public bool JobChange01;

    public override string Movie => JobChange01 ? "uiskillbooktreedialog_jobchange01" : "uiskillbooktreedialog";
    protected override string EntityMethod => "setRemainSkillPoint";
    /// <summary>GetTypeName @0x140ec51c0.</summary>
    public override string TypeName => JobChange01 ? "SkillBookTreeDialog_jobchange01" : "SkillBookTreeDialog";

    /// <summary>m_strSlotPrefix: the BookSlotBag's name; its slots are "skillTreeSlot.&lt;i&gt;" (SkillSlot.setData).</summary>
    public const string BagName = "skillTreeSlot";

    /// <summary>The local player's job system (PC::GetJobSystem).</summary>
    public JobSystem Jobs;
    /// <summary>The OnSkillPoint state (DAT_143ab32e8 / DAT_143ab32f8 and MyPC m_n179c).</summary>
    public SkillPoints Points = new();
    /// <summary>CQuestManagerCommon::IsQuestRewarded of the player.</summary>
    public Func<uint, bool> IsQuestRewarded = _ => false;
    /// <summary>ASTooltip::FUN_1409e1c50(skill, level, 1) with this dialog's simulator: the skill tooltip, null when none.</summary>
    public Func<SkillBookTreeController, uint, int, AsTooltip> SkillTooltip;

    /// <summary>RequestSkillBookTree mode 0 (OnLoadComplete :1954): the host sends it live, or answers with a fixture 0x10C.</summary>
    public event Action RequestTree;
    /// <summary>A callback whose client path sends a request or opens a part not here (by its callback name).</summary>
    public event Action<string> Button;

    /// <summary>FUN_140710590(packet): the connection's send. Without one (no live session) a request goes nowhere.</summary>
    public Action<IOutPacket> Send;
    /// <summary>PC::vfunc_65 of the local player (CanApply :612): in battle, the skill set cannot be applied.</summary>
    public Func<bool> InBattle = () => false;
    /// <summary>CFieldDataCli::HasLimitContent(0x10) (CanApply :612). STILL-OPEN: the field's limit contents are not read, so false.</summary>
    public Func<bool> FieldLimitsSkillSet = () => false;
    /// <summary>ShowMessageBox / FUN_140d73c60: show a box.</summary>
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;
    /// <summary>IsCheckbox0Checked / IsCheckbox1Checked of the box the callback got; the box's own flags without a loaded layer.</summary>
    public Func<MessageBoxData, int, bool> CheckboxChecked = (b, n) =>
        MessageBoxQueue.Layer != null ? MessageBoxQueue.Layer.CheckboxChecked(b, n) : n == 0 ? b.Check0Selected : b.Check1Selected;
    /// <summary>FUN_1412751b0(text, kind): a chat log line of the kind (0x14 for the apply notice).</summary>
    public event Action<string, int> ChatLine;
    /// <summary>PlaySystemSound(key).</summary>
    public event Action<string> Sound;

    /// <summary>m_nWaitServer: a request is out; the +/-, apply and tab callbacks wait for the server's Job answer
    /// (RefreshSlotsIfShown :699) or the close.</summary>
    public bool WaitServer { get; private set; }

    /// <summary>m_nJobLevel: the rank whose tree is shown (1, or 2 with feature 0xca after the second job change).</summary>
    public int JobLevel = 1;
    int maxTabs = 1;                    // m_nMaxTabs
    long tabId, shownTabId;             // m_nTabId (the tab in use), m_nShownTabId
    bool ready;                         // m_n290: a tab packet arrived (vfunc_28 takes callbacks only then)
    readonly List<(long Id, string Name, Dictionary<uint, uint> Slots)> tabs = new();      // m_tabs
    readonly Dictionary<long, SkillPointSimulator> simulators = new();                       // m_simulators
    SkillPointSimulator sim;            // m_spSimulator
    readonly Dictionary<uint, int> slotOrder = new();       // m_slotOrder
    readonly List<JobSkill> bag = new();                    // the BookSlotBag's skills
    static long lastId;

    public SkillPointSimulator Simulator => sim;
    public IReadOnlyList<(long Id, string Name, Dictionary<uint, uint> Slots)> Tabs => tabs;

    SkillTables T => Model.Tables.Skills;
    int Level => Pc.Level;
    bool Feature(int id) => Model.IsFeatureEnabled(id);
    string Common(string key, params object[] args) => Ms2.Quest.ClientFormat.Format(Model.Tables.Common(key) ?? "", args);

    /// <summary>CSkillBookTreeTabConfig's id (GenerateUniqueId): unique within the session here.</summary>
    static long NewTabId()
    {
        long now = DateTime.UtcNow.Ticks;
        lastId = Math.Max(lastId + 1, now);
        return lastId;
    }

    /// <summary>
    /// OnLoadComplete @0x140ec5570 (:1938): UIDialog's, m_buttonBar / m_addButton and SetMaxTabCount(3), the web guide button
    /// hidden (FUN_1419260d0), with feature 0xca m_nJobLevel = the rank of the sub job; then the dialog is hidden (vfunc_8(0))
    /// until the tab packet, and RequestSkillBookTree mode 0 is sent.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        Host.Invoke("SetMaxTabCount", 3u);
        if (Host.Child("m_webGuideButton") is { } web) Host.SetVisible(web, false);
        if (JobChange01 && Jobs != null) JobLevel = JobTable.GetJobRank(Jobs.SubJob);
        Host.SetVisible(Host.Entity, false);
        RequestTree?.Invoke();
    }

    /// <summary>vfunc_67 @0x140ec5340 (:505): the slot bag dropped, the state cleared, UIDialog's close. Its RequestReset (Job
    /// mode 8) is not sent.</summary>
    public override void Close()
    {
        bag.Clear();
        ready = false;
        WaitServer = false;
        shownTabId = 0;
        maxTabs = 1;
        simulators.Clear();
        sim = null;
        base.Close();
    }

    // ---- vfunc_79: the SkillBookTree packet ---------------------------------------------------------------------------

    /// <summary>vfunc_79 @0x140ec7890 (:1962), opcode 0x10C. Mode 0 loads the tabs (an empty name becomes the default tab name;
    /// none: one free tab "Build 1"), selects the one in use and shows the dialog. Mode 1 commits a tab's preview; mode 2 renames
    /// a tab; mode 4 (a paid tab) needs the add-tab flow, which is not here.</summary>
    public void OnSkillBookTree(SkillBookTreePacket p)
    {
        if (p.Mode == 0)
        {
            tabs.Clear();
            simulators.Clear();
            maxTabs = p.MaxTabs;
            tabId = p.TabId;
            foreach (var t in p.Tabs)
            {
                string name = t.Name.Length == 0 ? Common("s_skillbook_tree_config_default_tab_name", tabs.Count + 1) : t.Name;
                var slots = new Dictionary<uint, uint>();
                foreach (var (s, pts) in t.Slots) slots[(uint)s] = (uint)pts;
                tabs.Add((t.Id, name, slots));
                LoadTab(tabs[^1]);
            }
            if (tabs.Count == 0)
            {
                long id = AddTab(Common("s_skillbook_tree_config_default_tab_name", 1), false);
                tabId = id;
                shownTabId = id;
            }
            else if (FindTab(tabId) == null)
            {
                tabId = tabs[0].Id;
            }
            SelectTab(tabId);
            RefreshTabs(tabId);
            RefreshTabButtons(false);
            RefreshSlots(false);
            if (!Rt.ToBoolean(Host.Get(Host.Entity, "visible"))) Host.SetVisible(Host.Entity, true);
            ready = true;
        }
        else if (p.Mode == 1)
        {
            tabId = p.TabId;
            if (!simulators.TryGetValue(p.SavedTabId, out var s)) return;
            s.Commit(p.Rank);
            RefreshPoints();
        }
        else if (p.Mode == 2)
        {
            if (p.Result == 0) RenameTab(p.TabId, p.Name);
            else Button?.Invoke("renameFailed");
        }
        else if (p.Mode == 4)
        {
            maxTabs = p.MaxTabs;
            Button?.Invoke("addPaidTab");
        }
    }

    (long Id, string Name, Dictionary<uint, uint> Slots)? FindTab(long id)
    {
        foreach (var t in tabs) if (t.Id == id) return t;
        return null;
    }

    SkillPointSimulator NewSimulator()
    {
        var s = new SkillPointSimulator(T, Jobs, IsQuestRewarded)
        { LimitLevel1 = Model.Tables.SkillPointLimitLevel1, LimitLevel2 = Model.Tables.SkillPointLimitLevel2 };
        ResetSimulator(s);
        return s;
    }

    /// <summary>Reset(level, sub job, GetSkills(0, true), DAT_143ab32f8, m_n179c).</summary>
    void ResetSimulator(SkillPointSimulator s) =>
        s.Reset(Level, Jobs?.SubJob ?? 0, Jobs?.GetSkills(false, true) ?? new List<JobSkill>(), Points.ByRank, Points.Extra);

    /// <summary>AddTab @0x140ece8b0 (:782): a new tab over the player's book (a free tab from no points spent), its slots the
    /// preview's used points.</summary>
    long AddTab(string name, bool free)
    {
        if (Jobs == null) return 0;
        var s = NewSimulator();
        if (free) s.ResetRank(0);
        var slots = new Dictionary<uint, uint>();
        foreach (var (id, pts) in s.GetUsedPoints(0)) slots[id] = (uint)pts;
        long tab = NewTabId();
        simulators[tab] = s;
        tabs.Add((tab, name, slots));
        return tab;
    }

    /// <summary>LoadTab @0x140ecec20 (:806): the player's book auto-distributed by the tab's slots, then committed.</summary>
    void LoadTab((long Id, string Name, Dictionary<uint, uint> Slots) tab)
    {
        if (Jobs == null) return;
        var s = NewSimulator();
        s.AutoDistribute(tab.Slots.Keys.ToList(), tab.Slots.Values.ToList());
        s.Commit(0);
        simulators[tab.Id] = s;
    }

    /// <summary>RefreshTabs @0x140ecef10 (:832): the tab's preview reset to the player's book and its slots recomputed.</summary>
    void RefreshTabs(long id)
    {
        if (FindTab(id) is not { } tab || !simulators.TryGetValue(tab.Id, out var s)) return;
        tab.Slots.Clear();
        ResetSimulator(s);
        foreach (var (skill, pts) in s.GetUsedPoints(0)) tab.Slots[skill] = (uint)pts;
    }

    /// <summary>SelectTab @0x140ecf1c0 (:577).</summary>
    void SelectTab(long id)
    {
        if (FindTab(id) == null || !simulators.TryGetValue(id, out var s)) return;
        sim = s;
        shownTabId = id;
    }

    /// <summary>RenameTab @0x140ecb1a0 (:725), without its RequestTree.</summary>
    void RenameTab(long id, string name)
    {
        for (int i = 0; i < tabs.Count; i++)
            if (tabs[i].Id == id) tabs[i] = (id, name, tabs[i].Slots);
        Host.Invoke(Host.Entity, "renameTab", ((ulong)id).ToString(System.Globalization.CultureInfo.InvariantCulture), name);
    }

    /// <summary>RefreshTabButtons @0x140ecb5c0 (:676): m_buttonBar's data provider emptied, AddTab(name, id, false) per tab, and
    /// the tab in use selected.</summary>
    void RefreshTabButtons(bool keepSelection)
    {
        if (tabs.Count == 0) return;
        var bar = Host.Child("m_buttonBar");
        if (bar == null) return;
        ClearDataProvider(bar);
        int selected = 0;
        for (int i = 0; i < tabs.Count; i++)
        {
            Host.Invoke(Host.Entity, "AddTab", tabs[i].Name, ((ulong)tabs[i].Id).ToString(System.Globalization.CultureInfo.InvariantCulture), false);
            if (tabs[i].Id == tabId) selected = i;
        }
        if (!keepSelection) Host.Set(bar, "selectedIndex", selected);
    }

    /// <summary>ASButtonBar::ClearDataProvider: an empty data provider.</summary>
    void ClearDataProvider(object bar)
    {
        var dp = Host.RT.Construct(Host.RT.GetClass("scaleform.clik.data.DataProvider"), Array.Empty<object>());
        Host.Set(bar, "dataProvider", dp);
    }

    // ---- the tree -----------------------------------------------------------------------------------------------------

    /// <summary>RefreshSlots @0x140ecb300 (:737): unless kept, the slot bag rebuilt from the preview's merged skills (slot order =
    /// that order); the job, then the whole tree (setSkill) or an update (updateSkill); the slots pushed again (Invalidate).</summary>
    public void RefreshSlots(bool keep)
    {
        if (sim == null) return;
        if (!keep)
        {
            bag.Clear();
            bag.AddRange(sim.GetMerged(0));
            slotOrder.Clear();
            for (int i = 0; i < bag.Count; i++) slotOrder[bag[i].Id] = i;
        }
        RefreshJob();
        if (keep) RefreshTree();
        else RefreshAll();
        for (int i = 0; i < bag.Count; i++) Host.Push(SlotName(i));
    }

    // ---- status events (UIStatusEventDispatcher::NotifyStatusChanged, uistatuseventlistener.cpp:257) ----------------------

    /// <summary>vfunc_oa0_7 @0x140ec7750 (:873), event 4 (OnJob modes 0, 1, 2 and 7 of the local player,
    /// scenestategame.cpp:7463-7509): the window closes.</summary>
    public void OnJobChanged() => Close();

    /// <summary>vfunc_oa0_9 @0x140ec76d0 (:879), event 6 (OnLevelUp, scenestategame.cpp:9563): the shown tab's preview takes the
    /// level, then RefreshJob and RefreshTree.</summary>
    public void OnLevelChanged(int level)
    {
        if (sim == null) return;
        sim.SetLevel(level);
        RefreshJob();
        RefreshTree();
    }

    /// <summary>vfunc_oa0_28 @0x140ec77b0 (:889), event 0xf (OnSkillPoint, scenestategame.cpp:4241): the shown tab's preview takes
    /// the per-rank points (DAT_143ab32f8; the bonus m_n179c is not reapplied), then RefreshJob and RefreshTree.</summary>
    public void OnSkillPointsChanged()
    {
        if (sim == null) return;
        sim.SetExtraPoints(Points.ByRank);
        RefreshJob();
        RefreshTree();
    }

    /// <summary>vfunc_oa0_21 @0x140ec7830 (:899), event 0xe (the skill tier options changed, PC::vfunc_53 pc.cpp:4474): RefreshJob
    /// and RefreshTree. Nothing feeds the tier options yet, so nothing calls it.</summary>
    public void OnSkillTierChanged()
    {
        if (sim == null) return;
        RefreshJob();
        RefreshTree();
    }

    static string SlotName(int i) => BagName + "." + i.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>RefreshJob @0x140ecb540 (:662).</summary>
    void RefreshJob()
    {
        RefreshJobSkills(Jobs?.JobGroupId ?? 0);
        RefreshRemainPoint();
        RefreshPoints();
    }

    /// <summary>RefreshJobSkills @0x140ecb820 (:1527): with feature 0xca the tree body's "job0" / "job1" frame and its job
    /// symbol "_&lt;job&gt;", the sub job buttons (hidden for job 1) and their icons, SetSelectedSubJobLevel; else the job
    /// symbol. The tree line shows the job's frame (hidden on the rank 2 tree with 0xca).</summary>
    void RefreshJobSkills(int job)
    {
        string frame = "_" + job.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (JobChange01)
        {
            if (Host.Child("m_skillTreeBody") is { } body)
            {
                string j = JobLevel == 2 ? "job1" : "job0";
                Host.Invoke(body, "gotoAndStop", j);
                if (Host.Get(body, j) is AsObject jc && Host.Get(jc, "m_jobSymbol") is AsObject sym) Host.Invoke(sym, "gotoAndStop", frame);
            }
            for (int i = 1; i < 3; i++)
            {
                if (Host.Child(i == 1 ? "m_subJobBtn0" : "m_subJobBtn1") is not AsObject btn) continue;
                Host.SetVisible(btn, !(job == 1 && i > 0));
                if (Host.Get(btn, "m_jobIconOn") is AsObject on) Host.Invoke(on, "gotoAndStop", frame);
                if (Host.Get(btn, "m_jobIconOff") is AsObject off) Host.Invoke(off, "gotoAndStop", frame);
            }
            Host.Invoke("SetSelectedSubJobLevel", (uint)(JobLevel - 1));        // UpdateSubJobLevel (:656)
        }
        else if (Host.Child("m_jobSymbol") is { } sym) Host.Invoke(sym, "gotoAndStop", frame);
        if (Host.Child("m_treeLine") is { } line)
        {
            Host.SetVisible(line, true);
            if (JobChange01 && JobLevel == 2) Host.SetVisible(line, false);
            else Host.Invoke(line, "gotoAndStop", frame);
        }
    }

    /// <summary>MyPC::GetRemainingSkillPoints @0x14152e970 (mypc.cpp:3965): the rank's points (level, sources, bonus) less the
    /// book's used points, not below 0.</summary>
    public int RemainingSkillPoints(int rank) =>
        Math.Max(0, Points.RankTotal(rank) + JobSystem.GetLevelSkillPoint(Level, rank, Model.Tables.SkillPointLimitLevel1, Model.Tables.SkillPointLimitLevel2)
            + Points.Extra - (Jobs?.GetUsedPoint(rank) ?? 0));

    /// <summary>RefreshRemainPoint @0x140ecc030 (:1463): setRemainSkillPoint(the preview's remaining points, changed from the
    /// book's), and m_point's tooltip function (OnPointClick).</summary>
    void RefreshRemainPoint()
    {
        if (sim == null) return;
        int remain = sim.GetRemainPoints(JobLevel);
        Host.Invoke("setRemainSkillPoint", (uint)remain, RemainingSkillPoints(JobLevel) != remain);
        if (Host.Child("m_point") is AsObject point)
        {
            Host.Set(point, "externalTooltipFunction", new AsPlainFunction(Host.RT, new NativeMethod("OnPointClick", (_, _) => Host.ToAs(PointTooltip()))));
            // GetTooltipManager(2)->RegisterMouseEvent: the movie's TooltipManager (astooltipmanager.cpp:26)
            if (Host.RT.GetClass("scaleform.ms2.manager.TooltipManager") is { } tm) Host.Invoke(tm, "registerMouseEvent", point);
        }
    }

    /// <summary>GetJobSkillPoint @0x140ecb130 (:552): DAT_143ab32e8[source][m_nJobLevel].</summary>
    int GetJobSkillPoint(int source) => Points.Source(source, JobLevel);

    /// <summary>OnPointClick @0x140ec8720 (:1427): the point tooltip, total = level points + source points + bonus.</summary>
    public AsTooltip PointTooltip()
    {
        int lv1 = Model.Tables.SkillPointLimitLevel1, lv2 = Model.Tables.SkillPointLimitLevel2;
        uint baseP = (uint)JobSystem.GetLevelSkillPoint(Level, JobLevel, lv1, lv2);
        uint extra = (uint)Points.RankTotal(JobLevel);
        uint first = (uint)GetJobSkillPoint(1), second = (uint)GetJobSkillPoint(2);
        uint total = baseP + extra + (uint)Points.Extra;
        string text = JobChange01
            ? Common("s_msg_skillbook_skillpoint_tooltip_jobchange", total, baseP, second, first, (ushort)(JobLevel == 2 ? lv2 : lv1))
            : Common("s_msg_skillbook_skillpoint_tooltip", total, baseP, second, first);
        var t = new AsTooltip { ["tooltip"] = "export_common_tooltip_tooltipText" };
        t.SetTooltipAlign(4);
        t["text"] = text;
        return t;
    }

    /// <summary>RefreshPoints @0x140ecc2e0 (:1485): Save Skills enabled by a changed preview, Reset Skills by spent points, the
    /// use-tab / current-tab buttons by whether the shown tab is the one in use, auto distribute by the rank's points.</summary>
    void RefreshPoints()
    {
        if (Host.Child("m_applySkill") is { } apply) Host.Set(apply, "enabled", sim != null && sim.IsChanged());
        if (Host.Child("m_skillReset") is { } reset) Host.Set(reset, "enabled", HasPendingChanges());
        if (FindTab(shownTabId) is { } tab)
        {
            bool current = tab.Id == tabId;
            if (Host.Child("m_useCurrentTab") is { } use) Host.SetVisible(use, !current);
            if (Host.Child("m_currentTab") is { } cur) { Host.Set(cur, "enabled", false); Host.SetVisible(cur, current); }
        }
        if (Host.Child("m_autoDistribute") is { } auto) Host.Set(auto, "enabled", sim != null && sim.GetTotalPoints(JobLevel) > 0);
    }

    /// <summary>HasPendingChanges @0x140ecce40 (:599).</summary>
    bool HasPendingChanges()
    {
        if (sim == null) return false;
        if (shownTabId == tabId) return (Jobs?.TotalPoint ?? 0) > 0 || sim.GetSpentPoints(JobLevel) > 0;
        return sim.GetSpentPoints(JobLevel) > 0;
    }

    /// <summary>RefreshAll @0x140ecc8b0 (:1743): resetSkillTree, then setSkill for each skill of the shown rank (not extra, with
    /// skill data).</summary>
    void RefreshAll()
    {
        if (sim == null) return;
        Host.Invoke("resetSkillTree");
        foreach (var s in sim.EditSkills)
        {
            if (s.Extra || s.JobLevel != JobLevel || T.GetSkillData(s.Id) == null) continue;
            if (FillSkill(s) is { } info) Host.Invoke("setSkill", info);
        }
    }

    /// <summary>RefreshTree @0x140eccbb0 (:1773): updateSkill for each skill (not extra, with skill data).</summary>
    void RefreshTree()
    {
        if (sim == null) return;
        foreach (var s in sim.EditSkills)
        {
            if (s.Extra || T.GetSkillData(s.Id) == null) continue;
            if (FillSkill(s) is { } info) Host.Invoke("updateSkill", info);
        }
    }

    /// <summary>FillSkill @0x140ece300 (:1706).</summary>
    AsObject FillSkill(JobSkill s)
    {
        var d = T.GetSkillData(s.Id);
        if (d == null || s.Data == null) return null;
        bool learn = IsLearnable(s), passive = d.IsPassive, change = IsChangedSkill(s), disable = IsDisabled(s);
        bool highlight = !disable && s.Data.UiHighlight;
        int index = slotOrder.GetValueOrDefault(s.Id);
        return Host.Obj(("index", (uint)index), ("x", s.Data.UiX), ("y", s.Data.UiY), ("skillID", s.Id), ("passive", passive),
            ("newSkill", s.Changed), ("change", change), ("learn", learn), ("disable", disable), ("highlight", highlight),
            ("currentLevel", GetLevelText(s)), ("canPrevLevel", sim.CanLevelDown(s.Id) ? 1 : 0), ("canNextLevel", sim.CanLevelUp(s.Id) ? 1 : 0));
    }

    ushort Tier(uint id) => Jobs?.TierLevel(id) ?? 0;

    /// <summary>IsChangedSkill @0x140ecd7e0 (:1605): the preview's learnt flag or level differs from the book's.</summary>
    bool IsChangedSkill(JobSkill s) => Jobs?.FindSkill(s.Id) is { } cur && (cur.Learned != s.Learned || cur.Level != s.Level);

    /// <summary>IsLearnable @0x140ece040 (:1621): learnt, or a skill tree (tier) level.</summary>
    bool IsLearnable(JobSkill s) => s != null && (s.Learned || Tier(s.Id) > 0);

    /// <summary>IsDisabled @0x140ece160 (:1639): the level's requirements unmet, unless it has a tier level.</summary>
    bool IsDisabled(JobSkill s) => s == null || (!sim.CheckRequirements(s.Id, s.Level) && Tier(s.Id) == 0);

    /// <summary>GetLevelValue @0x140ecdd90 (:1657): learnable: min(max level, learnt level + tier); else the level.</summary>
    public ushort GetLevelValue(JobSkill s)
    {
        if (s == null) return 0;
        if (!IsLearnable(s)) return s.Level;
        var d = T.GetSkillData(s.Id);
        if (d == null) return 0;
        int owned = s.Learned ? s.Level : 0;
        return (ushort)Math.Min((int)d.MaxLevelRaw, owned + Tier(s.Id));
    }

    /// <summary>GetLevelText @0x140ecd960 (:1679): "level/max" (0 when not learnable) in the changed, tier or plain format.</summary>
    string GetLevelText(JobSkill s)
    {
        ushort level = GetLevelValue(s);
        if (!IsLearnable(s)) level = 0;
        var d = T.GetSkillData(s.Id);
        if (d == null) return "";
        ushort max = T.MaxLevel(d);
        if (IsChangedSkill(s)) return Common("s_msg_skillbook_job_and_level_change", level, max);
        if (Tier(s.Id) > 0) return Common("s_mag_skillbook_job_and_level_tier", level, max);
        return Common("s_mag_skillbook_job_and_level", level, max);
    }

    // ---- the slot bag -------------------------------------------------------------------------------------------------

    /// <summary>
    /// BookSlot::vfunc_8 @0x140a367a0 (bookslot.cpp:84): ASSlot(2).FUN_1409c4360(skill, 0x401) (asitemslot.cpp:1408): the skill
    /// data's icon path, upgradeCondition (CanUpgrade with the rank's remaining points), disable = not learnt, and a passive
    /// skill's disableDrag. Other names go to the base.
    /// </summary>
    public override object GetBinding(string name)
    {
        if (!name.StartsWith(BagName + ".", StringComparison.Ordinal) || !int.TryParse(name.AsSpan(BagName.Length + 1), out int i))
            return base.GetBinding(name);
        if (i < 0 || i >= bag.Count) return null;
        var s = bag[i];
        var d = T.GetSkillData(s.Id);
        if (d == null) return null;
        var slot = new List<(string, object)>
        {
            ("iconPath", d.Icon),
            ("upgradeCondition", CanUpgrade(s, RemainingSkillPoints(s.JobLevel))),
            ("disable", s.Learned ? 0 : 1),
        };
        if (d.IsPassive) slot.Add(("disableDrag", 1));
        return Host.Obj(slot.ToArray());
    }

    /// <summary>The book slot index of a name ("skillTreeSlot.&lt;i&gt;" inside the bag), else -1.</summary>
    int BookSlotIndex(string name) =>
        name != null && name.StartsWith(BagName + ".", StringComparison.Ordinal)
        && int.TryParse(name.AsSpan(BagName.Length + 1), System.Globalization.NumberStyles.None, null, out int i) && i < bag.Count ? i : -1;

    /// <summary>A BookSlot of the bag (DynamicCast&lt;DragSlot&gt; of its binding).</summary>
    public bool IsDragSlot(string name) => BookSlotIndex(name) >= 0;

    /// <summary>DragSlot::vfunc_10: the slot pushed again.</summary>
    public void RefreshSlot(string name) { if (IsDragSlot(name)) Host.Push(name); }

    /// <summary>BookSlot::vfunc_18 @0x140a36730 (bookslot.cpp:69): the source and the slot pushed again, nothing else.</summary>
    public void SlotDroppedOn(string name, string source, IDragSlotOwner sourceOwner)
    {
        sourceOwner?.RefreshSlot(source);
        RefreshSlot(name);
    }

    /// <summary>BookSlot keeps DragSlot's vfunc_12 and vfunc_17 (dragslot.cpp:51, :95: vfunc_17 is vfunc_12), so either branch
    /// of the open FUN_1422aeed0(2) pick pushes the slot again.</summary>
    public void SlotDropUnresolved(string name) => RefreshSlot(name);

    /// <summary>BookSlot::FUN_140a366a0 @0x140a366a0 (bookslot.cpp:54): the slot's skill id (JobSkill::GetID), 0 for a name
    /// that is not a book slot.</summary>
    public uint BookSkillId(string name) => BookSlotIndex(name) is var i and >= 0 ? bag[i].Id : 0;

    public override IEnumerable<string> GetBagSlotBindings(string bag) =>
        bag == BagName ? Enumerable.Range(0, this.bag.Count).Select(SlotName) : Array.Empty<string>();

    /// <summary>CanUpgrade @0x140ecf630 (:1801): not learnt: CanLearn; else CanLevelUp. The check argument is the rank's
    /// remaining points (0 = no).</summary>
    public bool CanUpgrade(JobSkill s, int check)
    {
        if (s == null) return false;
        if (!s.Learned) return CanLearn(s.Id, s.Level, check);
        return CanLevelUp(s.Id, s.Level, check);
    }

    /// <summary>CanLevelUp @0x140ecf740 (:1813).</summary>
    bool CanLevelUp(uint id, ushort level, int check)
    {
        if (check == 0) return false;
        var d = T.GetSkillData(id);
        if (d == null || T.MaxLevel(d) <= level) return false;
        return CanReachLevel(id, (ushort)(level + 1), check);
    }

    /// <summary>CanLearn @0x140ecf840 (:1829).</summary>
    bool CanLearn(uint id, ushort level, int check)
    {
        if (level > 1 || Jobs == null) return false;
        if (Jobs.IsLearned(id)) return false;
        return CanReachLevel(id, level, check);
    }

    /// <summary>CanReachLevel @0x140ecf8d0 (:1842): against the player's book (not the preview): the level the skill level
    /// needs, a rank the player has, the required skills learnt at their levels, the required quest rewarded.</summary>
    bool CanReachLevel(uint id, ushort level, int check)
    {
        if (check == 0 || Jobs?.Job is not { } job) return false;
        var data = job.FindSkill(id);
        if (data == null) return true;
        if (data.GetRequireLevel(level) > Level) return false;
        if (Jobs.FindSkill(id) is not { } s) return false;
        if (s.JobLevel > JobTable.GetJobRank(Jobs.SubJob)) return false;
        for (int i = 0; i < data.RequireSkillIds.Count; i++)
        {
            if (!Jobs.IsLearned(data.RequireSkillIds[i])) return false;
            if (Jobs.SkillLevel(data.RequireSkillIds[i]) < data.RequireSkillLevel(i)) return false;
        }
        return data.RequireQuestId == 0 || IsQuestRewarded(data.RequireQuestId);
    }

    // ---- vfunc_45 / vfunc_28 ------------------------------------------------------------------------------------------

    /// <summary>vfunc_45 @0x140ec5820 (:2046): the skill tooltip at the shown level; the next level's (when below the max, one
    /// up when learnable) and the previous level's (when it can go down), both aligned 7 with an empty enablePuton.</summary>
    public override IDictionary<string, object> GetUserData(string name, object[] args)
    {
        if (sim == null || SkillTooltip == null) return base.GetUserData(name, args);
        uint id = args.Length > 0 ? (uint)Rt.ToNumber(args[0]) : 0;
        if (string.Equals(name, "getTooltipFromSkillID", StringComparison.OrdinalIgnoreCase))
            return sim.GetEditSkill(id) is { } s ? SkillTooltip(this, s.Id, GetLevelValue(s)) : null;
        if (string.Equals(name, "getTooltipFromNextLevel", StringComparison.OrdinalIgnoreCase))
        {
            if (sim.GetEditSkill(id) is not { } s || T.GetSkillData(id) is not { } d) return null;
            ushort level = GetLevelValue(s);
            if (!(T.MaxLevel(d) > level)) return null;
            int next = IsLearnable(s) ? level + 1 : level;
            return Shifted(SkillTooltip(this, s.Id, next));
        }
        if (string.Equals(name, "getTooltipFromPrevLevel", StringComparison.OrdinalIgnoreCase))
        {
            if (!sim.CanLevelDown(id) || sim.GetEditSkill(id) is not { } s || s.Level == 1) return null;
            return Shifted(SkillTooltip(this, s.Id, s.Level - 1));
        }
        return base.GetUserData(name, args);
    }

    static AsTooltip Shifted(AsTooltip t)
    {
        if (t == null) return null;
        t.SetTooltipAlign(7);
        t["enablePuton"] = "";
        return t;
    }

    /// <summary>vfunc_28 @0x140ec5e20 (:2109): only after the tab packet (m_n290). The preview's +/- (SkillPointUp / Down), the
    /// required skill marks and a tab switch are local; the rest send requests and are raised unsent.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (!ready) return false;
        switch (name)
        {
            case "onSkillPointUp": if (WaitServer) return false; SkillPointUp((uint)Rt.ToNumber(args.ElementAtOrDefault(0))); return true;
            case "onSkillPointDown": if (WaitServer) return false; SkillPointDown((uint)Rt.ToNumber(args.ElementAtOrDefault(0))); return true;
            case "onRequireSkillEffect":
                // ASData pops in call order: the flag, then the skill id
                RequireSkillEffect((uint)Rt.ToNumber(args.ElementAtOrDefault(1)), Rt.ToBoolean(args.ElementAtOrDefault(0)));
                return true;
            case "onChangeTab":
                if (ulong.TryParse(Rt.ToString(args.ElementAtOrDefault(0)), out var tab))      // ParseUInt64W
                {
                    SelectTab((long)tab);
                    RefreshSlots(true);
                }
                return false;
            case "onApplySkill":
                if (WaitServer) return false;
                ApplySkill();
                return true;
            case "onResetSkill": case "onUseTab": case "onAutoDistribute": case "OnRegisterShortcutkey":
            case "onRenameTab": case "onAddTab": case "onChangeSubJob": case "onRemoveTab": case "onLapenshardBtn":
                Button?.Invoke(name);
                return true;
        }
        return false;
    }

    /// <summary>CanApply @0x140ecd4b0 (:612): not in battle and not in a field that limits the skill set; each refusal shows its box.</summary>
    bool CanApply()
    {
        if (InBattle()) { ShowBox(MessageBoxQueue.Create(0, Common("s_msg_skillbook_set_disable_battle"), null, "")); return false; }
        if (FieldLimitsSkillSet()) { ShowBox(MessageBoxQueue.Create(0, Common("s_msg_skillbook_set_disable_field"), null, "")); return false; }
        return true;
    }

    /// <summary>The apply branch of vfunc_28 (:2118): CanApply, then (CheckApply @0x140ecd6d0 :634, the platform state, which is
    /// 0 here; STILL-OPEN: its notice box) with feature 0xca and a sub job of rank above 1 the rank box (OnApplyConfirm answers
    /// it), otherwise RequestTree, RequestJob for every rank and the notice s_msg_skillbook_apply_skill.</summary>
    void ApplySkill()
    {
        if (!CanApply()) return;
        if (Feature(0xca) && Jobs != null)
        {
            int rank = JobTable.GetJobRank(Jobs.SubJob);
            if (rank > 1)
            {
                var box = MessageBoxQueue.CreateWithCallback(Common("s_msg_skillbook_apply_skill_joblevel"), 0x19, OnApplyConfirm);
                box.SetButtonPreset(1);
                box.MustCheck = true;
                box.SetCheckbox0(Common("s_msg_skillbook_skill_level_0"), JobLevel == 1);
                box.SetCheckbox1(Common("s_msg_skillbook_skill_level_1"), JobLevel == 2);
                box.Append(rank);
                ShowBox(box);
                return;
            }
        }
        SaveTree(shownTabId, 0, 0);
        RequestJob(false, false, 0);
        ChatLine?.Invoke(Common("s_msg_skillbook_apply_skill"), 0x14);
    }

    /// <summary>OnApplyConfirm @0x140ece840 (:992): button 0 reads the rank from the two checkboxes (both: 0, the first: 1, the
    /// second: 2; none: 0) when the box carries a sub job rank above 1, then RequestTree, RequestJob and the notice. The layer
    /// takes the next box after the callback.</summary>
    bool OnApplyConfirm(int result, MessageBoxData box)
    {
        if (result != 0) return true;
        int rank = 0;
        if (box.ArgInt(0, 0) > 1)
        {
            bool first = CheckboxChecked(box, 0), second = CheckboxChecked(box, 1);
            if (first && second) rank = 0;
            else if (first) rank = 1;
            else if (second) rank = 2;
        }
        SaveTree(shownTabId, 0, rank);
        RequestJob(false, false, rank);
        ChatLine?.Invoke(Common("s_msg_skillbook_apply_skill"), 0x14);
        return true;
    }

    /// <summary>RequestTree @0x140eca8c0 (:1122), mode 1: with <paramref name="keep"/> 0 the tab (all with id 0) takes the used
    /// points of its preview for the rank; then the tabs are saved, the tab in use first.</summary>
    void SaveTree(long id, int keep, int rank)
    {
        if (keep == 0)
        {
            foreach (var t in tabs)
            {
                if (id != 0 && t.Id != id) continue;
                if (!simulators.TryGetValue(t.Id, out var s)) continue;
                t.Slots.Clear();
                foreach (var (skill, pts) in s.GetUsedPoints(rank)) t.Slots[skill] = (uint)pts;
            }
        }
        Send?.Invoke(new SkillBookTreeSave(tabId, id, rank, tabs.Select(t =>
            new SkillTreeTab(t.Id, t.Name, t.Slots.Select(kv => ((int)kv.Key, (int)kv.Value)).ToList())).ToList()));
    }

    /// <summary>RequestJob @0x140ec9200 (:1180): the preview must validate; a tab not in use is only saved (RequestTree with the
    /// arguments as the client passes them); otherwise Job mode 9 (0xb confirmed) carries the merged (all: the snapshot) skills,
    /// the wait starts, the changed skills play their effect, the preview commits and the fixed sound plays.</summary>
    void RequestJob(bool confirm, bool all, int rank)
    {
        if (sim == null || !CanApply()) return;
        if (!sim.Validate(out _)) return;
        if (shownTabId != tabId)
        {
            SaveTree(shownTabId, rank, 0);
            return;
        }
        var skills = all ? sim.GetSnapshot(0) : sim.GetMerged(rank);
        Send?.Invoke(new JobSkillsRequest(confirm ? 0xb : 9, skills.Select(k => ((int)k.Id, (int)k.Level, k.Learned)).ToList()));
        WaitServer = true;
        var applied = new List<JobSkill>();
        if (!all)
        {
            applied = sim.GetChanged(rank);
            sim.Commit(rank);
        }
        foreach (var k in applied) Host.Invoke("playChangeEffect", k.Id);
        Sound?.Invoke("System_Skillbook_Skill_Fixed_01");
    }

    /// <summary>RefreshSlotsIfShown @0x140ec8220 (:699), called by OnJob modes 9, 10 and 11 of the local player
    /// (scenestategame.cpp:7513) after the job system took the packet's book: an open window refreshes its slots and the wait ends.</summary>
    public void RefreshSlotsIfShown()
    {
        if (!ready || sim == null) return;
        RefreshSlots(true);
        WaitServer = false;
    }

    /// <summary>SkillPointUp @0x140ecaa30 (:1355): the preview one level up, the tree updated, the change effect, the job part.</summary>
    void SkillPointUp(uint id)
    {
        if (sim == null) return;
        sim.LevelUp(id);
        RefreshTree();
        Host.Invoke("playChangeEffect", id);
        RefreshJob();
    }

    /// <summary>SkillPointDown @0x140ecaab0 (:1366): the preview one level down; when a learnt skill needs it, the notice
    /// s_msg_skillbook_require_learn_skill (raised as "requireLearnSkill:&lt;id&gt;").</summary>
    void SkillPointDown(uint id)
    {
        if (sim == null) return;
        if (!sim.LevelDown(id, out uint bad)) { Button?.Invoke("requireLearnSkill:" + bad); return; }
        RefreshTree();
        Host.Invoke("playChangeEffect", id);
        RefreshJob();
    }

    /// <summary>RequireSkillEffect @0x140ecac10 (:1384): for a skill not learnt that cannot level up, each required skill not
    /// at its level gets requireSkillEffect(id, flag, s_mag_skillbook_skillslot_require_level).</summary>
    void RequireSkillEffect(uint id, bool flag)
    {
        if (sim?.GetEditSkill(id) is not { } s || s.Learned || sim.CanLevelUp(id)) return;
        if (Jobs?.Job?.FindSkill(s.Id) is not { } data) return;
        if (T.Skills.GetSkillLevelData(s.Id, s.Level) == null) return;
        for (int i = 0; i < data.RequireSkillIds.Count; i++)
        {
            if (sim.GetEditSkill(data.RequireSkillIds[i]) is not { } req) continue;
            ushort need = data.RequireSkillLevel(i);
            if (!req.Learned || req.Level < need)
                Host.Invoke("requireSkillEffect", req.Id, flag, Common("s_mag_skillbook_skillslot_require_level", need));
        }
    }
}
