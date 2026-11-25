using Godot;
using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The party's damage meter, UIPartyDPSDialog (0x4e8c, movie uipartydpsdialog), over the session's battle record manager.
/// Ctrl+Q toggles it while in a party (uiscenegame.cpp:1313: DIK 0x1d held and DIK 0x10, ToggleDialog(0x4e8c)); the party
/// book's member removal drops a row (partymanager.cpp:956, RemoveMemberAt); its onNotice text goes to the party chat
/// (CommandChat kind 7).
/// </summary>
public partial class HudScene
{
    PartyDpsController partyDps;
    MovieDialog partyDpsMovie;
    BattleRecordManager shotRecords;                    // --uigen-open partydps: the offline records
    Action unwatchPartyDps;
    bool leftCtrlDown;                                  // DIK 0x1d: the left Ctrl key only (TrackLeftCtrl)
    /// <summary>The key that toggled the meter also goes to the casts (see <see cref="PartyDpsKeys"/>).</summary>
    bool partyDpsCastThrough;

    bool PartyDpsOpen => partyDps != null && !partyDps.IsClosed && !partyDps.Closing;

    void TogglePartyDps()
    {
        if (PartyDpsOpen) { partyDps.CloseKeepingTheGroup(); return; }   // ToggleDialog's close: vfunc_67, the group keeps running
        OpenPartyDps();
    }

    BattleRecordManager PartyDpsRecords() => Ms2Godot.Net.LiveSession.Instance?.BattleRecords ?? shotRecords;

    /// <summary>OpenDialog(0x4e8c); its OnLoadComplete starts the party group and ranks the members.</summary>
    void OpenPartyDps()
    {
        if (GuideRefusesOpen("PartyDPSDialog")) return;
        if (PartyDpsOpen || partyBook == null || !partyBook.IsInParty || PartyDpsRecords() is not { } records) return;
        if (partyDpsMovie != null) { partyDpsMovie.Host.Unload(); partyDpsMovie.QueueFree(); partyDpsMovie = null; }
        WatchPartyDps();
        menuModel ??= NewModel();
        var live = Ms2Godot.Net.LiveSession.Instance;
        var c = new PartyDpsController(partyBook, records)
        {
            SelfCharacterId = () => live?.CharacterId ?? PartyFixture.SelfCharacterId,
            PartyChat = text =>
            {
                if (live == null) { GD.Print($"party dps: notice ({text.Length} chars), offline: not sent"); return; }
                live.SendChat(new UserChatRequest(7, text, "", 0));
            },
        };
        if (live?.GameSession is { } gs) c.ServerNow = () => DateTimeOffset.FromUnixTimeSeconds(gs.ServerTime).LocalDateTime;
        var m = new MovieDialog(assets, c, menuModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (partyDpsMovie == m) partyDpsMovie = null;
        };
        partyDps = c; partyDpsMovie = m;
        GD.Print($"party dps: load={m.Loaded}");
        uiRoot.AddChild(m);
    }

    /// <summary>Live runs: the meter opened as Ctrl+Q opens it (TogglePartyDps); true while it is open.</summary>
    public bool PartyDpsToggleScripted() { TogglePartyDps(); return PartyDpsOpen; }

    /// <summary>Live runs: the meter's mode, group state and last ranking, as one log line.</summary>
    public string PartyDpsText()
    {
        if (partyDps == null) return "no meter";
        var rec = Ms2Godot.Net.LiveSession.Instance?.BattleRecords;
        var rows = new System.Text.StringBuilder();
        foreach (var m in partyDps.Ranking)
            rows.Append($" [{m.User.Name} {(rec == null ? "" : Ms2.Ui.Hud.PartyDpsController.MeterText(rec, Ms2.Ui.Hud.PartyDpsController.Group, m.CharacterId, partyDps.Mode))}]");
        return $"open {PartyDpsOpen}, mode {partyDps.Mode}, group running {rec?.IsGroupActive(Ms2.Ui.Hud.PartyDpsController.Group)}, time {rec?.ElapsedTimeText(Ms2.Ui.Hud.PartyDpsController.Group)}, ranking{rows}";
    }

    /// <summary>Live runs: the movie's own mode button (onTextToggle).</summary>
    public bool PartyDpsNextModeScripted() => PartyDpsOpen && partyDps.OnCallback("onTextToggle", System.Array.Empty<object>());

    /// <summary>The party book's RemoveMemberAt: an open meter drops its last row under ten members.</summary>
    void WatchPartyDps()
    {
        if (partyBook == null || unwatchPartyDps != null) return;
        var book = partyBook;
        Action<PartyMember> removed = _ => { if (PartyDpsOpen) partyDps.RemoveMemberIfUnderLimit(); };
        book.MemberRemoved += removed;
        unwatchPartyDps = () => book.MemberRemoved -= removed;
    }

    /// <summary>UIPartyDPSDialog::vfunc_72 each frame while it is open: closes out of a party, else the 100 ms ranking.</summary>
    void TickPartyDps()
    {
        if (PartyDpsOpen) partyDps.Update(Time.GetTicksMsec());
    }

    /// <summary>The left Ctrl key's state from its own press and release events (the modifier flag takes either Ctrl).</summary>
    void TrackLeftCtrl(InputEvent e)
    {
        if (e is InputEventKey { Keycode: Key.Ctrl, Location: KeyLocation.Left } k) leftCtrlDown = k.Pressed;
    }

    /// <summary>
    /// Left Ctrl+Q (the physical keys; IsKeyDown(0x1d) and WasKeyPressed(0x10), uiscenegame.cpp:1313) toggles the meter in a
    /// party; Escape closes it. The toggle is UISceneGamePlay::vfunc_13 (uiscenegame.cpp:1098), the UI scene's key handler,
    /// while the quick slot keys read the keyboard on their own: CKeyTable's key tests go through
    /// MS2InputService::WasKeyPressedGated (ms2inputservice.cpp:263), which for a key outside the modifier group
    /// (0x92..0xa1) is the raw NiInputKeyboard KeyWasPressed (:973) with no Ctrl test, so a Q bound to a quick slot casts too.
    /// </summary>
    bool PartyDpsKeys(InputEventKey k)
    {
        if (k.PhysicalKeycode == Key.Q && k.Pressed && !k.Echo && leftCtrlDown && k.CtrlPressed && !Ms2.Ui.InputBlock.Active && partyBook?.IsInParty == true)
        {
            TogglePartyDps();
            partyDpsCastThrough = true;
            return true;
        }
        if (PartyDpsOpen && k.Keycode == Key.Escape) { TogglePartyDps(); return true; }
        return false;
    }

    /// <summary>--uigen-open partydps: the meter on the party fixture with offline records (damage dealt per member), for the
    /// gallery (no server).</summary>
    void OpenPartyDpsShot()
    {
        if (partyBook == null) return;
        var ids = new List<long>();
        foreach (var m in partyBook.Members) if (m != null) ids.Add(m.CharacterId);
        if (shotRecords == null)
        {
            shotRecords = new BattleRecordManager { CharacterIdOf = id => id >= 1 && id <= ids.Count ? ids[id - 1] : 0, NpcClassOf = _ => 6 };
            foreach (var id in ids) shotRecords.AddPartyMemberRecord(id);
        }
        OpenPartyDps();
        for (int i = 1; i <= ids.Count; i++)            // object i is member i; object 0 a class 6 npc
            shotRecords.RecordDamage(i, 0, new[] { new Hit((byte)(i % 2), -12_345L * i * i), new Hit(2, 0) });
        // The movie shows its title bar and timer only under the pointer (ROLL_OVER: onMouseRollOver plays "show"; configUI
        // hides m_timer, ROLL_OUT plays "hide"): the shot has no mouse, so its pointer goes over the top row.
        if (PartyDpsOpen && partyDps.Host is { Entity: { } en } h)
            h.Player.MouseMove(Rt.ToNumber(h.Get(en, "x")) + Rt.ToNumber(h.Get(h.Get(en, "m_TopRank"), "x")) + 20,
                Rt.ToNumber(h.Get(en, "y")) + Rt.ToNumber(h.Get(h.Get(en, "m_TopRank"), "y")) + 10);
    }
}
