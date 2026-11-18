using Godot;
using Ms2.Quest;

namespace Ms2Godot.Ui;

/// <summary>
/// The quest log (uiquestdialog, UI id 0x4e3e, UIQuestDialog in the client; AS3 QuestDialog). This part is the dialog shell:
/// open and close (UIDialog::FUN_1419246f0 toggles, vfunc_65 loads the movie, vfunc_67 closes it), the open and close tweens
/// (OpenCloseTween), its place on the stage (Dialog.alignment), the tab and sub-tab bars (QuestDialog.configUI,
/// updateSubTab, UIQuestDialog::SetTab / SetSubTab / ChangeTab), the two list filters and the display settings panel.
/// The quest list (UIQuestDialog::RefreshTab, <see cref="QuestLog"/>) is drawn as QuestDialog.setList draws it: a
/// TreeViewDataProvider over the tree, one renderer per row (MultiRendererTreeList). The detail pane is the next step;
/// until then it shows the movie as built.
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

    /// <summary>The quest list's data: the quest table and the character's quest state; null until the table is loaded.</summary>
    readonly Func<QuestLog> logSource;
    QuestLog log;
    QuestLogTree tree;
    /// <summary>The list's renderers (_renderers), one per row, in the list's container.</summary>
    readonly List<MovieClipNode> rows = new();
    Node2D rowContainer;
    /// <summary>m_questList.selectedIndex (-1 none).</summary>
    int selectedIndex = -1;

    public QuestDialog(UiAssets assets, Func<string, bool> featureOn, Func<QuestLog> logSource)
    {
        this.assets = assets; this.featureOn = featureOn; this.logSource = logSource;
    }

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
        SetTab(keepTab); SetSubTab(keepSub); ChangeTab(tab); RefreshTab();
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
        tabs.Clear(); subTabs.Clear(); rows.Clear(); rowContainer = null; tree = null; selectedIndex = -1;
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
        mouse = new UiWidgets.MouseRouter(root) { Extra = () => tabs.Concat(subTabs).Concat(rows) };
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
        SelectRow(-1);
        UpdatePaper();
        ChangeTab(tab);
        RefreshTab();
    }

    /// <summary>
    /// UIQuestDialog::RefreshTab: sub-tab 0 lists the startable quests, the others the tab's quests (RefreshEpisodes on the
    /// main tab, RefreshList on the rest), each then setList and updatePaper. The filters are the check boxes' states:
    /// "proper level" selected (CollectRecommended reads it shown or not), "progress only" shown and selected.
    /// </summary>
    void RefreshTab()
    {
        log ??= logSource?.Invoke();
        if (log == null || body == null) return;
        (log.Tab, log.SubTab) = Tabs;
        log.ProperLevel = Checked(Clip("m_properLevel"));
        log.ProgressOnly = body.Direct("m_progressOnly") is { Last: > 0 } && Checked(Clip("m_progressOnly"));
        var list = log.Build();
        if (list == null) return;
        SetList(list);
        UpdatePaper();
    }

    // ---- the quest list ----------------------------------------------------------------------------------------------

    /// <summary>
    /// QuestDialog.setList: a TreeViewDataProvider over the tree becomes the list's data, and its renderers are made again
    /// (invalidateRenderers, validateNow), then updateExpandIcon.
    /// </summary>
    void SetList(QuestLogNode list)
    {
        tree = new QuestLogTree(list);
        DrawRenderers();
    }

    /// <summary>
    /// CoreList.draw with the renderers invalid, for a MultiRendererTreeList: the old renderers are dropped; one renderer per
    /// row (dataProvider.length) of the class its data names (rendererClassName; getRendererInfo), made while the row has
    /// data (drawRenderers); each placed under the one before it by that one's class height (drawLayout: rendererData
    /// rheight, the height of a new instance); then the rows' data (requestItemRange -> populateData: ListData index,
    /// label and selected, setData) and the expand icons (updateExpandIcon). The renderers sit in the list's container,
    /// whose scale undoes the list's (CoreList.draw: container.scaleX = 1 / scaleX), at the list's place under its mask.
    /// </summary>
    void DrawRenderers()
    {
        foreach (var r in rows) r.QueueFree();
        rows.Clear();
        listLength = tree?.Length ?? 0;
        var listChild = body?.Direct("m_questList");
        if (tree == null || listChild?.Node is not MovieClipNode list) return;
        if (rowContainer == null)
        {
            var b = listChild.Base;
            rowContainer = new Node2D { Scale = new Vector2(1 / b.X.Length(), 1 / b.Y.Length()) };
            list.AddChild(rowContainer);
        }
        var heights = new Dictionary<string, float>();
        float y = 0;
        for (int i = 0; i < listLength; i++)
        {
            var data = tree.ItemAt(i);
            if (data == null) continue;
            // getRendererInfo(data).rclass: a row without a class name has no renderer info, and the AS stops on it
            if (string.IsNullOrEmpty(data.Renderer)) break;
            var r = body.Movie.BuildClass(data.Renderer, u);
            if (r == null) break;
            if (r.Widget == null) UiWidgets.Attach(r);
            heights.TryAdd(data.Renderer, r.Bounds.Size.Y);
            if (i > 0) y += heights[tree.ItemAt(i - 1).Renderer ?? data.Renderer];
            r.Position = new Vector2(0, y);
            rowContainer.AddChild(r);
            int index = i;
            if (r.Widget is ButtonWidget w)
            {
                // the list selects (ListData.selected); a click selects the row (CoreList.handleItemClick)
                w.Toggle = true; w.AllowDeselect = false;
                w.Click += _ => OnItemClick(index);
            }
            rows.Add(r);
        }
        var range = tree.Range(0, listLength - 1);
        for (int i = 0; i < rows.Count; i++) SetData(rows[i], i < range.Count ? range[i] : null, i);
        UpdateExpandIcon();
    }

    /// <summary>populateData for one renderer: setListData (label, selected) then the renderer class's setData.</summary>
    void SetData(MovieClipNode r, QuestLogNode d, int index)
    {
        if (r.Widget is ButtonWidget w)
        {
            w.SetSelected(selectedIndex == index, false);
            // itemToLabel: the item's label; the region rows have no label and no label field to show one
            if (d?.Label != null) w.Set("label", d.Label);
        }
        switch (d?.Renderer)
        {
            case "qd_renderer_chapter":
                // ChapterListRenderer.setLevel: m_level.htmlText = data.level
                r.SetNamedHtml("m_level", d.Level ?? "");
                break;
            case "qd_renderer_region":
                // ItemRendererTile.setData
                r.SetNamedHtml("m_code", d.Code ?? "");
                r.SetNamedHtml("m_field", d.Field ?? "");
                break;
            case "qd_renderer_questList":
                SetQuestRow(r, d);
                break;
        }
    }

    /// <summary>
    /// QuestListRenderer.setData: the name; the register check box from regist; a quest in progress has the check box and
    /// the name enabled, the new mark after the name (but not past the give-up button), and the give-up button when it
    /// failed or else its state icon; any other row hides the check box, the new mark and the give-up button, greys the
    /// name and shows the "complete" icon. A forced quest's check box is selected and disabled.
    /// </summary>
    static void SetQuestRow(MovieClipNode r, QuestLogNode d)
    {
        var name = r.Direct("m_name")?.Node as MovieClipNode;
        var check = r.Direct("m_checkBox")?.Node as MovieClipNode;
        var icon = r.Direct("m_stateIcon")?.Node as MovieClipNode;
        name?.Widget?.Set("text", d.Label ?? "");
        if (check?.Widget is ButtonWidget cb) cb.SetSelected(d.Regist != 0, false);
        if (d.Progress)
        {
            check?.Widget?.Set("enabled", true);
            name?.GotoLabel("default");
            r.SetNamedVisible("m_new", d.IsNew != 0);
            var nameChild = r.Direct("m_name");
            float textWidth = name?.Children.Select(c => c.Node).OfType<TextNode>().FirstOrDefault()?.TextWidth ?? 0;
            float giveupX = r.Direct("m_giveup")?.Base.Origin.X ?? 0;
            r.SetChildX(r.Direct("m_new"), Math.Min(nameChild.Base.Origin.X + textWidth + 10, giveupX));
            bool fail = d.Icon == "fail";
            r.SetNamedVisible("m_giveup", fail);
            r.SetNamedVisible("m_stateIcon", !fail);
            if (!fail) icon?.GotoLabel(d.Icon);
        }
        else
        {
            r.SetNamedVisible("m_checkBox", false);
            name?.GotoLabel("disabled");
            r.SetNamedVisible("m_new", false);
            r.SetNamedVisible("m_giveup", false);
            icon?.GotoLabel("complete");
        }
        if (d.ForceRegist != 0 && check?.Widget is ButtonWidget fb)
        {
            fb.SetSelected(true, false);
            fb.Set("enabled", false);
        }
        // onRegister (the check box's SELECT): the row's regist follows it; telling the server is the C++ side's (not here)
        if (check?.Widget is ButtonWidget rb) rb.Select += (_, sel) => d.Regist = sel ? 1 : 0;
    }

    /// <summary>MultiRendererTreeList.updateExpandIcon: each row with children shows "collapse" when open, else "expand".</summary>
    void UpdateExpandIcon()
    {
        for (int i = 0; i < rows.Count; i++)
        {
            var d = tree.ItemAt(i);
            if (d == null || d.Type == QuestLogTree.TypeLeaf) continue;
            (rows[i].Direct("icon")?.Node as MovieClipNode)?.GotoLabel(d.Type == QuestLogTree.TypeOpen ? "collapse" : "expand");
        }
    }

    /// <summary>
    /// A row clicked: MultiRendererTreeList.handleItemClick selects it (CoreList), then a row with children flips open or
    /// closed and the renderers are made again (resetRenderer); the new index raises INDEX_CHANGE (onSelectRenderer).
    /// </summary>
    void OnItemClick(int index)
    {
        var d = tree?.ItemAt(index);
        if (d == null) return;
        if (d.Type != QuestLogTree.TypeLeaf)
        {
            d.Type = d.Type == QuestLogTree.TypeOpen ? QuestLogTree.TypeClosed : QuestLogTree.TypeOpen;
            selectedIndex = index;
            tree = new QuestLogTree(tree.Root);      // resetRenderer: validateLength over the same tree
            DrawRenderers();
            OnSelectRenderer(index);
            return;
        }
        SelectRow(index);
        OnSelectRenderer(index);
    }

    /// <summary>ScrollingListQuest.updateSelectedIndex: the old row deselected, the new one selected.</summary>
    void SelectRow(int index)
    {
        selectedIndex = index;
        for (int i = 0; i < rows.Count; i++) if (rows[i].Widget is ButtonWidget w) w.SetSelected(i == index, false);
    }

    /// <summary>
    /// QuestDialog.onSelectRenderer: a chapter row shows the paper's list line (updatePaper(true)) and tells the dialog
    /// (OnSelectChapter: SelectChapter, RefreshDetail); a quest row shows the paper's normal state and tells it
    /// (OnSelectQuest: SelectQuest, RemovePinned). The detail pane is the next step.
    /// </summary>
    void OnSelectRenderer(int index)
    {
        if (tree == null || index >= listLength) return;
        var d = rows.Count > index ? tree.ItemAt(index) : null;
        if (d == null) return;
        if (d.Chapter != 0)
        {
            UpdatePaper(true);
            log?.SelectChapter(d.Chapter);
        }
        else if (d.Id != 0)
        {
            UpdatePaper();
            log?.Pinned.Remove((int)d.Id);
        }
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
    /// QuestDialog.updatePaper: with no list row selected (or <paramref name="list"/>) the paper shows its "progress" state
    /// on the first sub-tab and "complete" on the others, with a line for the sub-tab and whether its list is empty; else
    /// its "normal" state.
    /// </summary>
    void UpdatePaper(bool list = false)
    {
        var paper = Clip("m_paper");
        if (paper == null) return;
        if (selectedIndex != -1 && !list) { paper.GotoLabel("normal", true); return; }
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

    /// <summary>Clicks a row of the quest list as the mouse would (for shots and tests).</summary>
    public void ClickRow(int index)
    {
        if (index < 0 || index >= rows.Count || rows[index].Widget is not ButtonWidget w) return;
        w.MouseOver(true); w.MouseDown(true); w.MouseDown(false); w.MouseOver(false);
    }

    /// <summary>Clicks a tab or sub-tab button as the mouse would (for shots and tests).</summary>
    public void ClickTab(bool sub, int index)
    {
        var bar = sub ? subTabs : tabs;
        if (index < 0 || index >= bar.Count || bar[index].Widget is not ButtonWidget w || !bar[index].Visible) return;
        w.MouseOver(true); w.MouseDown(true); w.MouseDown(false);
    }
}
