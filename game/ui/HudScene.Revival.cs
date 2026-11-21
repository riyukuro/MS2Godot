using Godot;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The death state's dialog: UIRevivalDialog (0x4e38, movie uirevivaldialog) is up while the own player is dead and goes
/// when the player is alive again. Its sends go through the session (Revival 0x1F, RequestMoveField). The environment reads
/// what the Godot client has (level, meso, the dead flag); the rest keeps the controller's defaults (STILL-OPEN: the field
/// flags, the dungeon room limits, the constants and the revival coupon are not in the Godot data yet).
/// </summary>
public partial class HudScene
{
    RevivalDialogController revival;
    MovieDialog revivalMovie;
    AutoReviveCountDownController autoRevive;
    MovieDialog autoReviveMovie;
    bool revivalWatched;

    bool RevivalOpen => revival != null && !revival.IsClosed;

    /// <summary>Each frame: open the dialog on the own death, close it on the revive; the open dialog runs its 1 s refresh.</summary>
    void RevivalTick()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live != null && !revivalWatched) { revivalWatched = true; live.AutoReviveStarted = OpenAutoRevive; }
        bool dead = live?.Me.Dead ?? false;
        if (dead && !RevivalOpen) OpenRevival(live);
        else if (!dead && RevivalOpen) revival.Close();
        if (RevivalOpen) revival.Update((uint)Time.GetTicksMsec());
        if (autoRevive is { IsClosed: false }) autoRevive.Update((uint)Time.GetTicksMsec());     // UIAutoReviveCountDownDialog::vfunc_72
    }

    /// <summary>OnAutoRevive (scenestategame.cpp:4332): open 0x4ea2 when it is closed, then arm it (FUN_141284d70).</summary>
    void OpenAutoRevive(int durationMs)
    {
        if (autoRevive == null || autoRevive.IsClosed)
        {
            if (autoReviveMovie != null) { autoReviveMovie.Host.Unload(); autoReviveMovie.QueueFree(); autoReviveMovie = null; }
            var c = new AutoReviveCountDownController();
            c.Unloaded += () => { autoReviveMovie?.Host.Unload(); autoReviveMovie?.QueueFree(); autoReviveMovie = null; };
            autoRevive = c;
            autoReviveMovie = new MovieDialog(assets, autoRevive, hudModel, ScreenMode.PixelSize(GetWindow()));
            uiRoot.AddChild(autoReviveMovie);
        }
        autoRevive.Arm((uint)Time.GetTicksMsec(), durationMs);
    }

    void OpenRevival(Ms2Godot.Net.LiveSession live)
    {
        if (revivalMovie != null) { revivalMovie.Host.Unload(); revivalMovie.QueueFree(); revivalMovie = null; }
        var c = new RevivalDialogController();
        var e = c.Env;
        e.HasPlayer = () => live.Me.ObjectId >= 0;
        e.IsDead = () => live.Me.Dead;
        e.Level = () => live.Me.User?.Level ?? 1;
        e.Meso = () => live.Me.Meso;
        e.RevivalBlocked = () => live.Me.RevivalBlocked;
        e.RoomRevivalsUsed = () => live.Me.DungeonRevivalsUsed;
        e.Send = live.SendRevival;
        e.Notice = text => SystemNoticeText(text);
        c.Unloaded += () => { revivalMovie?.Host.Unload(); revivalMovie?.QueueFree(); revivalMovie = null; };
        revival = c;
        revivalMovie = new MovieDialog(assets, revival, hudModel, ScreenMode.PixelSize(GetWindow()));
        uiRoot.AddChild(revivalMovie);
    }
}
