using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;

namespace Ms2.Ui.Hud;

/// <summary>
/// UISkillCompactControlDialog (client/ms2/uiskillcompactcontroldialog.cpp; UI id 0x4ef6, vfunc_48 @0x140fa35c0), the C++ side
/// of the compact skill control movie uiskillcompactcontrol (GetTypeName @0x140f9e330). Up to three named tabs, each with a
/// set of ticked skills of the job (CSkillCompactControlTable) and a hot key bound to the tab's key action 0xaae61..0xaae63.
/// The tabs come from the server (0xD1 mode 0, asked for at load) and go back on close (0x7B mode 1).
/// <para>The skill system side: FUN_141767df0 (the hot key capture flag of CSkillSystem) is raised as
/// <see cref="HotKeyCapture"/>, FUN_140fa1400 (CSkillSystem::SetCompactConfigs) as <see cref="ConfigsChanged"/>; the compact
/// auto cast that takes them is <see cref="Skills.CompactCast"/>.</para>
/// <para>STILL-OPEN: the capture's combo prefix keys (DAT_143acbd08), the
/// platform check of a typed tab name (IClientPlatform::vfunc_37, taken as accepted), the gamepad key set (FUN_1414319f0,
/// taken as off) and the hot key's text colour (FUN_140d01340) are not ported.</para>
/// </summary>
public sealed class SkillCompactControlController : UiController, IMessageBoxListener
{
    public override string Movie => "uiskillcompactcontrol";
    protected override string EntityMethod => "buildSkills";
    /// <summary>GetTypeName @0x140f9e330.</summary>
    public override string TypeName => "SkillCompactControl";

    /// <summary>The window's key action: CKeyTable::FUN_14142b300 case 400031 runs ToggleDialog(0x4ef6) (keytable.cpp:881). The
    /// default key table binds no key to it, so only a key the player set opens the window this way.</summary>
    public const long OpenKeyAction = 400031;

    /// <summary>True when DirectInput key <paramref name="dik"/> is bound to <see cref="OpenKeyAction"/> in <paramref name="keys"/>.</summary>
    public static bool OpensWithKey(KeyTable keys, int dik)
    {
        foreach (int k in keys.KeysOfAction(OpenKeyAction))
            if (k == dik) return true;
        return false;
    }

    /// <summary>The highest tab count (onAddTab / OnAddTabResult test m_lstConfig.GetCount() against 3).</summary>
    public const int MaxTabs = 3;
    /// <summary>The tab hot key actions GetTabKeyId hands out (0xaae61..0xaae63, string table 5 "{0}" names).</summary>
    public const long FirstTabKeyAction = 0xaae61, LastTabKeyAction = 0xaae63;

    /// <summary>The local player's job system (PC::GetJobSystem).</summary>
    public JobSystem Jobs;
    /// <summary>CKeyTable::FUN_1414319f0: 2 with the gamepad option on, else 0. STILL-OPEN: no gamepad here, so 0.</summary>
    public int KeySet;
    /// <summary>ASTooltip(player, 0x10, 2) + FUN_1409e1b80(skill, 0): the skill's tooltip at its learnt level (1 unknown).</summary>
    public Func<uint, int, AsTooltip> SkillTooltip;
    /// <summary>FUN_14151f150, the shown boxes (tests read them here).</summary>
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;

    /// <summary>SendOp::SkillMacro mode 0 (OnLoadComplete @0x140fa06a0): the host sends it live or answers with a fixture.</summary>
    public event Action RequestTabs;
    /// <summary>SendSkillMacroMode1 @0x140fa1240 (the close): every tab, for SendOp::SkillMacro mode 1.</summary>
    public event Action<IReadOnlyList<SkillMacroTab>> Save;
    /// <summary>FUN_140fa1400: CSkillSystem::SetCompactConfigs(m_lstConfig).</summary>
    public event Action<IReadOnlyList<SkillMacroTab>> ConfigsChanged;
    /// <summary>CSkillSystem::FUN_141767df0(on): the hot key field takes keys (1) or not (0).</summary>
    public event Action<bool> HotKeyCapture;

    /// <summary>CSkillCompactControlConfig: the name, m_30 (the key action, 0 for none) and m_skills (a std::set).</summary>
    public sealed class Tab
    {
        public string Name;
        public long KeyAction;
        public readonly SortedSet<uint> Skills = new();
        public Tab(string name) { Name = name; }
    }

    /// <summary>m_cur / m_prev: the hot key edit (m_nState 1 = a "replace the binding?" box is up, m_nKey, m_strTab).</summary>
    struct KeyEdit
    {
        public int State, Key;
        public string Tab;
        public readonly bool HasTab => !string.IsNullOrEmpty(Tab);
        public void Clear() { State = 0; Key = 0; Tab = ""; }
    }

    readonly List<Tab> tabs = new();        // m_lstConfig
    string current = "";                    // m_strTab
    string renaming = "";                   // m_strRename
    KeyEdit cur, prev;                      // m_cur, m_prev
    bool loaded;                            // m_bLoaded

    static readonly List<AsObject> NoRows = new();

    public IReadOnlyList<Tab> Tabs => tabs;
    public string CurrentTab => current;
    public bool Loaded => loaded;
    /// <summary>m_cur.HasTab and m_nState 0: vfunc_13 reads a key for the hot key (the host forwards key presses then).</summary>
    public bool CapturingKey => loaded && cur.HasTab && cur.State == 0;

    KeyTable Keys => Model.Keys;
    SkillCompactControlTable Table => Model.Tables.SkillCompactControl;

    // ---- load / close ---------------------------------------------------------------------------------------------------

    /// <summary>OnLoadComplete @0x140fa06a0: UIDialog's, m_buttonBar / m_addButton, then SendOp::SkillMacro mode 0.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        RequestTabs?.Invoke();
    }

    /// <summary>vfunc_67 @0x140f9e3c0: UIDialog's close, the capture off, SetCompactConfigs, SendSkillMacroMode1, unloaded.</summary>
    public override void Close()
    {
        base.Close();
        HotKeyCapture?.Invoke(false);
        PushConfigs();
        Save?.Invoke(Snapshot());
        loaded = false;
    }

    /// <summary>vfunc_65 @0x140f9e380: m_cur.Clear().</summary>
    public void ClearKeyEdit() => cur.Clear();

    /// <summary>The tabs as the packets carry them.</summary>
    public IReadOnlyList<SkillMacroTab> Snapshot()
    {
        var l = new SkillMacroTab[tabs.Count];
        for (int i = 0; i < tabs.Count; i++)
        {
            var t = tabs[i];
            var s = new int[t.Skills.Count];
            int j = 0;
            foreach (uint id in t.Skills) s[j++] = (int)id;
            l[i] = new SkillMacroTab(t.Name, t.KeyAction, s);
        }
        return l;
    }

    /// <summary>FUN_140fa1400: SetCompactConfigs(m_lstConfig).</summary>
    void PushConfigs() => ConfigsChanged?.Invoke(Snapshot());

    // ---- vfunc_79: 0xD1 -------------------------------------------------------------------------------------------------

    /// <summary>
    /// vfunc_79 @0x140f9fee0, opcode 0xD1 mode 0: the tabs replaced by the packet's (the first name kept); none gives one tab
    /// named common string 0xc39 with every job skill; the tab bar refilled (FUN_140fa1ff0), the first tab selected, its skill
    /// list and hot key shown, and the callbacks taken from now on (m_bLoaded).
    /// </summary>
    public void OnSkillMacro(SkillMacroPacket p)
    {
        if (p.Mode != 0) return;
        tabs.Clear();
        string first = "";
        foreach (var t in p.Tabs)
        {
            var tab = new Tab(t.Name) { KeyAction = t.KeyAction };
            foreach (int s in t.Skills) tab.Skills.Add((uint)s);
            tabs.Add(tab);
            if (first.Length == 0) first = t.Name;
        }
        if (tabs.Count == 0)
        {
            first = MessageBoxData.Common(0xc39);
            CreateTab(first);
        }
        RefillTabBar();
        current = first;
        Host.Invoke("selectTab", (object)current);
        BuildSkillList(first);
        ShowHotKey(first);
        loaded = true;
    }

    /// <summary>FUN_140fa1ff0: m_buttonBar's data provider emptied, then AddTab(name, 0) for the first tab and (name, 1) after
    /// (the movie's second argument makes a tab closable).</summary>
    void RefillTabBar()
    {
        if (tabs.Count == 0) return;
        if (Host.Child("m_buttonBar") is not { } bar) return;
        Host.Set(bar, "dataProvider", Host.RT.Construct(Host.RT.GetClass("scaleform.clik.data.DataProvider"), Array.Empty<object>()));
        int index = 0;
        foreach (var t in tabs)
        {
            Host.Invoke("AddTab", (object)t.Name, index);
            if (index == 0) index = 1;
        }
    }

    // ---- the tabs -------------------------------------------------------------------------------------------------------

    /// <summary>FindTab @0x140fa28b0: the first tab of the name, case-insensitive (CompareNoCase).</summary>
    public Tab FindTab(string name)
    {
        foreach (var t in tabs)
            if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) return t;
        return null;
    }

    /// <summary>CreateTab @0x140fa2720: a new tab at the end with every job skill ticked (GetJobSkills).</summary>
    Tab CreateTab(string name)
    {
        var t = new Tab(name);
        foreach (uint id in GetJobSkills()) t.Skills.Add(id);
        tabs.Add(t);
        return t;
    }

    /// <summary>RemoveTab @0x140fa29f0: the first tab of the name.</summary>
    void RemoveTab(string name)
    {
        if (FindTab(name) is { } t) tabs.Remove(t);
    }

    /// <summary>GetJobSkills @0x140fa1460: the job group's table skills, learnt ones (JobSystemCli::vfunc_2) ascending, then
    /// the rest ascending.</summary>
    public List<uint> GetJobSkills()
    {
        var list = new List<uint>();
        if (Jobs == null || Table.GetJobSkills(Jobs.JobGroupId) is not { } skills) return list;
        var learned = new SortedSet<uint>();
        var other = new SortedSet<uint>();
        foreach (var s in skills) (Jobs.IsLearned(s.SkillId) ? learned : other).Add(s.SkillId);
        list.AddRange(learned);
        list.AddRange(other);
        return list;
    }

    /// <summary>
    /// BuildSkillList @0x140fa16f0: the tab (made when missing); per table skill of the job that the job system has and whose
    /// rank is not above the sub job's, learnt ones first then the rest, each ascending, as {disable, skillID, iconPath,
    /// selected} (a skill without a skill table row is left out); buildSkills, then selectTab.
    /// </summary>
    public void BuildSkillList(string name)
    {
        var tab = FindTab(name) ?? CreateTab(name);
        if (Jobs == null) return;
        if (Table.GetJobSkills(Jobs.JobGroupId) is not { } skills) return;
        int rank = JobTable.GetJobRank(Jobs.SubJob);
        var learned = new SortedSet<uint>();
        var other = new SortedSet<uint>();
        foreach (var s in skills)
        {
            if (Jobs.FindSkill(s.SkillId) is not { } js) continue;
            if (js.JobLevel > rank) continue;
            (Jobs.IsLearned(s.SkillId) ? learned : other).Add(s.SkillId);
        }
        var rows = new List<AsObject>(learned.Count + other.Count);
        AddRows(rows, learned, 0, tab);
        AddRows(rows, other, 1, tab);
        Host.Invoke("buildSkills", rows.Count == 0 ? NoRows : rows);
        Host.Invoke("selectTab", (object)name);
    }

    void AddRows(List<AsObject> rows, SortedSet<uint> ids, int disable, Tab tab)
    {
        foreach (uint id in ids)
        {
            if (Model.Tables.Skills.GetSkillData(id) is not { } d) continue;
            rows.Add(Host.Obj(("disable", disable), ("skillID", id), ("iconPath", d.Icon), ("selected", tab.Skills.Contains(id) ? 1 : 0)));
        }
    }

    // ---- the hot key ----------------------------------------------------------------------------------------------------

    /// <summary>GetTabKeyId @0x140fa2520: 0 for no tab; the tab's m_30; else the first of 0xaae61..0xaae63 no tab holds; else 0.</summary>
    public long GetTabKeyId(string name)
    {
        var tab = FindTab(name);
        if (tab == null) return 0;
        if (tab.KeyAction != 0) return tab.KeyAction;
        for (long id = FirstTabKeyAction; id <= LastTabKeyAction; id++)
        {
            bool used = false;
            foreach (var t in tabs)
                if (t.KeyAction == id) { used = true; break; }
            if (!used) return id;
        }
        return 0;
    }

    /// <summary>IsKeyUnbound @0x140fa23a0: nothing is bound to the key.</summary>
    bool IsKeyUnbound(int key) => Keys.GetKeyAction(key) == 0;

    /// <summary>BindKeyToTab @0x140fa23f0: SetKeyBindingAndSync(key, 2, id, 0, set); on success m_cur's tab (not the key's)
    /// takes m_30 = id (none: 0) and SetCompactConfigs runs. (FUN_14142a8b0, the binding name refresh, has no reader here.)</summary>
    bool BindKeyToTab(int key, long id)
    {
        if (!Keys.SetKeyBindingAndSync(key, 2, id, 0, KeySet)) return false;
        if (FindTab(cur.Tab) is not { } tab) return false;
        tab.KeyAction = id;
        PushConfigs();
        return true;
    }

    /// <summary>FUN_140fa2180: m_hotKey's label = the name of the key bound to the tab's action, unfocused.</summary>
    void ShowHotKey(string name)
    {
        int key = Keys.FindKeyByActionAndPriority(GetTabKeyId(name), KeySet);
        if (Host.Child("m_hotKey") is not { } hot) return;
        Host.Set(hot, "label", Keys.GetKeyName(key));       // FUN_1422cdfb0
        Host.Set(hot, "focused", 0);                         // SetFocused(0)
    }

    /// <summary>
    /// vfunc_13 @0x140f9e700 once FindActiveKey gave <paramref name="key"/> while m_cur has a tab and no box (the combo prefix
    /// part is STILL-OPEN): a key of the other key set gets box 0x854 / 0x855; a fixed key (IsKeyRebindable) box 0x84c and
    /// the edit dropped; a free key is bound to the tab; a used one asks with box 0x84f (the action's name, a quick slot's
    /// with its key) unless the action is in key setting category 12, which is replaced at once. True = taken.
    /// </summary>
    public bool OnKey(int key)
    {
        if (!CapturingKey || key == 0) return false;
        if (KeySet == 2 ? key < 0x91 : key > 0x91)
        {
            ShowBox(MessageBoxQueue.Create(0, MessageBoxData.Common(KeySet == 2 ? 0x855 : 0x854), this, ""));
            return false;
        }
        if (!KeyTable.IsKeyRebindable(key))
        {
            cur.Clear();
            ShowBox(MessageBoxQueue.Create(0, MessageBoxData.Common(0x84c), null, ""));     // FUN_140d73bc0
            return true;
        }
        if (IsKeyUnbound(key))
        {
            if (BindKeyToTab(key, GetTabKeyId(cur.Tab)))
            {
                ShowHotKey(cur.Tab);
                cur.Clear();
            }
            return true;
        }
        cur.State = 1;
        cur.Key = key;
        long action = Keys.GetKeyAction(key);
        string name = KeyTable.IsQuickSlotAction(action)
            ? Ms2.Quest.ClientFormat.Format("{0}[{1}]", Keys.ActionName(action), Keys.GetKeyName(key))
            : Keys.ActionName(action);
        if (!Keys.IsListedInCategory12(action))
        {
            ShowBox(MessageBoxQueue.Create(1, Ms2.Quest.ClientFormat.Format(MessageBoxData.Common(0x84f), name), this, ""));
            return true;
        }
        if (BindKeyToTab(key, GetTabKeyId(cur.Tab)))
        {
            ShowHotKey(cur.Tab);
            cur.Clear();
        }
        HotKeyCapture?.Invoke(false);
        return true;
    }

    /// <summary>OnMessage @0x140f9e430 (every box with this listener): the box closed (FUN_14151f200); OK binds m_cur's key to
    /// the edited tab, anything else gives m_prev's key back to m_prev's tab; the hot key shown, the edit cleared, the
    /// capture off.</summary>
    public bool OnMessageBox(MessageBoxController sender, int button, string tag)
    {
        MessageBoxQueue.Next();
        if (button == 0) BindKeyToTab(cur.Key, GetTabKeyId(cur.Tab));
        else BindKeyToTab(prev.Key, GetTabKeyId(prev.Tab));
        ShowHotKey(cur.Tab);
        cur.Clear();
        HotKeyCapture?.Invoke(false);
        return true;
    }

    // ---- vfunc_28 -------------------------------------------------------------------------------------------------------

    /// <summary>vfunc_28 @0x140f9ef80: only once the tabs arrived (m_bLoaded). Every call returns 0 to the movie.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (!loaded) return false;
        switch (name.ToLowerInvariant())
        {
            case "onchangetab":
                if (args.Length > 0)
                {
                    current = Rt.ToString(args[0]) ?? "";
                    BuildSkillList(current);
                    ShowHotKey(current);
                }
                break;
            case "onclickcompactcontrolcheckbox":
                if (args.Length > 0)
                {
                    // ASData pops in call order: the skill id, then the flag
                    uint id = (uint)(int)Rt.ToNumber(args[0]);
                    bool on = Rt.ToBoolean(args.ElementAtOrDefault(1));
                    if (FindTab(current) is { } tab)
                    {
                        if (on) tab.Skills.Add(id);
                        else tab.Skills.Remove(id);
                    }
                }
                PushConfigs();
                break;
            case "onremovetab":
                if (args.Length > 0)
                {
                    string tabName = Rt.ToString(args[0]) ?? "";
                    BindKeyToTab(Keys.FindKeyByActionAndPriority(GetTabKeyId(tabName), 0), 0);
                    RemoveTab(tabName);
                }
                PushConfigs();
                break;
            case "onhotkeyfocusin":
            {
                cur.Clear();
                cur.Tab = current;
                int key = Keys.FindKeyByActionAndPriority(GetTabKeyId(current), KeySet);
                prev.Clear();
                prev.Key = key;
                prev.Tab = current;
                BindKeyToTab(key, 0);
                HotKeyCapture?.Invoke(true);
                break;
            }
            case "onhotkeyfocusout":
                if (cur.State == 0)
                {
                    cur.Clear();
                    HotKeyCapture?.Invoke(false);
                }
                break;
            case "onrenametab":
                if (args.Length > 0)
                {
                    renaming = Rt.ToString(args[0]) ?? "";
                    ShowNameBox(0xc37, renaming, OnRenameTabResult);
                }
                break;
            case "onaddtab":
                if (tabs.Count < MaxTabs) ShowNameBox(0xc36, "", OnAddTabResult);
                break;
            case "onresethotkey":
            {
                if (FindTab(current) is { } tab)
                {
                    tab.Skills.Clear();
                    BuildSkillList(current);
                }
                cur.Clear();
                cur.Tab = current;
                BindKeyToTab(Keys.FindKeyByActionAndPriority(GetTabKeyId(current), 0), 0);
                ShowHotKey(current);
                PushConfigs();
                break;
            }
            case "onselectall":
                if (FindTab(current) is { } all)
                {
                    all.Skills.Clear();
                    foreach (uint id in GetJobSkills()) all.Skills.Add(id);
                    BuildSkillList(current);
                }
                PushConfigs();
                break;
        }
        return false;
    }

    /// <summary>FUN_140d73e40(10, id): an input box of common string <paramref name="id"/>, SetInput(0, text, 5, 0, 0), the
    /// callback, shown (FUN_140d73c60).</summary>
    void ShowNameBox(int id, string text, Func<int, MessageBoxData, bool> answer)
    {
        var box = MessageBoxQueue.CreateWithCallback(MessageBoxData.Common(id), 10, answer);
        box.SetInput(0, text, 5, 0, 0);
        ShowBox(box);
    }

    /// <summary>The typed text: the box's answer after its first ',' (FUN_141a02d50, then Mid(Find(',') + 1)).</summary>
    static string TypedText(MessageBoxData d)
    {
        string t = d.ClickedTag ?? "";
        return t[(t.IndexOf(',') + 1)..];
    }

    /// <summary>OnRenameTabResult @0x140fa0820: OK with a name renames (the platform check taken as passed); an empty name
    /// keeps the box.</summary>
    bool OnRenameTabResult(int result, MessageBoxData d)
    {
        if (result != 0) return true;
        string text = TypedText(d);
        if (text.Length == 0) return false;
        return RenameTab(text);
    }

    /// <summary>RenameTab @0x140fa0a20: a name in use gets the box's error line (common 0xc38) and keeps it; else the box
    /// closes, the tab of m_strRename takes the name, the movie's renameTab, and the list built for it.</summary>
    bool RenameTab(string name)
    {
        if (name.Length == 0) return false;
        if (FindTab(name) != null)
        {
            MessageBoxQueue.Layer?.UpdateErrorMessage(MessageBoxData.Common(0xc38));
            return false;
        }
        MessageBoxQueue.Next();
        if (FindTab(renaming) is not { } tab) return false;
        tab.Name = name;
        Host.Invoke("renameTab", (object)renaming, name);
        BuildSkillList(name);
        return true;
    }

    /// <summary>OnAddTabResult @0x140fa0d80: OK below three tabs with a name adds it; an empty name keeps the box.</summary>
    bool OnAddTabResult(int result, MessageBoxData d)
    {
        if (result != 0) return true;
        if (tabs.Count >= MaxTabs) return false;
        string text = TypedText(d);
        if (text.Length == 0) return false;
        return AddTab(text);
    }

    /// <summary>AddTab @0x140fa0fa0: a name in use gets error 0xc38; else the box closes, the tab is made current with every
    /// job skill, its list built, and the movie's AddTab(name, 1).</summary>
    bool AddTab(string name)
    {
        if (name.Length == 0) return false;
        if (FindTab(name) != null)
        {
            MessageBoxQueue.Layer?.UpdateErrorMessage(MessageBoxData.Common(0xc38));
            return false;
        }
        MessageBoxQueue.Next();
        current = name;
        CreateTab(name);
        BuildSkillList(name);
        Host.Invoke("AddTab", (object)name, 1);
        return true;
    }

    // ---- vfunc_49 -------------------------------------------------------------------------------------------------------

    /// <summary>vfunc_49 @0x140f9fdb0: "compactSkill" (SkillCompactControlRenderer's getTooltip) gives the skill's tooltip at the
    /// job system's level, 1 when it has none (FUN_1409e1b80 -> FUN_1409e1c50(id, level, 0), astooltip.cpp:4861).</summary>
    public override IDictionary<string, object> GetTooltip(object[] args)
    {
        if (SkillTooltip == null) return null;
        string what = Rt.ToString(args.ElementAtOrDefault(0)) ?? "";
        if (!what.Contains("compactSkill", StringComparison.Ordinal)) return null;
        uint id = (uint)(int)Rt.ToNumber(args.ElementAtOrDefault(1));
        int level = Jobs?.FindSkill(id)?.Level ?? 1;
        return SkillTooltip(id, level);
    }
}
