using System.Globalization;
using System.Xml.Linq;
using Ms2.Avm2;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIMessenger (ms2/client/ms2/uimessenger.cpp), the C++ side of a chat window (uimessengerdialog). The movie's own
/// MessengerDialog draws everything; this side sizes and aligns the window from its option document, fills the tab bar and
/// the chat target list, sets the screen setting of the selected tab and takes the window's keys.
/// UIMessengerBrowser::LoadDefaultWindows (:3149) makes one window per table/default/DefaultMessenger&lt;i&gt;.xml, named
/// messengerdialog&lt;i&gt;.xml.
/// Not ported yet: the character folder's saved windows (LoadWindows :3689, FUN_141a27570) and saving them (vfunc_4 :638);
/// chat lines (the browser's log, filled by the chat packets, RecvOp 0x1d); sending a line (SendChat :4208, which only
/// sends; the line shows when the server sends it back) and slash commands (FUN_141a10fd0); the input history; whisper,
/// emoticon, tab menu, attach/detach and the extra components (m_2e0 / m_2f0).
/// </summary>
public sealed class MessengerController : UiController
{
    public override string Movie => "uimessengerdialog";
    protected override string EntityMethod => "updateAdditiveButton";
    public override string TypeName => "MessengerDialog";      // GetTypeName (:604)

    /// <summary>Unk_MessengerChatType values the target list uses (BuildSubjectList :1807).</summary>
    public const int Social = 0, Party = 7, Guild = 8, World = 0xb, Channel = 0xc, Super = 0x10, GuildMegaphone = 0x12, Talk = 0x14;
    /// <summary>IsTypeHidden (:3614): super chat needs feature 0x19.</summary>
    public const int SuperChatFeature = 0x19;
    /// <summary>DIK codes vfunc_13 (:810) reads.</summary>
    public const int DikReturn = 0x1c, DikTab = 0xf;

    /// <summary>MessengerData::TalkTab: tabName, close, detach, additive and its panel's screen setting.</summary>
    public sealed class TalkTab
    {
        public string Name = "";
        public int Close = 1, Detach = 1, Additive;          // TalkTab() (messengerdata.cpp:706)
        public readonly ScreenSetting Screen = new();
        public bool Global;                                   // the "global" switch read with this tab
    }

    /// <summary>MessengerData::ScreenSetting (messengerdata.cpp:374): fontSize 17, backgroundAlpha 50, showDamage 1.</summary>
    public sealed class ScreenSetting { public int FontSize = 0x11, BackgroundAlpha = 0x32, ShowDamage = 1; }

    readonly XDocument doc;                                   // m_doc
    /// <summary>m_strName, the window's file name (messengerdialog&lt;i&gt;.xml).</summary>
    public readonly string Name;
    bool first;                                               // m_bFirst
    float offsetX, offsetY;                                   // m_fOffsetX / m_fOffsetY
    int alignType = 7;                                        // m_nAlignType
    int tabSelectedIndex, targetSelectedIndex;                // m_nTabSelectedIndex / m_nTargetSelectedIndex
    public readonly List<TalkTab> Tabs = new();               // m_tabs
    /// <summary>PanelSetting's s_nGlobal and the shared screen setting DAT_143adda48 (messengerdata.cpp:600, :626).</summary>
    bool global;
    readonly ScreenSetting globalScreen = new();
    /// <summary>UIMessengerBrowser::m_hiddenTypes (HideType, from CSceneStateGame::OnMessengerBrowserStamp).</summary>
    public readonly HashSet<int> HiddenTypes = new();
    /// <summary>CGameRule's world / channel chat discount rates (m_2b0 / m_2d0 Get(0, 0), gamerule.cpp:757, :766), set by
    /// game events; 0 = none.</summary>
    public uint WorldChatRate, ChannelChatRate;
    /// <summary>A key held now (Unk_MsgKeys::FUN_14116d7a0), by DIK code.</summary>
    public Func<int, bool> IsKeyDown = _ => false;

    readonly List<int> subjects = new();                      // m_subjects
    readonly SortedDictionary<int, int> subjectIndex = new(); // m_subjectIndex: list row -> type
    AsObject textInput, textOutput, buttonBar, subject, emoticonPreview;
    bool scrollEnd;                                           // m_bScrollEnd

    /// <summary>The window as UIMessengerBrowser makes it: its option document, its name and m_184 (first load).</summary>
    public MessengerController(XDocument doc, string name, bool first)
    {
        this.doc = doc;
        Name = name;
        this.first = first;
    }

    /// <summary>UIMessengerBrowser::IsTypeHidden (:3614).</summary>
    bool IsTypeHidden(int type) => (type == Super && !Model.IsFeatureEnabled(SuperChatFeature)) || HiddenTypes.Contains(type);

    /// <summary>
    /// vfunc_5 (:657), where UIDialog reads its settings file: the window's own document (m_doc). UIMessenger's
    /// OnLoadComplete (:763) takes its children before UIDialog::OnLoadComplete gets here.
    /// </summary>
    public override void LoadTransform()
    {
        textInput = Host.Child("m_textInput") as AsObject;
        textOutput = Host.Child("m_textOutput") as AsObject;
        buttonBar = Host.Child("m_buttonBar") as AsObject;
        subject = Host.Child("m_subject") as AsObject;
        emoticonPreview = Host.Child("m_chatEmoticonPreview") as AsObject;
        if (doc?.Root is { } root && root.Name.LocalName == "MessengerDialog") LoadConfig(root);
    }

    static void Attr(XElement e, string name, ref int v)
    {
        if ((string)e.Attribute(name) is { } s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) v = i;
    }

    static void Attr(XElement e, string name, ref float v)
    {
        if ((string)e.Attribute(name) is { } s && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) v = f;
    }

    /// <summary>vfunc_54 (:714): size, place, selections and the tabs, then the additive button and setSize.</summary>
    int LoadConfig(XElement cfg)
    {
        int width = 0, height = 0;
        Attr(cfg, "width", ref width);
        Attr(cfg, "height", ref height);
        Attr(cfg, "offsetX", ref offsetX);
        Attr(cfg, "offsetY", ref offsetY);
        Attr(cfg, "alignType", ref alignType);
        Attr(cfg, "tabSelectedIndex", ref tabSelectedIndex);
        Attr(cfg, "targetSelectedIndex", ref targetSelectedIndex);
        if (first)
        {
            tabSelectedIndex = 0;
            targetSelectedIndex = FirstSubjectIndex();       // the list is not built yet: -1
            first = false;
        }
        foreach (var t in cfg.Elements("Tab"))
        {
            var tab = new TalkTab();
            if (!ReadTab(tab, t)) { Tabs.Clear(); return 0; }
            if (!AddTab(tab)) return 0;
        }
        UpdateAdditiveButton();
        Host.Invoke("setSize", width, height);                // ASUIComponent::SetSizeInt (asuicomponent.cpp:188)
        return Tabs.Count;
    }

    /// <summary>TalkTab::FUN_1414827c0 (messengerdata.cpp:718) and PanelSetting::FUN_141482220 (:620); of the panel only the
    /// screen setting (ScreenSetting::FUN_141481480, :380) is kept here (the notice, system, require and subject filters
    /// choose the log lines a tab shows, and there are none yet).</summary>
    bool ReadTab(TalkTab tab, XElement t)
    {
        tab.Name = (string)t.Attribute("tabName") ?? tab.Name;
        Attr(t, "close", ref tab.Close);
        Attr(t, "detach", ref tab.Detach);
        Attr(t, "additive", ref tab.Additive);
        if (tab.Name.Length == 0) return false;
        // XmlChild(.., true) (tinyxmlhelper.cpp:101) adds a missing element, so a missing PanelSetting or Screen reads as
        // one without attributes
        var panel = t.Element("PanelSetting");
        int g = global ? 1 : 0;
        if (panel != null) Attr(panel, "global", ref g);
        global = g != 0;
        ReadScreen(tab.Screen, panel);
        if (global) ReadScreen(globalScreen, panel);
        tab.Global = global;
        return true;
    }

    static void ReadScreen(ScreenSetting s, XElement panel)
    {
        int fontSize = 0, alpha = 0;
        if (panel?.Element("Screen") is { } e)
        {
            Attr(e, "fontSize", ref fontSize);
            Attr(e, "backgroundAlpha", ref alpha);
            Attr(e, "showDamage", ref s.ShowDamage);
        }
        s.FontSize = fontSize;
        s.BackgroundAlpha = alpha;
    }

    /// <summary>PanelSetting::FUN_1414821b0 (messengerdata.cpp:600): the shared setting while s_nGlobal is set.</summary>
    ScreenSetting ScreenOf(TalkTab tab) => global ? globalScreen : tab.Screen;

    TalkTab FindTab(string name) => Tabs.FirstOrDefault(t => t.Name == name);     // FindTabByName (:989)

    /// <summary>AddTab (:1051): a new name joins m_tabs and the bar's data provider as {label, close, detach}.</summary>
    bool AddTab(TalkTab tab)
    {
        if (FindTab(tab.Name) != null) return false;
        Tabs.Add(tab);
        if (buttonBar != null && Host.Get(buttonBar, "dataProvider") is AsObject dp)       // ASButtonBar::AddItem (asbuttonbar.cpp:171)
            Host.Invoke(dp, "push", Host.Obj(("label", tab.Name), ("close", tab.Close), ("detach", tab.Detach)));
        return true;
    }

    /// <summary>UpdateAdditiveButton (:1754).</summary>
    void UpdateAdditiveButton()
    {
        Host.Set(Host.Entity, "m_additiveTab", Tabs.Any(t => t.Additive != 0) ? 1 : 0);
        Host.Invoke("updateAdditiveButton");
    }

    /// <summary>vfunc_43 (:799): with m_60 clear the window is placed by its align type and offsets (ASDisplayObject::vfunc_2).</summary>
    public override void Layout(double stageW, double stageH) => Host.Align(Host.Entity, alignType, (int)offsetX, (int)offsetY);

    /// <summary>OnLoadComplete (:763), after UIDialog's.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        Host.Invoke("setTopMostEnable", 1);                  // FUN_141923680(1) -> ASWindow::SetTopMostEnable (aswindow.cpp:31)
        RebuildSubjectList();
        if (textInput != null) Host.Set(textInput, "maxChars", (double)(float)Model.Tables.MaxChatLength);    // SetMaxChars
        if (buttonBar != null)
        {
            Host.Set(buttonBar, "focused", 1);
            Host.Set(buttonBar, "selectedIndex", tabSelectedIndex);
        }
        UpdateAdditiveButton();
        scrollEnd = true;
        ReselectCurrentTab();
    }

    /// <summary>BuildSubjectList (:1807): the types in list order (hidden ones left out) and the preferred order.</summary>
    void BuildSubjectList()
    {
        subjects.Clear();
        subjects.AddRange(new[] { Social, Channel, World, Super, Party, Guild, GuildMegaphone, Talk });
        subjectIndex.Clear();
        int n = 0;
        foreach (var t in new[] { Super, World, Channel, Social, Party, Guild, GuildMegaphone, Talk })
            if (!IsTypeHidden(t)) subjectIndex[n++] = t;
    }

    /// <summary>FUN_141a14110 (:1839): the list row of the first type in m_subjects that is not hidden; -1 = none.</summary>
    int FirstSubjectIndex()
    {
        foreach (var t in subjects)
            if (!IsTypeHidden(t)) return IndexOfType(t);
        return -1;
    }

    int IndexOfType(int type)                                 // FUN_141a141c0 (:1852)
    {
        foreach (var (i, t) in subjectIndex) if (t == type) return i;
        return -1;
    }

    /// <summary>FUN_141a14580 (:1913) / FUN_141a14500: the type of a list row; the first type, else 0x1b.</summary>
    int TypeAt(int index)
    {
        if (subjectIndex.TryGetValue(index, out var t)) return t;
        return subjectIndex.TryGetValue(FirstSubjectIndex(), out var f) ? f : 0x1b;
    }

    /// <summary>DAT_143aef3b8 (texttohtml.cpp:48), the super chat icon of an item without its own.</summary>
    const string SuperChatDefaultIcon = "export_common_icon_megaphone6";

    static uint Price(uint value, uint rate) => (uint)(value * (1.0 - rate / 10000.0) + 1e-06);    // FUN_140482050 (gamerule.cpp:360)

    /// <summary>
    /// RebuildSubjectList (:4518): the target list's rows ({frame, price, originPrice, label, color}) for the types not
    /// hidden, at most 8 rows shown, the saved target selected.
    /// </summary>
    void RebuildSubjectList()
    {
        BuildSubjectList();
        if (subject == null) return;
        var dp = Host.Get(subject, "dataProvider") as AsObject;
        if (dp != null) Host.Invoke(dp, "cleanUp");          // ClearItems -> ASDataProvider::CleanUp (asdataprovider.cpp:61)
        Host.Set(subject, "enabled", 1);
        var t = Model.Tables;
        var c = t.ChatTextColors;
        void Row(params (string, object)[] m) { if (dp != null) Host.Invoke(dp, "push", Host.Obj(m)); }
        string L(string key) => Host.Player.Translate?.Invoke(key) ?? key;                      // GFxManager::Localize
        // the super chat row (feature 0x19): the icon of the selected super chat item (MyPC FUN_1416639e0) when the
        // inventory holds it (FUN_140a5e2e0), else of item 0. The inventory is not modelled, so it is item 0, whose icon
        // is the default one (FUN_14183ea40, texttohtml.cpp:334: DAT_143aef3b8)
        if (!IsTypeHidden(Super))
            Row(("frame", "super"), ("iconPath", SuperChatDefaultIcon), ("label", L("$s_talkpanelsetting_super")), ("color", c["chatTextSuper"]));
        if (!IsTypeHidden(World))
            Row(("frame", "world"), ("price", Price(t.MeratConsumeWorldChat, WorldChatRate)), ("originPrice", t.MeratConsumeWorldChat),
                ("label", L("$s_talkpanelsetting_world")), ("color", c["chatTextWorld"]));
        if (!IsTypeHidden(Channel))
            Row(("frame", "channel"), ("price", Price(t.MeratConsumeChannelChat, ChannelChatRate)), ("originPrice", t.MeratConsumeChannelChat),
                ("label", L("$s_talkpanelsetting_channel")), ("color", c["chatTextChannel"]));
        if (!IsTypeHidden(Social)) Row(("label", L("$s_talkpanelsetting_social")), ("color", c["chatTextNormal"]));
        if (!IsTypeHidden(Party)) Row(("label", L("$s_talkpanelsetting_party")), ("color", c["chatTextParty"]));
        if (!IsTypeHidden(Guild)) Row(("label", L("$s_talkpanelsetting_guild")), ("color", c["chatTextGuild"]));
        if (!IsTypeHidden(GuildMegaphone))
            Row(("frame", "guildMegaphone"), ("label", L("$s_talkpanelsetting_guild_megaphone")), ("color", c["chatTextGuild"]));
        if (!subjectIndex.ContainsKey(targetSelectedIndex)) targetSelectedIndex = FirstSubjectIndex();
        int count = dp != null ? (int)Rt.ToNumber(Host.Get(dp, "length")) : 0;
        Host.Set(subject, "menuRowCount", (uint)Math.Min(count, 8));
        Host.Set(subject, "selectedIndex", targetSelectedIndex);
        Validate(subject);
        UpdateSuperWorldChatEffect();
    }

    /// <summary>UpdateSuperWorldChatEffect (:2062).</summary>
    void UpdateSuperWorldChatEffect()
    {
        int type = TypeAt(targetSelectedIndex);
        Host.Invoke("updateSuperWorldChatEffect", type == Super && !IsTypeHidden(type));
    }

    /// <summary>ASUIComponent::vfunc_24(true) (asuicomponent.cpp:323): invalidate, then validateNow.</summary>
    void Validate(AsObject c)
    {
        Host.Invoke(c, "invalidate");
        Host.Invoke(c, "validateNow");
    }

    /// <summary>ReselectCurrentTab (:1121): vfunc_97 with the bar's selected label (GetSelectedTabName :973).</summary>
    void ReselectCurrentTab() => SelectTab(buttonBar != null ? Rt.ToString(Host.Get(buttonBar, "selectedLabel")) : "");

    /// <summary>
    /// vfunc_97 (:2237): the output shows the tab's lines (the browser's log, none yet) at the tab's font size, the
    /// background takes the tab's alpha (SetBackgroundAlpha :1156), and the bar's selection is kept.
    /// </summary>
    void SelectTab(string name)
    {
        var tab = FindTab(name);
        if (tab == null) return;
        var screen = ScreenOf(tab);
        Host.Invoke("backgroundAlpha", (uint)screen.BackgroundAlpha);
        SetOutput("", true);
        if (buttonBar != null) tabSelectedIndex = (int)Rt.ToNumber(Host.Get(buttonBar, "selectedIndex"));
    }

    int MaxScrollV() => textOutput != null && Host.Get(textOutput, "textField") is AsObject tf ? (int)Rt.ToNumber(Host.Get(tf, "maxScrollV")) : 0;
    int OutputPosition() => (int)Rt.ToNumber(Host.Get(textOutput, "position"));

    /// <summary>FUN_141a139c0 (:1739): clear and/or append the output's html, staying at the end if it was there.</summary>
    void SetOutput(string html, bool clear)
    {
        if (textOutput == null) return;
        bool atEnd = OutputPosition() == MaxScrollV();      // ASTextArea::IsScrolledToEnd (astextarea.cpp:44)
        if (clear) Host.Set(textOutput, "htmlText", "");
        if (html.Length > 0) Host.Invoke(textOutput, "appendHtml", html);
        if (atEnd) Host.Set(textOutput, "position", MaxScrollV());
    }

    /// <summary>vfunc_15 (:874), each frame: a pending scroll to the end.</summary>
    public void Process()
    {
        if (!scrollEnd) return;
        if (textOutput != null && OutputPosition() != MaxScrollV()) Host.Set(textOutput, "position", MaxScrollV());
        scrollEnd = false;
    }

    bool InputFocused => textInput != null && Rt.ToNumber(Host.Get(textInput, "focused")) != 0;      // GetFocused (asuicomponent.cpp)
    /// <summary>The input has the focus (typing goes to this window).</summary>
    public bool InputActive => InputFocused;

    /// <summary>ASUIComponent::SetFocused (asuicomponent.cpp:123): losing it clears the stage focus.</summary>
    void SetFocused(AsObject c, int n)
    {
        Host.Set(c, "focused", n);
        if (n == 0) Host.Set(Host.Player.Stage, "focus", null);
    }

    /// <summary>
    /// vfunc_13 (:810), a key pressed while the window is shown: Return (FUN_141a17700) and Escape (FUN_141a12260); true =
    /// used. FUN_141a12f30 (:1604) lets keys through for the current window when the input is focused or Tab is not held
    /// (one window here: Unk_MdNames' current name is this one's).
    /// </summary>
    public bool OnKey(int dik, bool escape)
    {
        if (!InputFocused && IsKeyDown(DikTab)) return false;
        if (dik == DikReturn) { ReturnKey(); return true; }
        if (escape) return Release();
        return false;
    }

    /// <summary>FUN_141a17700 (:2446): an open emoticon preview takes Return while typing; else FUN_141a11e30.</summary>
    void ReturnKey()
    {
        if (textInput == null) return;
        if (emoticonPreview != null && InputFocused && Rt.ToBoolean(Host.Get(emoticonPreview, "visible")))
        {
            Host.Invoke("pushChatEmoticon");
            return;
        }
        Submit();
    }

    /// <summary>
    /// FUN_141a11e30 (:1532). Focused: the trimmed text goes to SubmitLine (not sent here, see the class notes) and the input
    /// is released; empty, it is only released. Not focused: the input takes the focus with the caret after its text (the
    /// browser's last channel m_1b0 is 0 until a line is sent, so no whisper or talk target).
    /// </summary>
    void Submit()
    {
        if (InputFocused)
        {
            Release();
            return;
        }
        int length = Rt.ToString(Host.Get(textInput, "text")).Length;
        SetFocused(textInput, 1);
        if (!InputFocused) SetFocused(textInput, 1);          // ASTextInput::FocusAndSetSelection (astextinput.cpp:264)
        Validate(textInput);
        if (Host.Get(textInput, "textField") is AsObject tf) Host.Invoke(tf, "setSelection", length, length);
        Validate(textInput);
    }

    /// <summary>FUN_141a12260 (:1578): a focused input loses the focus and its text; true = it was focused.</summary>
    bool Release()
    {
        if (!InputFocused) return false;
        SetFocused(textInput, 0);
        Host.Set(textInput, "htmlText", "<FONT></FONT>");
        Validate(textInput);
        return true;
    }

    /// <summary>vfunc_28 (:3943), the window's calls after UIDialog's.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        switch (name.ToLowerInvariant())
        {
            case "eventfocusin": return true;                  // vfunc_90: Unk_MdNames::FUN_141482c80(name), one window
            case "eventfocusout": return true;                 // vfunc_91: the history iterator back to the end
            case "eventchangetab": SelectTab(args.Length > 0 ? Rt.ToString(args[0]) : ""); return true;
            case "eventpresssubjecttab": UpdateSuperWorldChatEffect(); return true;    // the super chat dialog only for type 0x10
            case "updatealigntypeandoffset": UpdateAlignAndOffset(); return true;
        }
        return false;
    }

    /// <summary>FUN_141a0f180 (:1217) -> UIDialog::FUN_141926620 (uidialog.cpp:1056): the align type and offsets from the
    /// window's quarter of the stage.</summary>
    void UpdateAlignAndOffset()
    {
        float x = (float)Rt.ToNumber(Host.Get(Host.Entity, "x")), y = (float)Rt.ToNumber(Host.Get(Host.Entity, "y"));
        float w = (float)Math.Ceiling(Rt.ToNumber(Host.Get(Host.Entity, "width"))), h = (float)Math.Ceiling(Rt.ToNumber(Host.Get(Host.Entity, "height")));
        float hw = (int)Host.Player.StageW * 0.5f, hh = (int)Host.Player.StageH * 0.5f;
        if (x <= hw && y <= hh) { alignType = 5; offsetX = x; offsetY = y; }
        else if (x >= hw && y <= hh) { alignType = 6; offsetX = x + w - hw * 2f; offsetY = y; }
        else if (x <= hw && y >= hh) { alignType = 7; offsetX = x; offsetY = y + h - hh * 2f; }
        else if (x >= hw && y >= hh) { alignType = 8; offsetX = x + w - hw * 2f; offsetY = y + h - hh * 2f; }
    }
}
