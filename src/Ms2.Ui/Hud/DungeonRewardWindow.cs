using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// The single UIDungeonRewardDialog object the client keeps for its life and what its packet and DungeonRecordManagerCli::OnOpenDialog
/// do with it. OnPacket (@0x140e22130, uidungeonrewarddialog.cpp:386): an open window is closed, the state reset, then the mode's state is
/// stored and modes 0, 1 and 3 call FUN_141924680 (uidialog.cpp:445), which opens the window unless there is no local player; mode 2 and an
/// unknown mode only store. The state stays in the object whether or not the window opened, until the next packet or an unload
/// (vfunc_26 -> ResetState). OnOpenDialog (dungeonrecordcli.cpp:306) opens the window with that state.
/// </summary>
public sealed class DungeonRewardWindow
{
    /// <summary>A new dialog state (the ResetState values).</summary>
    public Func<DungeonRewardDialogController> Create = () => new DungeonRewardDialogController();
    /// <summary>FUN_141924680: shows the window for the state; false when it did not open (no local player, a tutorial guide).</summary>
    public Func<DungeonRewardDialogController, bool> Open = _ => true;
    /// <summary>The window is open (not IsClosed).</summary>
    public Func<bool> IsOpen = () => false;
    /// <summary>Closes the open window and drops its movie.</summary>
    public Action CloseWindow = () => { };

    /// <summary>The controller of the window <see cref="Open"/> last showed.</summary>
    DungeonRewardDialogController shown;

    /// <summary>The state the dialog object holds without a window.</summary>
    public DungeonRewardDialogController Stored { get; private set; }

    public void OnPacket(DungeonRewardPacket packet)
    {
        CloseWindow();
        var c = Create();
        Stored = null;
        if (!c.OnPacket(packet))
        {
            if (packet.Mode == 2) Stored = c;
            return;
        }
        if (!Open(c)) Stored = c;
        else shown = c;
    }

    public void OnOpenRequested()
    {
        if (IsOpen()) return;
        var c = Stored ?? Create();
        Stored = null;
        if (!Open(c)) Stored = c;
        else shown = c;
    }

    /// <summary>
    /// <see cref="SceneChange.SceneLeft"/>: CloseClosableDialogs (@0x141adc050, uiservice.cpp:1496) closes the open window (the ctor sets no flag 2,
    /// uidungeonrewarddialog.cpp:293) through vfunc_68, which sends nothing. The state a mode 2 packet stored stays (only vfunc_26 resets it).
    /// STILL-OPEN: FUN_141ada610 runs only when the scene's open flag (FUN_141ad9e50) was set; it is taken as set.
    /// </summary>
    public void OnSceneLeft()
    {
        if (IsOpen() && shown != null && !shown.KeptAtSceneLeave) CloseWindow();   // IsOpen is the closed test of CloseClosableDialogs (vfunc_55)
    }
}
