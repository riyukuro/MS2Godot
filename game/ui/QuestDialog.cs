using Godot;

namespace Ms2Godot.Ui;

/// <summary>
/// The quest log (uiquestdialog, UI id 0x4e3e, UIQuestDialog in the client; AS3 QuestDialog). This part is the dialog shell:
/// open and close (UIDialog::FUN_1419246f0 toggles, vfunc_65 loads the movie, vfunc_67 closes it), the open and close tweens
/// (OpenCloseTween), its place on the stage (Dialog.alignment), the tab and sub-tab bars (QuestDialog.configUI,
/// updateSubTab, UIQuestDialog::SetTab / SetSubTab / ChangeTab), the two list filters and the display settings panel.
/// The quest list and the detail pane are filled by the later steps; until then the list is empty and the detail pane
/// shows the movie as built.
/// </summary>
public sealed class QuestDialog
{
    const string Movie = "uiquestdialog";

    /// <summary>The component inspector labels of the settings panel (UIQuestDialog_fla.setting_25: __setProp_epic_setting_Layer3_0 and the world, repeat and ok ones).</summary>
    static readonly (string, string, string)[] Inspector =
    {
        ("epic", "label", "\"$s_quest_setting_epic\""), ("world", "label", "\"$s_quest_setting_world\""),
        ("repeat", "label", "\"$s_quest_setting_repeat\""), ("ok", "label", "\"$s_worldmap_setting_ok\""),
    };

    readonly UiAssets assets;
    readonly Func<string, bool> featureOn;

    /// <summary>UIQuestDialog::m_180 and m_184, the tab and sub-tab kept while the dialog is closed (ctor: 0 and 1; vfunc_67 saves them).</summary>
    int savedTab = 0, savedSubTab = 1;
    /// <summary>UIQuestDialog::m_filterA / m_filterB per tab, the "proper level" and "progress only" check boxes (ResetFilters).</summary>
    readonly bool[] properLevel = { false, false, true, false }, progressOnly = { true, true, true, true };
    /// <summary>
    /// CGameOption etc: displayEpicQuest, displayWorldQuestV2, displayRepeatQuest (FUN_141261780 sets all three true). The
    /// client saves them with the character's options (SaveCharacterSettings); here they live for the session.
    /// </summary>
    public bool DisplayEpic = true, DisplayWorld = true, DisplayRepeat = true;

    Control parent; float u; Vector2 stage;
    MovieClipNode root, body;
    UiWidgets.MouseRouter mouse;
    readonly List<MovieClipNode> tabs = new(), subTabs = new();
    int tab, subTab;
    /// <summary>The number of rows in the quest list (m_questList.dataProvider.length); the list step fills it.</summary>
    int listLength;
    Vector2 regPos;                   // the dialog's registration point on the stage, in stage units (its x, y)
    bool closing;                     // the close tween runs; the movie is unloaded at its end (OnEndClose -> Unload)

    public QuestDialog(UiAssets assets, Func<string, bool> featureOn) { this.assets = assets; this.featureOn = featureOn; }

    /// <summary>Whether the dialog is open (UIDialog::vfunc_55 false): loaded and not closing.</summary>
    public bool IsOpen => root != null && !closing;

    /// <summary>The layer and scale to build on; a dialog already open is built again there at the same stage-relative place.</summary>
    public void Attach(Control parent, float u, Vector2 stage)
    {
        var oldStage = this.stage;
        bool reopen = IsOpen;
        Drop();
        this.parent = parent; this.u = u; this.stage = stage;
        if (!reopen) return;
        // UIDialog::vfunc_18 on a resize: the position keeps its ratio to the movie's size, x * new width / old width
        var keep = new Vector2(regPos.X * stage.X / oldStage.X, regPos.Y * stage.Y / oldStage.Y);
        int keepTab = tab, keepSub = subTab;
        Build();
        SetTab(keepTab); SetSubTab(keepSub); ChangeTab(tab);
        regPos = keep; Place(0);
    }

    /// <summary>
    /// CUIService::ToggleDialog -> UIDialog::FUN_1419246f0: closed, it opens (FUN_141924680 -> vfunc_65); open, it closes
    /// (vfunc_67). The quest log key is action 400007 (table/default/defaultkey.xml: L), CKeyTable::FUN_14142b300
    /// (keytable.cpp: case 400007, ToggleDialog(0x4e3e)).
    /// </summary>
    public void Toggle()
    {
        if (IsOpen) Close(); else Open();
    }

    /// <summary>
    /// UIDialog::vfunc_65: the movie is loaded (configUI), then UIQuestDialog::OnLoadInit and OnLoadComplete run, and the
    /// open tween starts (UIDialog::OnLoadComplete: startOpen while tweens are on; the ctor turns them on).
    /// </summary>
    public void Open()
    {
        if (parent == null || IsOpen) return;
        Drop();                                   // a dialog still closing is loaded anew
        Build();
        // OnLoadInit: RefreshDetail (the detail step), then SetTab(m_180), SetSubTab(m_184), ChangeTab(GetTab()), RefreshTab
        SetTab(savedTab);
        SetSubTab(savedSubTab);
        ChangeTab(tab);
        RefreshTab();
        // initializeSetting: the display settings' check boxes from the options
        SetCheck(Setting("epic"), DisplayEpic); SetCheck(Setting("world"), DisplayWorld); SetCheck(Setting("repeat"), DisplayRepeat);
        // OnLoadComplete hides the web guide button (FUN_1419260d0("m_webGuideButton", 0))
        body.HideNamed("m_webGuideButton");
        // UIDialog::vfunc_43: no kept place, so ASGameEntity::vfunc_25 invokes "alignment"
        Align();
        StartOpen();
    }

    /// <summary>
    /// UIQuestDialog::vfunc_67: the tabs are kept (m_180 = GetTab(), m_184 = GetSubTab()), then UIDialog::vfunc_67 closes:
    /// with tweens on, stopTweens and startClose; the movie unloads when the close tween ends (OnEndClose).
    /// </summary>
    public void Close()
    {
        if (!IsOpen) return;
        savedTab = tab; savedSubTab = subTab;
        StartClose();
    }

    void Drop()
    {
        if (root?.GetParent() is Node holder) holder.QueueFree();
        root = null; body = null; mouse = null; closing = false; tween = null;
        tabs.Clear(); subTabs.Clear();
    }

    // ---- build -------------------------------------------------------------------------------------------------------

    void Build()
    {
        var m = new UiMovie(assets, Movie);
        root = m.BuildRoot(u);
        root.Scale = new Vector2(u, u);
        var holder = new Node2D(); holder.AddChild(root); parent.AddChild(holder);
        body = root.Children.Select(c => c.Node).OfType<MovieClipNode>().FirstOrDefault();
        if (body == null) return;
        // the dialog's size is UIComponent's: its bounds when it was made (initialize), before configUI hides anything
        size = body.ContentRect() is { } r ? r.Size : Vector2.Zero;
        UiWidgets.ApplyInspector(root, Inspector);
        // __setProp_m_title_qd: m_title.text = "$s_quest_information"
        body.FindClip("m_title")?.Widget?.Set("text", "$s_quest_information");
        mouse = new UiWidgets.MouseRouter(root) { Extra = () => tabs.Concat(subTabs) };
        ConfigUI();
    }

    Vector2 size;

    /// <summary>QuestDialog.configUI, the parts of this step.</summary>
    void ConfigUI()
    {
        Button("m_close", () => Close());                            // OnCloseDialog -> UIDialog::vfunc_70 -> vfunc_67
        // m_tab: the main, event and sub tabs, and the fame tab with the FameContents feature on; the first selected
        var tabLabels = new List<string> { "$s_quest_tab_main", "$s_quest_tab_event", "$s_quest_tab_sub" };
        if (featureOn("FameContents")) tabLabels.Add("$s_quest_tab_fame");
        // __setProp_m_tab_qd: export_btn_tab renderers 125 wide, 1 apart, in a row
        BuildBar("m_tab", "export_btn_tab", tabLabels, 125, 1, false, tabs, i => { tab = i; OnChangeTab(); });
        tab = 0;
        // m_subTab: can begin, in progress, completed; __setProp_m_subTab_qd: export_renderer_category, 137 wide, in a column
        BuildBar("m_subTab", "export_renderer_category", new[] { "$s_quest_tab_beginable", "$s_quest_tab_progress", "$s_quest_tab_complete" },
            137, 0, true, subTabs, i => { subTab = i; OnChangeTab(); });
        subTab = 0;
        // both check boxes start selected; a click tells the C++ dialog (onSelectProperLevel / onSelectProgressOnly)
        var proper = Clip("m_properLevel"); var progress = Clip("m_progressOnly");
        SetCheck(proper, true); SetCheck(progress, true);
        if (proper?.Widget is ButtonWidget pb) pb.Select += (_, sel) => { if (tab != 4) properLevel[tab] = sel; RefreshTab(); };
        if (progress?.Widget is ButtonWidget gb) gb.Select += (_, sel) => { if (tab != 4) progressOnly[tab] = sel; RefreshTab(); };
        // the display settings panel: hidden, the gear button shows or hides it, its OK button hides it and sets the options
        var setting = Clip("m_setting");
        bool shown = false;
        body.SetNamedVisible("m_setting", false);
        Button("m_settingBtn", () => body.SetNamedVisible("m_setting", shown = !shown));
        if (setting?.FindClip("ok")?.Widget is ButtonWidget ok)
            ok.Click += _ =>
            {
                body.SetNamedVisible("m_setting", shown = false);
                // UIQuestDialog::vfunc_28 onOkSetting: the options, then the quest guide, minimap and world refresh (not built here)
                DisplayEpic = Checked(Setting("epic")); DisplayWorld = Checked(Setting("world")); DisplayRepeat = Checked(Setting("repeat"));
            };
        UpdatePaper();
        UpdateSubTab();
    }

    MovieClipNode Clip(string name) => body?.Direct(name)?.Node as MovieClipNode;
    MovieClipNode Setting(string name) => Clip("m_setting")?.Direct(name)?.Node as MovieClipNode;

    void Button(string name, Action click)
    {
        if (Clip(name)?.Widget is ButtonWidget b) b.Click += _ => click();
    }

    static void SetCheck(MovieClipNode c, bool on) { if (c?.Widget is ButtonWidget b) b.SetSelected(on, false); }
    static bool Checked(MovieClipNode c) => c?.Widget is ButtonWidget { Selected: true };

    /// <summary>
    /// CLIK ButtonBar.updateRenderers for a bar placed as <paramref name="name"/>: one <paramref name="cls"/> per label, its
    /// width set to <paramref name="buttonWidth"/> (autoSize none), laid in a row (or a column) <paramref name="spacing"/>
    /// apart from the bar's origin, as long as it ends inside the bar's own size (_width / _height: the placed 10 x 10 box
    /// times its scale); the container undoes the bar's scale (container.scaleX = 1 / scaleX). setupRenderer makes each a
    /// toggle that cannot be deselected (allowDeselect false); a click on another selects it (ButtonGroup) and raises
    /// BUTTON_SELECT. The selected index is 0 (configUI).
    /// </summary>
    void BuildBar(string name, string cls, IReadOnlyList<string> labels, float buttonWidth, float spacing, bool vertical,
        List<MovieClipNode> into, Action<int> select)
    {
        var bar = body.Direct(name);
        if (bar == null) return;
        bar.Last = -1;                                   // the bar's own placeholder box is not drawn
        var barSize = new Vector2(10 * bar.Base.X.Length(), 10 * bar.Base.Y.Length());
        float at = 0;
        for (int i = 0; i < labels.Count; i++)
        {
            var b = body.Movie.BuildClass(cls, u);
            if (b == null) break;
            var bounds = b.Bounds;
            if (Mathf.Abs(bounds.Size.X - buttonWidth) > 0.01f)
            {
                // width = buttonWidth: Button.draw sets its actual size, a 9-slice symbol slicing to it
                b.QueueFree();
                b = body.Movie.BuildClassSized(cls, u, buttonWidth, bounds.Size.Y);
                if (b == null) break;
            }
            var bsize = new Vector2(buttonWidth, bounds.Size.Y);
            float extent = vertical ? bsize.Y : bsize.X;
            if ((vertical ? barSize.Y : barSize.X) <= extent + spacing + at) { b.QueueFree(); break; }
            b.Position = bar.Base.Origin + (vertical ? new Vector2(0, at) : new Vector2(at, 0));
            at += extent + spacing;
            if (b.Widget == null) UiWidgets.Attach(b);
            // TabButton.configUI: setCount(0) hides the count, and the red circle is hidden
            b.HideNamed("m_count"); b.HideNamed("m_redCircle");
            if (b.Widget is ButtonWidget w)
            {
                w.Toggle = true; w.AllowDeselect = false;
                w.Set("label", labels[i]);
                w.SetSelected(i == 0, false);
                int index = i;
                w.Click += _ =>
                {
                    // ButtonBar.handleButtonGroupChange: only a new index changes the bar and raises BUTTON_SELECT
                    if (index == (into == tabs ? tab : subTab)) return;
                    SelectIndex(into, index);
                    select(index);
                };
            }
            body.AddChild(b);
            into.Add(b);
        }
        body.GotoFrame(body.Frame);
    }

    /// <summary>ButtonBar.selectedIndex = i: the old button deselected, the new one selected (no BUTTON_SELECT).</summary>
    static void SelectIndex(List<MovieClipNode> bar, int index)
    {
        for (int i = 0; i < bar.Count; i++) if (bar[i].Widget is ButtonWidget w) w.SetSelected(i == index, false);
    }

    // ---- tabs --------------------------------------------------------------------------------------------------------

    /// <summary>UIQuestDialog::SetTab: 4 (no tab bar) is ignored; m_tab.selectedIndex, then updateSubTab.</summary>
    void SetTab(int t)
    {
        if (t == 4) return;
        if (tabs.Count > 0) { tab = t; SelectIndex(tabs, t); }
        UpdateSubTab();
    }

    /// <summary>UIQuestDialog::SetSubTab: 3 (no sub-tab bar) is ignored; m_subTab.selectedIndex, then updateSubTab.</summary>
    void SetSubTab(int t)
    {
        if (t == 3) return;
        if (subTabs.Count > 0) { subTab = t; SelectIndex(subTabs, t); }
        UpdateSubTab();
    }

    /// <summary>UIQuestDialog::ChangeTab: the two filter check boxes show the tab's filters.</summary>
    void ChangeTab(int t)
    {
        if (t == 4) return;
        SetCheck(Clip("m_properLevel"), properLevel[t]);
        SetCheck(Clip("m_progressOnly"), progressOnly[t]);
    }

    /// <summary>
    /// QuestDialog.onChangeTab (either bar): updateSubTab, the list's selection and scroll reset, updatePaper, then the C++
    /// dialog's OnChangeTab: ChangeTab(GetTab()), RefreshTab, RefreshDetail.
    /// </summary>
    void OnChangeTab()
    {
        UpdateSubTab();
        UpdatePaper();
        ChangeTab(tab);
        RefreshTab();
    }

    /// <summary>
    /// UIQuestDialog::RefreshTab: sub-tab 0 lists the startable quests, the others the tab's quests (RefreshEpisodes on the
    /// main tab, RefreshList on the rest). The list is the next step; the paper then follows the list (setList -> updatePaper).
    /// </summary>
    void RefreshTab()
    {
        listLength = 0;
        UpdatePaper();
    }

    /// <summary>
    /// QuestDialog.updateSubTab. The main tab (0) names the first sub-tab "can begin (epic)"; the event and fame tabs (1, 3)
    /// name it "can begin" and hide the completed sub-tab, moving off it to "in progress"; the sub tab (2) names it "can
    /// begin" with all three shown. The "proper level" box is enabled on the first sub-tab only and shown, with its label,
    /// on every sub-tab but "in progress", where the "progress only" box and label show instead.
    /// </summary>
    void UpdateSubTab()
    {
        if (subTabs.Count < 3) return;
        bool noComplete = false;
        switch (tab)
        {
            case 0: Label(subTabs[0], "$s_quest_tab_beginable_epic"); subTabs[2].Visible = true; break;
            case 1: case 3: Label(subTabs[0], "$s_quest_tab_beginable"); subTabs[2].Visible = false; noComplete = true; break;
            case 2: Label(subTabs[0], "$s_quest_tab_beginable"); subTabs[2].Visible = true; break;
        }
        if (Clip("m_properLevel")?.Widget is ButtonWidget pl) pl.Set("enabled", subTab == 0);
        bool progress = subTab == 1;
        body.SetNamedVisible("m_properLevel", !progress);
        body.SetNamedVisible("m_properLevelTf", !progress);
        body.SetNamedVisible("m_progressOnly", progress);
        body.SetNamedVisible("m_progressOnlyTf", progress);
        if (noComplete && subTab == 2) { subTab = 1; SelectIndex(subTabs, 1); }
    }

    static void Label(MovieClipNode b, string key) => b.Widget?.Set("label", key);

    /// <summary>
    /// QuestDialog.updatePaper: with no list row selected the paper shows its "progress" state on the first sub-tab and
    /// "complete" on the others, with a line for the sub-tab and whether its list is empty; else its "normal" state.
    /// </summary>
    void UpdatePaper()
    {
        var paper = Clip("m_paper");
        if (paper == null) return;
        paper.GotoLabel(subTab == 0 ? "progress" : "complete", true);
        string key = subTab switch
        {
            0 => listLength > 0 ? "$s_quest_select_beginable" : "$s_quest_select_no_beginable",
            1 => listLength > 0 ? "$s_quest_select_quest" : "$s_quest_find_new_quest",
            _ => listLength > 0 ? "$s_quest_select_complete_quest" : "$s_quest_no_have_quest",
        };
        paper.SetNamedText("textField", assets.Str(key));
    }

    // ---- place and tweens --------------------------------------------------------------------------------------------

    /// <summary>
    /// Dialog.alignment with its _alignType 0 (centre) and no offset: Align.computePosition, x = (stage width - width) / 2
    /// and y likewise, each set through DisplayObject x / y (SetX: floor to a twip).
    /// </summary>
    void Align()
    {
        static float Twip(float v) => MathF.Floor(v * 20) / 20;
        regPos = new Vector2(Twip((stage.X - size.X) * 0.5f), Twip((stage.Y - size.Y) * 0.5f));
        Place(0);
    }

    void Place(float dy)
    {
        if (root == null || body == null) return;
        root.Position = (regPos + new Vector2(0, dy) - body.Position) * u;
    }

    /// <summary>A CLIK Tween on the dialog's y and alpha (fastTransform false: the properties set each tick).</summary>
    sealed class Tween
    {
        public double Duration, Position;
        public float Y0, Y1, A0, A1;
        public Tween Next;
        public Action Done;
    }

    Tween tween;
    float y, alpha = 1;                 // the tweened y offset from regPos and the dialog's alpha
    float endY;                         // OpenCloseTween.m_endTweenPosY, as an offset

    /// <summary>
    /// OpenCloseTween.startOpenAni (m_startAlpha 0, m_startOffsetY 25, m_topOffsetY 7): from 25 below at alpha 0, 160 ms to
    /// 7 above at alpha 1, then 80 ms back to its place, both Regular.easeOut.
    /// </summary>
    void StartOpen()
    {
        endY = 0;
        y = 25; alpha = 0;
        var second = new Tween { Duration = 80, Y0 = -7, Y1 = 0, A0 = 1, A1 = 1 };
        tween = new Tween { Duration = 160, Y0 = y, Y1 = -7, A0 = 0, A1 = 1, Next = second };
        Apply();
    }

    /// <summary>
    /// OpenCloseTween.startCloseAni: stopOpenTweens (back to its place at alpha 1), then 100 ms to 25 below at alpha 0
    /// (Regular.easeOut); onCloseComplete puts y back and OnEndClose unloads the movie.
    /// </summary>
    void StartClose()
    {
        closing = true;
        if (root == null || !root.IsVisibleInTree()) { Drop(); return; }
        tween = null; y = endY; alpha = 1;
        tween = new Tween { Duration = 100, Y0 = y, Y1 = endY + 25, A0 = 1, A1 = 0, Done = Drop };
        Apply();
    }

    /// <summary>
    /// Tween.tick on each frame: the position moves by the milliseconds since the last tick; ease Regular.easeOut
    /// (-c * t * (t - 2) + b over 0..1); at the end the next tween starts and onComplete runs.
    /// </summary>
    public void Process(double delta)
    {
        if (tween == null) return;
        var t = tween;
        t.Position += delta * 1000;
        double p = Math.Min(1, t.Position / t.Duration);
        double e = -p * (p - 2);
        y = (float)(t.Y0 + (t.Y1 - t.Y0) * e);
        alpha = (float)(t.A0 + (t.A1 - t.A0) * e);
        if (p >= 1) { tween = t.Next; if (t.Done != null) { t.Done(); return; } }
        Apply();
    }

    void Apply()
    {
        if (root == null) return;
        Place(MathF.Floor(y * 20) / 20);
        root.Modulate = new Color(1, 1, 1, alpha);
    }

    // ---- input -------------------------------------------------------------------------------------------------------

    /// <summary>Mouse over the dialog's buttons, bars and check boxes while it is open.</summary>
    public void Input(InputEvent e)
    {
        if (IsOpen) mouse?.Handle(e);
    }

    /// <summary>The selected tab and sub-tab (UIQuestDialog::GetTab / GetSubTab: 4 and 3 with no bar).</summary>
    public (int Tab, int SubTab) Tabs => (tabs.Count > 0 ? tab : 4, subTabs.Count > 0 ? subTab : 3);

    /// <summary>Clicks a tab or sub-tab button as the mouse would (for shots and tests).</summary>
    public void ClickTab(bool sub, int index)
    {
        var bar = sub ? subTabs : tabs;
        if (index < 0 || index >= bar.Count || bar[index].Widget is not ButtonWidget w || !bar[index].Visible) return;
        w.MouseOver(true); w.MouseDown(true); w.MouseDown(false);
    }
}
