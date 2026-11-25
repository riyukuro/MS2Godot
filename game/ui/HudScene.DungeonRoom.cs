using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The dungeon room window, UIDungeonRoomDialog (0x4ebd, movie uidungeonroomdialog), over the session's record manager. The client
/// keeps one persistent UI object that its openers prime with the set-before-load calls (SetPortalId, SetGroupType, SetRoom,
/// OpenDungeonOrRoom) and open; here each open makes a fresh controller and applies the same calls (an open one is closed first, as the
/// portal action does, actionkeyobject.cpp:1076-1190). Openers: the hotkey (action 400037, keytable.cpp:828), the portal types 8, 0xc, 0xe
/// and 0x11 (<see cref="Ms2.Ui.PortalAction"/>), the level-up guide's dungeon rows and the party "dungeonRoom" chat link; the portal's
/// leave closes it. Its 0x53 and 0x1A handlers are fed from the party book and the session.
/// STILL-OPEN: the bookmark of the dungeon category, the world map and minimap, the guild and event openers (uidungeoncategory.cpp:654,
/// uiworldmapdialog.cpp:594, uiminimapentity.cpp:689, uiguilddialog.cpp:1230, gameeventcli.cpp:741) are not wired to it; the
/// <see cref="DungeonRoomHooks"/> this scene has no source for stay null (see the panel's summary); the gear score is the one the
/// character list block carried at login (the live value, FUN_14152ea50, has no source here).
/// </summary>
public partial class HudScene
{
    DungeonRoomDialogController dungeonRoom;
    MovieDialog dungeonRoomMovie;
    Action unwatchDungeonRoom;

    bool DungeonRoomOpen => dungeonRoom != null && !dungeonRoom.IsClosed;

    /// <summary>The portal and record hooks of the session, the 0x53 and 0x1A feeds; once per scene, after <see cref="WatchParty"/>.</summary>
    void WatchDungeonRoom()
    {
        if (unwatchDungeonRoom != null) return;
        var live = Ms2Godot.Net.LiveSession.Instance;
        Action<PartyMessage> applied = msg =>
        {
            if (!DungeonRoomOpen) return;
            dungeonRoom.OnPartyApplied(msg);                    // UIDungeonRoomDialog::OnPartyPacket
            dungeonRoom.OnRoomPacket53(msg.Mode);               // OnRoomPacket53 (the same 0x53 packet)
        };
        Action<int, int> roomPacket = (cmd, type) => { if (DungeonRoomOpen) dungeonRoom.OnRoomPacket(cmd, type); };
        var book = partyBook;
        if (book != null) book.Applied += applied;
        if (live != null)
        {
            live.DungeonRoomPacket += roomPacket;
            // PortalAction::OpenDungeonRoomDialog (@0x140956290): OpenDungeonOrRoom(portalId); no room, no dialog (s_msg_preparing)
            live.OpenDungeonRoomDialog = portalId => OpenDungeonRoom(c => { c.SetPortalId(portalId); return c.OpenDungeonOrRoom((uint)portalId); });
            // PortalAction::OpenDungeonRoomDialogWithMode (@0x1409569a0): SetGroupType(mode), SetPortalId(portalId)
            live.OpenDungeonRoomDialogWithMode = (portalId, mode) => OpenDungeonRoom(c => { c.SetGroupType(mode); c.SetPortalId(portalId); return true; });
            // PortalAction::FUN_140956a20 (@0x140956a20): OpenRoom(portalId), SetPortalId(portalId); opens whatever OpenRoom found
            live.OpenDungeonRoomDialogRoom = portalId => OpenDungeonRoom(c => { c.OpenRoom((uint)portalId); c.SetPortalId(portalId); return true; });
            // PortalAction::vfunc_3 (@0x140955990): CloseDialog(0x4ebd) when open
            live.CloseDungeonRoomDialog = () => { if (DungeonRoomOpen) dungeonRoom.Close(); };
        }
        unwatchDungeonRoom = () =>
        {
            if (book != null) book.Applied -= applied;
            if (live != null)
            {
                live.DungeonRoomPacket -= roomPacket;
                live.OpenDungeonRoomDialog = null; live.OpenDungeonRoomDialogWithMode = null;
                live.OpenDungeonRoomDialogRoom = null; live.CloseDungeonRoomDialog = null;
            }
            if (DungeonRoomOpen) dungeonRoom.Close();
        };
    }

    /// <summary>ToggleDialog(0x4ebd): an open window closes (vfunc_67), a closed one opens as it is (no room: the first group that has rooms).</summary>
    void ToggleDungeonRoom()
    {
        if (DungeonRoomOpen) { dungeonRoom.Close(); return; }
        OpenDungeonRoom(null);
    }

    /// <summary>The party link "dungeonRoom,&lt;room&gt;" and the level-up guide's rows: the window on that room (OpenRoom, then the open).</summary>
    void OpenDungeonRoomOf(int room) =>
        OpenDungeonRoom(c => { c.ResetGroup(); return c.OpenDungeonOrRoom((uint)room); });

    /// <summary>
    /// OpenDialog(0x4ebd) after the opener's calls. <paramref name="prime"/> runs on the new controller before the movie loads and
    /// answers false when the opener refused (OpenDungeonOrRoom found no room, so nothing opens).
    /// </summary>
    void OpenDungeonRoom(Func<DungeonRoomDialogController, bool> prime)
    {
        if (GuideRefusesOpen("DungeonRoomDialog")) return;
        if (DungeonRoomOpen) dungeonRoom.Close();               // the openers close the open dialog first (:1085, :1176, :1190)
        if (dungeonRoomMovie != null) { dungeonRoomMovie.Host.Unload(); dungeonRoomMovie.QueueFree(); dungeonRoomMovie = null; }
        WatchDungeonRoom();
        menuModel ??= NewModel();
        var tables = menuModel.Tables;
        var live = Ms2Godot.Net.LiveSession.Instance;
        var c = new DungeonRoomDialogController(partyBook)
        {
            PreloadTables = tables,
            Records = live?.DungeonRecords ?? new DungeonRecordBook(),
            Send = p =>
            {
                if (live != null) live.SendParty(p);
                else GD.Print($"dungeon room: offline, not sent: {p}");
            },
            NoticeText = SystemNoticeText,
            Notice = key => SystemNoticeText(tables.Common(key) ?? ""),
            LocalPlayer = () => hudModel.HasLocalPlayer ? ((ushort, uint)?)((ushort)hudModel.Player.Level, (uint)(live?.Me.User?.GearScore ?? 0)) : null,
        };
        c.Hooks.LocalName = () => hudModel.HasLocalPlayer ? hudModel.Player.Name : "";
        c.Hooks.SelfCharacterId = () => live?.CharacterId ?? PartyFixture.SelfCharacterId;
        c.Hooks.IsVip = () => live == null ? null : live.Me.PremiumTime > live.ServerTime;              // PC::IsVip
        c.Hooks.ClearState = id => live?.DungeonEnterStateOf((int)id);                                  // CMyCharacter::GetDungeonEnterState
        if (live != null) c.Hooks.AchieveName = id => AchieveTextsFor(live.Achieves).Name(id);
        if (prime != null && !prime(c)) return;
        var m = new MovieDialog(assets, c, menuModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (dungeonRoomMovie == m) dungeonRoomMovie = null;
        };
        dungeonRoom = c; dungeonRoomMovie = m;
        GD.Print($"dungeon room: load={m.Loaded}");
        uiRoot.AddChild(m);
    }

    // ---- scripted live runs (MS2_LIVE_PARTY "dungeonroom") ----

    /// <summary>Scripted runs: the open key (action 400037).</summary>
    public void DungeonRoomKey() => ToggleDungeonRoom();

    /// <summary>Scripted runs: the state of the window, for the log.</summary>
    public string DungeonRoomText() => !DungeonRoomOpen
        ? "closed"
        : $"open load={dungeonRoomMovie?.Loaded} room={dungeonRoom.Room?.Id} group={dungeonRoom.CurrentGroup} rooms={dungeonRoom.Rooms.Count} " +
          $"groups={dungeonRoom.GroupIds.Count} portal={dungeonRoom.PortalId} panel={(dungeonRoom.Panel != null)}";

    /// <summary>UIDungeonRoomDialog's per-frame look at its list.</summary>
    void TickDungeonRoom()
    {
        if (DungeonRoomOpen) dungeonRoom.Tick();
    }
}
