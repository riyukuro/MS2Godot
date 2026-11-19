namespace Ms2.Ui;

/// <summary>
/// What the character list hands the movie for one character: the object FUN_1418f42c0 (uicharacterlistdialog.cpp:1933)
/// fills for buildCharacterList / addCharacter.
/// </summary>
public sealed record LobbyCharacterRow(string CharName, int Gender, int JobCode, string LevelJob, string ImgPath,
    string CreatedDate, string LastPos, string CharSN);

/// <summary>
/// UICharacterListDialog (ms2/client/ms2/uicharacterlistdialog.cpp): the character select dialog's C++ side over the
/// uicharacterlistdialog movie (scaleform.ms2.controls.dialog.CharacterListDialog). What the client does with the answers
/// (the preview, the game start, character creation, the server list) is the host's, through the events.
/// </summary>
public sealed class CharacterListController : UiController
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
    /// <summary>m_1d4: the scene the dialog pops down for (FUN_1418f11c0 :1418-1426), 0 for none.</summary>
    int popdownScene;

    /// <summary>
    /// The client keeps this dialog open while the select map loads again; the host builds it anew, so it carries the
    /// dialog's state over (m_198, m_1a0, m_188, m_1c8, m_220, m_180) with the row the list had selected
    /// (<see cref="KeepRow"/>), which buildCharacterList then selects instead of RefreshLobby's pick.
    /// </summary>
    public void CarryFrom(CharacterListController old)
    {
        SavedName = old.SavedName; SavedSn = old.SavedSn;
        selectedSn = old.selectedSn; previewedSn = old.previewedSn; fadedIn = old.fadedIn; Selecting = old.Selecting;
        KeepRow = old.ListIndex;
    }

    /// <summary>The row to select on the rebuilt list (<see cref="CarryFrom"/>); null for RefreshLobby's own pick.</summary>
    public int? KeepRow;

    /// <summary>OnSelectCharacter went through (vfunc_28 :2263-2313): the character's id, "" for an empty slot. The input is off.</summary>
    public event Action<string> SelectCharacter;
    /// <summary>OnGameStart with a character not waiting for deletion (vfunc_28 :2246-2261 -> FUN_1418f13f0).</summary>
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
        built = true;
    }

    /// <summary>FUN_1418f42c0 (uicharacterlistdialog.cpp:1933-1956): one character's object.</summary>
    object Row(LobbyCharacterRow c) => Host.Obj(
        ("charname", c.CharName ?? ""), ("gender", c.Gender), ("jobcode", c.JobCode), ("levelJob", c.LevelJob ?? ""),
        ("imgPath", c.ImgPath ?? ""), ("createddate", c.CreatedDate ?? ""), ("lastpos", c.LastPos ?? ""), ("charSN", c.CharSN ?? ""));

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
            GameStart?.Invoke(found.CharSN);
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
        if (Is(name, "OnEndPopdown")) { EndPopdown?.Invoke(); return true; }
        return false;
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
        if (sn == null) { previewedSn = null; return; }
        SavedSn = sn;
        SavedName = "";
        previewedSn = sn;
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
