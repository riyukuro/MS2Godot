using System;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui;

namespace Ms2Godot.Net;

/// <summary>
/// CSceneStateGame::OnResponseRide (0x79, @0x14172a8f0, scenestategame.cpp:12026) for the own pc: modes 0, 1 and 2
/// (<see cref="OwnRide"/>). STILL-OPEN: other pcs' rides (modes 0/1 and the RideSync records), the passenger modes 3-5,
/// mode 6 (PC::FUN_1416642c0 and RideOnAction m_b28).
/// </summary>
public partial class LiveSession
{
    /// <summary>CommonResource +0x2b0 and +0x2b8 (commonresource.cpp:164-165): PCBase::vfunc_120 and vfunc_121's effects.</summary>
    public const string RideOnEffectKey = "item_Ride_Use", RideOffEffectKey = "item_Ride_UnUse";

    void OnResponseRide(ResponseRide r)
    {
        bool own = player != null && IsInstanceValid(player);
        switch (OwnRide.For(r, selfObject, own && player.Mount != null))
        {
            case OwnRide.Step.RideOn when own:
                OwnRideOn(r.RideOn);
                break;
            case OwnRide.Step.RideOff when own:
                // MyPC::vfunc_121: RemoveSkillSet of the ridee's skillSetId, PC::vfunc_121, NotifyStatusChanged(0xc, 0), then
                // the action's switch. STILL-OPEN: the skill set, the status event and the switch (UseSkill's cast, Interact's
                // state 0x19, Taxi 0x25, CashCall 0x2a, TakeLR's chair, Hold 7, Recall ...); a react ride-off reads null
                // (no case 20 in ReadRideOffAction, rideonaction.cpp:159), so it is a plain ride off.
                bool wasRiding = player.Mount != null;
                player.RideOff();
                // PCBase::vfunc_121: the ride-off effect only when a ridee was released (the body returns before it
                // otherwise). STILL-OPEN: the vfunc_25 test before both effects, rideOffBySkillEffect for type 0x18.
                if (wasRiding) PlayOwnCommonEffect(RideOffEffectKey);
                if (OwnRide.SummonsAfterRideOff(r, selfObject))
                    GD.Print($"live: ride off for summon {r.RideeId} item {r.ItemUid}");   // STILL-OPEN: state 0x26, StateSummonData
                break;
        }
    }

    /// <summary>PCBase::vfunc_120 (pcbase.cpp:3289) and MyPC::vfunc_120 (mypc.cpp:3295) of the own pc.</summary>
    void OwnRideOn(RideOnAction a)
    {
        if (fieldBuilder == null) return;
        Mount m;
        // Ridee::FUN_1417043a0 (ridee.cpp:411) returns 0 for an unknown riding id: no ridee, no state change
        try { m = Mount.Load(PackSet.Shared(Settings.DataDir), fieldBuilder, a.RideId); }
        catch (Exception ex) { GD.PushWarning($"live: ride {a.RideId}: {ex.Message}"); return; }
        player.RideOn(m);
        PlayOwnCommonEffect(RideOnEffectKey);
        // STILL-OPEN: hideRider (AddHideReason 2), the RideeItemInfo of a type 1 action, the camera target, PC::FUN_1416642c0
        // (m_b28); MyPC: ClearRecords, AddSkillSet of skillSetId, the dialog closes for hidePCInfo above 1 (0x4e2d, 0x4f2d,
        // 0x4ec8, 0x2711, 0x4f76, 0x4ec3), NotifyStatusChanged(0xc, 1)
    }
}
