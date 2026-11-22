using Godot;
using Ms2Godot.Movement;
using Act = Ms2Godot.Movement.ActionAnims.Act;

namespace Ms2Godot;

/// <summary>StateRope (state 9): hanging on a rope segment, climbing it and turning around it.</summary>
public partial class Player
{
    // StateRope m_n10: 0x33 idle, 0x34 up, 0x35 down, 0x36 cycle ended, 0x37 turn, 0x38 take hold, 0x39 let go
    enum RopeSub { Idle = 0x33, Up = 0x34, Down = 0x35, Ended = 0x36, Turn = 0x37, Take = 0x38, Drop = 0x39 }
    RopeSub rsub;
    Climbables.Climb rope;
    Vector3 ropeBase;                     // m_pos54, Godot space
    Vector3 ropeFrom;                     // m_pos48
    float ropeAngle;                      // m_fAngle (radians, the client's negated yaw)
    float ropeTake, ropeTakeDur, ropeKeyT, ropeTurnT = -1;
    float ropeStepT = -1, ropeStepDur, ropeStepPrev;   // m_timerA0
    bool ropeBegan, ropeRestart, ropeEndDone;   // ropeEndDone: this playback's EndOfSequence has run

    /// <summary>MyPC::TryUseChairOrBed for the rope (side 2 never takes one).</summary>
    bool TryRope(int side)
    {
        if (side == 2 || Mount != null) return false;
        var r = Climbs.FindRope(GlobalPosition, side);
        if (r == null) return false;
        if (side == 1) Input2 = Vector2.Zero;
        Enter(MoveState.Rope);
        RopeEnter(r);
        return true;
    }

    void RopeEnter(Climbables.Climb r)
    {
        // StateRope::vfunc_4
        jumping = false;
        kVel = Vector3.Zero;
        Velocity = Vector3.Zero;
        rope = r;
        rsub = RopeSub.Take;
        ropeTake = 0;
        ropeTakeDur = MoveConfig.RopeTakeTime;
        ropeKeyT = 0;
        ropeTurnT = -1;
        ropeStepT = -1;
        ropeRestart = false;
        ropeFrom = GlobalPosition;
        ropeBase = r.Position;
        // the rope in front of the facing plane: keep the facing; behind: turn to it
        var toRope = ropeBase - GlobalPosition;
        toRope.Y = 0;
        if (Forward.Dot(toRope) >= 0) ropeAngle = -Mathf.DegToRad(yawDeg);
        else
        {
            float yaw = Mathf.RadToDeg(Mathf.Atan2(toRope.X, toRope.Z));
            ropeAngle = -Mathf.DegToRad(yaw);
            yawDeg = targetYawDeg = yaw;
        }
    }

    /// <summary>The hold point: 20 from the rope axis at the angle (x = sin, y = cos in MS2 axes), the given height.</summary>
    Vector3 RopePoint(float y)
    {
        var o = FieldBuilder.ToGodotPoint(new Ms2.Formats.Model.Vec3(MoveConfig.RopeRadius * Math.Sin(ropeAngle), MoveConfig.RopeRadius * Math.Cos(ropeAngle), 0));
        return new Vector3(ropeBase.X + o.X, y, ropeBase.Z + o.Z);
    }

    void RopeUpdate(Vector2 rawInput, bool jumpPressed, float dt)
    {
        // StateRope::vfunc_8: up / down after 100 ms; left / right once held 500 ms since the press
        var input = Vector2.Zero;
        ropeKeyT += dt;
        if (ropeKeyT >= MoveConfig.RopeKeyDelay) input.Y = rawInput.Y > 0 ? 1 : rawInput.Y < 0 ? -1 : 0;
        bool sidePressed = rawInput.X != 0 && prevRawInput.X != rawInput.X;
        if (sidePressed) ropeTurnT = 0;
        if (ropeTurnT >= 0) ropeTurnT += dt;
        if (ropeTurnT >= MoveConfig.RopeTurnDelay) input.X = rawInput.X < 0 ? -1 : rawInput.X > 0 ? 1 : 0;
        if (jumpPressed)
        {
            var d = WorldDir(rawInput);
            if (rawInput.X == 0 && rawInput.Y > 0) d = Vector3.Zero;
            StartJump(d, d != Vector3.Zero, 0);
            return;
        }

        // StateRope::vfunc_3
        if (rsub == RopeSub.Take)
        {
            ropeTake += dt;
            float r = Mathf.Min(ropeTake / ropeTakeDur, 1f);
            var top = RopePoint(ropeBase.Y + MoveConfig.RopeGrip);
            var target = ropeFrom + (top - ropeFrom) * r;
            if (!Overlaps(target)) GlobalPosition = target;
            yawDeg = targetYawDeg = -Mathf.RadToDeg(ropeAngle);
            if (r >= 1f) rsub = RopeSub.Idle;
        }
        else
        {
            if (input.Y > 0) { if (rsub is RopeSub.Ended or RopeSub.Idle) rsub = RopeSub.Up; }
            else if (input.Y < 0) rsub = RopeSub.Down;
            if (input.X != 0)
            {
                ropeAngle += (input.X < 0 ? 1 : -1) * MoveConfig.RopeTurnRate * dt;
                if (input.Y == 0) { rsub = RopeSub.Turn; ropeStepT = -1; }
            }
            if (rsub == RopeSub.Ended && rawInput == Vector2.Zero && ropeStepT < 0) rsub = RopeSub.Idle;
        }
        if (State != MoveState.Rope) return;
        RopeMove(RopeStep(dt), input.X);
        if (State == MoveState.Rope) RopeSequenceEvents();
    }

    /// <summary>
    /// StateRope::FUN_1417e1e60: this frame's climb step; the next segment or the end of the rope. With the rope's Climbable
    /// flag (0x4df0c0de) off, a ground hit below the feet lets go (sub 0x39, statemanager.cpp:1438-1446): a ground ray cast
    /// there and then (CPhysXHelper::GetGroundDistance, length 0 = unlimited), not the cached PCBase::vfunc_93 value, which
    /// stays 0 from the ground the pc left.
    /// </summary>
    float RopeStep(float dt)
    {
        var pos = GlobalPosition;
        float step = 0;
        if (!rope.Usable)
        {
            if (Ray(pos, Vector3.Down, MoveConfig.UnlimitedRay, GroundGroups) != null) rsub = RopeSub.Drop;
            return 0;
        }
        if (rsub is RopeSub.Up or RopeSub.Down && ropeStepT >= 0)
        {
            ropeStepT += dt;
            float r = Mathf.Min(ropeStepT / ropeStepDur, 1f);
            step = MoveConfig.RopeStep * (r - ropeStepPrev);
            ropeStepPrev = r;
            if (r >= 1f) ropeStepT = -1;
        }
        if (rsub == RopeSub.Up)
        {
            var next = Climbs.FindRope(pos, 1, MoveConfig.RopeNextUp);
            if (next == null)
            {
                if (pos.Y + step > ropeBase.Y + MoveConfig.RopeGrip) { rsub = RopeSub.Idle; ropeStepT = -1; }
            }
            else SetRope(next);
        }
        else if (rsub == RopeSub.Down)
        {
            var next = Climbs.FindRope(pos, 2, MoveConfig.RopeNextDown);
            if (next == null)
            {
                if (pos.Y - step < ropeBase.Y) { Enter(MoveState.Fall); return 0; }
            }
            else SetRope(next);
        }
        return step;
    }

    void SetRope(Climbables.Climb r)
    {
        if (r == null || r == rope) return;
        rope = r;
        ropeBase = r.Position;
    }

    /// <summary>StateRope::MoveAlongRope.</summary>
    void RopeMove(float dz, float turn)
    {
        var target = GlobalPosition;
        if (rsub == RopeSub.Up) target.Y += dz;
        else if (rsub == RopeSub.Down) target.Y -= dz;
        else if (rsub == RopeSub.Drop)
        {
            target.Y -= dz;
            Enter(MoveState.Fall);
        }
        if (turn != 0)
        {
            var p = RopePoint(target.Y);
            target.X = p.X;
            target.Z = p.Z;
            yawDeg = targetYawDeg = -Mathf.RadToDeg(ropeAngle);
        }
        if (target != GlobalPosition && !Overlaps(target)) GlobalPosition = target;
    }

    /// <summary>TextKeyEvent ("begin" of a climb cycle starts the 400 ms step) and EndOfSequence.</summary>
    void RopeSequenceEvents()
    {
        bool climbSeq = CurrentAnim == Anims?.Name(Act.PoleUp) || CurrentAnim == Anims?.Name(Act.PoleDown);
        if (climbSeq && !ropeBegan)
        {
            float begin = Anims.TextKey(CurrentAnim, "begin");
            if (begin < 0 || Anim.CurrentAnimationPosition >= begin)
            {
                ropeBegan = true;
                ropeStepT = 0;
                ropeStepPrev = 0;
                ropeStepDur = MoveConfig.RopeStepTime;
            }
        }
        if (Anim.IsPlaying() || ropeEndDone) return;
        ropeEndDone = true;
        if (climbSeq) { rsub = RopeSub.Ended; ropeRestart = true; }
        else if (CurrentAnim == Anims?.Name(Act.PoleTurn) || CurrentAnim == Anims?.Name(Act.PoleTake)) rsub = RopeSub.Idle;
    }

    void RopeAnimation()
    {
        // StateRope::vfunc_9
        Act? a = rsub switch
        {
            RopeSub.Idle => Act.PoleIdle,
            RopeSub.Up => Act.PoleUp,
            RopeSub.Down => Act.PoleDown,
            RopeSub.Turn => Act.PoleTurn,
            RopeSub.Take => Act.PoleTake,
            _ => null,
        };
        if (a == null) return;
        bool restart = ropeRestart && a is Act.PoleUp or Act.PoleDown;
        if (restart || Anims?.Name(a.Value) != CurrentAnim)
        {
            ropeRestart = false;
            ropeEndDone = false;
            if (a is Act.PoleUp or Act.PoleDown) ropeBegan = false;
        }
        PlayAct(a.Value, restart: restart);
    }
}
