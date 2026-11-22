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
    /// <summary>StateTaxiCall::vfunc_8 (@0x1417d9e0, statetaxicalldata.cpp:302-304) returns before State::vfunc_8 (state.cpp:~632, where
    /// the queued skill is cast) while m_n10 is not 0x65: in phases 0x66-0x6a no skill fires. In 0x65 one does, and the cast's state
    /// change ends the call (<see cref="TaxiSkillCast"/>). No allocation.</summary>
    public bool TaxiHoldsSkill => taxiCall is { HoldsSkill: true };

    /// <summary>A skill's cast in phase 0x65 sends the owner to the skill state (ChangeState): the call ends.</summary>
    public void TaxiSkillCast(bool entersSkillState) { if (entersSkillState) EndTaxiCall(); }

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

    /// <summary>The own state is set (CStateManager::ChangeState, statemanager.cpp:291, through PCBase::vfunc_59, pcbase.cpp:1098): the
    /// old state's vfunc_5 runs and the state is swapped in every phase, so the call ends and no RequestTaxi goes out, 0x69 included
    /// (<see cref="TaxiCallState.LeaveState"/>). Run for the server's UserState, OnDeadUser's vfunc_137, a ride and a skill's cast.
    /// STILL-OPEN: a hit with a reaction cancels the call before 0x69 (<see cref="TaxiCallState.Cancel"/>, pc.cpp:952/1075), to be
    /// wired when the own player's hit reaction lands.</summary>
    void EndTaxiCall()
    {
        if (taxiCall == null) return;
        taxiCall.LeaveState();
        taxiCall = null;
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

    /// <summary>The call is no longer the own state: a state other than the one the call began in (the server's UserState),
    /// death, a ride, or a skill's cast. A hit's reaction is not here (STILL-OPEN, <see cref="EndTaxiCall"/>).</summary>
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
