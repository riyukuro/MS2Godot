using Godot;
using Ms2Godot.Movement;
using Act = Ms2Godot.Movement.ActionAnims.Act;

namespace Ms2Godot;

/// <summary>
/// StateDash (state 0x14): a 20 m/s rush along the facing that falls with gravity and ends in a skid. No key
/// starts it in the client: the server's state sync or a skill puts the actor in it, so <see cref="StartDash"/> is
/// the entry point here.
/// </summary>
public partial class Player
{
    // StateDash m_n10: 0x1d rushing, 0x1e past the dash distance, 0x1f landing skid
    enum DashSub { Rush = 0x1d, Far = 0x1e, Skid = 0x1f }
    DashSub dsub;
    Vector3 dashFrom;
    float dashStopT, dashStopDur;
    bool dashSkidding;

    /// <summary>StateDash::vfunc_4: the dash speed along the facing; any jump stops.</summary>
    public void StartDash()
    {
        if (Mount != null || State is MoveState.Ladder or MoveState.Rope or MoveState.Climb) return;
        jumping = false;
        Enter(MoveState.Dash);
        baseSpeed = MoveConfig.DashSpeed;
        dsub = DashSub.Rush;
        dashFrom = GlobalPosition;
        dashSkidding = false;
        kVel = Forward * baseSpeed;
    }

    void DashUpdate(Vector2 input, bool upPressed)
    {
        // StateDash::vfunc_8: in the rush a held up key takes a ladder, a pressed one a rope (kind 0)
        if (dsub == DashSub.Rush && input.Y > 0 && (TryLadder(3) || (upPressed && TryRope(1)))) return;
        if (dsub == DashSub.Skid) kVel = Forward * baseSpeed;
        else kVel = Forward * MoveConfig.DashSpeed;
    }

    /// <summary>StateDash::vfunc_3 after the move: the stop checks and the skid's falling speed.</summary>
    void DashAfterMove(Vector3 before, float dt)
    {
        var moved = GlobalPosition - before;
        moved.Y = 0;
        switch (dsub)
        {
            case DashSub.Rush:
                if (moved.Length() < MoveConfig.DashMinStep) { Enter(MoveState.Idle); return; }
                if (IsOnFloor() && stateTime > dt) { DashSkid(); return; }
                var d = GlobalPosition - dashFrom;
                d.Y = 0;
                if (d.Length() >= MoveConfig.DashDistance) dsub = DashSub.Far;
                break;
            case DashSub.Far:
                if (IsOnFloor()) DashSkid();
                break;
            case DashSub.Skid:
            {
                // the stop timer runs to the sequence's "stop" key; the speed falls as 20 * (1 - ratio)
                if (dashStopDur > 0)
                {
                    dashStopT += dt;
                    float r = Mathf.Min(dashStopT / dashStopDur, 1f);
                    baseSpeed = MoveConfig.DashSpeed * (1f - r);
                }
                else baseSpeed = 0;
                if (!Anim.IsPlaying() || CurrentAnim != Anims?.Name(Act.JumpDashLand)) Enter(MoveState.Idle);
                break;
            }
        }
    }

    void DashSkid()
    {
        dsub = DashSub.Skid;
        dashStopT = 0;
        PlayAct(Act.JumpDashLand, restart: true);
        float stop = Anims?.TextKey(CurrentAnim, "stop") ?? -1;
        dashStopDur = stop > 0 ? stop : 0;
        dashSkidding = true;
    }

    void DashAnimation()
    {
        if (dsub != DashSub.Skid) PlayAct(Act.JumpDashDown);
    }

    /// <summary>StateDash::vfunc_5: back to the run speed.</summary>
    void DashExit()
    {
        baseSpeed = MoveConfig.RunSpeed;
        kVel = Vector3.Zero;
        dashSkidding = false;
    }
}
