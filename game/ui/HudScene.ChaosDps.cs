using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The chaos damage meter, UIChaosDPSDialog (0x4f23, movie uichaosdpsdialog), over the session's battle record manager (group 2).
/// It opens from the options' Apply while the chaos meter is on and the player is in a party (CGameOptionInterface::vfunc_10,
/// gameoptionstruct.cpp:376-384) and from the game scene's open list under the same two conditions (uiservice.cpp:751-754); its own
/// frame update closes it when the saved option, the party or feature 0xbe stops holding (@0x140e06630); the party book's member
/// removal drops a row (partymanager.cpp:962-965, RemoveMemberAt); its onNotice text goes to the party chat (CommandChat kind 7).
/// </summary>
public partial class HudScene
{
    ChaosDpsController chaosDps;
    MovieDialog chaosDpsMovie;
    Action unwatchChaosDps;

    bool ChaosDpsOpen => chaosDps != null && !chaosDps.IsClosed && !chaosDps.Closing;

    /// <summary>IsChaosDamageMeterOn (@0x14125bba0 family): the switch and the allowed flag of the saved interface option.</summary>
    static bool ChaosMeterOn() => GameOptions.Current.InterfaceOption is { EnableChaosDamageMeter: true, ChaosDamageMeterAllowed: true };

    /// <summary>The game scene's open list (uiservice.cpp:751-754): the meter on and in a party.</summary>
    void ChaosDpsOnEntry()
    {
        if (ChaosMeterOn() && partyBook?.IsInParty == true) OpenChaosDps();
    }

    /// <summary>The options' Apply (gameoptionstruct.cpp:376-384): OpenDialog(0x4f23) while the meter is on and in a party; CloseDialog when it is off.</summary>
    void ShowChaosDps(bool on)
    {
        if (on) { if (partyBook?.IsInParty == true) OpenChaosDps(); }
        else if (ChaosDpsOpen) chaosDps.CloseKeepingTheGroup();
    }

    /// <summary>OpenDialog(0x4f23); its OnLoadComplete starts the chaos group and ranks the members.</summary>
    void OpenChaosDps()
    {
        if (ChaosDpsOpen || uiRoot == null || partyBook == null || !partyBook.IsInParty) return;
        if (GuideRefusesOpen("ChaosDPSDialog")) return;
        if (Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        if (chaosDpsMovie != null) { chaosDpsMovie.Host.Unload(); chaosDpsMovie.QueueFree(); chaosDpsMovie = null; }
        WatchChaosDps();
        menuModel ??= NewModel();
        var c = new ChaosDpsController(partyBook, live.BattleRecords)
        {
            SelfCharacterId = () => live.CharacterId,
            MeterOn = ChaosMeterOn,
            FeatureEnabled = () => hudModel.IsFeatureEnabled(0xbe),
            ClearMeterOption = () => { if (GameOptions.Current.InterfaceOption is { } o) o.EnableChaosDamageMeter = false; },
            PartyChat = text => live.SendChat(new UserChatRequest(7, text, "", 0)),
        };
        if (live.GameSession is { } gs) c.ServerNow = () => DateTimeOffset.FromUnixTimeSeconds(gs.ServerTime).LocalDateTime;
        var m = new MovieDialog(assets, c, menuModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (chaosDpsMovie == m) chaosDpsMovie = null;
        };
        chaosDps = c; chaosDpsMovie = m;
        GD.Print($"chaos dps: load={m.Loaded}");
        uiRoot.AddChild(m);
    }

    /// <summary>The party book's RemoveMemberAt: an open chaos meter drops its last row under ten members.</summary>
    void WatchChaosDps()
    {
        if (partyBook == null || unwatchChaosDps != null) return;
        var book = partyBook;
        Action<PartyMember> removed = _ => { if (ChaosDpsOpen) chaosDps.RemoveMemberIfUnderLimit(); };
        book.MemberRemoved += removed;
        unwatchChaosDps = () => book.MemberRemoved -= removed;
    }

    /// <summary>UIChaosDPSDialog::vfunc_72 each frame while it is open.</summary>
    void TickChaosDps()
    {
        if (ChaosDpsOpen) chaosDps.Update(Time.GetTicksMsec());
    }

    /// <summary>Live runs: the options' Apply with the chaos meter set to <paramref name="on"/>; true while it is open.</summary>
    public bool ChaosDpsShowScripted(bool on)
    {
        if (GameOptions.Current.InterfaceOption is { } o) o.EnableChaosDamageMeter = on;
        ShowChaosDps(on);
        return ChaosDpsOpen;
    }
}
