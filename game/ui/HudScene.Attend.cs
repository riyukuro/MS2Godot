using Godot;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The attendance gift window, UIAttendGiftDialog (0x4eb8, movie uiattendgiftdialog) as <see cref="AttendGiftDialogController"/> over the
/// session's <see cref="AttendGiftState"/> (the client keeps one DT dialog that every attendance event and packet tells, open or not;
/// the movie is made on each open). Openers: the game menu entry "attend" (id 38, ToggleDialog(0x4eb8)), whose visibility is
/// CGameMenuItem::IsVisible's attendGift filter (the dialog has active events, gameeventmanagercli.cpp:345-350) and whose badge is
/// GetTotalUsablePoints (uigamemenudialog.cpp:1359-1363); and the Attendance packet's "select event" kind (the state's Open).
/// STILL-OPEN: the item preview of the right click needs ItemPreviewMgr::m_bHudOn, which no source here sets (the shop leaves it
/// unset too); the game menu's badge is read when the menu refreshes (its own trigger for this id is not recovered); the event
/// openers of the popup (UIDTAttendPopup), the cash attendance (0x4eb9) and the long term attendance are later parts of the row.
/// </summary>
public partial class HudScene
{
    AttendGiftDialogController attendGift;
    MovieDialog attendGiftMovie;
    bool attendWatched;

    bool AttendGiftOpen => attendGift != null && !attendGift.IsClosed;

    /// <summary>Once per scene: the event strings and the game menu's refresh when an attendance event starts or ends
    /// (CGameMenuItem::IsAffectedBy, "AttendGift"), and the state's notice and open hooks.</summary>
    void WatchAttend()
    {
        if (attendWatched || Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        attendWatched = true;
        Ms2Godot.Net.LiveSession.EventStringOf = key => hudModel.Tables.GameEventStrings.GetString(key);
        var state = live.AttendDialog;
        state.SystemNotice = key => SystemNoticeText(hudModel.Tables.Common(key) ?? "");
        state.Open = () => OpenAttendGift();
        live.Attend.Started += _ => gameMenu?.MarkDirty();
        live.Attend.Ended += _ => gameMenu?.MarkDirty();
    }

    /// <summary>The game menu's attendGift filter (CGameMenuItem::IsVisible @0x140cf0bf0): the dialog has active events. The other
    /// filters have no source here and hide their entries.</summary>
    bool GameMenuFilterPasses(GameMenuItem item) =>
        item.FilterType == "attendGift" && Ms2Godot.Net.LiveSession.Instance?.AttendDialog.HasActiveEvents == true;

    /// <summary>ToggleDialog(0x4eb8): an open window closes (vfunc_67), a closed one opens.</summary>
    void ToggleAttendGift()
    {
        if (AttendGiftOpen) { attendGift.Close(); return; }
        OpenAttendGift();
    }

    void OpenAttendGift()
    {
        if (Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        if (GuideRefusesOpen("AttendGiftDialog")) return;
        WatchAttend();
        if (AttendGiftOpen) return;
        if (attendGiftMovie != null) { attendGiftMovie.Host.Unload(); attendGiftMovie.QueueFree(); attendGiftMovie = null; }
        var tables = hudModel.Tables;
        var c = new AttendGiftDialogController(live.AttendDialog)
        {
            Send = live.SendAttendance,
            FindItemUid = live.FirstItemUid,
            Language = ClientLanguageIndex,
            ActionKeyDown = CompactActionDown,
            PreviewItem = item => OpenItemPreview(item, 0),     // ItemPreviewMgr::OpenPreview(item, 0) (uiattendgiftdialog.cpp:628)
        };
        var m = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (attendGiftMovie == m) attendGiftMovie = null;
        };
        attendGift = c; attendGiftMovie = m;
        GD.Print($"attend gift: load={m.Loaded}");
        uiRoot.AddChild(m);
    }

    /// <summary>Scripted runs: the state of the window, for the log.</summary>
    public string AttendGiftText()
    {
        var s = Ms2Godot.Net.LiveSession.Instance?.AttendDialog;
        return !AttendGiftOpen
            ? $"closed events={s?.Events.Count} active={s?.ActiveEvents.Count}"
            : $"open load={attendGiftMovie?.Loaded} events={s.Events.Count} active={s.ActiveEvents.Count} selected={s.Selected?.Id} points={s.GetTotalUsablePoints()}";
    }
}
