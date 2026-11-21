using Godot;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The death state's dialog: UIRevivalDialog (0x4e38, movie uirevivaldialog) is up while the own player is dead and goes
/// when the player is alive again. Its sends go through the session (Revival 0x1F, RequestMoveField). The environment reads
/// what the Godot client has (level, meso, the dead flag, the field's revival flags, the merat wallet, the own buffs and the
/// constants, RevivalWiring); the rest keeps the controller's defaults (STILL-OPEN: the dungeon room limits, the meso revival
/// rule, the equipment's cost reductions and the revival coupon).
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
    /// The dialog is opened by <see cref="OnOwnDied"/>, never from here.</summary>
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

    /// <summary>MyPC::vfunc_137's dead branch (mypc.cpp:1347-1355), each time it runs: ClosePopupMenu and open 0x4e38 only when
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
            id => hudModel.Tables.MapName(id), PaidInputs());
        c.Unloaded += () => { revivalMovie?.Host.Unload(); revivalMovie?.QueueFree(); revivalMovie = null; };
        revival = c;
        revivalMovie = new MovieDialog(assets, revival, hudModel, ScreenMode.PixelSize(GetWindow()));
        uiRoot.AddChild(revivalMovie);
    }

    /// <summary>The paid revive's inputs: the merat wallet summed (DAT_143ab2978), the own buffs and the client constants; null
    /// before the own buff list exists (the paid buttons then send nothing).</summary>
    RevivalPaidInputs PaidInputs()
    {
        if (ownBuffs == null) return null;
        slotConstants ??= Ms2.Ui.FeatureElements.Constants(assets.Packs);
        var wallet = hudModel.Player;
        return new RevivalPaidInputs(() =>
        {
            long sum = 0;
            if (wallet.MeratWallet != null) foreach (var m in wallet.MeratWallet) sum += m;
            return sum;
        }, ownBuffs, slotConstants);
    }
}
