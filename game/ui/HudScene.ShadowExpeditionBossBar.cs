using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The field game score board's dialogs: the shadow expedition boss bar, UIShadowExpeditionBossBar (0x4f0e, movie uishadowexpeditionbossbar;
/// uishadowexpeditionbossbar.cpp), and the dark stream score dialog, UIDarkStreamScoreDialog (0x4ee4, movie uidarkstreamscoredialog;
/// uidarkstreamscoredialog.cpp), with their packet listener FieldGameScoreBoardCli (opcode ShadowExpedition, <see cref="FieldGameScoreBoard"/>).
/// The listener finds the dialog (FindObject), gives it the points (and the bar its title) and opens it (FUN_141924680, uidialog.cpp:445)
/// when it is not open.
/// The game scene's leave closes both (<see cref="CloseBossBar"/>: UISceneGame::vfunc_49 -> CloseClosableDialogs, uiservice.cpp:1496: the ctors
/// set flag 1, not flag 2); a same-server field change does not.
/// STILL-OPEN: where the client creates the instances FindObject returns is not traced, a dialog is made when the first packet asks for it.
/// </summary>
public partial class HudScene
{
    readonly FieldGameScoreBoard scoreBoard = new();
    ShadowExpeditionBossBarController bossBar;
    DarkStreamScoreDialogController scoreDialog;
    MovieDialog bossBarMovie, scoreDialogMovie;
    bool scoreBoardWired;

    bool BossBarOpen => bossBar != null && !bossBar.IsClosed;
    bool ScoreDialogOpen => scoreDialog != null && !scoreDialog.IsClosed;

    void OnShadowExpedition(FieldGameScoreBoardPacket packet)
    {
        if (!scoreBoardWired)
        {
            scoreBoardWired = true;
            scoreBoard.ServerString = s => hudModel.Tables.TriggerStrings.GetString(s);
            scoreBoard.FindBossBar = () => BossBarOpen ? bossBar : (bossBar = new ShadowExpeditionBossBarController { NotApplied = s => NotApplied.Print(s) });
            scoreBoard.FindScoreDialog = () => ScoreDialogOpen ? scoreDialog : (scoreDialog = new DarkStreamScoreDialogController());
            scoreBoard.Open = OpenScoreBoardDialog;
        }
        scoreBoard.OnPacket(packet);
    }

    /// <summary>FUN_141924680: no local player, no dialog.</summary>
    void OpenScoreBoardDialog(UiController c)
    {
        if (!hudModel.HasLocalPlayer) return;
        bool isBar = c == bossBar;
        menuModel ??= NewModel();
        var m = new MovieDialog(assets, c, menuModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (bossBarMovie == m) bossBarMovie = null;
            if (scoreDialogMovie == m) scoreDialogMovie = null;
        };
        if (isBar) bossBarMovie = m; else scoreDialogMovie = m;
        GD.Print(isBar ? $"boss bar: {bossBar.Title} {bossBar.Cur}/{bossBar.Max} load={m.Loaded}"
            : $"score dialog: {scoreDialog.Score} align {scoreDialog.Align} offset {scoreDialog.OffsetY} load={m.Loaded}");
        uiRoot.AddChild(m);
    }

    /// <summary>The scene leave: both dialogs close (flag 1, not flag 2), and the board forgets the one it stored.</summary>
    void CloseBossBar()
    {
        if (BossBarOpen) bossBar.Close();
        if (bossBarMovie != null)
        {
            bossBarMovie.Host.Unload(); bossBarMovie.QueueFree(); bossBarMovie = null;
        }
        bossBar = null;
        if (ScoreDialogOpen) scoreDialog.Close();
        if (scoreDialogMovie != null)
        {
            scoreDialogMovie.Host.Unload(); scoreDialogMovie.QueueFree(); scoreDialogMovie = null;
        }
        scoreDialog = null;
        scoreBoard.DropDialog();                     // the stored dialog is the one just closed: the next packet asks for it again
    }

    /// <summary>Scripted runs: the state of the bar, for the log.</summary>
    public string BossBarText() => !BossBarOpen ? "closed" : $"open {bossBar.Title} {bossBar.Cur}/{bossBar.Max} load={bossBarMovie?.Loaded}";

    /// <summary>Scripted runs: the state of the score dialog, for the log.</summary>
    public string ScoreDialogText() => !ScoreDialogOpen ? "closed" : $"open {scoreDialog.Score} align {scoreDialog.Align} offset {scoreDialog.OffsetY} load={scoreDialogMovie?.Loaded}";
}
