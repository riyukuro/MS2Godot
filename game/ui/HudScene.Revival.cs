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

    bool RevivalOpen => revival != null && !revival.IsClosed;

    /// <summary>Each frame: open the dialog on the own death, close it on the revive; the open dialog runs its 1 s refresh.</summary>
    void RevivalTick()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        bool dead = live?.Me.Dead ?? false;
        if (dead && !RevivalOpen) OpenRevival(live);
        else if (!dead && RevivalOpen) revival.Close();
        if (RevivalOpen) revival.Update((uint)Time.GetTicksMsec());
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
        e.Send = live.SendRevival;
        e.Notice = text => SystemNoticeText(text);
        c.Unloaded += () => { revivalMovie?.Host.Unload(); revivalMovie?.QueueFree(); revivalMovie = null; };
        revival = c;
        revivalMovie = new MovieDialog(assets, revival, hudModel, ScreenMode.PixelSize(GetWindow()));
        uiRoot.AddChild(revivalMovie);
    }
}
