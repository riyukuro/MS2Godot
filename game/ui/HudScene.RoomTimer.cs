using Godot;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The room timer (RoomTimerManager, roomtimermanager.cpp) and its timer dialog (UITimerDialog, 0x4e3a, uitimerdialog.cpp):
/// the RoomTimer packet drives it, the game scene's open list shows an active timer again (CUIService, uiservice.cpp:746),
/// and a field move resets it with the field timer (WorldService::FUN_141bbc1d0, worldservice_1.cpp:3373-3376).
/// STILL-OPEN: the gauge window (UITimerGaugeDialog 0x4f61, type 2, with its lap icons) is not ported, so its packets are
/// logged; the low-time effect (timeLimit) is logged as in the trigger timer dialog.
/// </summary>
public partial class HudScene
{
    RoomTimerManager roomTimer;            // the client's singleton, kept across relayouts and field moves
    TimerDialogController timerDialog;     // its type 1 window, kept between opens
    MovieDialog timerDialogMovie;
    Action unwatchRoomTimer;

    void RoomTimerPane()
    {
        if (roomTimer == null)
        {
            timerDialog = new TimerDialogController
            {
                ServerTick = () => Net.LiveSession.Instance?.ServerTick ?? 0,
                NowMs = () => (uint)Time.GetTicksMsec(),
                PlayEffect = name => { GD.Print($"timer: effect {name} is not played (no screen effect path)"); return 0; },
                OpenRequested = OpenTimerDialog,
            };
            timerDialog.Unloaded += TimerDialogUnloaded;
            roomTimer = new RoomTimerManager
            {
                GetTimer = t => t == Ms2.Net.Packets.RoomTimerMessage.TimerDialog ? timerDialog : null,
                Common = id => hudModel?.Tables.Common(id) ?? "",
                Notice = (text, type) => ChatLog.ShowSystemNotice?.Invoke(text, type, true),
                Unapplied = m => GD.Print($"live: RoomTimer command {m.Command} type {m.Type} read, not applied (the timer gauge 0x4f61 is not ported)"),
            };
        }
        unwatchRoomTimer?.Invoke();
        unwatchRoomTimer = null;
        if (Net.LiveSession.Instance is { } live)
        {
            Action<Ms2.Net.Packets.RoomTimerMessage> on = roomTimer.Dispatch;
            live.RoomTimerReceived += on;
            unwatchRoomTimer = () => live.RoomTimerReceived -= on;
        }
        roomTimer.Show();
    }

    /// <summary>FUN_141924680 on 0x4e3a: its movie loaded when it is not.</summary>
    void OpenTimerDialog()
    {
        if (timerDialogMovie != null) return;
        timerDialogMovie = new MovieDialog(assets, timerDialog, hudModel, ScreenMode.PixelSize(GetWindow()));
        uiRoot.AddChild(timerDialogMovie);
    }

    void TimerDialogUnloaded()
    {
        timerDialogMovie?.Host.Unload();
        timerDialogMovie?.QueueFree();
        timerDialogMovie = null;
    }

    /// <summary>UITimerDialog::vfunc_72 while it is open.</summary>
    void RoomTimerTick()
    {
        if (timerDialogMovie != null && timerDialog is { IsClosed: false }) timerDialog.Update();
    }

    /// <summary>WorldService::FUN_141bbc1d0's map change: RoomTimerManager::Reset and the field timer (m_800-m_808) cleared.</summary>
    void RoomTimerMapChange()
    {
        roomTimer?.Reset();
        Net.LiveSession.Instance?.FieldTimer.Set(0, 0, 0);
    }

    void RoomTimerUnwatch()
    {
        unwatchRoomTimer?.Invoke();
        unwatchRoomTimer = null;
    }
}
