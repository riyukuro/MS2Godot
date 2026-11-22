using System;
using Ms2.Formats.Model;
using Ms2.Ui;

namespace Ms2Godot.Net;

/// <summary>
/// TaxiStationAction (action key 8, <see cref="Ms2.Ui.TaxiStationAction"/>) over the live field: its poll runs in the
/// interact poll between gathering and the interact npc, and the key runs <see cref="TaxiKey"/>.
/// STILL-OPEN: Unk_PCMove::FUN_141683ef0 (the can-move check) is not ported, so it is taken as passing; the field's other
/// gates (the move cover, a field move) already stop the poll.
/// STILL-OPEN: the home privilege (FUN_14050da30) is unknown, so a home station never takes the key.
/// </summary>
public partial class LiveSession
{
    /// <summary>The taxi action, its requested list (DAT_143ab3098) and the field's stations.</summary>
    public readonly TaxiStationAction TaxiAction = new();
    /// <summary>TaxiStationAction::vfunc_2 (@0x14095bef0, actionkeyobject.cpp:811-821): the HUD opens the world map (0x4e42)
    /// when closed, calls SetTaxiMode(1) and adds guide trigger 0x11. Unset: the key does nothing.</summary>
    public Action TaxiKey;
    bool taxiPolled;
    Vec3 taxiAt;
    Func<bool> taxiPoll;

    /// <summary>vfunc_1 at the last polled point: the current field is DAT_143ab2870, the found list DAT_143ab3078.</summary>
    bool PollTaxi()
    {
        TaxiAction.Send ??= Send;
        bool hasPlayer = selfObject >= 0 && player != null && IsInstanceValid(player);
        return TaxiAction.Poll(hasPlayer, Me.Dead, true, InteractPlayerStateId(), taxiAt, null, Portals?.FieldId ?? 0,
                               Ms2Godot.Ui.StubHudData.Instance.FoundTaxis);
    }

    void UseTaxi()
    {
        if (selfObject < 0 || player == null || !IsInstanceValid(player)) return;   // vfunc_2: no player, return
        TaxiKey?.Invoke();
    }

    // ---- the ride: the world map's TakeTaxi (@0x141b5a550) enters StateTaxiCall (0x25) with the field ----

    /// <summary>Client::Constants TaxiCallDuration / TaxiCallBestDriverDuration (ms) and TaxiCallBestDriverLevel (the HUD sets
    /// them from the constants table).</summary>
    public int TaxiCallDurationMs = 3000, TaxiBestDriverDurationMs = 3000, TaxiBestDriverLevel = 25;
    TaxiCallState taxiCall;
    int taxiStateAtBegin;

    /// <summary>GetCurStateId() is 0x25: a call is running.</summary>
    public bool TaxiCalling => taxiCall is { Finished: false };
    /// <summary>The local player is in the field (WorldService::GetLocalPlayer).</summary>
    public bool TaxiPlayerReady => selfObject >= 0 && player != null && IsInstanceValid(player);
    public bool TaxiPlayerDead => Me.Dead;
    public int TaxiPlayerStateId => InteractPlayerStateId();
    public bool TaxiPlayerRiding => player != null && IsInstanceValid(player) && player.Mount != null;
    /// <summary>WorldService::FUN_141bcfd30(0x85359406, the player's point, TaxiStationFindDistance, 1, 0) != 0.</summary>
    public bool TaxiStationNear => TaxiAction.FindStation(taxiAt) >= 0;

    /// <summary>
    /// vfunc_59(0x25, new StateTaxiCallData(field)): vfunc_4 starts phase 0x65 for TaxiCallDuration, or
    /// TaxiCallBestDriverDuration from TaxiCallBestDriverLevel; vfunc_3 sends RequestTaxi mode 1 at 0x69.
    /// STILL-OPEN: the turn toward the station (yaw + 180, SetRotationAndTargetYaw) and the state's animation and effects.
    /// </summary>
    public void StartTaxiCall(int field, int level)
    {
        taxiCall = new TaxiCallState { Send = Send };
        taxiStateAtBegin = Me.State;
        taxiCall.Begin(true, field, TaxiCallState.Duration(level, TaxiBestDriverLevel, TaxiCallDurationMs, TaxiBestDriverDurationMs), true);
    }

    /// <summary>StateTaxiCall::vfunc_6 (@0x1417fdae0, statetaxicalldata.cpp:318-324) through <see cref="TaxiCallState.Cancel"/>: the
    /// state is left (a move input in 0x65, death, a knockback, a skill, any other state set) and the call ends in every phase but
    /// 0x69, where the request goes out.</summary>
    void EndTaxiCall()
    {
        if (taxiCall == null) return;
        taxiCall.Cancel();
        if (taxiCall.Finished) taxiCall = null;
    }

    /// <summary>The Player's per-frame ask (<see cref="Player.TaxiHold"/>). StateTaxiCall::vfunc_8 (@0x1417d9e0, :302-315): in 0x65
    /// the move direction sends the owner to state 2 (the call ends); in the other phases the state holds the body.</summary>
    bool TaxiHoldsMove(bool moveInput)
    {
        if (taxiCall == null) return false;
        bool held = taxiCall.HoldsMove(moveInput);
        if (taxiCall.Finished) taxiCall = null;
        return held;
    }

    /// <summary>The call is no longer the own state: a state other than the one the call began in (the server's UserState, a
    /// knockback), death, a ride, or a skill's cast.</summary>
    bool TaxiStateLeft() =>
        selfObject < 0 || player == null || !IsInstanceValid(player) || Me.Dead || Me.State != taxiStateAtBegin
        || player.Mount != null || player.DeathHoldsMove || player.SkillSequence != null;

    void TickTaxi(double delta)
    {
        if (taxiCall == null) return;
        if (TaxiStateLeft()) { EndTaxiCall(); if (taxiCall == null) return; }
        taxiCall.Tick(delta);
        if (taxiCall.Finished) taxiCall = null;
    }
}
