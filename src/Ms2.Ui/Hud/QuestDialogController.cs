using Ms2.Avm2;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIQuestDialog (ms2/client/ms2/uiquestdialog.cpp): the quest log. It drives the list: the tab and sub-tab (SetTab /
/// SetSubTab / GetTab / GetSubTab), the filter check boxes (ChangeTab, RefreshTab's reads) and the list data
/// (RefreshEpisodes / RefreshList / RefreshCompleted build the tree, then setList and updatePaper), and the detail pane
/// (RefreshDetail, SelectQuest: m_questInfo's setData). The tree and the detail come from <see cref="QuestLog"/> and
/// <see cref="QuestDetail"/>. The calls that act on the game (give up, tracking, the goto buttons, the settings) are
/// raised as events for the game to carry out.
/// </summary>
public sealed class QuestDialogController : UiController
{
    public override string Movie => "uiquestdialog";
    protected override string EntityMethod => "setList";

    public QuestLog Log;
    /// <summary>The live inputs of the detail pane (field, guild, time zone, button rules).</summary>
    public QuestDetailContext Detail = new();
    /// <summary>m_180 / m_184: the tab and sub-tab the dialog opens on (ctor: 0 and 1).</summary>
    public int OpenTab = 0, OpenSubTab = 1;
    /// <summary>m_filterA / m_filterB per tab (ResetFilters): "proper level" and "progress only".</summary>
    readonly bool[] filterA = { false, false, true, false }, filterB = { true, true, true, true };
    /// <summary>CGameOption etc m_20 / m_21 / m_22: displayEpicQuest, displayWorldQuestV2, displayRepeatQuest.</summary>
    public bool DisplayEpic = true, DisplayWorld = true, DisplayRepeat = true;

    /// <summary>GiveUpQuest / OnGiveUpConfirm's packet (uiquestdialog.cpp:1813, :1826): Quest mode 6 with the quest id.</summary>
    public event Action<int> SendGiveUp;
    /// <summary>GiveUpQuest's confirm box (CreateMessageBox type 1, then FUN_140d73c60).</summary>
    public Action<MessageBoxData> ShowMessageBox = MessageBoxQueue.Show;
    /// <summary>The local player is in the world (WorldService::GetLocalPlayer, which vfunc_o178_2 needs).</summary>
    public Func<bool> HasLocalPlayer = () => true;
    /// <summary>onRegister: CQuest::RequestTrackingState(regist) of the quest (the tracking packet, questmanager_1.cpp).</summary>
    public event Action<int, bool> RegisterQuest;
    /// <summary>
    /// onOkSetting: the three display options were saved (CGameOption etc m_20..m_22); the client then refreshes the world
    /// map guide and the minimap quest icons, FUN_141bd3880, and SaveCharacterSettings.
    /// </summary>
    public event Action SettingsChanged;
    /// <summary>
    /// onGotoNpc (SendQuestMode12), onGotoDungeon (SendQuestMode13), onGotoDispatch (FUN_1416cf870), onGotoReEntry
    /// (SendQuestMode19), onGotoFindParty / onGotoFindPartyCancel (FUN_1416cf980(id, 0 / 1)): the callback name and the quest.
    /// </summary>
    public event Action<string, int> Goto;
    /// <summary>CanAdd: UIQuestGuideDialog(0x4e43)::CanAdd; null when the guide dialog is not open (the call is not handled).</summary>
    public Func<bool?> CanAddToGuide = () => null;
    /// <summary>ShowLimitMessage: FUN_141275260(0x5f1, 1, 1), a system message of common string 0x5f1.</summary>
    public event Action ShowLimitMessage;

    /// <summary>OnLoadInit (uiquestdialog.cpp:314) without the focus quest, then OnLoadComplete.</summary>
    public override void OnLoadComplete()
    {
        RefreshDetail();
        SetTab(OpenTab);
        SetSubTab(OpenSubTab);
        ChangeTab(GetTab());
        RefreshTab();
        Host.Invoke(Host.Entity, "initializeSetting", "epic", DisplayEpic);
        Host.Invoke(Host.Entity, "initializeSetting", "world", DisplayWorld);
        Host.Invoke(Host.Entity, "initializeSetting", "repeat", DisplayRepeat);
        base.OnLoadComplete();
    }

    /// <summary>vfunc_28 (uiquestdialog.cpp:349): the movie's calls.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        uint UInt(int i) => i < args.Length ? Rt.ToUint32(args[i]) : 0;
        int Int(int i) => i < args.Length ? Rt.ToInt32(args[i]) : 0;
        bool Bool(int i) => i < args.Length && Rt.ToBoolean(args[i]);
        switch (name.ToLowerInvariant())       // _stricmp
        {
            case "onselectquest":
                if (args.Length == 0) return false;
                SelectQuest((int)UInt(0));
                Log?.Pinned.Remove((int)UInt(0));        // RemovePinned
                return true;
            case "onselectchapter":
                if (args.Length != 0) { Log?.SelectChapter(Int(0)); RefreshDetail(); }
                return true;
            case "onchangetab":
                ChangeTab(GetTab());
                RefreshTab();
                RefreshDetail();
                return true;
            case "ongiveupquest":
                if (args.Length != 0) GiveUpQuest((int)UInt(0));
                return true;
            case "onregister":
                if (args.Length != 0) RegisterQuest?.Invoke((int)UInt(0), Bool(1));
                return true;
            case "canadd":
                if (CanAddToGuide() is not { } can) return false;
                Host.ReturnValue = can;
                return true;
            case "showlimitmessage":
                ShowLimitMessage?.Invoke();
                return true;
            case "onselectproperlevel":
                SetFilter(filterA, args);
                return true;
            case "onselectprogressonly":
                SetFilter(filterB, args);
                return true;
            case "onoksetting":
                if (args.Length != 0)
                {
                    (DisplayEpic, DisplayWorld, DisplayRepeat) = (Bool(0), Bool(1), Bool(2));
                    SettingsChanged?.Invoke();
                }
                return true;
            case "ongotonpc":
            case "ongotodungeon":
                if (args.Length != 0) Goto?.Invoke(name, (int)UInt(0));
                return true;
            case "ongotodispatch":
            case "ongotoreentry":
            case "ongotofindparty":
            case "ongotofindpartycancel":
                // these return 1 only with an argument (:478-530)
                if (args.Length == 0) return false;
                Goto?.Invoke(name, (int)UInt(0));
                return true;
        }
        return false;
    }

    /// <summary>onSelectProperLevel / onSelectProgressOnly: the tab's filter (not on tab 4), then RefreshTab.</summary>
    void SetFilter(bool[] filter, object[] args)
    {
        if (args.Length == 0) return;
        int tab = GetTab();
        if (tab != 4) filter[tab] = Rt.ToBoolean(args[0]);
        RefreshTab();
    }

    /// <summary>
    /// GiveUpQuest @0x141aa1b50 (uiquestdialog.cpp:1792): a taken quest with data; a time-limited one (FUN_14074b090) not
    /// yet expired asks first (s_quest_giveup_warning, OnGiveUpConfirm @0x141aa1fa0 sends on button 0), else the packet goes
    /// at once; RefreshDetail after the send.
    /// </summary>
    public void GiveUpQuest(int id)
    {
        if (Log?.State.Get(id) is not { } q) return;
        if (Log.Table.Quest(id) is not { } d) return;
        if (q.State != 1) return;
        // IsExpired: the record's own flag (an unknown end time reads as not expired)
        bool confirm = QuestCommon.IsTimeLimited(d) && !q.Expired;
        if (confirm)
        {
            var box = MessageBoxQueue.CreateWithCallback(MessageBoxData.CommonByKey("s_quest_giveup_warning"), 1, (button, _) =>
            {
                if (button != 0) return true;
                SendGiveUp?.Invoke(id);
                RefreshDetail();
                return true;
            });
            ShowMessageBox(box);
            return;
        }
        SendGiveUp?.Invoke(id);
        RefreshDetail();
    }

    /// <summary>vfunc_55 with the movie up: the dialog is open.</summary>
    bool Open => !IsClosed && Host?.Entity != null;

    /// <summary>vfunc_o178_1 @0x141a9aa90 (uiquestdialog.cpp:586): a quest taken goes into m_pinned; RefreshTab when open.</summary>
    public void OnQuestAccepted(int id)
    {
        Log?.Pinned.Add(id);
        if (Open) RefreshTab();
    }

    /// <summary>
    /// vfunc_o178_2 @0x141a9ab10 (:594): a quest rewarded, with a local player: CloseChapter of its chapter (m_28); when
    /// open, RefreshTab, SelectListIndex(-1), RefreshDetail and updatePaper(1).
    /// </summary>
    public void OnQuestRewarded(int chapter)
    {
        if (!HasLocalPlayer()) return;
        CloseChapter(chapter);
        if (Open) RefreshAll();
    }

    /// <summary>vfunc_o178_5 @0x141a9acc0 (:617): a quest given up or expired; when open, the same refresh as a reward.</summary>
    public void OnQuestRemoved()
    {
        if (Open) RefreshAll();
    }

    /// <summary>vfunc_o178_6 @0x141a9ada0 (:630): a quest's tracking changed; RefreshTab when open. vfunc_o178_3 (progress)
    /// is empty.</summary>
    public void OnQuestTracking()
    {
        if (Open) RefreshTab();
    }

    void RefreshAll()
    {
        RefreshTab();
        SelectListIndex(-1);
        RefreshDetail();
        if (Host.Entity != null) Host.Invoke("updatePaper", 1);
    }

    /// <summary>CloseChapter @0x141aa2520 (:1910) on the sub-tab GetSubTab reads. Without the movie (before the first open,
    /// and after each close: OnEndClose -> Unload, uiswfentity.cpp:241, m_30 = 0) GetButtonBar is null
    /// (uidailymissiondialog.cpp:687), GetSubTab gives 3 and GetFlags none: the reward closes nothing, and nothing is kept
    /// for the next open.</summary>
    public void CloseChapter(int chapter)
    {
        if (Log == null) return;
        Log.SubTab = Host?.Entity != null ? GetSubTab() : 3;
        Log.CloseChapter(chapter);
    }

    /// <summary>SelectListIndex @0x141a9c0e0 (:832): m_questList's selected index, vfunc_24(true) (invalidate, validateNow),
    /// then ScrollToSelected (scrollToSelected, ascorelist.cpp:133).</summary>
    public void SelectListIndex(int index)
    {
        if (Host.Child("m_questList") is not { } list) return;
        Host.Set(list, "selectedIndex", index);
        Host.Invoke(list, "invalidate");
        Host.Invoke(list, "validateNow");
        Host.Invoke(list, "scrollToSelected");
    }

    /// <summary>GetSubTabFilter (:932): the sub-tab 0, 1 or 2, else 4.</summary>
    public int GetSubTabFilter() => GetSubTab() switch { 0 => 0, 1 => 1, 2 => 2, _ => 4 };

    /// <summary>RefreshDetail (:1228): the empty pane.</summary>
    public void RefreshDetail()
    {
        if (Host.Child("m_questInfo") is { } info) Host.Invoke(info, "setData", Info(QuestDetail.Empty(GetTab())));
    }

    /// <summary>SelectQuest (:777): the quest's detail, when the quest has data (and a record off sub-tab 0).</summary>
    public void SelectQuest(int id)
    {
        if (Log == null) return;
        if (Host.Child("m_questInfo") is not { } info) return;
        var data = QuestDetail.Select(Log, id, GetSubTabFilter(), Detail);
        if (data != null) Host.Invoke(info, "setData", Info(data));
    }

    AsObject Info(QuestInfoObject o) => Host.Obj(o.Members.Select(m => (m.Name, Value(m.Value))).ToArray());

    object Value(object v) => v switch
    {
        QuestInfoObject o => Info(o),
        IReadOnlyList<object> list => new AsArray(Host.RT, list.Select(Value).ToList()),
        _ => v,
    };

    object Bar(string name) => Host.Child(name);

    /// <summary>GetTab: m_tab's selected index, 4 without the bar.</summary>
    public int GetTab() => Bar("m_tab") is { } b ? (int)Rt.ToNumber(Host.Get(b, "selectedIndex")) : 4;
    /// <summary>GetSubTab: m_subTab's selected index, 3 without the bar.</summary>
    public int GetSubTab() => Bar("m_subTab") is { } b ? (int)Rt.ToNumber(Host.Get(b, "selectedIndex")) : 3;

    /// <summary>SetTab: m_tab's selected index (not for 4), then updateSubTab.</summary>
    public void SetTab(int tab)
    {
        if (tab == 4) return;
        if (Bar("m_tab") is { } b) Host.Set(b, "selectedIndex", tab);
        Host.Invoke("updateSubTab");
    }

    /// <summary>SetSubTab: m_subTab's selected index (not for 3), then updateSubTab.</summary>
    public void SetSubTab(int tab)
    {
        if (tab == 3) return;
        if (Bar("m_subTab") is { } b) Host.Set(b, "selectedIndex", tab);
        Host.Invoke("updateSubTab");
    }

    public override string TypeName => "QuestDialog";

    /// <summary>
    /// vfunc_67 (uiquestdialog.cpp:544): the tab and sub-tab are kept for the next open (m_180 / m_184), then UIDialog's.
    /// ClearOpened destroys the reward item info slots, which are not hosted here.
    /// </summary>
    public override void Close()
    {
        if (Host?.Entity != null) (OpenTab, OpenSubTab) = (GetTab(), GetSubTab());
        base.Close();
    }

    static readonly (string Name, int Tab)[] FilterKeys = { ("World", 2), ("Epic", 0), ("Event", 1) };

    /// <summary>vfunc_53 (uiquestdialog.cpp:559): tab.properLevel* then tab.progressOnly*, World, Epic, Event (tabs 2, 0, 1).</summary>
    protected override void SaveSettings(DialogSettings s)
    {
        foreach (var (n, t) in FilterKeys) s.Set("tab", "properLevel" + n, filterA[t] ? 1 : 0);
        foreach (var (n, t) in FilterKeys) s.Set("tab", "progressOnly" + n, filterB[t] ? 1 : 0);
    }

    /// <summary>vfunc_54 (uiquestdialog.cpp:572): ResetFilters (:1836), then the saved filters over them.</summary>
    protected override void LoadSettings(DialogSettings s)
    {
        bool[] a = { false, false, true, false }, b = { true, true, true, true };
        a.CopyTo(filterA, 0);
        b.CopyTo(filterB, 0);
        foreach (var (n, t) in FilterKeys) { int v = filterA[t] ? 1 : 0; s.Get("tab", "properLevel" + n, ref v); filterA[t] = v != 0; }
        foreach (var (n, t) in FilterKeys) { int v = filterB[t] ? 1 : 0; s.Get("tab", "progressOnly" + n, ref v); filterB[t] = v != 0; }
    }

    /// <summary>ChangeTab: the tab's filters onto the two check boxes.</summary>
    public void ChangeTab(int tab)
    {
        if (tab == 4) return;
        if (Host.Child("m_properLevel") is { } a) Host.Set(a, "selected", filterA[tab]);
        if (Host.Child("m_progressOnly") is { } p) Host.Set(p, "selected", filterB[tab]);
    }

    bool Selected(object o) => o != null && Rt.ToBoolean(Host.Get(o, "selected"));

    /// <summary>
    /// RefreshTab: the tree for the tab and sub-tab, with "proper level" selected and "progress only" shown and selected,
    /// then setList(root) and updatePaper. No tree (no local player) leaves the list as it is.
    /// </summary>
    public void RefreshTab()
    {
        if (Log == null) return;
        (Log.Tab, Log.SubTab) = (GetTab(), GetSubTab());
        Log.ProperLevel = Selected(Host.Child("m_properLevel"));
        var po = Host.Child("m_progressOnly");
        Log.ProgressOnly = po != null && Rt.ToBoolean(Host.Get(po, "visible")) && Selected(po);
        var root = Log.Build();
        if (root == null) return;
        Host.Invoke("setList", Node(root, Log.SubTab == 0, true));
        Host.Invoke("updatePaper");
    }

    /// <summary>
    /// The GFx object of a tree node, with the members in the order the client sets them: the root (type, nodes); a quest
    /// (AddQuestNode: id, regist, forceRegist, progress, label, isNew, icon, rendererClassName; RefreshCompleted's: id,
    /// regist, progress as an int, label, isNew, icon, rendererClassName); a chapter (chapter, level, label, type,
    /// rendererClassName, nodes); a continent (label, type, rendererClassName); a region (code, field, type,
    /// rendererClassName).
    /// </summary>
    AsObject Node(QuestLogNode n, bool startable, bool root = false)
    {
        var m = new List<(string, object)>();
        if (root) { m.Add(("type", n.Type)); m.Add(("nodes", Nodes(n, startable))); return Host.Obj(m.ToArray()); }
        switch (n.Renderer)
        {
            case "qd_renderer_questList" when startable:
                m.AddRange(new (string, object)[] { ("id", n.Id), ("regist", 0), ("progress", 0), ("label", n.Label), ("isNew", 0), ("icon", "") });
                break;
            case "qd_renderer_questList":
                m.AddRange(new (string, object)[] { ("id", n.Id), ("regist", n.Regist), ("forceRegist", n.ForceRegist), ("progress", n.Progress),
                                                    ("label", n.Label), ("isNew", n.IsNew), ("icon", n.Icon) });
                break;
            case "qd_renderer_chapter":
                m.AddRange(new (string, object)[] { ("chapter", n.Chapter), ("level", n.Level), ("label", n.Label), ("type", n.Type) });
                break;
            case "qd_renderer_continent":
                m.AddRange(new (string, object)[] { ("label", n.Label), ("type", n.Type) });
                break;
            case "qd_renderer_region":
                m.AddRange(new (string, object)[] { ("code", n.Code), ("field", n.Field), ("type", n.Type) });
                break;
            default: throw new InvalidOperationException("quest log node renderer " + n.Renderer);
        }
        m.Add(("rendererClassName", n.Renderer));
        if (n.Renderer == "qd_renderer_chapter") m.Add(("nodes", Nodes(n, startable)));
        return Host.Obj(m.ToArray());
    }

    AsArray Nodes(QuestLogNode n, bool startable) =>
        new(Host.RT, (n.Nodes ?? new()).Select(c => (object)Node(c, startable)).ToList());
}
