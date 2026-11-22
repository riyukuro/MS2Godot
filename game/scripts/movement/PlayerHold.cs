using System;
using Godot;
using Ms2.Net.Replay;
using Act = Ms2Godot.Movement.ActionAnims.Act;

namespace Ms2Godot;

/// <summary>
/// The own pc's StateHold (0x20, stateholddata.cpp:146-501) with its StateHoldData: ResponseRide mode 3 puts the own pc on
/// another player's mount seat (target = the owner, node = the seat's rideBone). Other players' holds are
/// <see cref="RemotePcStates"/>.
/// STILL-OPEN: the camera on the ridee (CameraMgr::SetTarget, FUN_1417ccd50) and back on the own pc for modes 4 / 5, the
/// shadow (vfunc_31), the job 0x46 hold skill key (FUN_1417cce10, feature 0xae), the own pc or a ridee as the held actor,
/// a hold entered without data (vfunc_59(1) at once), the vfunc_36 key 0x493e2 and FUN_14166ebf0(ridee) of the seat
/// placement.
/// </summary>
public partial class Player
{
    /// <summary>The held actor of the field by object id and node name (WorldService::FUN_141bd1200, FUN_1416757c0).</summary>
    public Func<int, string, HoldTarget> HoldLookup;

    /// <summary>StateHold::vfunc_5 left the hold while the camera was on the ridee: SendRequestRideMode4.</summary>
    public event Action HoldLeft;

    /// <summary>StateHoldData's skill (0x1312d1f, stateholddata.cpp:108).</summary>
    public const int HoldSkillId = 20000031;

    int holdTargetId;
    string holdNode = "";
    float holdEntryYaw;                   // StateHold m_kDir (vfunc_20 at entry)
    bool holdCameraOnRidee;               // the camera target is not the own pc (m_bRiding set at entry, until modes 4 / 5)

    /// <summary>MyPC::FUN_14152e060: the held actor's id while in the hold, else 0.</summary>
    public int HoldTargetId => State == MoveState.Hold ? holdTargetId : 0;

    /// <summary>
    /// ResponseRide mode 3 for the own pc (scenestategame.cpp:12073-12087): vfunc_59(0x20) with StateHoldData{owner, the
    /// seat's rideBone}. StateHold::vfunc_4 (@0x1417cbcb0, stateholddata.cpp:161-185): StopJump; m_bRiding = the target
    /// has a ridee (FUN_1417ccd50, which also moves the camera there); SendRequestCube with a held item; the entry
    /// position and direction kept; the hit report and collision flags cleared.
    /// </summary>
    public void HoldOn(int targetId, string node)
    {
        if (DeadFlag) return;                               // PC::vfunc_124 (pc.cpp:1199): only Dead / Revival
        holdTargetId = targetId;
        holdNode = node ?? "";
        Enter(MoveState.Hold);
        StopJump();
        Velocity = Vector3.Zero;
        var t = HoldLookup?.Invoke(targetId, holdNode) ?? default;
        holdCameraOnRidee = t.Kind == HoldKind.Pc && t.HasRidee;
        HeldStateEntered?.Invoke(Ms2.Ui.HeldItemState.Hold);
        holdEntryYaw = yawDeg;
        ClearHitReport();
    }

    /// <summary>ResponseRide modes 4 / 5 for the hold's owner (scenestategame.cpp:12099-12112): the camera is back on the
    /// own pc, so leaving the hold sends no mode 4.</summary>
    public void HoldCameraBack(int ownerId)
    {
        if (HoldTargetId == ownerId) holdCameraOnRidee = false;
    }

    /// <summary>
    /// StateHold::vfunc_5 (@0x1417cbe70, stateholddata.cpp:188-201): StopJump, the entry direction back (vfunc_21(m_kDir)),
    /// FUN_141676060(pos) (the fall start when none is kept), and SendRequestRideMode4 while the camera is not on the own
    /// pc.
    /// </summary>
    void HoldExit()
    {
        StopJump();
        yawDeg = targetYawDeg = holdEntryYaw;
        if (float.IsNaN(fallStartY)) fallStartY = GlobalPosition.Y;
        if (holdCameraOnRidee) { holdCameraOnRidee = false; HoldLeft?.Invoke(); }
    }

    /// <summary>
    /// StateHold::vfunc_8 (@0x1417cc440, stateholddata.cpp:251-274), then vfunc_3 (@0x1417cbfa0, :204-248). The input: no
    /// velocity; the jump key (0x493e1) pressed leaves for StateJump sub 0 with the move direction, unless the held PC has
    /// a ridee (MyPC::FUN_141527960). The placement: a lost node or target, or a dead target, falls back to StateJump sub
    /// 0x22 (m_nFallbackState 6, m_nFallbackSub); a PC target puts the pc on its ridee's seat (Ridee::FUN_1417083a0) and
    /// stays put without one; a ridee target leaves it; any other actor puts it on the node (its rotation only when the
    /// Euler angles are unique).
    /// </summary>
    void HoldUpdate(Vector3 dir, bool hasInput, bool jumpPressed)
    {
        kVel = Vector3.Zero;
        var t = HoldLookup?.Invoke(holdTargetId, holdNode) ?? default;
        if (jumpPressed && !(t.Kind == HoldKind.Pc && t.HasRidee))
        {
            StartJump(dir, hasInput, 0);
            return;
        }
        if (!t.HasNode || t.Kind == HoldKind.None || t.Dead)
        {
            StartJump(Vector3.Zero, false, 0x22);
            return;
        }
        if (t.Kind == HoldKind.Pc)
        {
            if (!t.HasSeat) return;
            if (!RemotePcStates.EulerZ(t.SeatRow0, out float z)) z = 0f;     // the non-unique branch sets z to 0
            HoldPlace(t.SeatPosition, z * -57.2957764f);
            return;
        }
        if (t.Kind == HoldKind.Ridee) return;
        HoldPlace(t.NodePosition, RemotePcStates.EulerZ(t.NodeRow0, out float zn) ? zn * -57.2957764f : yawDeg);
    }

    /// <summary>vfunc_19 (the feet, MS2 axes) and vfunc_21 (the Z rotation, degrees).</summary>
    void HoldPlace(System.Numerics.Vector3 feet, float rotZ)
    {
        GlobalPosition = new Vector3(feet.X, feet.Z, -feet.Y);
        yawDeg = targetYawDeg = rotZ;
    }

    /// <summary>StateHold::vfunc_9 (@0x1417cc5a0, stateholddata.cpp:277-374): the held npc 0xa7e02a plays "Ride_Idle_U",
    /// the npcs 0xadf341 / 0xadf342 "Idle_A", a PC target its mount's passenger sequence at the ridee's speed, any other
    /// actor HOLD_IDLE (0x3e).</summary>
    void HoldAnimation()
    {
        var t = HoldLookup?.Invoke(holdTargetId, holdNode) ?? default;
        if (t.Kind == HoldKind.None) return;
        if (t.Kind == HoldKind.Npc)
        {
            if (t.NpcId == 0xa7e02a) { HoldSequence("Ride_Idle_U", 1f); return; }
            if (t.NpcId == 0xadf341 || t.NpcId == 0xadf342) { HoldSequence("Idle_A", 1f); return; }
        }
        else if (t.Kind == HoldKind.Pc)
        {
            if (t.HasSeat) HoldSequence(StateRideAnim.ForPassenger(t.RideeState, t.RideeSub, t.RideeSpIdle, t.Seat), t.RideeAnimSpeed);
            return;
        }
        PlayAct(Act.HoldIdle);
    }

    void HoldSequence(string name, float speed)
    {
        var key = Anims?.Sequence(name, true);
        if (key != null && key != CurrentAnim) PlaySequence(key, speed, false);
    }
}
