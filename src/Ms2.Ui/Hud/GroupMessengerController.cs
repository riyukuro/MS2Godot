using Ms2.Quest;
using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIGroupMessenger (ms2/client/ms2/uigroupmessenger.cpp), the C++ side of a group chat window (uigroupmessenger). The
/// movie's GroupMessenger draws the window; this side fills its header (RefreshWindow :1110), its lines (m_textOutput,
/// AddMessageLine) and its member list (m_userList, OnBuildScrollingList :1135), sends the typed line to the chat
/// (SendInput :1208, <see cref="GroupChatBook.SendTalk"/>) and answers its edit and leave boxes (OnMessage :555).
/// <see cref="GroupMessengerBrowserController"/> makes and drops the windows (OpenChatWindow) and keeps their lines.
/// STILL-OPEN: the settings file GroupMessenger&lt;index&gt;.xml (vfunc_4 :377, vfunc_5 :396, vfunc_53 / vfunc_54 :424,
/// :446: offsetX, offsetY, alignType, scrollPosition, width, height, name, alpha, userlist, minimize; vfunc_84 deletes it)
/// is neither written, read nor deleted; the typing profile timer (vfunc_15, updateProfile), the UGC picture listener and
/// icons (FUN_1414842b0, the member entry's image), the emoticon events, the invite dialog (0x4e90), the member popup
/// menu (OpenMemberPopupMenu) and "itemPreview" are not ported.
/// </summary>
public sealed class GroupMessengerController : UiController, IMessageBoxListener
{
    public override string Movie => "uigroupmessenger";
    protected override string EntityMethod => "setData";
    public override string TypeName => "GroupMessenger";      // GetTypeName (:301)

    /// <summary>The box tags (the statics :88).</summary>
    public const string EditTag = "groupEdit", ExitTag = "groupExit";

    /// <summary>m_nGroupId: the chat's oid.</summary>
    public readonly int GroupId;
    /// <summary>m_nIndex: the browser's slot (0 to 2), the settings file's number.</summary>
    public readonly int Index;
    readonly GroupMessengerBrowserController browser;

    /// <summary>m_strName: the window's name (the window list's label, Unk_MdNames' key).</summary>
    public string Name { get; private set; }
    float offsetX, offsetY;
    int alignType;
    int scrollPosition;
    int alpha = 100;
    bool userList;
    bool minimized;                // UIDialog m_6c (vfunc_64)
    bool saves = true;             // m_80
    bool closing;
    /// <summary>m_inputHistory: the links put in the input (SetInputText); cleared with the input.</summary>
    public readonly List<string> InputHistory = new();

    /// <summary>
    /// The ctor (@0x141947920, :282): offsets 0, align type 0, scroll position 0, alpha 100, no member list, the file
    /// saved (m_80 1), the tween on; with <paramref name="deleteSave"/> vfunc_84(0); the name is StringFormat("{0} {1}",
    /// common 0x5d0, the slot + 1).
    /// </summary>
    public GroupMessengerController(GroupMessengerBrowserController browser, int groupId, int index, bool deleteSave)
    {
        this.browser = browser;
        GroupId = groupId;
        Index = index;
        if (deleteSave) DeleteSave(false);
        Name = ClientFormat.Format("{0} {1}", browser.Common(0x5d0), index + 1);
    }

    /// <summary>GetXmlFileName (@0x14194b8a0, :1038): "GroupMessenger%d.xml" by the slot.</summary>
    public string SettingsFile => TypeName + Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".xml";

    /// <summary>vfunc_64: minimized (UIDialog m_6c).</summary>
    public override bool Minimized => minimized;

    /// <summary>vfunc_69 (@0x141947f10, :329): the Esc key minimizes the window.</summary>
    public override void EscapeClose() => Minimize();
    /// <summary>m_bUserList: the member list is shown.</summary>
    public bool UserListShown => userList;
    /// <summary>m_nAlpha.</summary>
    public int Alpha => alpha;
    /// <summary>m_80: the settings file is written (vfunc_84 clears it).</summary>
    public bool Saves => saves;

    /// <summary>The window's own vfunc_4 / vfunc_53 (:377) write GroupMessenger&lt;index&gt;.xml, not UIDialog's file.
    /// STILL-OPEN: not written.</summary>
    public override void SaveTransform() { }

    /// <summary>vfunc_5 (:396) reads GroupMessenger&lt;index&gt;.xml. STILL-OPEN: not read; the place is the ctor's.</summary>
    public override void LoadTransform() { }

    /// <summary>vfunc_84 (@0x141948b30, :470): the file is written again only when <paramref name="delete"/> is false
    /// (m_80); the file is deleted (STILL-OPEN: no file to delete).</summary>
    public void DeleteSave(bool delete) => saves = !delete;

    /// <summary>vfunc_43 (@0x141948f40, :508): placed by its align type and offsets, which are then taken back from the place
    /// (UIDialog::FUN_141926620). m_60 does not occur without the file.</summary>
    public override void Layout(double stageW, double stageH)
    {
        Host.Align(Host.Entity, alignType, (int)offsetX, (int)offsetY);
        (alignType, offsetX, offsetY) = GroupMessengerBrowserController.AlignFromPlace(Host, alignType, offsetX, offsetY);
    }

    AsObject Child(string name) => Host?.Entity == null ? null : Host.Child(name) as AsObject;
    AsObject Input => Child("m_textInput");
    AsObject Output => Child("m_textOutput");

    /// <summary>
    /// OnLoadComplete (@0x141948c20, :479): UIDialog's, then the header; the browser's lines of the chat (a copy) added
    /// again (AddMessageLine), the saved scroll position put back on m_textOutput, and m_textInput's maxChars from
    /// Constants MaxChatLength.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        RefreshWindow();
        int saved = scrollPosition;
        foreach (var log in browser.LogsOf(GroupId).ToArray()) AddMessageLine(log.Name, log.Text, log.System);
        if (Output is { } output)
        {
            scrollPosition = saved;
            Host.Set(output, "scrollPosition", scrollPosition);     // ASScrollingList::SetScrollPosition
        }
        if (Input is { } input) Host.Set(input, "maxChars", (double)(float)Model.Tables.MaxChatLength);
    }

    /// <summary>
    /// vfunc_67 (@0x141947da0, :317): UIDialog's close, the input's links cleared and the browser told
    /// (FUN_141953460, <see cref="GroupMessengerBrowserController.CloseWindowByName"/>), which closes this window again
    /// once it is out of the list.
    /// </summary>
    public override void Close()
    {
        if (closing) return;                 // the browser's close of it below comes back here once
        closing = true;
        if (Host?.Entity != null) base.Close();
        InputHistory.Clear();
        browser.CloseWindowByName(Name);
    }

    /// <summary>vfunc_69 (@0x141947f10, :329): vfunc_63(1).</summary>
    public void Minimize() => SetMinimized(true);

    /// <summary>
    /// vfunc_63 (@0x141947f40, :337): the flag set, the header refreshed and the file saved (vfunc_4). Minimized: a
    /// focused input loses the focus and its text, and the name leaves Unk_MdNames (RemoveName). Shown: m_textOutput
    /// scrolls to its line count when that is under the unread lines + 5, else to the unread lines + 5; the name
    /// becomes current (FUN_141482c80) and the window comes to the front (one movie per window here). Then the chat's
    /// lines are marked read.
    /// </summary>
    public void SetMinimized(bool n)
    {
        minimized = n;
        RefreshWindow();
        SaveTransform();
        if (n)
        {
            if (Input is { } input && InputFocused(input))
            {
                SetFocused(input, 0);
                Host.Set(input, "htmlText", "");
                Validate(input);
            }
            browser.Names.RemoveName(Name);
        }
        else
        {
            if (Output is { } output)
            {
                uint count = (uint)browser.GetUnreadLineCount(GroupId);
                uint items = (uint)ItemCount(output);
                uint rows = items < count + 5 ? items : count + 5;
                Host.Invoke(output, "scrollToIndex", rows);
                Validate(output);
            }
            browser.Names.MakeCurrent(Name);
        }
        browser.MarkChatRead(GroupId);
    }

    /// <summary>
    /// vfunc_13 (@0x141949020, :523): only for the window that takes keys (FUN_14194c6d0, <see cref="TakesKeys"/>):
    /// Return (DIK 0x1c, FUN_14194cbb0) or Escape (ClearInput); true = used.
    /// </summary>
    public bool OnKey(int dik, bool escape)
    {
        if (!TakesKeys()) return false;
        if (dik == MessengerController.DikReturn) return ReturnKey();
        if (escape) return ClearInput();
        return false;
    }

    /// <summary>The input has the focus (typing goes to this window).</summary>
    public bool InputActive => Input is { } input && InputFocused(input);

    /// <summary>
    /// FUN_14194c6d0 (@0x14194c6d0, :1189): not minimized, the current name of Unk_MdNames (GetCurrentName) and either the input
    /// focused or no text field focused (CUIService::IsTextFieldFocused, <see cref="InputBlock.TextFieldFocused"/>).
    /// </summary>
    bool TakesKeys()
    {
        if (minimized) return false;
        if (browser.Names.GetCurrentName() != Name) return false;
        return InputActive || !InputBlock.TextFieldFocused();
    }

    /// <summary>FUN_14194cbb0 (@0x14194cbb0, :1248): focused, the line is sent (SendInput); else the input takes the focus with the
    /// caret after its text. 1 either way.</summary>
    bool ReturnKey()
    {
        if (Input is not { } input) return true;
        if (InputFocused(input)) SendInput();
        else
        {
            int length = Rt.ToString(Host.Get(input, "text")).Length;
            SetFocused(input, 1);
            FocusAndSetSelection(input, length, length);
            Validate(input);
        }
        return true;
    }

    /// <summary>
    /// SendInput (@0x14194c830, :1208): the trimmed text; empty clears the input. With an item, achievement, bill or
    /// market link (HasChatLink) the html without its P, BR, TEXTFORMAT and FONT tags (FUN_140d2c650) is sent, else
    /// the text with "&lt;" / "&gt;" escaped (FUN_141842320); then the input is cleared.
    /// STILL-OPEN: CBanTextChecker's MaskBannedWords / FilterReservedWords before the send are not ported.
    /// </summary>
    public bool SendInput()
    {
        if (Input is not { } input) return false;
        string text = UserCommands.Trim(Rt.ToString(Host.Get(input, "text")));
        if (text.Length == 0) { ClearInput(); return false; }
        string html = Rt.ToString(Host.Get(input, "htmlText"));
        if (MessengerController.HasChatLink(html)) browser.Book.SendTalk(GroupId, MessengerBrowser.RemoveChatTags(html));
        else browser.Book.SendTalk(GroupId, MessengerController.Escape(text));
        ClearInput();
        return true;
    }

    /// <summary>ClearInput (@0x14194cd00, :1269): only with the input focused: the links cleared, the focus lost and the
    /// html "&lt;FONT&gt;&lt;/FONT&gt;"; 1 then, else 0.</summary>
    public bool ClearInput()
    {
        if (Input is not { } input || !InputFocused(input)) return false;
        InputHistory.Clear();
        SetFocused(input, 0);
        Host.Set(input, "htmlText", "<FONT></FONT>");
        Validate(input);
        return true;
    }

    /// <summary>
    /// SetInputText (@0x14194bcc0, :1098): with an input, AppendInputText(text, select to the end) after vfunc_78 (the
    /// window to the front; one movie per window here); the text is kept in the history either way.
    /// </summary>
    public void SetInputText(string text)
    {
        if (Input is { } input) AppendInputText(input, text ?? "", true);
        InputHistory.Add(text ?? "");
    }

    /// <summary>AppendInputText (@0x14194d100, :1314): nothing at maxChars or when the html, a space and the text reach
    /// 0x800; else the html + text + " " is set, and with <paramref name="selectEnd"/> the caret goes after the text.</summary>
    void AppendInputText(AsObject input, string text, bool selectEnd)
    {
        if (Rt.ToString(Host.Get(input, "text")).Length >= (int)Rt.ToNumber(Host.Get(input, "maxChars"))) return;
        string html = Rt.ToString(Host.Get(input, "htmlText"));
        if ((uint)(html.Length + 1 + text.Length) >= 0x800) return;
        Host.Set(input, "htmlText", html + text + " ");
        Validate(input);
        int length = Rt.ToString(Host.Get(input, "text")).Length;
        if (selectEnd && length > 0) FocusAndSetSelection(input, length, length);
        Validate(input);
    }

    // ---- the lines -------------------------------------------------------------------------------------------------

    /// <summary>AddChatMessage (@0x14194a5b0, :800): the line, then the browser's log of it (unread while minimized).</summary>
    public void AddChatMessage(string name, string msg)
    {
        AddMessageLine(name, msg, false);
        browser.AddTalkLog(GroupId, name, msg, false, minimized);
    }

    /// <summary>AddSystemMessage (@0x14194a650, :809): as <see cref="AddChatMessage"/>, a system line.</summary>
    public void AddSystemMessage(string name, string msg)
    {
        AddMessageLine(name, msg, true);
        browser.AddTalkLog(GroupId, name, msg, true, minimized);
    }

    string F(int id, params object[] args) => ClientFormat.Format(browser.Common(id), args);

    /// <summary>AddInvitedMessage (@0x14194a340, :760): the header, then line 0x80e with the inviter and the other name.</summary>
    public void AddInvitedMessage(string name, string other) { RefreshWindow(); AddSystemMessage(name, F(0x80e, name, other)); }

    /// <summary>FUN_14194a3c0 (:769): the header, then line 0x810 (left, 0) or 0x813 (refused, 1), "" for another mode.</summary>
    public void AddMemberRemovedMessage(string name, int mode)
    {
        RefreshWindow();
        AddSystemMessage(name, mode == 0 ? F(0x810, name) : mode == 1 ? F(0x813, name) : "");
    }

    /// <summary>AddMemberOnlineMessage (@0x14194a4d0, :782): the header, then line 0x811.</summary>
    public void AddMemberOnlineMessage(string name) { RefreshWindow(); AddSystemMessage(name, F(0x811, name)); }

    /// <summary>AddMemberOfflineMessage (@0x14194a540, :791): the header, then line 0x812.</summary>
    public void AddMemberOfflineMessage(string name) { RefreshWindow(); AddSystemMessage(name, F(0x812, name)); }

    /// <summary>
    /// AddMessageLine (@0x14194d3d0, :1335): an item on m_textOutput: frame (GetMessageFrameName: "notify" for a system
    /// line, "me" for the local player's name, else "other"), text, iconPath, characterName and visibleBg
    /// (<see cref="ShowsLineBackground"/>); validated, then scrolled to the end. STILL-OPEN: the icon (FUN_1414842b0, the
    /// UGC picture) is "".
    /// </summary>
    void AddMessageLine(string name, string msg, bool system)
    {
        if (Output is not { } output) return;
        string frame = system ? "notify" : browser.LocalName() == name ? "me" : "other";
        var item = Host.Obj(("frame", frame), ("text", msg ?? ""), ("iconPath", ""), ("characterName", name ?? ""),
            ("visibleBg", ShowsLineBackground(msg)));
        PushItem(output, item);
        Validate(output);
        Host.Invoke(output, "scrollToIndex", (uint)ItemCount(output));      // ScrollToEnd
    }

    /// <summary>ShowsLineBackground (@0x14194da30, :1384): an empty line, or one not starting with "&lt;", has the background; else not when
    /// its lower case holds "img://emoticon:".</summary>
    public static bool ShowsLineBackground(string msg)
    {
        if (string.IsNullOrEmpty(msg) || msg[0] != '<') return true;
        return !msg.ToLowerInvariant().Contains("img://emoticon:", StringComparison.Ordinal);
    }

    // ---- the header and the member list ----------------------------------------------------------------------------

    /// <summary>
    /// RefreshWindow (@0x14194bde0, :1110): nothing without the chat; the movie's setData {alpha, minimize, userlist,
    /// groupName, userCount "%d/%d" of the members and 20}, then the browser's window list.
    /// </summary>
    public void RefreshWindow()
    {
        var chat = browser.Book.FindChat(GroupId);
        if (chat == null) return;
        if (Host?.Entity != null)
            Host.Invoke("setData", Host.Obj(("alpha", alpha), ("minimize", minimized ? 1 : 0), ("userlist", userList ? 1 : 0),
                ("groupName", Name), ("userCount", $"{chat.MemberCount}/{GroupChat.MaxMembers}")));
        browser.RefreshWindowList();
    }

    /// <summary>
    /// OnBuildScrollingList (@0x14194c060, :1135): shown, with the member list and the chat: m_userList cleared, then one
    /// item per member, the joined ones first (FUN_14137bdc0, then FUN_14137bee0): online (the entry's kind, 1 joined),
    /// iconPath, characterName and levelAndJobName (common 0x20 of the level and the job's name); at most 8 rows.
    /// STILL-OPEN: the entry's image (the UGC picture) is "".
    /// </summary>
    public void BuildUserList()
    {
        if (minimized || !userList) return;
        var chat = browser.Book.FindChat(GroupId);
        if (chat == null || Child("m_userList") is not { } list) return;
        if (Host.Get(list, "dataProvider") is AsObject dp) Host.Invoke(dp, "cleanUp");
        Validate(list);
        foreach (var m in chat.JoinedMembers.Concat(chat.OtherMembers))
            PushItem(list, Host.Obj(("online", m.Joined ? 1 : 0), ("iconPath", ""), ("characterName", m.Name),
                ("levelAndJobName", ClientFormat.Format(browser.Common(0x20), m.User.Level, Model.Tables.JobName(m.User.Job)))));
        int count = ItemCount(list);
        Host.Set(list, "rowCount", (uint)(count > 8 ? 8 : count));
        Validate(list);
    }

    /// <summary>IsGroupMember (@0x14194b9d0, :1061): the chat has a member of the character id.</summary>
    public bool IsGroupMember(long characterId) => browser.Book.FindChat(GroupId) is { } chat && chat.HasMemberById(characterId);

    // ---- the movie's calls -----------------------------------------------------------------------------------------

    /// <summary>
    /// vfunc_28 (@0x141949900, :640), after UIDialog's. eventFocusIn makes the name current; OnHideDialog minimizes
    /// (FUN_141953160); OnShowUserList / OnHideUserList; OnShowConfigDialog shows box 0x15 tagged groupEdit with the
    /// name and the alpha; OnLeaveGroupMessenger box 1 of common 0x81b with the name, tagged groupExit;
    /// OnBuildScrollingList; eventTextInput drops the links not put in by <see cref="SetInputText"/>; scrollPosition is
    /// kept. OnShowInviteDialog (:655) sets the invite dialog's chat (FindInviteGroupDialog 0x4e90, SetGroupId) and opens it.
    /// STILL-OPEN: OnPopupMenu and the emoticon events are not ported.
    /// </summary>
    public override bool OnCallback(string name, object[] args)
    {
        switch (name.ToLowerInvariant())
        {
            case "eventfocusin": browser.Names.MakeCurrent(Name); return true;           // vfunc_85
            case "onhidedialog": browser.MinimizeWindow(Name); return true;
            case "onshowinvitedialog":
                if (browser.FindInviteDialog() is { } invite)
                {
                    invite.SetGroupId(GroupId);
                    browser.OpenDialog(InviteGroupDialogController.UiId);
                }
                return true;
            case "onshowuserlist": userList = true; RefreshWindow(); return false;
            case "onhideuserlist": userList = false; RefreshWindow(); return false;
            case "onshowconfigdialog":
            {
                var box = MessageBoxQueue.Create(0x15, "", this, EditTag);
                box.Args.Add(Name);
                box.Append(alpha);
                MessageBoxQueue.Show(box);
                return false;
            }
            case "onleavegroupmessenger":
                MessageBoxQueue.Show(MessageBoxQueue.Create(1, F(0x81b, Name), this, ExitTag));
                return false;
            case "onbuildscrollinglist": BuildUserList(); return false;
            case "eventtextinput": RemoveUnlistedLinks(); return true;
            case "scrollposition":
                if (args is { Length: > 0 }) scrollPosition = (int)(float)Rt.ToNumber(args[0]);
                return false;
        }
        return false;
    }

    /// <summary>
    /// OnMessage (@0x141949130, :555), the edit and leave boxes. groupEdit with button 0: the box's m_textInput through
    /// the platform's name check (Unk_BlockList 0x4e93 "MessengerName") and FUN_14194ce40 (<see cref="NameTaken"/>,
    /// which returns 0 without closing the box when it fails), then SetName; the box's m_slider0 position as the alpha;
    /// the header and the window list. The file saved (vfunc_4) and 1 either way. groupExit with button 0 leaves the
    /// chat (SendGroupChatMode4 flag 0). STILL-OPEN: the platform check (IClientPlatform vfunc_37) is not ported and
    /// lets the name through.
    /// </summary>
    public bool OnMessageBox(MessageBoxController sender, int button, string tag)
    {
        if (tag == EditTag)
        {
            if (button == 0)
            {
                var movieBox = sender.CurrentBox?.MovieBox;
                if (movieBox != null && !Rt.IsNullish(sender.Host.Get(movieBox, "m_textInput")))
                {
                    string name = sender.InputText(sender.CurrentBox);
                    if (NameTaken(name)) return false;
                    Name = name;
                }
                if (movieBox != null && sender.Host.Get(movieBox, "m_slider0") is AsObject slider)
                    alpha = (int)Rt.ToNumber(sender.Host.Get(slider, "position"));
                RefreshWindow();
                browser.RefreshWindowList();
            }
            SaveTransform();
            return true;
        }
        if (tag == ExitTag)
        {
            if (button == 0) browser.Book.SendLeave(GroupId, 0);
            return true;
        }
        return false;
    }

    /// <summary>
    /// FUN_14194ce40 (@0x14194ce40, :1287): another window of the name shows box 0 of common 0x817 and fails (1). STILL-OPEN:
    /// CBanTextChecker::FUN_140225f70's name check before it is not ported.
    /// </summary>
    bool NameTaken(string name)
    {
        var other = browser.FindWindowByName(name);
        if (other == null || other == this) return false;
        MessageBoxQueue.Show(MessageBoxQueue.Create(0, browser.Common(0x817), null, ""));
        return true;
    }

    /// <summary>
    /// vfunc_45 (@0x141949640, :603), the movie's getUserData. Nothing without a local player. "getTooltip": the
    /// browser's item of the argument's uid (_atoi64, m_items) in an ASTooltip of the local player with flags 2
    /// (FUN_1409e0d40 / <see cref="ItemTooltip.Fill"/>), its close button and SetEnablePreview.
    /// STILL-OPEN: "itemPreview" (ItemPreviewMgr::OpenPreview while IsHudOn) is not ported.
    /// </summary>
    public override IDictionary<string, object> GetUserData(string name, object[] args)
    {
        if (Model?.HasLocalPlayer != true) return null;
        if (name != "getTooltip") return null;
        var item = browser.LinkedItem(NumberText.ParseInt64W(args.Length > 0 ? Rt.ToString(args[0]) : ""));
        if (item == null) return null;
        var tip = new ItemTooltip(Model, 2)
        {
            Owner = (browser.LocalCharacterId(), Model.Player.Name),
            Localize = key => Host.Player.Translate?.Invoke(key) ?? key,
            OwnerWearsUid = browser.OwnerWearsUid,
        };
        if (!tip.Fill(item)) return null;
        tip.Data["closeButton"] = true;
        tip.SetEnablePreview(item);
        return tip.Data;
    }

    // ---- ASUIComponent helpers -------------------------------------------------------------------------------------

    bool InputFocused(AsObject input) => Rt.ToNumber(Host.Get(input, "focused")) != 0;

    /// <summary>ASUIComponent::SetFocused (asuicomponent.cpp:123): losing it clears the stage focus.</summary>
    void SetFocused(AsObject c, int n)
    {
        Host.Set(c, "focused", n);
        if (n == 0) Host.Set(Host.Player.Stage, "focus", null);
    }

    /// <summary>ASUIComponent::vfunc_24(true): invalidate, then validateNow.</summary>
    void Validate(AsObject c)
    {
        Host.Invoke(c, "invalidate");
        Host.Invoke(c, "validateNow");
    }

    /// <summary>ASTextInput::FocusAndSetSelection (astextinput.cpp:264).</summary>
    void FocusAndSetSelection(AsObject input, int begin, int end)
    {
        if (!InputFocused(input)) SetFocused(input, 1);
        Validate(input);
        if (Host.Get(input, "textField") is AsObject tf) Host.Invoke(tf, "setSelection", begin, end);
    }

    /// <summary>ASCoreList::GetItemCount: the data provider's length, 0 without one.</summary>
    int ItemCount(AsObject list) => Host.Get(list, "dataProvider") is AsObject dp ? (int)Rt.ToNumber(Host.Get(dp, "length")) : 0;

    /// <summary>ASCoreList::AddItem: pushed on the data provider.</summary>
    void PushItem(AsObject list, AsObject item)
    {
        if (Host.Get(list, "dataProvider") is AsObject dp) Host.Invoke(dp, "push", item);
    }

    /// <summary>
    /// FUN_140d2c2a0 (unit_140d2a7e0_chatlinkhelpers.cpp:423) over m_inputHistory, on eventTextInput: with the input
    /// focused, links kept and html in it, each "&lt;A HREF=" .. "/A&gt;" not one of the kept links is removed; if any
    /// was, the html is set and validated, and an input left without text gets "&lt;Font&gt;&lt;/Font&gt;".
    /// </summary>
    void RemoveUnlistedLinks()
    {
        if (Input is not { } input || !InputFocused(input) || InputHistory.Count == 0) return;
        string html = Rt.ToString(Host.Get(input, "htmlText"));
        if (string.IsNullOrEmpty(html)) return;
        bool changed = false;
        foreach (var link in MessengerBrowser.FindLinks(html, "<A HREF=", "/A>"))
        {
            if (MessengerBrowser.IsListedLink(link, InputHistory)) continue;
            html = html.Replace(link, "", StringComparison.Ordinal);
            changed = true;
        }
        if (!changed) return;
        Host.Set(input, "htmlText", html);
        Validate(input);
        if (Rt.ToString(Host.Get(input, "text")).Length == 0)
        {
            Host.Set(input, "htmlText", "<Font></Font>");
            Validate(input);
        }
    }
}
