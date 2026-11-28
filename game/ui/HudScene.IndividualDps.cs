using Godot;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The personal damage meter, UIIndividualDPSDialog (0x4edf, movie uiindividualdpsdialog), over the session's battle record
/// manager. It opens from the options' Apply (CGameOptionInterface::vfunc_10, gameoptionstruct.cpp:367-375: OpenDialog while
/// the individual meter is on, CloseDialog when it is not) and from the game scene's open list while the saved option has it
/// on (uiservice.cpp:749); its own frame update closes it when the saved option stops holding.
/// STILL-OPEN: the chaos meter (0x4f23, UIChaosDPSDialog) and its CDPSMeterSystem feed (DpsStat) are not ported.
/// </summary>
public partial class HudScene
{
    IndividualDpsController individualDps;
    MovieDialog individualDpsMovie;

    bool IndividualDpsOpen => individualDps != null && !individualDps.IsClosed && !individualDps.Closing;

    /// <summary>IsIndividualDamageMeterOn (@0x14125bb90): the switch and the allowed flag of the saved interface option.</summary>
    static bool IndividualMeterOn() => GameOptions.Current.InterfaceOption is { EnableIndividualDamageMeter: true, IndividualDamageMeterAllowed: true };

    /// <summary>The game scene's open list (uiservice.cpp:749).</summary>
    void IndividualDpsOnEntry()
    {
        if (IndividualMeterOn()) OpenIndividualDps();
    }

    /// <summary>The options' Apply (gameoptionstruct.cpp:367-375): OpenDialog(0x4edf) while the meter is on, else CloseDialog.</summary>
    void ShowIndividualDps(bool on)
    {
        if (on) OpenIndividualDps(); else if (IndividualDpsOpen) individualDps.CloseFromOptions();
    }

    /// <summary>OpenDialog(0x4edf): a closed dialog loads its movie again (no tween: the close unloaded it).</summary>
    void OpenIndividualDps()
    {
        if (IndividualDpsOpen || uiRoot == null) return;
        if (GuideRefusesOpen("IndividualDPSDialog")) return;
        if (Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        if (individualDpsMovie != null) { individualDpsMovie.Host.Unload(); individualDpsMovie.QueueFree(); individualDpsMovie = null; }
        menuModel ??= NewModel();
        var c = new IndividualDpsController(live.BattleRecords)
        {
            SelfCharacterId = () => live.CharacterId,
            MeterOn = IndividualMeterOn,
            ClearMeterOption = () => { if (GameOptions.Current.InterfaceOption is { } o) o.EnableIndividualDamageMeter = false; },
        };
        var m = new MovieDialog(assets, c, menuModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (individualDpsMovie == m) individualDpsMovie = null;
        };
        individualDps = c; individualDpsMovie = m;
        GD.Print($"individual dps: load={m.Loaded}");
        uiRoot.AddChild(m);
    }

    /// <summary>UIIndividualDPSDialog::vfunc_72 each frame while it is open.</summary>
    void TickIndividualDps()
    {
        if (IndividualDpsOpen) individualDps.Update(Time.GetTicksMsec());
    }

    /// <summary>Live runs: the options' Apply with the individual meter set to <paramref name="on"/>; true while it is open.</summary>
    public bool IndividualDpsShowScripted(bool on)
    {
        if (GameOptions.Current.InterfaceOption is { } o) o.EnableIndividualDamageMeter = on;
        ShowIndividualDps(on);
        return IndividualDpsOpen;
    }

    /// <summary>Live runs: the movie's own mode button (onTextToggle).</summary>
    public bool IndividualDpsNextModeScripted() => IndividualDpsOpen && individualDps.OnCallback("onTextToggle", System.Array.Empty<object>());

    /// <summary>Live runs: the meter's mode and the text last written, as one log line.</summary>
    public string IndividualDpsText() => individualDps == null ? "no meter"
        : $"open {IndividualDpsOpen}, mode {individualDps.Mode}, title {individualDps.Title}, meter {individualDps.MeterText}, time {individualDps.TimeText}";
}
