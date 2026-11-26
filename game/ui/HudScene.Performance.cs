using Godot;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The frame rate dialog, UIPerformanceDialog (0x4e7d, movie uiperformancedialog), over CMainSystem's frame rate and the
/// session's time sync round trip. Its routes: the options' "show frame rate" on Apply (gameoption.cpp:1339-1342: OpenDialog
/// or CloseDialog), left Ctrl+P (CUIService::FUN_141aec690, uiservice.cpp:2879: ToggleDialog) and the game scene's open
/// list when that option is saved on (uiservice.cpp:744).
/// </summary>
public partial class HudScene
{
    PerformanceDialogController performance;
    MovieDialog performanceMovie;
    /// <summary>CMainSystem's m_fFps, ticked every frame whether the dialog is open or not, as OnTick is.</summary>
    readonly FrameRateMeter frameRate = new();

    bool PerformanceOpen => performance != null && !performance.IsClosed;

    /// <summary>The game scene's open list (uiservice.cpp:744): the dialog opens when the saved graphic option's frame rate
    /// flag (+0x14) is set.</summary>
    void PerformanceOnEntry()
    {
        if (GameOptions.Current.GraphicOption.ShowFrameRate) OpenPerformance();
    }

    /// <summary>The options' Apply (gameoption.cpp:1339-1342): OpenDialog(0x4e7d) when shown, else CloseDialog(0x4e7d).</summary>
    void ShowPerformance(bool shown)
    {
        if (shown) OpenPerformance(); else ClosePerformance();
    }

    void TogglePerformance()
    {
        if (PerformanceOpen) ClosePerformance(); else OpenPerformance();
    }

    /// <summary>OpenDialog(0x4e7d): a closed dialog loads its movie again (no tween: the close unloaded it).</summary>
    void OpenPerformance()
    {
        if (PerformanceOpen || uiRoot == null) return;
        if (GuideRefusesOpen("PerformanceDialog")) return;
        if (performanceMovie != null) { performanceMovie.Host.Unload(); performanceMovie.QueueFree(); performanceMovie = null; }
        menuModel ??= NewModel();
        var c = new PerformanceDialogController();
        var m = new MovieDialog(assets, c, menuModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (performanceMovie == m) performanceMovie = null;
        };
        performance = c; performanceMovie = m;
        GD.Print($"performance: load={m.Loaded}");
        uiRoot.AddChild(m);
    }

    /// <summary>CloseDialog(0x4e7d): vfunc_67, which with no tween saves the place and unloads at once.</summary>
    void ClosePerformance()
    {
        if (PerformanceOpen) performance.Close();
    }

    /// <summary>CMainSystem::OnTick's frame count, then UIPerformanceDialog::vfunc_72 while the dialog is open.</summary>
    void TickPerformance()
    {
        frameRate.Tick(Time.GetTicksUsec() / 1_000_000.0);
        if (PerformanceOpen)
            performance.Update(frameRate.Fps, Ms2Godot.Net.LiveSession.Instance?.GameSession?.Latency.Average ?? 0);
    }

    /// <summary>
    /// CUIService::FUN_141aec690 (uiservice.cpp:2879), each UI tick: nothing while CInputKeyHandler::FUN_14116d7a0(0xf) holds
    /// (<see cref="Ms2.Ui.InputBlock"/>); P pressed (DIK 0x19) with the left Ctrl held (DIK 0x1d) toggles the dialog. The
    /// key is not taken: the key table still sees the P, as in the client.
    /// </summary>
    void PerformanceKeys(InputEventKey k)
    {
        if (k.PhysicalKeycode == Key.P && k.Pressed && !k.Echo && leftCtrlDown && k.CtrlPressed && !Ms2.Ui.InputBlock.Active)
            TogglePerformance();
    }

    /// <summary>Live runs: the options' Apply with "show frame rate" set to <paramref name="shown"/>; true while it is open.</summary>
    public bool PerformanceShowScripted(bool shown)
    {
        EnsureOptionHost();
        optionHost.ShowFrameRate(shown);
        return PerformanceOpen;
    }

    /// <summary>Live runs: left Ctrl+P (<see cref="TogglePerformance"/>); true while it is open.</summary>
    public bool PerformanceToggleScripted() { TogglePerformance(); return PerformanceOpen; }

    /// <summary>Live runs: the dialog's line, or "closed".</summary>
    public string PerformanceText() => PerformanceOpen ? performance.Text ?? "" : "closed";
}
