using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The dungeon finder, UIDungeonControlCenter (0x4f84, movie uidungeoncontrolcenter). The game menu's dungeonSearch entry
/// (FUN_14193d5e0, uigamemenudialog.cpp:2213) and the key action 400018 (keytable.cpp:806-808) toggle it while feature 0xf9 is on,
/// else UIDungeonSearchDialog (0x4f1c). Its find component sends the Party match request and cancel, its tiles open the room window,
/// its lobby buttons send RoomDungeon, its 0x53 and 0x1a handlers are fed from the party book and the session.
/// STILL-OPEN: the search dialog 0x4f1c, the setting dialog 0x4f86 and the detail popup 0x4f85 are not in this HUD (the buttons log
/// and open nothing); the account session state (FUN_140930b40) and DAT_143ab2878 have no feed.
/// </summary>
public partial class HudScene
{
    DungeonControlCenterController dungeonControlCenter;
    MovieDialog dungeonControlCenterMovie;
    Action unwatchDungeonControlCenter;

    bool DungeonControlCenterOpen => dungeonControlCenter != null && !dungeonControlCenter.IsClosed;

    /// <summary>The 0x53 and 0x1a feeds; once per scene (the client registers them in OnLoadComplete and drops them at close).</summary>
    void WatchDungeonControlCenter()
    {
        if (unwatchDungeonControlCenter != null) return;
        var live = Ms2Godot.Net.LiveSession.Instance;
        Action<PartyMessage> applied = msg => { if (DungeonControlCenterOpen) dungeonControlCenter.OnPartyApplied(msg); };
        Action<int, int> roomPacket = (cmd, _) => { if (DungeonControlCenterOpen) dungeonControlCenter.OnRewardPacket(cmd); };
        var book = partyBook;
        if (book != null) book.Applied += applied;
        if (live != null) live.DungeonRoomPacket += roomPacket;
        unwatchDungeonControlCenter = () =>
        {
            if (book != null) book.Applied -= applied;
            if (live != null) live.DungeonRoomPacket -= roomPacket;
            if (DungeonControlCenterOpen) dungeonControlCenter.Close();
        };
    }

    /// <summary>FUN_14193d5e0 (uigamemenudialog.cpp:2213): ToggleDialog(0x4f84) when feature 0xf9 is on, else ToggleDialog(0x4f1c).</summary>
    void ToggleDungeonSearch()
    {
        if (!hudModel.IsFeatureEnabled(DungeonControlCenterController.FinderFeature))
        {
            GD.Print($"dungeon finder: dialog 0x{DungeonControlCenterController.SearchDialogUiId:x} is not in this HUD");
            return;
        }
        if (DungeonControlCenterOpen) { dungeonControlCenter.Close(); return; }
        OpenDungeonControlCenter();
    }

    /// <summary>OpenDialog(0x4f84).</summary>
    void OpenDungeonControlCenter()
    {
        if (GuideRefusesOpen("DungeonControlCenter")) return;
        if (dungeonControlCenterMovie != null)
        {
            dungeonControlCenterMovie.Host.Unload(); dungeonControlCenterMovie.QueueFree(); dungeonControlCenterMovie = null;
        }
        WatchDungeonControlCenter();
        menuModel ??= NewModel();
        var tables = menuModel.Tables;
        var live = Ms2Godot.Net.LiveSession.Instance;
        var c = new DungeonControlCenterController(partyBook)
        {
            PreloadTables = tables,
            Records = live?.DungeonRecords ?? new DungeonRecordBook(),
            Send = p =>
            {
                if (live != null) live.SendParty(p);
                else GD.Print($"dungeon finder: offline, not sent: {p}");
            },
            LocalPlayer = () => hudModel.HasLocalPlayer ? ((ushort, uint)?)((ushort)hudModel.Player.Level, (uint)(live?.Me.User?.GearScore ?? 0)) : null,
            SelfCharacterId = () => live?.CharacterId ?? PartyFixture.SelfCharacterId,
            ClearState = id => live?.DungeonEnterStateOf((int)id),                              // CMyCharacter::GetDungeonEnterState
            MemberClearState = (member, id) => live?.Party.Find(member)?.Counter((int)id),      // CPartyMember::counters.Get
            ServerTime = () => live?.ServerTime ?? System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            TogglePartySearch = TogglePartySearch,
            OpenSetting = OpenDungeonSearchSetting,
            TogglePopupDetail = () => GD.Print("dungeon finder: dialog 0x4f85 is not in this HUD"),
            OpenBookmarkRoomDialog = () => OpenDungeonRoom(d => { d.ResetGroup(); return true; }),
            OpenRoomDialog = id => OpenDungeonRoom(d => { d.OpenRoom(id); return true; }),
        };
        var m = new MovieDialog(assets, c, menuModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (dungeonControlCenterMovie == m) dungeonControlCenterMovie = null;
        };
        dungeonControlCenter = c; dungeonControlCenterMovie = m;
        GD.Print($"dungeon finder: load={m.Loaded}");
        uiRoot.AddChild(m);
    }

    /// <summary>Scripted runs: the finder opened (or left open).</summary>
    public bool DungeonControlCenterScripted()
    {
        if (!DungeonControlCenterOpen) OpenDungeonControlCenter();
        return DungeonControlCenterOpen;
    }

    /// <summary>Scripted runs: the movie's callback on the open finder.</summary>
    public bool DungeonControlCenterCall(string name, params object[] args) =>
        DungeonControlCenterOpen && dungeonControlCenter.OnCallback(name, args);

    /// <summary>Scripted runs: the state of the window, for the log.</summary>
    public string DungeonControlCenterText() => !DungeonControlCenterOpen
        ? "closed"
        : $"open load={dungeonControlCenterMovie?.Loaded} subTab={dungeonControlCenter.SubTab} bookmarks={dungeonControlCenter.Bookmarks.Count} " +
          $"recommended={dungeonControlCenter.GetRecommendedRooms().Count}";

    DungeonSearchSettingController dungeonSearchSetting;
    MovieDialog dungeonSearchSettingMovie;

    bool DungeonSearchSettingOpen => dungeonSearchSetting != null && !dungeonSearchSetting.IsClosed;

    /// <summary>
    /// UIDungeonControlCenter::OnSetting's dialog (uidungeoncontrolcenter.cpp): GetDungeonSearchSettingDialog(0x4f86), SetSelected(bookmarks),
    /// OpenDialog. The OK button gives the ticked ids to the open control center (SetBookmarks) and closes the dialog.
    /// </summary>
    void OpenDungeonSearchSetting(IReadOnlyCollection<uint> bookmarks)
    {
        if (GuideRefusesOpen("DungeonSearchSettingDialog")) return;
        if (DungeonSearchSettingOpen) dungeonSearchSetting.Close();
        if (dungeonSearchSettingMovie != null) { dungeonSearchSettingMovie.Host.Unload(); dungeonSearchSettingMovie.QueueFree(); dungeonSearchSettingMovie = null; }
        menuModel ??= NewModel();
        var c = new DungeonSearchSettingController
        {
            RoomTable = () => menuModel.Tables.DungeonRooms,
            CanEnterRoom = id => DungeonControlCenterOpen && dungeonControlCenter.CanEnterDungeonRoom(id),
            HasLocalPlayer = () => hudModel.HasLocalPlayer,
            ApplyBookmarks = ids => { if (DungeonControlCenterOpen) dungeonControlCenter.SetBookmarks(ids); },
        };
        c.SetSelected(bookmarks);
        var m = new MovieDialog(assets, c, menuModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (dungeonSearchSettingMovie == m) dungeonSearchSettingMovie = null;
        };
        dungeonSearchSetting = c; dungeonSearchSettingMovie = m;
        GD.Print($"dungeon search setting: load={m.Loaded}");
        uiRoot.AddChild(m);
    }

    /// <summary>UIDungeonSearchSettingDialog's per-frame look at its list.</summary>
    void TickDungeonSearchSetting()
    {
        if (DungeonSearchSettingOpen) dungeonSearchSetting.Tick();
    }
}
