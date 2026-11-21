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
    Ms2Godot.Net.LiveSession revivalLive;      // the session whose handlers are bound

    bool RevivalOpen => revival != null && !revival.IsClosed;

    /// <summary>Each frame: bind to the current session, close the dialog on the revive; the open dialog runs its 1 s refresh.
    /// The dialog is opened once per death by <see cref="OnOwnDied"/>, never from here.</summary>
    void RevivalTick()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live != revivalLive) { RevivalUnwatch(); if (live != null) { revivalLive = live; live.AutoReviveStarted = OpenAutoRevive; live.Me.Died += OnOwnDied; } }
        if (RevivalOpen && !(live?.Me.Dead ?? false)) revival.Close();
        if (RevivalOpen) revival.Update((uint)Time.GetTicksMsec());
        if (autoRevive is { IsClosed: false }) autoRevive.Update((uint)Time.GetTicksMsec());     // UIAutoReviveCountDownDialog::vfunc_72
    }

    void RevivalUnwatch()
    {
        if (revivalLive == null) return;
        revivalLive.Me.Died -= OnOwnDied;
        if (revivalLive.AutoReviveStarted == (Action<int>)OpenAutoRevive) revivalLive.AutoReviveStarted = null;
        revivalLive = null;
    }

    /// <summary>MyPC::vfunc_137's dead branch (mypc.cpp:1347-1355): once, at the death, ClosePopupMenu and open 0x4e38 only when
    /// the field has neither auto-revival type 1 nor 2 (CFieldData::HasAutoRevivalType1 / 2, fielddata.cpp:827, 830). A field
    /// without data opens it (the client always has the data; ours has none only on a failed table read).</summary>
    void OnOwnDied()
    {
        var live = revivalLive;
        if (live == null) return;
        var fd = RevivalFieldData(live);
        if (fd != null && (fd.HasAutoRevivalType1 || fd.HasAutoRevivalType2)) return;
        if (popupMenu is { IsClosed: false }) popupMenu.Close();
        if (!RevivalOpen) OpenRevival(live);
    }

    Ms2.Formats.World.FieldData RevivalFieldData(Ms2Godot.Net.LiveSession live)
    {
        if (live.FieldId is not int id) return null;
        try { return Ms2.Formats.World.FieldData.FindById(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), id); } catch { return null; }
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
        RevivalWiring.Connect(e, live.Me, () => live.FieldId, _ => RevivalFieldData(live),
            live.SendRevival, text => SystemNoticeText(text), id => hudModel.Tables.Common(id),
            id => hudModel.Tables.MapName(id));
        c.Unloaded += () => { revivalMovie?.Host.Unload(); revivalMovie?.QueueFree(); revivalMovie = null; };
        revival = c;
        revivalMovie = new MovieDialog(assets, revival, hudModel, ScreenMode.PixelSize(GetWindow()));
        uiRoot.AddChild(revivalMovie);
    }
}
