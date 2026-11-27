using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The shadow expedition boss bar, UIShadowExpeditionBossBar (0x4f0e, movie uishadowexpeditionbossbar; uishadowexpeditionbossbar.cpp), and
/// its packet listener FieldGameScoreBoardCli (opcode ShadowExpedition, <see cref="FieldGameScoreBoard"/>). The listener finds the dialog
/// (FindObject), gives it the points and the title and opens it (FUN_141924680, uidialog.cpp:445) when it is not open.
/// The field's scene leave closes it (UISceneGame::vfunc_49 -> CloseClosableDialogs, uiservice.cpp:1496: the ctor sets flag 1, not flag 2).
/// STILL-OPEN: where the client creates the
/// instance FindObject returns is not traced, the bar is made when the first packet asks for it.
/// </summary>
public partial class HudScene
{
    readonly FieldGameScoreBoard scoreBoard = new();
    ShadowExpeditionBossBarController bossBar;
    MovieDialog bossBarMovie;
    bool scoreBoardWired;

    bool BossBarOpen => bossBar != null && !bossBar.IsClosed;

    void OnShadowExpedition(FieldGameScoreBoardPacket packet)
    {
        if (!scoreBoardWired)
        {
            scoreBoardWired = true;
            scoreBoard.ServerString = s => hudModel.Tables.TriggerStrings.GetString(s);
            scoreBoard.FindBossBar = () => BossBarOpen ? bossBar : (bossBar = new ShadowExpeditionBossBarController());
            scoreBoard.Open = OpenBossBar;
        }
        scoreBoard.OnPacket(packet);
    }

    /// <summary>FUN_141924680: no local player, no dialog.</summary>
    void OpenBossBar(ShadowExpeditionBossBarController c)
    {
        if (!hudModel.HasLocalPlayer) return;
        menuModel ??= NewModel();
        var m = new MovieDialog(assets, c, menuModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (bossBarMovie == m) bossBarMovie = null;
        };
        bossBarMovie = m;
        GD.Print($"boss bar: {c.Title} {c.Cur}/{c.Max} load={m.Loaded}");
        uiRoot.AddChild(m);
    }

    void CloseBossBar()
    {
        if (BossBarOpen) bossBar.Close();
        if (bossBarMovie != null)
        {
            bossBarMovie.Host.Unload(); bossBarMovie.QueueFree(); bossBarMovie = null;
        }
        bossBar = null;
    }

    /// <summary>Scripted runs: the state of the bar, for the log.</summary>
    public string BossBarText() => !BossBarOpen ? "closed" : $"open {bossBar.Title} {bossBar.Cur}/{bossBar.Max} load={bossBarMovie?.Loaded}";
}
