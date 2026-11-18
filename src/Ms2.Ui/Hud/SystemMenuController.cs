namespace Ms2.Ui.Hud;

/// <summary>One channel row of the system menu's list (UIServerListDialog::BuildChannelList @0x141ad3370).</summary>
public readonly record struct ChannelRow(int Id);

/// <summary>
/// The C++ side of the system menu, UISystemMenuDialog (client/ms2/uisystemmenudialog.cpp; UI id 0x4e21), for its movie
/// uisystemmenudialog. The menu's layout, buttons and channel list are the movie's; this sends in the leave entry, the
/// red circle and the channel rows, and handles the movie's calls.
/// </summary>
public sealed class SystemMenuController : UiController
{
    public override string Movie => "uisystemmenudialog";
    protected override string EntityMethod => "arrangeConrols";

    /// <summary>The field's CFieldDataCli::m_nMapType; null when the field has no field data.</summary>
    public int? MapType;
    /// <summary>GetLeaveButtonText(map type) (@0x141b146a0); null when it cannot be made here (the entry stays hidden).</summary>
    public string LeaveText;
    /// <summary>FUN_1418fd280: the player is in a house of their own (no house state offline: false).</summary>
    public bool InOwnHouse;
    /// <summary>m_198 (SetRedCircle @0x141b144f0): the option entry's red circle.</summary>
    public bool RedCircle;
    /// <summary>GetCurrentChannel.</summary>
    public int CurrentChannel = 1;
    /// <summary>The current server's channels in map order (CMultiServer::m_channels); rows above the channel limit are
    /// already left out by the caller when the build is older than 50.</summary>
    public IReadOnlyList<ChannelRow> Channels = Array.Empty<ChannelRow>();
    /// <summary>GetStringCommon(0x579), s_word_ch.</summary>
    public string ChannelWord = "";
    /// <summary>The field's channel group name (string table 0x17 by GetFieldChannelGroupId); not used when the client
    /// country is 2 (DAT_143ab0248).</summary>
    public string ChannelGroupName = "";
    public bool CountryIs2;

    /// <summary>OpenGameOptionDialog(page) @0x141b14560: the options dialog opens on that page (0 for OnGameOption, which
    /// opens it without SetTab), then the menu closes.</summary>
    public event Action<int> OpenGameOption;
    /// <summary>OnSelectCharacter @0x141b14600: GetNetworkService()->SendRequestQuit(). The menu stays.</summary>
    public event Action SelectCharacter;
    /// <summary>FUN_1418fc760 OnGameExit (uichatballoon.cpp:463): message box 0x288 (s_msg_game_over), type 0x17.</summary>
    public event Action GameExit;
    /// <summary>The leave entry's click: OnLeaveHomeClick (map type 3) or OnLeaveFieldClick, with the map type.</summary>
    public event Action<int> Leave;
    /// <summary>WorldService::FUN_141bc80c0(channel): asks to change channel; false keeps the current one selected.</summary>
    public Func<int, bool> ChangeChannel = _ => false;

    bool open;

    /// <summary>FUN_141924740: open and not closing (the ctor @0x141b12ad0 turns the tween on, as UiController opens).</summary>
    public bool IsOpen => open;

    /// <summary>The menu's leave entry shows for these map types (FUN_1403751b0: 3, 5, 7, 8, 9, 13, 14, 17, 90).</summary>
    public static bool HasLeaveEntry(int mapType) => mapType is 3 or 5 or 7 or 8 or 9 or 13 or 14 or 17 or 90;

    /// <summary>
    /// GetLeaveButtonText @0x141b146a0 from the common strings by key (the numeric ids are commonstringtable.cpp's): a home
    /// is 0xaaf s_room_exit_ugc_indoor outside the design (0xabb), blueprint (0xabc) and preview (0xabd) states; 8 and 14
    /// format 0xab1 s_room_exit_massive_event with the dungeon name (null when there is none to give).
    /// </summary>
    public static string LeaveButtonText(int mapType, Func<string, string> common, int houseState = 0, string dungeonName = null,
        Func<string, object[], string> format = null) => mapType switch
    {
        3 => common(houseState switch
        {
            1 => "s_room_exit_msg_design",
            3 => "s_room_exit_msg_blueprint",
            2 => "s_room_exit_msg_blueprint_preview",
            _ => "s_room_exit_ugc_indoor",
        }),
        5 => common("s_room_exit_dungeon"),
        7 => common("s_room_exit_bonus"),
        8 or 14 => dungeonName == null || format == null ? null : format("s_room_exit_massive_event", new object[] { dungeonName }),
        9 => common("s_room_exit_pvp_zone"),
        13 => common("s_room_exit_arcade"),
        _ => common("s_room_exit_name_" + mapType.ToString(System.Globalization.CultureInfo.InvariantCulture)),
    };

    /// <summary>OnLoadInit @0x141b12d50, then OnLoadComplete @0x141b132d0 (UIDialog's, setTopMostEnable(1), Notify).
    /// OnLoadInit runs here, after the movie's first frames, as the host has no separate load-init step.</summary>
    public override void OnLoadComplete()
    {
        OnLoadInit();
        base.OnLoadComplete();
        open = true;
        Host.Invoke("setTopMostEnable", true);   // FUN_141923680(1): ASWindow::SetTopMostEnable
        Notify();
    }

    void OnLoadInit()
    {
        var exitRoom = Host.Child("m_exitRoom");
        if (MapType is int t && HasLeaveEntry(t) && (t == 3 || !InOwnHouse))
        {
            if (LeaveText != null && exitRoom != null)
            {
                Func<object[], object> click = _ => { Leave?.Invoke(t); return null; };
                Host.Invoke(exitRoom, "addEventListener", "buttonClick", click, false, 0, false);   // ASButton::AddClickHandler
                Host.Set(exitRoom, "label", LeaveText);                                              // FUN_1422cdfb0
                Host.SetVisible(exitRoom, true);                                                     // vfunc_3(1)
            }
            else Host.RT.NoteMissing($"system menu: the leave entry's text for map type {t}");
        }
        Host.Invoke("arrangeConrols");
        Host.Invoke("setSystemOptionNotifyRedCircle", RedCircle);
    }

    /// <summary>
    /// Notify @0x141b13640: m_180 = the current channel; the rows {id, label, labelDropdown = "{0} {1}" of s_word_ch and the
    /// id} with the current one's index (BuildChannelList @0x141ad3370); the prefix is the channel group name and a space
    /// when there is one.
    /// </summary>
    public void Notify()
    {
        if (Host?.Entity == null) return;
        int selected = 0, i = 0;
        var rows = new List<object>();
        foreach (var c in Channels)
        {
            string label = ChannelWord + " " + c.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            rows.Add(new Dictionary<string, object> { ["id"] = c.Id, ["label"] = label, ["labelDropdown"] = label });
            if (c.Id == CurrentChannel) selected = i;
            i++;
        }
        string name = CountryIs2 ? "" : ChannelGroupName ?? "";
        string prefix = name.Length == 0 ? "" : name + " ";
        Host.Invoke("buildChannelList", rows, selected, prefix);
    }

    /// <summary>SelectCurrentChannel @0x141b14630.</summary>
    public void SelectCurrentChannel()
    {
        if (Host?.Entity != null) Host.Invoke("setSelectedChannel", CurrentChannel);
    }

    /// <summary>vfunc_67 (UIDialog's close; UISystemMenuDialog's also sets m_180 = 0).</summary>
    public override void Close()
    {
        open = false;
        base.Close();
    }

    /// <summary>vfunc_28 @0x141b13330: FUN_1418fc760's calls (OnGameExit, OnGameOption), then the menu's. UIDialog's
    /// (OnCloseDialog, OnEndClose, the drag calls) are taken by the host first.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (Is(name, "OnGameExit")) { GameExit?.Invoke(); return true; }
        if (Is(name, "OnGameOption")) { OpenGameOption?.Invoke(0); return true; }
        if (Is(name, "OnGameOptionDialog")) { OpenGameOptionDialog(1); return true; }
        if (Is(name, "OnSelectCharacter")) { SelectCharacter?.Invoke(); return true; }
        if (Is(name, "onGraphic")) { OpenGameOptionDialog(6); return true; }
        if (Is(name, "OnChangeChannel"))
        {
            if (args.Length > 0 && !ChangeChannel(Avm2.Rt.ToInt32(args[0]))) SelectCurrentChannel();
            return true;
        }
        return false;
    }

    void OpenGameOptionDialog(int page)
    {
        OpenGameOption?.Invoke(page);
        Close();
    }

    static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
