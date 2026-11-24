using Ms2.Quest;
using Ms2.Ui.Hud;

namespace Ms2.Ui;

/// <summary>
/// What the character list hands the movie for one character: the object FUN_1418f42c0 (uicharacterlistdialog.cpp:1933)
/// fills for buildCharacterList / addCharacter. <paramref name="DeleteTime"/> is the entry's delete time (CCharacterInfo::Read,
/// :348: unix seconds, 0 without a delete wait), which the row's waitDelTime is made from.
/// </summary>
public sealed record LobbyCharacterRow(string CharName, int Gender, int JobCode, string LevelJob, string ImgPath,
    string CreatedDate, string LastPos, string CharSN, long DeleteTime = 0);

/// <summary>
/// UICharacterListDialog (ms2/client/ms2/uicharacterlistdialog.cpp): the character select dialog's C++ side over the
/// uicharacterlistdialog movie (scaleform.ms2.controls.dialog.CharacterListDialog). What the client does with the answers
/// (the preview, the game start, character creation, the server list) is the host's, through the events.
/// </summary>
public sealed class CharacterListController : UiController, IMessageBoxListener
{
    public override string Movie => "uicharacterlistdialog";

    /// <summary>UICharacterListDialog's ctor sets the flag 0 (uicharacterlistdialog.cpp:556): vfunc_56, an
    /// always-on one.</summary>
    public override bool AlwaysOn => true;
    protected override string EntityMethod => "buildCharacterList";

    /// <summary>
    /// No startOpen on load: UIDialog::OnLoadComplete invokes it only when m_c4 is set (uidialog.cpp:704-719), and the
    /// constructor (uicharacterlistdialog.cpp:549-558) sets flag 0 (SetFlag), not the tween. buildCharacterList plays the
    /// movie's own popup.
    /// </summary>
    protected override bool IsDialog => false;

    /// <summary>m_1a8: the characters, in list order.</summary>
    public readonly List<LobbyCharacterRow> Characters = new();
    /// <summary>m_198: GameOption's last character name (constructor, :554); its row is selected first.</summary>
    public string SavedName = "";
    /// <summary>m_1a0: GameOption's last character id (constructor, :555), the selection when no name matches.</summary>
    public string SavedSn = "";
    /// <summary>DAT_1439f4a84 (CharMaxCount, networkservice.cpp:1069): the account's character limit, buildCharacterList's third argument.</summary>
    public int CharacterLimit = 6;
    /// <summary>DAT_1439f4a88 (CharMaxCount): m_charlist.maxRow.</summary>
    public int CharacterRows = 8;

    /// <summary>FUN_141900750 (uicommon.cpp:1493): the initData object; null without a server list dialog (no initData).</summary>
    public (string Name, string IconPath)? Server;
    /// <summary>CFeatureManager::IsFeatureEnabled(0x112): m_deleteAccount stays visible only with it (OnLoadComplete :716-720).</summary>
    public bool DeleteAccountEnabled;
    /// <summary>The platform's vfunc_1, or a test server: m_serverlist's enabled and visible (OnLoadComplete :721-728).</summary>
    public bool ServerListEnabled = true;

    /// <summary>m_1dc: set once RefreshLobby built the list; vfunc_13 sends Enter only after that.</summary>
    bool built;
    /// <summary>m_188: the character the list has selected, by id; null for an empty slot.</summary>
    string selectedSn;
    /// <summary>m_1c8: the previewed character's id (FUN_1418ee5c0 :1076), null without a preview.</summary>
    string previewedSn;
    /// <summary>m_220: the select scene's fade-in finished (FUN_1418f1fd0).</summary>
    bool fadedIn;
    /// <summary>m_1d4: the scene the dialog pops down for (FUN_1418f11c0 :1418-1426, FUN_1418f13f0 :1498), 0 for none.</summary>
    int popdownScene;

    /// <summary>m_1d4 for a game start (FUN_1418f13f0 :1498): the popdown ends in the enter request (OnEndPopdown :1654).</summary>
    const int GameStartScene = 0x620000c;

    /// <summary>A game start is under way (m_1d4 = 0x620000c): the list pops down and takes no selection until the scene changes.</summary>
    public bool Starting => popdownScene == GameStartScene;

    /// <summary>
    /// The client keeps this dialog open while the select map loads again; the host builds it anew, so it carries the
    /// dialog's state over (m_198, m_1a0, m_188, m_1c8, m_220, m_180) with the row the list had selected
    /// (<see cref="KeepRow"/>), which buildCharacterList then selects instead of RefreshLobby's pick.
    /// </summary>
    public void CarryFrom(CharacterListController old)
    {
        SavedName = old.SavedName; SavedSn = old.SavedSn;
        selectedSn = old.selectedSn; previewedSn = old.previewedSn; Selecting = old.Selecting;
        // the map change is what the client's vfunc_58 fade-in follows (m_220 set); the rebuilt dialog's own OnSelectCharacter("") on an
        // empty list would otherwise load the map again, for ever
        fadedIn = true;
        popdownScene = old.popdownScene;   // m_1d4 lives with the dialog, which the client keeps open over the map load
        KeepRow = old.ListIndex;
        // the characters' delete clocks (CCharacterInfo m_nSeconds / m_nStarted) and m_1d8 live in the kept dialog too
        foreach (var kv in old.clocks) clocks[kv.Key] = kv.Value;
        deleteSent = old.deleteSent;
    }

    /// <summary>The row to select on the rebuilt list (<see cref="CarryFrom"/>); null for RefreshLobby's own pick.</summary>
    public int? KeepRow;

    /// <summary>OnSelectCharacter went through (vfunc_28 :2263-2313): the character's id, "" for an empty slot. The input is off.</summary>
    public event Action<string> SelectCharacter;
    /// <summary>
    /// The game start's popdown ended (OnEndPopdown with m_1d4 0x620000c, :1654 -> FUN_1418f1790): UIServerListDialog::EnterGame
    /// with the character m_188. Raised once per Start, after the list has slid away with its input off.
    /// </summary>
    public event Action<string> GameStart;
    /// <summary>OnCreatePC (FUN_1418f1260 :1441) under the limit: the create dialog 0x4eea opens.</summary>
    public event Action CreateCharacter;
    /// <summary>OnCreatePC at the limit: message 9 with the limit (BlockFormat(9, DAT_1439f4a84)).</summary>
    public event Action<int> CharacterLimitReached;
    /// <summary>OnDeletePC (vfunc_28 :2218-2244): the delete confirmation message box.</summary>
    public event Action DeleteCharacter;
    /// <summary>OnSceneLobbyServer / onClickRegionList (FUN_1418f11c0 :1418): the dialog pops down for the server list.</summary>
    public event Action ServerList;
    /// <summary>OnGameExit (FUN_1418fc760, uichatballoon.cpp:463): the quit message box.</summary>
    public event Action GameExit;
    /// <summary>OnGameOption (FUN_1418fc760): the option dialog 0x4ef3.</summary>
    public event Action GameOption;
    /// <summary>OnEndPopdown: the popdown finished.</summary>
    public event Action EndPopdown;
    /// <summary>onClickDownArrow / onClickUpArrow swapped two rows (m_1a8's entries swapped too, :2367-2405).</summary>
    public event Action<int, int> Swapped;

    /// <summary>m_180: a selection is being handled (set by OnSelectCharacter, cleared by PreviewCharacter).</summary>
    public bool Selecting { get; private set; }

    /// <summary>m_charlist.selectedIndex: the row the list has selected, -1 for none.</summary>
    public int ListIndex => Host?.Get(Host.Entity, "m_charlist") is { } list ? (int)Avm2.Rt.ToNumber(Host.Get(list, "selectedIndex")) : -1;

    /// <summary>OnLoadComplete (uicharacterlistdialog.cpp:694-729).</summary>
    public override void OnLoadComplete()
    {
        // UIDialog::OnLoadComplete first (:696): the dialog is open, so vfunc_ugc_1's vfunc_55 check lets pictures through
        MarkOpen();
        if (Server is { } s) Host.Invoke(Host.Entity, "initData", Host.Obj(("name", s.Name ?? ""), ("iconPath", s.IconPath ?? "")));
        RefreshLobby();
        if (!DeleteAccountEnabled && Host.Get(Host.Entity, "m_deleteAccount") is { } del) Host.Set(del, "visible", false);
        if (Host.Get(Host.Entity, "m_serverlist") is { } server)
        {
            Host.Set(server, "enabled", ServerListEnabled);
            Host.Set(server, "visible", ServerListEnabled);
        }
    }

    /// <summary>
    /// RefreshLobby (uicharacterlistdialog.cpp:1345-1395): m_charlist.maxRow, then buildCharacterList(rows, selected,
    /// limit), the selected row being the one named m_198, else the one with id m_1a0, else the first.
    /// SortCharacters (:1230) returns at once without a saved order (GameOption m_charList empty), so the order is kept.
    /// </summary>
    public void RefreshLobby()
    {
        if (Host.Get(Host.Entity, "m_charlist") is { } list) Host.Set(list, "maxRow", CharacterRows);
        int select = 0;
        bool byName = SavedName.Length > 0;
        var rows = new object[Characters.Count];
        for (int i = 0; i < Characters.Count; i++)
        {
            var c = Characters[i];
            if (byName && SavedName == c.CharName) select = i;
            else if (SavedSn == c.CharSN) select = i;
            rows[i] = Row(c);
        }
        int count = rows.Length;
        select = count == 0 ? -1 : Math.Max(0, Math.Min(count - 1, select));
        if (KeepRow is int keep) select = keep;
        Host.Invoke(Host.Entity, "buildCharacterList", rows, select, CharacterLimit);
        // :1361-1376: each character with seconds left (compared unsigned) gets its renderer in the pending list
        for (int i = 0; i < Characters.Count; i++)
            if ((uint)SecondsLeft(Clock(Characters[i].CharSN)) > 0
                && Host.Invoke(Host.Entity, "getCharacterRenderer", i) is Avm2.Flash.AsDisplayObject renderer)
                AddPending(Characters[i].CharSN, renderer);
        built = true;
    }

    /// <summary>FUN_1418f42c0 (uicharacterlistdialog.cpp:1933-1956): one character's object; a character with a delete time
    /// (FUN_1418ea020() > 0) gets waitDelTime, its seconds left (FUN_1418e9f30), which the renderer's setData turns into
    /// requestDelete: the wait state, or the ready state at 0.</summary>
    object Row(LobbyCharacterRow c)
    {
        var o = Host.Obj(
            ("charname", c.CharName ?? ""), ("gender", c.Gender), ("jobcode", c.JobCode), ("levelJob", c.LevelJob ?? ""),
            ("imgPath", c.ImgPath ?? ""), ("createddate", c.CreatedDate ?? ""), ("lastpos", c.LastPos ?? ""), ("charSN", c.CharSN ?? ""));
        var clock = Clock(c.CharSN);
        if (clock.DeleteTime > 0) Host.Set(o, "waitDelTime", (uint)SecondsLeft(clock));
        return o;
    }

    /// <summary>vfunc_28 (uicharacterlistdialog.cpp:2207-2440).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        // FUN_1418fc760 (uichatballoon.cpp:463-476), ahead of the dialog's own names
        if (Is(name, "OnGameExit")) { GameExit?.Invoke(); return true; }
        if (Is(name, "OnGameOption")) { GameOption?.Invoke(); return true; }
        if (Is(name, "OnSceneLobbyServer") || Is(name, "onClickRegionList"))
        {
            // FUN_1418f11c0: HidePopups unless a selection is being handled, then the scene to go to (0x6200009 the server
            // list; 0x620001a when the platform's vfunc_1 is set)
            if (!Selecting) Popdown();
            popdownScene = 0x6200009;
            ServerList?.Invoke();
            return true;
        }
        if (Is(name, "OnCreatePC"))
        {
            // FUN_1418f1260 (:1441-1459); the character-count game event (message 10) is not run here
            if (Characters.Count >= CharacterLimit) CharacterLimitReached?.Invoke(CharacterLimit);
            else CreateCharacter?.Invoke();
            return true;
        }
        if (Is(name, "OnDeletePC")) { DeleteCharacter?.Invoke(); return true; }
        if (Is(name, "OnGameStart"))
        {
            if (args.Length == 0) return false;
            var found = Find(Arg(args));
            selectedSn = found?.CharSN;
            if (found == null) return false;
            if (Clock(found.CharSN).DeleteTime > 0) return false;   // a character with a delete time does not start (:2259)
            if (NameMustChange(found)) return true;
            BeginGameStart();
            return true;
        }
        if (Is(name, "OnSelectCharacter"))
        {
            if (Selecting) return true;
            int state = popdownScene;
            if (state > 0 && selectedSn == null) state = 0;
            if (state != 0 || args.Length == 0) return true;
            string sn = Arg(args);
            string prev = selectedSn;
            selectedSn = null;
            bool changed = true;
            if (Find(sn) is { } found)
            {
                selectedSn = found.CharSN;
                if (SavedSn == sn && previewedSn == sn) changed = false;
            }
            // `spPrev != m_188 || (m_188 == 0 && m_220 == 0)`: the selected character again does nothing
            if (changed && (prev != selectedSn || (selectedSn == null && !fadedIn)))
            {
                SetInput(false);
                Selecting = true;
                SelectCharacter?.Invoke(selectedSn ?? "");
            }
            return true;
        }
        if (Is(name, "onClickDownArrow") || Is(name, "onClickUpArrow"))
        {
            if (args.Length == 0) return true;
            int index = (int)Avm2.Rt.ToNumber(args[0]);
            bool down = Is(name, "onClickDownArrow");
            if (!down && index == 0) return true;
            int other = down ? index + 1 : index - 1;
            if (Host.Get(Host.Entity, "m_charlist") is { } list && Avm2.Rt.ToBoolean(Host.Invoke(list, "swapIndex", index, other))
                && index < Characters.Count && other < Characters.Count)
            {
                (Characters[index], Characters[other]) = (Characters[other], Characters[index]);
                Swapped?.Invoke(index, other);
            }
            return true;
        }
        if (Is(name, "onClickCancelDelete"))
        {
            // :2314-2322: the renderer's cancel button in the wait state
            if (args.Length > 0) CancelDelete(Find(Arg(args)));
            return true;
        }
        if (Is(name, "onClickRevivalChar") || Is(name, "onClickFinalDelete"))
        {
            // :2324-2356: the ready state's buttons ask first, a type 1 box (OK / cancel) answered in OnMessageBox:
            // s_char_ask_revival_char (8) tagged "revivalChar,<id>", s_char_ask_delete_final (6) tagged "finalDelete,<id>"
            if (args.Length > 0 && Find(Arg(args)) is { } found)
            {
                bool revival = Is(name, "onClickRevivalChar");
                ShowBox(ClientFormat.Format(MessageBoxData.Common(revival ? 8 : 6), found.CharName ?? ""), 1,
                    (revival ? "revivalChar," : "finalDelete,") + found.CharSN);
            }
            return true;
        }
        if (Is(name, "OnEndPopdown")) { OnEndPopdown(); return true; }
        return false;
    }

    /// <summary>m_1e8: the character list's EndList flag (OnCharacterListPacket mode 4, :906); the game start checks the
    /// name while it is set.</summary>
    public bool CheckNames;
    /// <summary>CBanTextChecker::FUN_140225210(name, reason, 1): the checker's result for the 12 letter form; null when the
    /// client has no checker (the check is skipped).</summary>
    public Func<string, int> CheckName;

    /// <summary>
    /// FUN_1418f13f0's start (uicharacterlistdialog.cpp:1472-1490): with m_1e8 set, a selected character whose name the
    /// checker finds too long (<see cref="InvalidNameRename.TooLong"/>) gets the rename box (<see cref="InvalidNameRename.Create"/>)
    /// and the start ends there: the list stays up, nothing pops down. True when the box opened.
    /// </summary>
    bool NameMustChange(LobbyCharacterRow found)
    {
        if (!CheckNames || CheckName == null || CheckName(found.CharName ?? "") != InvalidNameRename.TooLong) return false;
        if (!long.TryParse(found.CharSN, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long id)) return false;
        ShowRenameBox(InvalidNameRename.Create(id, MessageBoxData.CommonByKey));
        return true;
    }

    /// <summary>FUN_1418f20d0 (uicharacterlistdialog.cpp:1724-1727), the rename box's cancel: the input on.</summary>
    public void RenameCancelled()
    {
        if (Host?.Entity != null) SetInput(true);
    }

    /// <summary>
    /// vfunc_79 case 0xc type 7 (uicharacterlistdialog.cpp:605-633), CharacterList 7: the input on; code 1 gives character
    /// <paramref name="sn"/>'s row the name (the movie's updateCharacterName with its index), any other shows the client's
    /// text for the code (<see cref="InvalidNameRename.AnswerText"/>).
    /// </summary>
    public void OnNameChanged(int code, string sn, string name)
    {
        if (Host?.Entity == null) return;
        SetInput(true);
        if (code == 1)
        {
            int index = Characters.FindIndex(c => c.CharSN == sn);
            if (index < 0) return;
            Characters[index] = Characters[index] with { CharName = name ?? "" };
            Host.Invoke(Host.Entity, "updateCharacterName", index, name ?? "");
            return;
        }
        ShowRenameBox(MessageBoxQueue.Create(0, InvalidNameRename.AnswerText(code, name, MessageBoxData.Common), null, ""));   // no listener
    }

    /// <summary>
    /// FUN_1418f13f0 (uicharacterlistdialog.cpp:1470-1499), OnGameStart's body: HidePopups (popdown, input off) unless a
    /// selection is being handled (m_180), and m_1d4 = 0x620000c, which stops every OnSelectCharacter while the character
    /// is set (:2271-2276) and makes the popdown's end send the enter request (<see cref="OnEndPopdown"/>). With m_180 set
    /// the popdown waits for the preview (FUN_1418f1d00 from PreviewCharacter, <see cref="Previewed"/>).
    /// The rename check before it is <see cref="NameMustChange"/>, run at the Start only (FUN_1418f1d00 pops down without it).
    /// </summary>
    void BeginGameStart()
    {
        if (Host?.Entity != null && !Selecting) Popdown();
        popdownScene = GameStartScene;
    }

    /// <summary>
    /// OnEndPopdown (uicharacterlistdialog.cpp:1635-1663): the scene m_1d4 names, then m_1d4 = 0. A game start sends the
    /// enter request (FUN_1418f1790 -> EnterGame, <see cref="GameStart"/>); the input stays off (only a failed EnterGame
    /// calls ShowPopup). The other scenes' popdowns raise <see cref="EndPopdown"/> as before.
    /// </summary>
    void OnEndPopdown()
    {
        int scene = popdownScene;
        popdownScene = 0;
        if (scene == GameStartScene)
        {
            if (selectedSn != null) GameStart?.Invoke(selectedSn);
            return;
        }
        EndPopdown?.Invoke();
    }

    /// <summary>
    /// FUN_1418f1d00 (uicharacterlistdialog.cpp:1615-1631) for a game start, run when a preview is done (PreviewCharacter
    /// :1056, FUN_1418ee5c0 :1077): a Start that came while a selection was being handled pops the list down now. The host
    /// that builds the dialog again over a map load (the client keeps it open) calls it once the rebuilt movie stands where
    /// the kept one was (its popup stepped to the end); before the movie is loaded it does nothing.
    /// </summary>
    public void ResumeGameStart()
    {
        if (popdownScene == GameStartScene && Host?.Entity != null) BeginGameStart();
    }

    /// <summary>
    /// FUN_1418f1fd0 (uicharacterlistdialog.cpp:1705-1709), from UISceneLobbySelect::vfunc_58 when the scene's fade-in is
    /// over (uiscenelobbyselect.cpp:411-416): m_220 set, the dialog's input on.
    /// </summary>
    public void FadeInComplete()
    {
        fadedIn = true;
        SetInput(true);
    }

    /// <summary>
    /// PreviewCharacter (uicharacterlistdialog.cpp:1039-1058) with the character <paramref name="sn"/> (null for none):
    /// m_180 cleared; a character's preview selects it (FUN_1418ee5c0 -> SelectCharacter :1263: m_1a0 = id, m_198
    /// emptied) and sets m_1c8, none clears m_1c8 (ClearPreviewCharacter :1022). The input comes back with the next
    /// fade-in (<see cref="FadeInComplete"/>), not here.
    /// </summary>
    public void Previewed(string sn)
    {
        Selecting = false;
        if (sn == null) { previewedSn = null; ResumeGameStart(); return; }
        SavedSn = sn;
        SavedName = "";
        previewedSn = sn;
        ResumeGameStart();
    }

    /// <summary>FUN_1412761e0 (unit_141274d10_uihelpers.cpp:613-622): mouseChildren and mouseEnabled of the dialog.</summary>
    public void SetInput(bool on)
    {
        Host.Set(Host.Entity, "mouseChildren", on);
        Host.Set(Host.Entity, "mouseEnabled", on);
    }

    /// <summary>The dialog takes the mouse (its mouseChildren, what LobbyDialog.getMouseState reads).</summary>
    public bool InputOn => Avm2.Rt.ToBoolean(Host.Get(Host.Entity, "mouseChildren"));

    /// <summary>vfunc_13 (uicharacterlistdialog.cpp:787-800): Enter -> onEnterKey once the list is built and with the mouse on.</summary>
    public bool EnterKey()
    {
        if (!built) return false;
        if (InputOn) Host.Invoke(Host.Entity, "onEnterKey");
        return true;
    }

    /// <summary>HidePopups (uicharacterlistdialog.cpp:1084-1091): popdown, input off.</summary>
    public void Popdown()
    {
        Host.Invoke(Host.Entity, "popdown");
        SetInput(false);
    }

    /// <summary>
    /// ShowPopup (uicharacterlistdialog.cpp:1571-1579): with the movie loaded, the camera dialog 0x4ef2 opened (not ported:
    /// character select has no camera dialog here), the movie's popup and the input on. A failed game start ends here
    /// (a refused start, vfunc_79 case 0xe :591-597; an enter that could not be sent, FUN_1418f1790 :1503-1517; the
    /// failed connect box's OK, UISceneLobbySelect::vfunc_64), so the list takes input again.
    /// </summary>
    public void ShowPopup()
    {
        if (Host?.Entity == null) return;
        Host.Invoke(Host.Entity, "popup");
        SetInput(true);
    }

    // ---- the character delete (uicharacterlistdialog.cpp) -------------------------------------------------------------

    /// <summary>CCharacterInfo's delete fields: m_nDeleteTime (unix seconds, 0 for none), m_nSeconds (the wait left when it was
    /// set) and m_nStarted (GetServerTick then, 0 for none).</summary>
    sealed class DeleteClock { public long DeleteTime; public uint Seconds; public long Started; }

    /// <summary>The characters' delete clocks by id; a character's is made from its row's delete time when first asked for,
    /// as CCharacterInfo::Read does it (FUN_1418e9fa0(nDeleteTime), :348).</summary>
    readonly Dictionary<string, DeleteClock> clocks = new();
    /// <summary>m_1f0: the renderers in the wait state, with their character.</summary>
    readonly List<(string Sn, object Renderer)> pending = new();
    /// <summary>m_1d8: a delete request (mode 2 or 4) is out; cleared by the DeleteEntry and BeginDelete answers.</summary>
    bool deleteSent;

    /// <summary>GetServerTime: the server's clock, unix seconds.</summary>
    public Func<long> ServerNow = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    /// <summary>CMainSystem::GetServerTick, milliseconds.</summary>
    public Func<long> ServerTick = () => Environment.TickCount64;
    /// <summary>FUN_14083bdc0(CMsTime(t), true, "/", ":"): a date and time as text.</summary>
    public Func<long, string> DateText = unix => MailTexts.DateString(unix, 0);
    /// <summary>ShowMessageBox(text, type, this, tag); the dialog is the listener (<see cref="OnMessageBox"/>).</summary>
    public Action<string, int, string> ShowBox;
    /// <summary>FUN_14151f150 for the rename box (<see cref="InvalidNameRename.Create"/>, answered by the box layer) and the
    /// rename answer's text box.</summary>
    public Action<MessageBoxData> ShowRenameBox = MessageBoxQueue.Show;

    /// <summary>FUN_1418f13d0 -> SendCharacterManagementMode2 (:1520): CharacterManagement 2 for MyPC, the previewed character.</summary>
    public event Action<string> DeleteRequest;
    /// <summary>CancelDelete (:1537): CharacterManagement 3.</summary>
    public event Action<string> CancelDeleteRequest;
    /// <summary>CheckDelete (:1552): CharacterManagement 4, the final delete of a character whose wait is over.</summary>
    public event Action<string> FinalDeleteRequest;
    /// <summary>CharacterRemoved with the list left empty (:1404-1408): ClearPreviewCharacter and CloseDialog(0x4ef2).</summary>
    public event Action ListEmptied;

    public CharacterListController()
    {
        ShowBox = (text, type, tag) => MessageBoxQueue.ShowMessageBox(text, type, this, tag);
    }

    DeleteClock Clock(string sn)
    {
        sn ??= "";
        if (clocks.TryGetValue(sn, out var c)) return c;
        c = new DeleteClock();
        clocks[sn] = c;
        SetDeleteTime(c, Find(sn)?.DeleteTime ?? 0);
        return c;
    }

    /// <summary>FUN_1418e9ed0 (:459): the seconds left from now, on the server tick.</summary>
    void SetSeconds(DeleteClock c, uint seconds)
    {
        c.Seconds = seconds;
        c.Started = seconds > 0 ? ServerTick() : 0;
    }

    /// <summary>FUN_1418e9fa0 (:480): the delete time and the seconds to it from GetServerTime (0 when it is past).</summary>
    int SetDeleteTime(DeleteClock c, long deleteTime)
    {
        c.DeleteTime = deleteTime;
        long now = ServerNow();
        int seconds = now < deleteTime ? (int)(deleteTime - now) : 0;
        SetSeconds(c, (uint)seconds);
        return seconds;
    }

    /// <summary>FUN_1418e9f30 (:468): the seconds left, m_nSeconds less the whole seconds since m_nStarted (0 without a
    /// wait). As in the client it goes below 0 when a second is skipped, which the unsigned callers read as a long wait.</summary>
    int SecondsLeft(DeleteClock c)
    {
        if (c.Started > 0 && c.Seconds > 0)
        {
            uint passed = (uint)((ServerTick() - c.Started) / 1000);
            return (int)(c.Seconds - passed);
        }
        return 0;
    }

    /// <summary>The seconds left of character <paramref name="sn"/>'s delete wait (FUN_1418e9f30).</summary>
    public int DeleteSecondsLeft(string sn) => SecondsLeft(Clock(sn));
    /// <summary>The delete time of character <paramref name="sn"/> (FUN_1418ea020), 0 without one.</summary>
    public long DeleteTimeOf(string sn) => Clock(sn).DeleteTime;
    /// <summary>The characters with a renderer in the pending list (m_1f0).</summary>
    public IEnumerable<string> PendingDeletes => pending.Select(p => p.Sn);

    /// <summary>AddPending (:1173): the renderer of a character in its wait, replacing the one it had.</summary>
    void AddPending(string sn, object renderer)
    {
        int i = pending.FindIndex(p => p.Sn == sn);
        if (i >= 0) pending[i] = (sn, renderer);
        else pending.Add((sn, renderer));
    }

    /// <summary>SetDeleteState (@0x1418ef190, :1214): RemovePending, then the movie's cancelDelete(index): the row back to
    /// no delete state.</summary>
    void SetDeleteState(int index, string sn)
    {
        pending.RemoveAll(p => p.Sn == sn);
        if (Host?.Entity != null) Host.Invoke(Host.Entity, "cancelDelete", index);
    }

    /// <summary>
    /// vfunc_15 (@0x1418ead80, :649-678), every frame: each pending renderer's m_delete.m_label gets s_char_delete_waiting
    /// (common string 3) with the time left (FUN_141b708a0(left, 0)); the first one at 0 leaves the list and its renderer
    /// is invoked readyDelete (the final delete and restore buttons), and the pass ends there.
    /// </summary>
    public void Tick()
    {
        if (IsClosed || Host?.Entity == null || pending.Count == 0) return;
        for (int i = 0; i < pending.Count; i++)
        {
            var (sn, renderer) = pending[i];
            if (renderer == null) continue;
            uint left = (uint)SecondsLeft(Clock(sn));
            if (left > 0)
            {
                string text = ClientFormat.Format(MessageBoxData.Common(3), TimeSpanText(left, false));
                if (Host.Get(renderer, "m_delete") is { } del && Host.Get(del, "m_label") is { } label) Host.Set(label, "text", text);
            }
            else
            {
                pending.RemoveAt(i);
                Host.Invoke(renderer, "readyDelete");
                return;
            }
        }
    }

    /// <summary>FUN_141b708a0 @0x141b708a0 (util.cpp:999): a time span as text (s_common ids 0x97b-0x980).</summary>
    public static string TimeSpanText(uint seconds, bool showSeconds)
    {
        string F(int id, params object[] a) => ClientFormat.Format(MessageBoxData.Common(id), a);
        if (seconds < 60) return F(0x97b, seconds);
        if (seconds < 3600)
        {
            uint m = seconds / 60, s = seconds % 60;
            return s == 0 ? F(0x97c, m) : F(0x97e, m, s);
        }
        uint h = seconds / 3600, mm = seconds / 60 % 60, rest = seconds % 60;
        if (showSeconds && rest != 0) return F(0x980, h, mm, rest);
        return mm == 0 ? F(0x97d, h) : F(0x97f, h, mm);
    }

    /// <summary>
    /// ShowDeleteError (@0x1418f47e0, :1959-1977): the text of a delete error, shown in a plain box (ShowMessageBox(text, 0,
    /// 0, "")): 1 s_char_err_already_destroy, 2 s_char_err_exist_ugc_map, 3 s_char_err_guild_master, 4 s_char_err_guild,
    /// 5 s_char_err_ugc_market, 6 s_char_err_black_market_count, 7 s_char_err_unread_mail, 8 s_char_err_no_destroy_wait,
    /// 9 s_char_err_meso_market_count, 11 s_char_err_wedding, any other s_char_err_destroy with the code.
    /// </summary>
    public static string DeleteErrorText(int error) => (uint)error switch
    {
        1 => MessageBoxData.Common(0x16),
        2 => MessageBoxData.Common(0x15),
        3 => MessageBoxData.Common(0x19),
        4 => MessageBoxData.Common(0x1a),
        5 => MessageBoxData.Common(0x1b),
        6 => MessageBoxData.Common(0x1c),
        7 => MessageBoxData.Common(0x1d),
        8 => MessageBoxData.Common(0x1e),
        9 => MessageBoxData.CommonByKey("s_char_err_meso_market_count") ?? "",
        11 => MessageBoxData.CommonByKey("s_char_err_wedding") ?? "",
        _ => ClientFormat.Format(MessageBoxData.Common(0x17), (uint)error),
    };

    void ShowDeleteError(int error) => ShowBox(DeleteErrorText(error), 0, "");

    /// <summary>
    /// OnCharacterListPacket mode 2, DeleteEntry (:940-962): error 0 erases the character (EraseCharacter) and the movie's row
    /// with it (CharacterRemoved); any other zeroes its seconds, shows the error (ShowDeleteError) and puts its row back to
    /// no delete state (SetDeleteState). Then m_1d8 = 0. Returns the index the character had, -1 when it stayed.
    /// </summary>
    public int OnDeleteEntry(int error, string sn)
    {
        int index = -1;
        if (error == 0)
        {
            index = Characters.FindIndex(c => c.CharSN == sn);
            if (index >= 0) Characters.RemoveAt(index);
        }
        if (!IsClosed)
        {
            if (index >= 0) CharacterRemoved(sn, index);
            else
            {
                int at = Characters.FindIndex(c => c.CharSN == sn);
                if (at >= 0) SetSeconds(Clock(sn), 0);
                ShowDeleteError(error);
                SetDeleteState(at, sn);
            }
        }
        deleteSent = false;
        return index;
    }

    /// <summary>
    /// CharacterRemoved (@0x1418f0e20, :1399-1414): the movie's deleteCharacter(index) and the dialog's input on; with no
    /// character left the preview goes (<see cref="ListEmptied"/>); then the character's own folder goes
    /// (<see cref="RemoveCharacterFolder"/>, :1410-1413).
    /// </summary>
    void CharacterRemoved(string sn, int index)
    {
        clocks.Remove(sn);
        pending.RemoveAll(p => p.Sn == sn);
        if (Host?.Entity == null) return;
        Host.Invoke(Host.Entity, "deleteCharacter", index);
        SetInput(true);
        if (selectedSn == sn) selectedSn = null;
        if (Characters.Count == 0) ListEmptied?.Invoke();
        RemoveCharacterFolder?.Invoke(sn);
    }

    /// <summary>The end of CharacterRemoved: the removed character's folder under the account's (DeleteDirectoryTree on
    /// RESOURCE_PATH + FUN_1412756c0(account, character), <see cref="CharacterFolderRemoval"/>), by the character's id.</summary>
    public event Action<string> RemoveCharacterFolder;

    /// <summary>
    /// OnCharacterListPacket mode 5, BeginDelete (:964-1000), skipped while the dialog is closed: error 0 sets the delete time
    /// (FUN_1418e9fa0) and, with a time, the movie's requestDelete(index, seconds), whose renderer joins the pending list;
    /// error 10 shows s_char_err_next_delete_char_date with the time (FUN_14083bdc0); any other ShowDeleteError. Then m_1d8 = 0.
    /// </summary>
    public void OnBeginDelete(string sn, int error, long deleteTime)
    {
        if (!IsClosed)
        {
            if (error == 0)
            {
                int index = Characters.FindIndex(c => c.CharSN == sn);
                if (index >= 0)
                {
                    int seconds = SetDeleteTime(Clock(sn), deleteTime);
                    if (deleteTime > 0 && Host?.Entity != null
                        && Host.Invoke(Host.Entity, "requestDelete", index, (uint)seconds) is Avm2.Flash.AsDisplayObject renderer)
                        AddPending(sn, renderer);
                }
            }
            else if (error == 10)
                ShowBox(ClientFormat.Format(MessageBoxData.CommonByKey("s_char_err_next_delete_char_date") ?? "", DateText(deleteTime)), 0, "");
            else ShowDeleteError(error);
        }
        deleteSent = false;
    }

    /// <summary>
    /// OnCharacterListPacket mode 6, CancelDelete (:1002-1018), skipped while the dialog is closed: error 0 clears the delete
    /// time (FUN_1418e9fa0(0)) and puts the row back to no delete state (SetDeleteState); any other ShowDeleteError.
    /// </summary>
    public void OnCancelDelete(string sn, int error)
    {
        if (IsClosed) return;
        if (error == 0)
        {
            int index = Characters.FindIndex(c => c.CharSN == sn);
            if (index >= 0)
            {
                SetDeleteTime(Clock(sn), 0);
                SetDeleteState(index, sn);
            }
        }
        else ShowDeleteError(error);
    }

    /// <summary>CancelDelete (@0x1418f1940, :1537): the character's seconds zeroed, then CharacterManagement 3.</summary>
    void CancelDelete(LobbyCharacterRow c)
    {
        if (c == null) return;
        SetSeconds(Clock(c.CharSN), 0);
        CancelDeleteRequest?.Invoke(c.CharSN);
    }

    /// <summary>The request m_1d8 waits for was not answered (the connection failed): the dialog takes delete requests again.
    /// The client has no such step; its session ends with the connection.</summary>
    public void DeleteUnanswered() => deleteSent = false;

    /// <summary>
    /// OnMessage (@0x1418eb540, :735-785) for the dialog's boxes: any answer closes the box (FUN_14151f200); an OK splits the
    /// tag at the first ',': "deletePC,&lt;typed&gt;" sends the delete when the typed name is MyPC's (CompareNoCase), else
    /// s_char_err_delete_name (FUN_140d73bc0(0x18)); "revivalChar,&lt;id&gt;" cancels the wait (CancelDelete);
    /// "finalDelete,&lt;id&gt;" sends the final delete (CheckDelete). MyPC is the previewed character.
    /// </summary>
    public bool OnMessageBox(MessageBoxController sender, int button, string tag)
    {
        MessageBoxQueue.Next();
        if (button != 0) return true;
        var parts = (tag ?? "").Split(',', 2);   // FUN_14028cb70(tag, parts, ",", 2)
        if (parts.Length != 2) return true;
        switch (parts[0])
        {
            case "deletePC":
                if (parts[1].Length > 0 && Find(previewedSn) is { } pc
                    && string.Equals(parts[1], pc.CharName ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    SendDelete(pc.CharSN);
                    return true;
                }
                ShowBox(MessageBoxData.Common(0x18), 0, "");
                break;
            case "revivalChar":
                if (ulong.TryParse(parts[1], out ulong rid) && rid > 0) CancelDelete(Find(parts[1]));
                break;
            case "finalDelete":
                if (ulong.TryParse(parts[1], out ulong fid) && fid > 0 && !deleteSent && Find(parts[1]) != null)
                {
                    deleteSent = true;   // CheckDelete (:1552-1569)
                    FinalDeleteRequest?.Invoke(parts[1]);
                }
                break;
        }
        return true;
    }

    /// <summary>SendCharacterManagementMode2 (@0x1418f1880, :1520-1534): once (m_1d8), for MyPC.</summary>
    void SendDelete(string sn)
    {
        if (deleteSent || sn == null) return;
        deleteSent = true;
        DeleteRequest?.Invoke(sn);
    }

    LobbyCharacterRow Find(string sn) => sn == null ? null : Characters.Find(c => c.CharSN == sn);

    /// <summary>
    /// vfunc_ugc_1 (@0x1418ed310), UIUGCEventDispatcher's profile picture: the character's row (FindCharacter) takes the new
    /// picture's path and the movie updateProfile(index, path). <paramref name="path"/> is GetProfileImagePath(key, 1).
    /// </summary>
    public void OnProfileImage(long characterId, string path)
    {
        if (IsClosed) return;
        int i = Characters.FindIndex(c => c.CharSN == characterId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (i < 0) return;
        Characters[i] = Characters[i] with { ImgPath = path ?? "" };
        Host?.Invoke(Host.Entity, "updateProfile", i, path ?? "");
    }

    static string Arg(object[] args) => args.Length > 0 ? Avm2.Rt.ToString(args[0]) : null;

    static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
