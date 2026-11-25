using System;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Net;

/// <summary>
/// CSceneStateGame::OnResponseRide (0x79, @0x14172a8f0, scenestategame.cpp:12026) for the own pc: modes 0, 1 and 2
/// (<see cref="OwnRide"/>), other players' in <see cref="Net.RemoteLayer.OnRide"/>; the own pc's passenger map and
/// no-boarding flag of modes 3-6 (<see cref="RidePassengers"/>), mode 3's own StateHold on another player's mount
/// (<see cref="Player.HoldOn"/>). STILL-OPEN: RecvOp NpcRide 0xE7 (CSceneStateGame::OnNpcRide,
/// scenestategame.cpp:4871: i32 npc id, then CNpc::FUN_141092c50: u8 mode 0 an i64, 1 an i32 owner object id the npc turns
/// toward and is offset from (FUN_141093eb0, vfunc_51), 2 clears it; the port has no npc bound to an owner) and the ridee
/// skill set (mypc.cpp:1219-1226).
/// The own pc's ride-off-first cast (IsUnrideOnUse, mypc.cpp:2447-2470) is <see cref="RideOffForSkill"/>.
/// </summary>
public partial class LiveSession
{
    /// <summary>CommonResource +0x2b0 and +0x2b8 (commonresource.cpp:164-165): PCBase::vfunc_120 and vfunc_121's effects.</summary>
    public const string RideOnEffectKey = "item_Ride_Use", RideOffEffectKey = "item_Ride_UnUse";

    /// <summary>
    /// MyPC::FUN_141525580's ride off before a skill (mypc.cpp:2447-2470): MyPC::SendRequestRideMode1 with the action
    /// (<see cref="Ms2.Ui.Skills.SkillUseCheck"/>), the battle mount check off (a skill action); a ride that refuses shows notice
    /// 0x655 (FUN_141275260(0x655, 1, 1)). False without the own pc on a ride.
    /// </summary>
    public bool RideOffForSkill(RideOffAction action)
    {
        if (game == null || player == null || !IsInstanceValid(player) || player.Mount == null) return false;
        var ride = player.Mount.Data;
        if (RideOff.Request(true, ride != null, ride?.Type ?? 0, ride?.EnableRideOffUI ?? false, action.Type, false,
                new RideOffSkillRequest(action), Send) == RideOff.Outcome.CantRide)
            SystemNoticeSink?.Invoke(MessageBoxData.Common(RideOff.CantRideNoticeId));
        return true;
    }

    /// <summary>The attack key (action 0x49401) as held state, which the HUD's key events set (Unk_PCMove::IsActionKeyDown).</summary>
    public bool AttackKeyHeld;

    /// <summary>The own CEquipment::GetAbilityRate (the HUD keeps the worn part and the buffs); 0 without the HUD.</summary>
    public Func<int, float> OwnAbilityRate = _ => 0f;

    /// <summary>The own buffs' HasEnabledWithEventBuffType (the HUD keeps the buffs); false without the HUD.</summary>
    public Func<int, bool> OwnEventBuff = _ => false;

    /// <summary>String 0x48 and s_strNoticeEp (ridee.cpp:347): Ridee::UpdateRunEffects' RunX without the EP.</summary>
    public const int RunXNoEpNoticeId = 0x48;
    public const string RunXNoEpSound = "system_notice_error_ep";

    /// <summary>Ridee::UpdateRunEffects' inputs for the own mount: the attack key, the rider's EP (stat 10), the field's
    /// useEPSkill, the ride effects at the pc (InsertEffect with the ridee as actor), and the no-EP notice
    /// (FUN_141275260(0x48, 1, 0), then PlaySystemSound).</summary>
    void WireRunX(Player p)
    {
        p.AttackKeyDown = () => AttackKeyHeld && !Ms2.Ui.InputBlock.Active;
        p.CurrentEp = () => Ms2Godot.Ui.StubHudData.Instance.EpCurrent;
        p.FieldUsesEp = FieldUsesEp;
        p.RideAbilityRate = type => OwnAbilityRate(type);
        p.RiderEventBuff = type => OwnEventBuff(type);
        p.PlayRideEffect = path =>
        {
            if (remote == null || player == null || !IsInstanceValid(player)) return 0;
            PlayOwnEffect(path);
            return remote.EffectLength(path);
        };
        // Ridee::vfunc_12's loop effect at the own mount (ridee.cpp:527-534), the actor's kind the own pc's
        p.KeepRideEffect = (path, actor) => SpawnKeptEffect(path, actor, Ms2.Audio.SoundMusicEffectState.CasterMyPc);
        p.StopRideEffect = StopKeptEffect;
        p.RideEffectScope = () => remote?.KeptEffectScope;
        p.RunXNoEp += () =>
        {
            string text = MessageBoxData.Common(RunXNoEpNoticeId);
            if (!string.IsNullOrEmpty(text)) SystemNoticeSink?.Invoke(text);
            FieldAudio.Instance?.PlayUi(RunXNoEpSound);
        };
    }

    /// <summary>The own pc's PCBase m_mapB28 (passenger object id to seat) and PC m_b1224 (ResponseRide modes 3-6).</summary>
    public readonly System.Collections.Generic.Dictionary<int, int> OwnPassengers = new();
    public bool OwnNoBoarding { get; private set; }

    void OnResponseRide(ResponseRide r)
    {
        if (r != null && r.Mode >= 3)
        {
            // FindPCById(owner) for modes 3-6: the own pc's map here, another player's in the remote layer; no owner, nothing
            if (r.ObjectId != selfObject) { if (remote == null || !remote.OnRide(r)) return; }
            else if (!RidePassengers.Apply(r, OwnPassengers) && r.Mode == 6) OwnNoBoarding = r.Flag;
            if (player == null || !IsInstanceValid(player)) return;
            // mode 3 with the own pc as the rider: vfunc_59(0x20, StateHoldData{owner, the seat's rideBone}) (:12073-12087)
            if (RidePassengers.BoardsSelf(r, selfObject)) player.HoldOn(r.ObjectId, remote?.SeatBone(r.ObjectId, r.Seat) ?? "");
            // modes 4 / 5: CameraMgr::SetTarget(the own pc) when the own pc holds on to this owner (:12096-12112)
            else if (r.Mode is 4 or 5) player.HoldCameraBack(r.ObjectId);
            return;
        }
        // modes 0-2 find any actor (FUN_141bd1200): another player's PC rides in the remote layer
        if (r != null && r.ObjectId != selfObject) { remote?.OnRide(r); return; }
        bool own = player != null && IsInstanceValid(player);
        switch (OwnRide.For(r, selfObject, own && player.Mount != null))
        {
            case OwnRide.Step.RideOn when own:
                OwnRideOn(r.RideOn);
                break;
            case OwnRide.Step.RideOff when own:
                // MyPC::vfunc_121: RemoveSkillSet of the ridee's skillSetId, PC::vfunc_121, NotifyStatusChanged(0xc, 0), then
                // the action's switch: Hold 7 boards the nearest mount (HoldAfterRideOff), CoupleEmotion 0x13 asks the couple emotion
                // (CoupleAfterRideOff). STILL-OPEN: the skill set and
                // the other cases (UseSkill's cast, Interact's state 0x19, Taxi 0x25, TakeLR's
                // chair, Recall ...); a react ride-off reads null (no case 20 in ReadRideOffAction, rideonaction.cpp:159), so
                // it is a plain ride off.
                bool wasRiding = player.Mount != null;
                string skillEffect = r.RideOff?.Type == RemoteLayer.UnRideSkillType ? player.Mount?.Data?.RideOffBySkillEffect : null;
                player.RideOff();
                OwnRideChanged?.Invoke(null);           // NotifyStatusChanged(0xc, 0) after PC::vfunc_121 (mypc.cpp:3365)
                // PCBase::vfunc_121: the ride-off effect only when a ridee was released (the body returns before it
                // otherwise), then an UnRideSkill's rideOffBySkillEffect (pcbase.cpp:3397-3401, :3423-3429). STILL-OPEN:
                // the vfunc_25 test before the effects.
                if (wasRiding) { PlayOwnCommonEffect(RideOffEffectKey); PlayOwnEffect(skillEffect); OwnPassengers.Clear(); }   // and ClearPassengers
                if (r.RideOff?.Type == RideOffHoldRequest.ActionType) HoldAfterRideOff();
                RecallAfterRideOff(r.RideOff);   // case 8 (mypc.cpp:3467-3472)
                CashCallAfterRideOff(r.RideOff); // case 4 (mypc.cpp:3420-3425)
                if (r.RideOff is RideOffCoupleEmotion couple) CoupleAfterRideOff?.Invoke(couple.Value30, couple.Value28);
                if (OwnRide.SummonsAfterRideOff(r, selfObject)) SummonAfterRideOff(r);
                break;
        }
    }

    /// <summary>PCBase::vfunc_120 (pcbase.cpp:3289) and MyPC::vfunc_120 (mypc.cpp:3295) of the own pc.</summary>
    void OwnRideOn(RideOnAction a)
    {
        if (fieldBuilder == null) return;
        Mount m;
        // Ridee::FUN_1417043a0 (ridee.cpp:411) returns 0 for an unknown riding id: no ridee, no state change
        try { m = Mount.Load(PackSet.Shared(Settings.DataDir), fieldBuilder, a.RideId, Me?.User?.Gender ?? 1); }
        catch (Exception ex) { GD.PushWarning($"live: ride {a.RideId}: {ex.Message}"); return; }
        player.RideOn(m);
        PlayOwnCommonEffect(RideOnEffectKey);
        OwnNoBoarding = a.Flag;           // PC::FUN_1416642c0(m_b28), pcbase.cpp:3341-3343
        // Player.RideOn hides the rider of a hideRider row. MyPC::vfunc_120's window closes for hidePCInfo above 1 and its
        // NotifyStatusChanged(0xc, 1) (mypc.cpp:3306-3317) are the HUD's (OwnRideChanged). STILL-OPEN: the RideeItemInfo of
        // a type 1 action, the camera target; MyPC: ClearRecords, AddSkillSet of skillSetId.
        OwnRideChanged?.Invoke(m.Data.UiRow);
    }

    /// <summary>The own ride on (the mount's row) and off (null), after PCBase::vfunc_120 / vfunc_121: MyPC's window
    /// closes and the riding status event follow it (mypc.cpp:3306-3317, :3365).</summary>
    public event Action<Ms2.Ui.RideeRow> OwnRideChanged;

    /// <summary>The own mount's row, null when the own pc does not ride (MyPC::IsRiding / GetRidee()->GetData()).</summary>
    public Ms2.Ui.RideeRow OwnRidee => player != null && IsInstanceValid(player) ? player.Mount?.Data?.UiRow : null;
}
