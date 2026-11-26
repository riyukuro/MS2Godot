using System.Globalization;
using System.Xml.Linq;
using Ms2.Avm2;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIMessenger (ms2/client/ms2/uimessenger.cpp), the C++ side of a chat window (uimessengerdialog). The movie's own
/// MessengerDialog draws everything; this side sizes and aligns the window from its option document, fills the tab bar and
/// the chat target list, sets the screen setting of the selected tab, shows the lines of the shared log
/// (<see cref="MessengerBrowser"/>, which takes the chat packets' lines, RecvOp 0x1d) its tab's panel setting lets through,
/// and takes the window's keys.
/// UIMessengerBrowser::LoadDefaultWindows (:3149) makes one window per table/default/DefaultMessenger&lt;i&gt;.xml, named
/// messengerdialog&lt;i&gt;.xml.
/// Return sends the typed line (<see cref="SubmitLine"/>): a slash command through the client's command table
/// (<see cref="UserCommands"/>), else packet 0x11 to the selected target (<see cref="SendChat"/>, which only sends; the
/// line shows when the server sends it back).
/// Not ported yet: the character folder's saved windows (LoadWindows :3689, FUN_141a27570) and saving them (vfunc_4 :638);
/// the input history; the tab name (0x4e62) and emoticon (0x4f80) dialogs the window opens, the emoticon table and the
/// character's sticker sets its sends check; the extra components (m_2e0 / m_2f0).
/// </summary>
public sealed class MessengerController : UiController
{
    /// <summary>Neither UIMessengerBrowser's ctor (@0x141a20f90) nor UIMessenger's (@0x141a08af0) calls SetTweenEnabled: m_c4
    /// stays 0, no open / close tween.</summary>
    protected override bool TweenEnabled => false;

    public override string Movie => "uimessengerdialog";

    /// <summary>UIMessenger's ctor sets the flag 0 (uimessenger.cpp:595): vfunc_56, an always-on one.</summary>
    public override bool AlwaysOn => true;
    protected override string EntityMethod => "updateAdditiveButton";
    public override string TypeName => "MessengerDialog";      // GetTypeName (:604)

    /// <summary>Unk_MessengerChatType values the target list uses (BuildSubjectList :1807).</summary>
    public const int Social = 0, Party = 7, Guild = 8, World = 0xb, Channel = 0xc, Super = 0x10, GuildMegaphone = 0x12, Talk = 0x14;
    /// <summary>IsTypeHidden (:3614): super chat needs feature 0x19.</summary>
    public const int SuperChatFeature = 0x19;
    /// <summary>DIK codes vfunc_13 (:810) reads.</summary>
    public const int DikReturn = 0x1c, DikUp = 0x79, DikDown = 0x7e, DikTab = 0xf, DikSpace = 0x39;
    /// <summary>The whisper action (IsActionKeyPressed 0x49405, vfunc_13 :823; code 300037 in defaultkey.xml, the H key).</summary>
    public const long ActionWhisper = 0x49405;

    /// <summary>MessengerData::TalkTab: tabName, close, detach, additive and its panel setting (m_panel).</summary>
    public sealed class TalkTab
    {
        public string Name = "";
        public int Close = 1, Detach = 1, Additive;          // TalkTab() (messengerdata.cpp:706)
        public readonly PanelSetting Panel = new();
        public ScreenSetting Screen => Panel.Screen;
        public bool Global;                                   // s_nGlobal after this tab was read

        /// <summary>TalkTab::FUN_141482880 (messengerdata.cpp:730): tabName, close, detach and additive on the element, then
        /// its panel (<see cref="PanelSetting.Write"/>); false for a tab without a name.</summary>
        public bool Write(XElement e, MessengerBrowser shared, Func<int, bool> featureEnabled)
        {
            e.SetAttributeValue("tabName", Name);
            e.SetAttributeValue("close", Close);
            e.SetAttributeValue("detach", Detach);
            e.SetAttributeValue("additive", Additive);
            if (Name.Length == 0) return false;
            Panel.Write(e, shared, featureEnabled);
            return true;
        }
    }

    readonly XDocument doc;                                   // m_doc
    /// <summary>m_strName, the window's file name (messengerdialog&lt;i&gt;.xml).</summary>
    public readonly string Name;
    bool first;                                               // m_bFirst
    float offsetX, offsetY;                                   // m_fOffsetX / m_fOffsetY
    int alignType = 7;                                        // m_nAlignType
    int tabSelectedIndex, targetSelectedIndex;                // m_nTabSelectedIndex / m_nTargetSelectedIndex
    public readonly List<TalkTab> Tabs = new();               // m_tabs
    /// <summary>m_pBrowser, the log and panel statics all windows share.</summary>
    public readonly MessengerBrowser Browser;
    /// <summary>UIMessengerBrowser::m_hiddenTypes (HideType, from CSceneStateGame::OnMessengerBrowserStamp).</summary>
    public readonly HashSet<int> HiddenTypes = new();
    /// <summary>CGameRule's world / channel chat discount rates (m_2b0 / m_2d0 Get(0, 0), gamerule.cpp:757, :766), set by
    /// game events; 0 = none.</summary>
    public uint WorldChatRate, ChannelChatRate;
    /// <summary>CInputKeyHandler::FUN_14116d7a0(0xf) (uimessenger.cpp:1613), <see cref="InputBlock"/>.</summary>
    public Func<bool> InputBlocked = () => InputBlock.Active;

    readonly List<int> subjects = new();                      // m_subjects
    readonly SortedDictionary<int, int> subjectIndex = new(); // m_subjectIndex: list row -> type
    AsObject textInput, textOutput, buttonBar, subject, emoticonPreview;
    bool scrollEnd;                                           // m_bScrollEnd

    /// <summary>The window as UIMessengerBrowser makes it: its option document, its name, m_184 (first load) and the browser
    /// (a window of its own when none is given); the window joins the browser's m_windows.</summary>
    public MessengerController(XDocument doc, string name, bool first, MessengerBrowser browser = null)
    {
        this.doc = doc;
        Name = name;
        this.first = first;
        Browser = browser ?? new MessengerBrowser();
        Browser.Windows.Add(this);
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
        Browser.Model ??= Model;
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

    /// <summary>TalkTab::FUN_1414827c0 (messengerdata.cpp:718) and its panel (<see cref="PanelSetting.Read"/>).</summary>
    bool ReadTab(TalkTab tab, XElement t)
    {
        tab.Name = (string)t.Attribute("tabName") ?? tab.Name;
        Attr(t, "close", ref tab.Close);
        Attr(t, "detach", ref tab.Detach);
        Attr(t, "additive", ref tab.Additive);
        if (tab.Name.Length == 0) return false;
        // XmlChild(.., true) (tinyxmlhelper.cpp:101) adds a missing element, so a missing PanelSetting reads as one
        // without attributes or children
        tab.Panel.Read(t.Element("PanelSetting"), Browser, Model.IsFeatureEnabled);
        tab.Global = Browser.Global;
        return true;
    }

    /// <summary>PanelSetting::FUN_1414821b0 (messengerdata.cpp:600): the shared setting while s_nGlobal is set.</summary>
    ScreenSetting ScreenOf(TalkTab tab) => Browser.ScreenOf(tab.Panel);

    TalkTab FindTab(string name) => Tabs.FirstOrDefault(t => t.Name == name);     // FindTabByName (:989)

    /// <summary>GetSelectedTabName (:973): ASButtonBar::GetSelectedLabel (asbuttonbar.cpp:219), the label of the data
    /// provider's row at selectedIndex (GetLabelAt :226); "" without the bar, out of range or for a label not a string.</summary>
    string SelectedTabName
    {
        get
        {
            if (buttonBar == null) return "";
            int index = (int)Rt.ToNumber(Host.Get(buttonBar, "selectedIndex"));
            if (index < 0 || Host.Get(buttonBar, "dataProvider") is not AsObject dp) return "";
            if (index >= (int)Rt.ToNumber(Host.Get(dp, "length"))) return "";
            return Host.Get(dp, index.ToString(CultureInfo.InvariantCulture)) is AsObject row && Host.Get(row, "label") is string label ? label : "";
        }
    }

    /// <summary>GetCurrentTab (:981): the only tab, else the bar's selected one.</summary>
    public TalkTab CurrentTab => Tabs.Count == 1 ? Tabs[0] : FindTab(SelectedTabName);

    /// <summary>FUN_141a131a0 (:1646): a line the tab does not show (no tab shows none).</summary>
    static bool Hidden(TalkTab tab, int kind) => tab == null || tab.Panel.Hidden(kind);

    /// <summary>AddTab (:1051): a new name joins m_tabs and the bar's data provider as {label, close, detach}.</summary>
    bool AddTab(TalkTab tab)
    {
        if (FindTab(tab.Name) != null) return false;
        Tabs.Add(tab);
        if (buttonBar != null && Host.Get(buttonBar, "dataProvider") is AsObject dp)       // ASButtonBar::AddItem (asbuttonbar.cpp:171)
            Host.Invoke(dp, "push", Host.Obj(("label", tab.Name), ("close", tab.Close), ("detach", tab.Detach)));
        return true;
    }

    /// <summary>vfunc_86 (@0x141a0f040, :1200): no tab but <paramref name="except"/> has that name.</summary>
    public bool HasNoTabNamed(TalkTab except, string name) => !Tabs.Any(t => t != except && t.Name == name);

    /// <summary>The bar's data provider row whose label is <paramref name="label"/> (ASButtonBar::FUN_1422e3590 /
    /// RemoveItemByLabel, asbuttonbar.cpp:192, :255), -1 for none.</summary>
    int BarIndexOf(string label)
    {
        if (Host.Get(buttonBar, "dataProvider") is not AsObject dp) return -1;
        int n = (int)Rt.ToNumber(Host.Get(dp, "length"));
        for (int i = 0; i < n; i++)
            if (Host.Get(dp, i.ToString(CultureInfo.InvariantCulture)) is AsObject row && Host.Get(row, "label") is string l && l == label)
                return i;
        return -1;
    }

    /// <summary>SelectTabByName (@0x141a0e2a0, :1016): the bar takes the focus, selects the row of that label
    /// (FUN_1422e3590: SetSelectedIndex) and validates.</summary>
    public void SelectTabByName(string name)
    {
        if (buttonBar == null) return;
        SetFocused(buttonBar, 1);
        int i = BarIndexOf(name);
        if (i >= 0) Host.Set(buttonBar, "selectedIndex", i);
        Validate(buttonBar);
    }

    /// <summary>RemoveTabByName (@0x141a0e350, :1028): the tab leaves m_tabs and the bar (RemoveItemByLabel: the data
    /// provider's splice), which validates and updates the additive button; false for no such tab.</summary>
    public bool RemoveTabByName(string name)
    {
        var tab = FindTab(name);
        if (tab == null) return false;
        Tabs.Remove(tab);
        if (buttonBar != null)
        {
            int i = BarIndexOf(name);
            if (i >= 0 && Host.Get(buttonBar, "dataProvider") is AsObject dp) Host.Invoke(dp, "splice", i, 1);
            Validate(buttonBar);
            UpdateAdditiveButton();
        }
        return true;
    }

    /// <summary>InsertTab (@0x141a0e650, :1072): <see cref="AddTab"/>, then the bar validates and the additive button updates.</summary>
    public bool InsertTab(TalkTab tab)
    {
        if (tab == null || !AddTab(tab)) return false;
        if (buttonBar != null) { Validate(buttonBar); UpdateAdditiveButton(); }
        return true;
    }

    /// <summary>CreateTab (@0x141a0e810, :1095): a new tab with those flags, inserted.</summary>
    public bool CreateTab(string name, int close, int detach, int additive) =>
        InsertTab(new TalkTab { Name = name, Close = close, Detach = detach, Additive = additive });

    /// <summary>FUN_141a13d20 (@0x141a13d20, :1792): the first common 0x57d formatted with 1..99 no window's tab has, else
    /// common 0x57d itself.</summary>
    string NewTabName()
    {
        for (int i = 1; i < 100; i++)
        {
            string name = Ms2.Quest.ClientFormat.Format(Model.Tables.Common(0x57d), i);
            if (Browser.TabNameFree(null, name)) return name;
        }
        return Model.Tables.Common(0x57d);
    }

    /// <summary>vfunc_92 (@0x141a15f60, :2154), eventAdditiveTab: while the tab name dialog is closed, a new tab of the next
    /// free name (closable, detachable), selected; the state's new tab mode is 2 for this window and the dialog opens.
    /// The window's save (vfunc_4) is not ported.</summary>
    public void AddAdditiveTab()
    {
        if (!Browser.IsNewTabDialogClosed()) return;
        string name = NewTabName();
        if (!CreateTab(name, 1, 1, 0)) return;
        SelectTabByName(name);
        Browser.NewTabMode = 2;
        Browser.NewTabWindow = this;
        Browser.OpenNewTabDialog?.Invoke();
    }

    /// <summary>vfunc_96 (@0x141a16310, :2217), eventTabMenu: 0 = the tab name dialog for a new tab (mode 1, this window);
    /// 1 = the selected tab is removed and the window closes when that was its last (CloseEmptyWindow).</summary>
    public void TabMenu(int menu)
    {
        if (menu == 0)
        {
            Browser.NewTabMode = 1;
            Browser.NewTabWindow = this;
            Browser.OpenNewTabDialog?.Invoke();
        }
        else if (menu == 1 && RemoveTabByName(SelectedTabName))
            Browser.CloseEmptyWindow(Name);
    }

    /// <summary>vfunc_98 (@0x141a165c0, :2261), eventAttachTab: the tab moves to the window of that name, which selects it;
    /// this window closes when it was its last tab.</summary>
    public void AttachTab(string name, string window)
    {
        var tab = FindTab(name);
        if (tab == null) return;
        var target = Browser.FindMessengerByName(window);
        if (target == null) return;
        if (!RemoveTabByName(tab.Name)) return;
        if (target.InsertTab(tab)) target.SelectTabByName(tab.Name);
        Browser.CloseEmptyWindow(Name);
    }

    /// <summary>FUN_141a16730 (@0x141a16730, :2283), eventDetachTab: with two tabs or more, the tab leaves this window for a
    /// new one at that place and size with that target selected (CreateWindowWithTabs); this window reselects its tab.</summary>
    public void DetachTab(string name, int x, int y, int width, int height, int target)
    {
        var tab = FindTab(name);
        if (tab == null || Tabs.Count < 2) return;
        if (!RemoveTabByName(tab.Name)) return;
        Browser.CreateWindowWithTabs(new[] { tab }, x, y, width, height, target);
        ReselectCurrentTab();
    }

    /// <summary>SendEmoticon (@0x141a0fe20, :1416): a usable emoticon goes out as ChatSticker 3 with this window's name and
    /// the selected type; false when it is not usable (<see cref="MessengerBrowser.IsEmoticonUsable"/>).</summary>
    public bool SendEmoticon(int id)
    {
        if (!Browser.IsEmoticonUsable(id)) return false;
        Browser.SendSticker?.Invoke(Ms2.Net.Packets.ChatStickerRequest.Send(id, Name, SelectedType()));
        return true;
    }

    /// <summary>SetEmoticonFavorite (@0x141a10490, :1477): ChatSticker 5 (favorite) or 6 for a usable emoticon.</summary>
    public bool SetEmoticonFavorite(int id, bool favorite)
    {
        if (!Browser.IsEmoticonUsable(id)) return false;
        Browser.SendSticker?.Invoke(Ms2.Net.Packets.ChatStickerRequest.SetFavorite(id, favorite));
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

    /// <summary>ReselectCurrentTab (:1121): vfunc_97 with the bar's selected label.</summary>
    public void ReselectCurrentTab() => SelectTab(SelectedTabName);

    /// <summary>
    /// vfunc_97 (:2237): the output shows the log's lines the tab lets through at the tab's font size (ReplaceFontSize
    /// :1143), the background takes the tab's alpha (SetBackgroundAlpha :1156), the bar's selection is kept, and the
    /// browser counts the whole log as shown.
    /// </summary>
    void SelectTab(string name)
    {
        var tab = FindTab(name);
        if (tab == null) return;
        var html = new System.Text.StringBuilder();
        foreach (var log in Browser.Logs)
            if (!Hidden(tab, log.Kind)) html.Append(log.Msg);
        var screen = ScreenOf(tab);
        string text = MessengerBrowser.ReplaceFontSize((uint)screen.FontSize, html.ToString());
        Host.Invoke("backgroundAlpha", (uint)screen.BackgroundAlpha);
        SetOutput(text, true);
        if (buttonBar != null) tabSelectedIndex = (int)Rt.ToNumber(Host.Get(buttonBar, "selectedIndex"));
        Browser.Drawn();
    }

    /// <summary>vfunc_88 (:2128): a new line the current tab shows is appended at its font size; false = no tab.</summary>
    public bool AppendLine(string msg, int kind)
    {
        var tab = CurrentTab;
        if (tab == null) return false;
        if (Hidden(tab, kind)) return true;
        SetOutput(MessengerBrowser.ReplaceFontSize((uint)ScreenOf(tab).FontSize, msg), false);
        return true;
    }

    /// <summary>
    /// vfunc_87 (@0x141a15ae0, :2094), a whisper came in (UIMessengerBrowser::vfunc_84): nothing without tabs or a movie;
    /// else the names of the tabs whose subject setting shows whispers (FUN_141482200 m_nWhisper), the current tab left
    /// out, go to the movie's whisperFrom as an array, when there is one. The text and the whisper window are not read.
    /// </summary>
    public void WhisperFrom(string text, string window)
    {
        if (Tabs.Count == 0 || Host?.Entity == null) return;
        var current = CurrentTab;
        var names = new List<object>();
        foreach (var tab in Tabs)
            if (tab.Panel["Subject", "whisper"] != 0 && tab != current) names.Add(tab.Name);
        if (names.Count > 0) Host.Invoke("whisperFrom", names);
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
    /// used. FUN_141a12f30 (:1604) lets keys through only for the current window (Unk_MdNames' current name,
    /// <see cref="MessengerBrowser.GetCurrentName"/>; a focused group window takes it), and then when the input is
    /// focused or input is not blocked (<see cref="InputBlocked"/>; FUN_14116d7a0 with mask 0xf, a text field focused or the
    /// loading screen up, not a key).
    /// </summary>
    public bool OnKey(int dik, bool escape)
    {
        if (Browser.GetCurrentName() != Name) return false;
        if (!InputFocused && InputBlocked()) return false;
        if (dik == DikReturn) { ReturnKey(); return true; }
        if (!escape && dik != 0 && Model.Keys.KeysOfAction(ActionWhisper).Contains(dik)) return WhisperKey();
        if (dik == DikSpace) return SpaceKey();
        if (escape) return Release();
        if (dik == DikUp && InputFocused) { HistoryKey(true); return true; }
        if (dik == DikDown && InputFocused) { HistoryKey(false); return true; }
        if (dik == DikTab) return TabKey();
        return false;
    }

    /// <summary>
    /// vfunc_13 (:823), the whisper action key: with the input not focused it takes the focus and starts a whisper to the
    /// browser's last whisper target (BeginWhisper, :2482), and the key is used; with it focused the key is not used (the
    /// letter goes to the field).
    /// </summary>
    bool WhisperKey()
    {
        if (textInput == null || InputFocused) return false;
        SetFocused(textInput, 1);
        BeginWhisper(Browser.WhisperTarget);
        return true;
    }

    /// <summary>
    /// vfunc_13 (:834), Space (0x39) while a text field has the focus: FUN_141a14600 (:1922) acts on the type of the
    /// slash command typed so far (<see cref="InputChatType"/>): a type of the target list selects it (SelectSubject, which
    /// takes the command word off the line), a whisper (4) starts "whisper command + last target", a talk (0x14) the same
    /// with the club command; other types do nothing. The key is used either way. STILL-OPEN: the client's test is
    /// CUIService::IsTextFieldFocused (any field); here this window's input has to be focused.
    /// </summary>
    bool SpaceKey()
    {
        if (textInput == null || !InputFocused) return false;
        switch (InputChatType())
        {
            case Social: case Party: case Guild: case World: case Channel: case Super: case GuildMegaphone:
                SelectSubject(InputChatType());
                break;
            case 4: BeginWhisper(Browser.WhisperTarget); break;
            case Talk: BeginTalk(Browser.TalkTarget); break;
        }
        return true;
    }

    /// <summary>
    /// GetInputChatType (@0x141a14250, :1969): the trimmed input text under 2 characters, or not starting with "/", is 0x1b
    /// (the debug-mode emoticon alias, 0x18, is not modelled); a slash command gives its chat type, the first shown
    /// type (FUN_141a14500) when that one is hidden.
    /// </summary>
    int InputChatType()
    {
        string text = UserCommands.Trim(Rt.ToString(Host.Get(textInput, "text")));
        if (text.Length < 2 || text[0] != '/') return UserCommands.NoChatType;
        int type = Commands.ChatType(text);
        return IsTypeHidden(type) ? TypeAt(FirstSubjectIndex()) : type;
    }

    /// <summary>
    /// vfunc_13 (:810), Tab (0xf) after Return, Escape, Up and Down: with the input not focused it is not used. With it
    /// focused, the client first completes the slash command popup's first match (m_strLastWhisper, set by vfunc_74 :4116 /
    /// :4177 from the debug command table); that popup is not ported, so the string is always empty here, and Tab goes on to
    /// FUN_141a14760 (:1958): the next target.
    /// </summary>
    bool TabKey()
    {
        if (textInput == null || !InputFocused) return false;
        NextTarget();
        return true;
    }

    /// <summary>
    /// FUN_141a14760 (:1958): the target list's next row, the first after the last (rows counted by menuRowCount, at most
    /// eight); the super chat row without a super chat item (<see cref="MessengerBrowser.HasSuperChatItem"/>) is skipped to
    /// the world row, or to the first type when the world is hidden; then SelectSubject (:1946).
    /// </summary>
    void NextTarget()
    {
        if (subject == null) return;
        uint count = (uint)Rt.ToNumber(Host.Get(subject, "menuRowCount"));
        uint next = (uint)Rt.ToNumber(Host.Get(subject, "selectedIndex")) + 1;
        if (next >= count) next = 0;
        int type = TypeAt((int)next);
        if (type == Super && !Browser.HasSuperChatItem())
            type = IsTypeHidden(World) ? TypeAt(FirstSubjectIndex()) : World;
        SelectSubject(type);
    }

    /// <summary>
    /// FUN_141a13c20 (:1775), Up (0x79) or Down (0x7e) with the input focused: the browser's next history entry
    /// (<see cref="MessengerBrowser.GetHistoryEntry"/>) replaces the kept links and the input text, the caret after it. In
    /// vfunc_13 (:810) these keys come after Return and Escape. vfunc_94 after it is empty in the client.
    /// </summary>
    void HistoryKey(bool up)
    {
        if (textInput == null || !InputFocused) return;
        if (!Browser.GetHistoryEntry(up, out var entry)) return;
        inputLinks.Clear();
        inputLinks.AddRange(entry.Links);
        SetInputText(entry.Text, true);
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
    /// FUN_141a11e30 (:1532), Return. Focused: the links not put there by <see cref="AppendToInput"/> removed
    /// (<see cref="RemoveUnlistedLinks"/>), the text with "&lt;" / "&gt;" escaped (FUN_141842320) and the html text
    /// without its P, BR, TEXTFORMAT and FONT tags (FUN_140d2c650); the trimmed text, unless empty, goes to
    /// <see cref="SubmitLine"/>; then the input is released (FUN_141a12260). The IME composition string the client
    /// appends (GetImeCompositionString(2)) is not kept here. Not focused (after vfunc_78,
    /// the extra components, not ported): after a whisper (the browser's last channel 4) the input starts a whisper to
    /// its target, after a club line (0x1a) a talk to its club, else it takes the focus with the caret after its text.
    /// </summary>
    void Submit()
    {
        if (InputFocused)
        {
            RemoveUnlistedLinks();
            string text = Escape(Rt.ToString(Host.Get(textInput, "text")));
            string html = MessengerBrowser.RemoveChatTags(Rt.ToString(Host.Get(textInput, "htmlText")));
            text = UserCommands.Trim(text);
            if (text.Length > 0) SubmitLine(text, html, true);
            Release();
            return;
        }
        if (Browser.LastChannel == 4) { SetFocused(textInput, 1); BeginWhisper(Browser.WhisperTarget); return; }
        if (Browser.LastChannel == 0x1a) { SetFocused(textInput, 1); BeginTalk(Browser.TalkTarget); return; }
        int length = Rt.ToString(Host.Get(textInput, "text")).Length;
        SetFocused(textInput, 1);
        FocusAndSetSelection(length, length);
        Validate(textInput);
    }

    /// <summary>m_inputHistory (+0x1f8): the links <see cref="AppendToInput"/> put in the input; cleared when the input is
    /// released (FUN_141a12260); the client also clears them in vfunc_67 (:622), where the port drops the controller.</summary>
    readonly List<string> inputLinks = new();
    public IReadOnlyList<string> InputLinks => inputLinks;

    /// <summary>
    /// UIMessenger::AppendToInput (@0x141a0ec30, :1164): nothing when the input's text is already maxChars long (GetMaxChars)
    /// or the html with the link and a space would be longer without its tags (FUN_141a14ce0, :2046); else that html is
    /// set and validated, the caret goes after the text (FocusAndSetSelection) when there is text, and the link is kept.
    /// STILL-OPEN: the vfunc_78 call before the caret (the extra components) is not ported.
    /// </summary>
    public void AppendToInput(string link)
    {
        if (textInput == null) return;
        int max = (int)Rt.ToNumber(Host.Get(textInput, "maxChars"));
        if (Rt.ToString(Host.Get(textInput, "text")).Length >= max) return;
        string html = Rt.ToString(Host.Get(textInput, "htmlText")) + link + " ";
        if (!FitsWithoutTags(html, max)) return;
        Host.Set(textInput, "htmlText", html);
        Validate(textInput);
        int length = Rt.ToString(Host.Get(textInput, "text")).Length;
        if (length > 0) FocusAndSetSelection(length, length);
        Validate(textInput);
        inputLinks.Add(link);
    }

    /// <summary>FUN_141a14ce0 (:2046): each "&lt;" .. next "&gt;" removed, from the first "&lt;" again each time (an
    /// unclosed one stays); true when what is left is at most <paramref name="max"/> long.</summary>
    public static bool FitsWithoutTags(string text, int max)
    {
        int open;
        while ((open = text.IndexOf('<')) != -1)
        {
            int close = text.IndexOf('>', open + 1);
            if (close == -1) break;
            text = text.Remove(open, close - open + 1);
        }
        return text.Length <= max;
    }

    /// <summary>
    /// FUN_140d2c2a0 (unit_140d2a7e0_chatlinkhelpers.cpp:423), on Return and eventTextInput: with the input focused, links
    /// kept and html in it, each "&lt;A HREF=" .. "/A&gt;" (FindLinks) that is not one of the kept links
    /// (<see cref="MessengerBrowser.IsListedLink"/>) is removed (every copy); if any was, the html is set and validated,
    /// and an input left without text gets "&lt;Font&gt;&lt;/Font&gt;". A hand-typed link does not survive.
    /// </summary>
    void RemoveUnlistedLinks()
    {
        if (textInput == null || !InputFocused || inputLinks.Count == 0) return;
        string html = Rt.ToString(Host.Get(textInput, "htmlText"));
        if (string.IsNullOrEmpty(html)) return;
        bool changed = false;
        foreach (var link in MessengerBrowser.FindLinks(html, "<A HREF=", "/A>"))
        {
            if (MessengerBrowser.IsListedLink(link, inputLinks)) continue;
            html = html.Replace(link, "", StringComparison.Ordinal);
            changed = true;
        }
        if (!changed) return;
        Host.Set(textInput, "htmlText", html);
        Validate(textInput);
        if (Rt.ToString(Host.Get(textInput, "text")).Length == 0)
        {
            Host.Set(textInput, "htmlText", "<Font></Font>");
            Validate(textInput);
        }
    }

    /// <summary>ASTextInput::FocusAndSetSelection (astextinput.cpp:264): the focus if it has none, then the text field's
    /// selection.</summary>
    void FocusAndSetSelection(int begin, int end)
    {
        if (!InputFocused) SetFocused(textInput, 1);
        Validate(textInput);
        if (Host.Get(textInput, "textField") is AsObject tf) Host.Invoke(tf, "setSelection", begin, end);
    }

    /// <summary>FUN_141842320 (texttohtml.cpp:708): "&lt;" and "&gt;" as their entities.</summary>
    public static string Escape(string text) => (text ?? "").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>HasChatLink (@0x140d2aa60, unit_140d2a7e0_chatlinkhelpers.cpp:208): the html holds an item, achievement,
    /// bill or meret market link (found past its first character).</summary>
    public static bool HasChatLink(string html)
    {
        html ??= "";
        foreach (var e in new[] { "event:itemTooltip", "event:achieveTooltip", "event:billDialog", "event:searchFromMeratmarket" })
            if (html.IndexOf(e, StringComparison.Ordinal) > 0) return true;
        return false;
    }

    /// <summary>RemoveSubstring (@0x141a14c40): the first (case-sensitive) part removed; false without it.</summary>
    static bool RemoveSubstring(ref string text, string part)
    {
        int pos = text.IndexOf(part, StringComparison.Ordinal);
        if (pos < 0) return false;
        text = text.Remove(pos, part.Length);
        return true;
    }

    /// <summary>
    /// SubmitLine (@0x141a0f1d0, :1223): a line with links is sent as its html. A line starting with "/" is a command
    /// (<see cref="RunCommand"/>); else the selected target's type (FUN_141a144a0) unless FUN_141a12380 asks first, sent
    /// through FUN_141a12e40 (SendChat with no target). With bHistory (Return, FUN_141a11e30; the emoticon line passes 0)
    /// a used command (its text) or a sent line (the html when it has links) goes to the browser's history
    /// (<see cref="MessengerBrowser.RecordHistory"/>) with the links the input holds.
    /// </summary>
    public void SubmitLine(string text, string html, bool history = false)
    {
        bool links = HasChatLink(html);
        string send = links ? html : text;
        if (text.Length > 0 && text[0] == '/')
        {
            if (RunCommand(text, html, links) != 0 && history) Browser.RecordHistory(text, inputLinks);
            return;
        }
        int type = SelectedType();
        if (Browser.ConfirmSend(type, send)) return;
        SendChat(type, send, "", 0);
        if (history) Browser.RecordHistory(send, inputLinks);
    }

    /// <summary>FUN_141a144a0 (:1896): the type of the target list's selected row, else the first type.</summary>
    int SelectedType() => subject != null ? TypeAt((int)Rt.ToNumber(Host.Get(subject, "selectedIndex"))) : TypeAt(FirstSubjectIndex());

    UserCommands Commands => Model?.Tables.UserCommands ?? UserCommands.Empty;

    /// <summary>
    /// FUN_141a10fd0 (:4364), a slash command line; 1 = used. An emotion's command, then a user command function
    /// (CUserCommandCli::Execute) runs; else the first word's chat type (FUN_141a11c60). A target type selects that target
    /// (and with super chat shown and selected, opens its dialog); a club line takes the club name and the rest as the
    /// message (an unknown club gets notice 0x50), a whisper the target and the rest (no target gets notice 0x4e); with
    /// links the message is the html without "command target ". Then SendChat.
    /// </summary>
    public int RunCommand(string text, string html, bool links)
    {
        if (Browser.ExecuteEmotion(text)) return 1;
        if (Commands.Execute(text, Browser.CommandHooks) != 0) return 1;
        int pos = 0;
        string command = UserCommands.Tokenize(text, " ", ref pos);
        if (CommandChat(command, text, out int type)) return 1;
        string msg = "", target = "";
        long clubId = 0;
        switch (type)
        {
            case Social or Party or Guild or 9 or World or Channel or Super or GuildMegaphone:
                SelectSubject(type);
                if (!IsTypeHidden(Super) && SelectedType() == Super) Browser.OpenSuperChatDialog?.Invoke();
                return 1;
            case Talk:
            {
                string club = UserCommands.Tokenize(text, " ", ref pos).Trim('"');
                msg = UserCommands.Trim(UserCommands.Mid(text, pos));
                if (msg.Length == 0) return 0;
                clubId = Browser.ClubIdByName(club);
                if (clubId == 0) { Browser.ShowSystemNotice?.Invoke(Model.Tables.Common(0x50), 1, true); return 0; }
                if (links)
                {
                    msg = html;
                    if (!RemoveSubstring(ref msg, command + " " + club + " ")) return 0;
                }
                if (msg.Length == 0) return 0;
                break;
            }
            case 4:
                target = UserCommands.Tokenize(text, " ", ref pos).Trim('"');
                msg = UserCommands.Trim(UserCommands.Mid(text, pos));
                if (msg.Length == 0) return 0;
                if (target.Length == 0) { Browser.ShowSystemNotice?.Invoke(Model.Tables.Common(0x4e), 1, true); return 0; }
                if (links)
                {
                    msg = html;
                    if (!RemoveSubstring(ref msg, command + " " + target + " ")) return 0;
                }
                if (msg.Length == 0 || target.Length == 0) return 0;
                break;
        }
        SendChat(type, msg, target, clubId);
        return 1;
    }

    /// <summary>
    /// FUN_141a11c60 (:1509): the command's chat type (0x1b for none; an empty command is used up as is). A command with a
    /// function has every "command" in the line replaced by "/function". No type, a command (10) or a function (0x15)
    /// line goes out as it is with the selected target's type (FUN_141a11be0); true = sent.
    /// </summary>
    bool CommandChat(string command, string text, out int type)
    {
        type = UserCommands.NoChatType;
        if (command.Length == 0) return true;
        var info = Commands.Find(command);
        int t = UserCommands.NoChatType;
        if (info != null)
        {
            t = info.ChatType;
            if (info.Function.Length > 0) text = text.Replace(command, "/" + info.Function, StringComparison.Ordinal);
        }
        type = t;
        if (t is UserCommands.NoChatType or UserCommands.CommandType or UserCommands.FunctionType)
        {
            SendAsIs(SelectedType(), text);
            return true;
        }
        return false;
    }

    /// <summary>FUN_141a11be0: the line sent as the type's CommandChat, no other test; the last channel is its kind.</summary>
    void SendAsIs(int type, string text)
    {
        Browser.SendUserChat(new Ms2.Net.Packets.UserChatRequest(type, text, "", 0));
        Browser.LastChannel = ChatHtml.Kind(type);
    }

    /// <summary>
    /// SendChat (@0x141a10660, :4208), nothing without the local player. STILL-OPEN: CBanTextChecker's MaskBannedWords and
    /// FilterReservedWords are not ported. By the type's kind (FUN_141482f00), a non-empty line (kind 1 even an empty
    /// one) goes out through FUN_141276f80: wedding / super (0x17; type 0x19 with feature 0x104 and a WeddingChat super
    /// chat item), world, channel and normal as the type; a whisper as 4, keeping its target; party as 7 in a party,
    /// else notice 0x335; guild as 8 in a guild, else 0x388; guild megaphone as 0x12 in a guild with permission 0x800
    /// (else 0x388 / 0x415); a club line as 0x14 with the club id for a known club, keeping its name, else 0x36b. Each
    /// send sets the browser's last channel; then the idle timer is reset.
    /// </summary>
    public void SendChat(int type, string text, string target, long clubId)
    {
        if (Model?.HasLocalPlayer != true) return;
        var b = Browser;
        int channel = ChatHtml.Kind(type);
        text ??= ""; target ??= "";
        bool Send(int kind, long value = 0)
        {
            b.SendUserChat(new Ms2.Net.Packets.UserChatRequest(kind, text, target, value));
            b.LastChannel = channel;
            return true;
        }
        switch (channel)
        {
            case 0x17:
                if (text.Length > 0)
                {
                    if (b.IsFeatureEnabled(MessengerBrowser.WeddingFeature) && b.SuperChatItemFunction() == "WeddingChat") type = 0x19;
                    Send(type);
                }
                break;
            case 0x15 or 0x16:
                if (text.Length > 0) Send(type);
                break;
            case 1:
                Send(type);
                break;
            case 4:
                if (text.Length > 0) { Send(4); b.WhisperTarget = target; }
                break;
            case 2:
                if (text.Length > 0) { if (b.IsInParty()) Send(Party); else b.Notice(0x335); }
                break;
            case 3:
                if (text.Length > 0) { if (b.IsInGuild()) Send(Guild); else b.Notice(0x388); }
                break;
            case 0x18:
                if (text.Length == 0) break;
                if (!b.IsInGuild()) { b.Notice(0x388); break; }
                if (!b.HasGuildPermission(Model.Player.Name ?? "", 0x800)) { b.Notice(0x415); break; }
                Send(GuildMegaphone);
                break;
            case 0x1a:
                if (text.Length > 0 && clubId != 0)
                {
                    if (b.HasClub(clubId)) { Send(Talk, clubId); b.TalkTarget = b.ClubName(clubId) ?? ""; }
                    else b.Notice(0x36b);
                }
                break;
        }
        b.ResetIdleTimer?.Invoke();
    }

    /// <summary>SelectSubject (@0x141a146d0, :1946): only for the current window (FUN_141a12f30, see <see cref="OnKey"/>);
    /// the type's row selected, then vfunc_93.</summary>
    void SelectSubject(int type)
    {
        if (!InputFocused && InputBlocked()) return;
        if (subject == null) return;
        Host.Set(subject, "selectedIndex", IndexOfType(type));
        SubjectChanged();
    }

    /// <summary>
    /// vfunc_93 (:2171), the target changed (also eventChangeTalkTarget): a line starting with "/" loses its first word
    /// and the space after it (the whole html without a space), the input gets the html back with the caret at the end,
    /// and the selected row becomes the saved target. STILL-OPEN: for a super, world or channel target the client then
    /// calls GetClientPlatform()->vfunc_43(2, false, 1000), not identified.
    /// </summary>
    void SubjectChanged()
    {
        if (textInput == null) return;                       // FUN_1422dc2d0: the movie is loaded
        string html = Rt.ToString(Host.Get(textInput, "htmlText"));
        string text = Rt.ToString(Host.Get(textInput, "text"));
        if (text.Length > 0 && text[0] == '/')
        {
            int space = text.IndexOf(' ');
            if (space != -1) RemoveSubstring(ref html, text.Substring(0, space + 1));
            else html = "";
        }
        SetInputText(html, true);
        if (subject != null)
        {
            targetSelectedIndex = (int)Rt.ToNumber(Host.Get(subject, "selectedIndex"));
            UpdateSuperWorldChatEffect();
        }
    }

    /// <summary>SetInputText (:2470): the input's html text; with bSelectEnd the caret after it; then validated.</summary>
    void SetInputText(string html, bool selectEnd)
    {
        if (textInput == null) return;
        Host.Set(textInput, "htmlText", html);
        if (selectEnd && html.Length > 0) FocusAndSetSelection(html.Length, html.Length);
        Validate(textInput);
    }

    /// <summary>BeginWhisper (@0x141a17900, :2482): "{whisper command} " or "{whisper command} {target} " (common 0xaa5).</summary>
    void BeginWhisper(string target) => BeginLine(0xaa5, target);
    /// <summary>BeginTalk (@0x141a17a60): the same with the club command (common 0xaa6).</summary>
    void BeginTalk(string target) => BeginLine(0xaa6, target);

    void BeginLine(int id, string target)
    {
        string command = Model.Tables.Common(id);
        SetInputText(string.IsNullOrEmpty(target) ? command + " " : command + " " + target + " ", true);
    }

    /// <summary>FUN_141a14bb0 (:2025): a whisper to anyone but the local player (MyPC vfunc_77, its name); none without
    /// a local player.</summary>
    public void WhisperTo(string target)
    {
        if (Model == null || !Model.HasLocalPlayer) return;
        if (Model.Player.Name == target) return;
        BeginWhisper(target);
    }

    /// <summary>FUN_141a12260 (:1578): a focused input loses the focus, its kept links and its text; true = it was focused.</summary>
    bool Release()
    {
        if (!InputFocused) return false;
        inputLinks.Clear();
        SetFocused(textInput, 0);
        Host.Set(textInput, "htmlText", "<FONT></FONT>");
        Validate(textInput);
        return true;
    }

    /// <summary>The local player's CEquipment by item uid (PC::GetEquipByUid) for a linked item's tooltip
    /// (<see cref="ItemTooltip.OwnerWearsUid"/>); null leaves that block out.</summary>
    public Func<long, bool> OwnerWearsUid;

    /// <summary>
    /// UIMessenger::vfunc_45 (@0x141a0a900, :4623), the movie's getUserData. Nothing without a local player. "getTooltip":
    /// the linked item of the argument's uid (_atoi64, <see cref="MessengerBrowser.LinkedItem"/>) in an ASTooltip of the
    /// local player with flags 2 (FUN_1409e0d40 / <see cref="ItemTooltip.Fill"/>), its close button (ShowCloseButton,
    /// astooltip.cpp:159: "closeButton" true) and SetEnablePreview; nothing when the uid links none or the fill fails.
    /// "whisper": a whisper to the argument's name (FUN_141a14bb0, <see cref="WhisperTo"/>), nothing returned.
    /// "hyperLinkLeft": a click on a party link, <see cref="PartyLinks.Handle(string)"/> (nothing without the browser's links).
    /// "getAchieve": a click on a trophy link, <see cref="Achieve"/>.
    /// STILL-OPEN: "itemPreview" (ItemPreviewMgr's OpenPreview while IsHudOn), "menu",
    /// "moveToHome", "billDialog", "searchFromMeratmarket", "itemSelect" and the rest are not ported and
    /// answer nothing.
    /// </summary>
    public override IDictionary<string, object> GetUserData(string name, object[] args)
    {
        if (Model?.HasLocalPlayer != true) return null;
        if (name == "whisper") { WhisperTo(args.Length > 0 ? Rt.ToString(args[0]) : ""); return null; }
        if (name == "hyperLinkLeft")                                  // :4756, HandlePartyLink; nothing returned
        {
            Browser.PartyLinks?.Handle(args.Length > 0 ? Rt.ToString(args[0]) : "");
            return null;
        }
        if (name == "getAchieve") return Achieve(args);
        if (name != "getTooltip") return null;
        var item = Browser.LinkedItem(NumberText.ParseInt64W(args.Length > 0 ? Rt.ToString(args[0]) : ""));
        if (item == null) return null;
        var tip = new ItemTooltip(Model, 2)
        {
            Owner = (Browser.LocalCharacterId(), Model.Player.Name),
            Localize = key => Host.Player.Translate?.Invoke(key) ?? key,
            OwnerWearsUid = OwnerWearsUid,
        };
        if (!tip.Fill(item)) return null;
        tip.Data["closeButton"] = true;
        tip.SetEnablePreview(item);
        return tip.Data;
    }

    /// <summary>
    /// getAchieve (:4653), from the movie's click on an 'event:achieveTooltip,id,4,grade,text,date' link (its fields 1, 3, 4
    /// and 5): the achieve tooltip of the trophy and grade (atoi of each; ASTooltip of the local player with flags 2,
    /// <see cref="MessengerBrowser.AchieveTooltip"/>), with its close button (ShowCloseButton) and state the link's text,
    /// with the date in brackets ("{0} ({1})") when the date is not empty; nothing without the trophy's data.
    /// </summary>
    AsTooltip Achieve(object[] args)
    {
        string Arg(int i) => args.Length > i ? Rt.ToString(args[i]) : "";
        var tip = Browser.AchieveTooltip(Formats.World.ReservedWords.Atoi(Arg(0)), Formats.World.ReservedWords.Atoi(Arg(1)));
        if (tip == null) return null;
        tip["closeButton"] = true;
        string state = Arg(2), date = Arg(3);
        if (date.Length != 0) state = Quest.ClientFormat.Format("{0} ({1})", state, date);
        tip["state"] = state;
        return tip;
    }

    /// <summary>vfunc_28 (:3943), the window's calls after UIDialog's.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        switch (name.ToLowerInvariant())
        {
            case "eventfocusin": Browser.MakeCurrent(Name); return true;     // vfunc_90 (:2141)
            case "eventfocusout": Browser.ResetHistoryPosition(); return true;     // vfunc_91 (:2150)
            case "eventchangetab": SelectTab(args.Length > 0 ? Rt.ToString(args[0]) : ""); return true;
            case "eventchangetalktarget": SubjectChanged(); return true;     // vfunc_93
            case "eventtextinput": RemoveUnlistedLinks(); return true;       // vfunc_28 :4042
            case "eventpresssubjecttab":
                UpdateSuperWorldChatEffect();
                if (!IsTypeHidden(Super) && SelectedType() == Super) Browser.OpenSuperChatDialog?.Invoke();
                return true;
            case "updatealigntypeandoffset": UpdateAlignAndOffset(); return true;
            case "eventemotion": Browser.ToggleDynamicAction?.Invoke(); return false;     // :4052, falls to return 0
            // vfunc_94 (@0x141a162a0): FUN_141a131f0 or FUN_141a13200 by the input's chat type, both empty; and the whisper
            // list's show / hide call those two directly
            case "eventchangetext": case "eventshowwhisperlist": case "eventhidewhisperlist": return true;
            case "eventwhispertarget": if (args.Length > 0) WhisperTo(Rt.ToString(args[0])); return true;   // vfunc_95 (@0x141a162e0)
            case "eventadditivetab": AddAdditiveTab(); return true;
            case "eventtabmenu": if (args.Length > 0) TabMenu((int)Rt.ToNumber(args[0])); return true;
            case "eventattachtab":
                if (args.Length > 0) AttachTab(Rt.ToString(args[0]), args.Length > 1 ? Rt.ToString(args[1]) : "");
                return true;
            case "eventdetachtab":
                if (args.Length > 0)
                    DetachTab(Rt.ToString(args[0]), ArgInt(args, 1), ArgInt(args, 2), ArgInt(args, 3), ArgInt(args, 4), ArgInt(args, 5));
                return true;
            case "eventchatemoticonopen": Browser.ToggleEmoticonDialog?.Invoke(); return false;   // :4056, return 0
            case "eventchatemoticonpush":
                if (args.Length == 0) return false;
                SendEmoticon(ArgInt(args, 0));
                return false;                                  // :4063, return 0
            case "eventchatemoticonfavorite":
                if (args.Length == 0) return false;
                SetEmoticonFavorite(ArgInt(args, 0), args.Length > 1 && Rt.ToBoolean(args[1]));
                return false;
        }
        return false;
    }

    /// <summary>ASData::PopFloat / PopInt of the argument at that place, truncated; 0 when missing.</summary>
    static int ArgInt(object[] args, int i) => i < args.Length ? (int)Rt.ToNumber(args[i]) : 0;

    // ---- chat emoticons (uimessenger.cpp:1265-1500, :1689) -------------------------------------------------------------

    ChatEmoticons.ChatEmoticon FindEmoticon(uint id) => Model?.Tables.ChatEmoticonData.GetEmoticon(id);

    /// <summary>
    /// OnChatEmoticonSelected (@0x141a0f5d0, :1265): a pick for another window closes this window's preview; an unknown
    /// emoticon, no emoticon state or one the player cannot use does nothing; else the preview of it is shown and the window
    /// goes on top.
    /// </summary>
    public void OnChatEmoticonSelected(string target, uint id)
    {
        if (target != Name) { CloseEmoticonPreview(); return; }
        var emo = FindEmoticon(id);
        if (emo == null) return;
        var cli = Browser.ChatEmoticons;
        if (cli == null || !cli.IsEmoticonUsable(emo.Id)) return;
        ShowEmoticonPreview(new[] { emo });
        Host.Invoke("setTopMostLayer");       // UIDialog::SetTopMostLayer (@0x1419235d0)
    }

    /// <summary>
    /// TypeEmoticon (@0x141a10010, :1440): the emoticon's img tag line is submitted. When the input holds a slash command of
    /// the whisper chat type (4) with a target, the line starts with "command target "; without such a prefix the target
    /// of <paramref name="type"/> is selected first. STILL-OPEN: FUN_140988d60 (the split) is read as a split at spaces
    /// without empty parts, as the command line parser here does.
    /// </summary>
    public void TypeEmoticon(int type, uint id)
    {
        var emo = FindEmoticon(id);
        if (emo == null) return;
        string size = emo.Width != 0 || emo.Height != 0 ? $"width='{emo.Width}' height='{emo.Height}'" : "";
        string line = $"<img src='img://emoticon:{emo.Id}' vspace='{emo.VSpace}' {size} />";
        string prefix = "";
        if (textInput != null)
        {
            string input = UserCommands.Trim(Rt.ToString(Host.Get(textInput, "text")));
            if (input.Length > 0 && input[0] == '/')
            {
                int pos = 0;
                string command = UserCommands.Tokenize(input, " ", ref pos);
                string target = UserCommands.Tokenize(input, " ", ref pos);
                if (command.Length > 0 && target.Length > 0 && (Commands.Find(command)?.ChatType ?? UserCommands.NoChatType) == 4)
                    prefix = command + " " + target + " ";
            }
        }
        if (prefix.Length == 0) SelectSubject(type);
        line = prefix + line;
        SubmitLine(line, line);
    }

    /// <summary>ShowEmoticonPreview (@0x141a13510, :1689): openChatEmoticonPreview with the emoticons' id, image path, size
    /// and favorite flag; nothing for none or without the emoticon state.</summary>
    void ShowEmoticonPreview(IReadOnlyList<ChatEmoticons.ChatEmoticon> emoticons)
    {
        if (emoticons.Count == 0) return;
        var cli = Browser.ChatEmoticons;
        if (cli == null || Host?.Entity == null) return;
        var arr = new List<object>();
        foreach (var e in emoticons)
            arr.Add(new Dictionary<string, object>
            {
                ["id"] = e.Id, ["imgPath"] = e.ImagePath, ["width"] = e.Width, ["height"] = e.Height,
                ["favorite"] = cli.IsEmoticonFavorite(e.Id),
            });
        Host.Invoke("openChatEmoticonPreview", new Dictionary<string, object> { ["emoticonDatas"] = arr });
    }

    /// <summary>CloseEmoticonPreview (@0x141a13940).</summary>
    public void CloseEmoticonPreview()
    {
        if (Host?.Entity != null) Host.Invoke("closeChatEmoticonPreview");
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
