using Godot;
using Ms2Godot.Movement;
using Act = Ms2Godot.Movement.ActionAnims.Act;

namespace Ms2Godot;

/// <summary>
/// StateClimb (state 0x1d): climbing a wall of cubes. The client enters it through skill 20000011
/// (PCBase::FUN_1416788d0), cast when a jump or a fall runs into a cube wall while descending in a field whose
/// fielddata checkClimb is not 0.
/// </summary>
public partial class Player
{
    // StateClimb m_n10
    enum ClimbSub
    {
        Hang = 0x56, Up = 0x57, UpLeft = 0x58, UpRight = 0x59, Down = 0x5a, DownLeft = 0x5b, DownRight = 0x5c,
        Left = 0x5d, Right = 0x5e, OverTop = 0x5f, TakeUp = 0x60, TakeDown = 0x61, DownLand = 0x62,
    }
    ClimbSub csub;
    bool climbAltUp, climbAltSide, climbUpLanded;
    float climbInputT, climbHangT;
    Vector2 climbInput;

    /// <summary>PCBase::FUN_141678820 and PC::FUN_14165b500(1): a cube wall hit in the air, then the snap onto it.</summary>
    bool TryClimb()
    {
        if (!CheckClimb || !wallHit || Mount != null || !SnapToWall()) return false;
        Enter(MoveState.Climb);
        ClimbEnter(ClimbSub.TakeUp);
        return true;
    }

    /// <summary>
    /// PC::FUN_14165b500(1): a ray 90 above the feet along +Y then +X (MS2 axes; +X first when the yaw is under 135)
    /// up to 58; its normal's reverse is cast again, and a cube there puts the feet 28 off the wall, facing it.
    /// </summary>
    bool SnapToWall()
    {
        if (Cubes == null) return false;
        var dirs = new[] { FieldBuilder.ToGodotPoint(new Ms2.Formats.Model.Vec3(0, 1, 0)), Vector3.Right };
        if (Mathf.PosMod(yawDeg, 360f) < 135f) (dirs[0], dirs[1]) = (dirs[1], dirs[0]);
        var origin = GlobalPosition + new Vector3(0, MoveConfig.ClimbHigh, 0);
        foreach (var d in dirs)
        {
            if (Ray(origin, d, MoveConfig.ClimbSnapReach, ClimbGroups) is not var (_, n1)) continue;
            if (Ray(origin, -n1, MoveConfig.ClimbSnapReach, ClimbGroups) is not var (p2, n2)) continue;
            if (Cubes.CubeAt(p2 - n2) == null) continue;
            var target = p2 + n2 * MoveConfig.ClimbSnapGap - new Vector3(0, MoveConfig.ClimbHigh, 0);
            // PC::FUN_14165c090: a wall ahead at +54 along the current facing, and room for the body
            if (Ray(target + new Vector3(0, MoveConfig.ClimbLow, 0), Forward, MoveConfig.ClimbRay, ClimbGroups) == null || Overlaps(target)) continue;
            GlobalPosition = target;
            FaceNow(-n2);
            return true;
        }
        return false;
    }

    void FaceNow(Vector3 dir)
    {
        dir.Y = 0;
        if (dir.LengthSquared() < 1e-6f) return;
        yawDeg = targetYawDeg = Mathf.RadToDeg(Mathf.Atan2(dir.X, dir.Z));
    }

    void ClimbEnter(ClimbSub sub)
    {
        // StateClimb::vfunc_4
        jumping = false;
        kVel = Vector3.Zero;
        Velocity = Vector3.Zero;
        csub = sub;
        climbAltUp = climbAltSide = false;
        climbInputT = 0;
        climbHangT = 0;
        climbUpLanded = false;
    }

    /// <summary>
    /// StateClimb::FUN_1417bc010: the PhysX collision groups the climb rays test, 0x201 = groups 0 and 9 (cubes and
    /// props). Group 8 (water) is not among them, so a fluid cube is no climbing wall.
    /// </summary>
    const uint ClimbGroups = 0x201;

    /// <summary>StateClimb::FUN_1417bda60's top-of-wall rays set m_groups = 1: group 0 only, so a cube (group 9) or water
    /// (group 8) above the hands is no more wall there.</summary>
    const uint TopGroups = 1;

    /// <summary>CPhysXHelper::GetGroundDistance casts on 0x389: groups 0, 3, 7, 8 and 9 (water included).</summary>
    const uint GroundGroups = 0x389;

    /// <summary>The nearest hit of a ray on a collider whose PropCollisionGroup bit is in <paramref name="groups"/>
    /// (a collider without one is group 0); the client's CastRay passes the groups to raycastAllShapes.</summary>
    readonly RayQuery rayQ = RayQuery.CastRay();

    (Vector3 pos, Vector3 normal)? Ray(Vector3 from, Vector3 dir, float len, uint groups)
    {
        // the Exclude getter hands back a copy: grow our own array and set it again after each skipped body
        var q = rayQ.Set(from, from + dir.Normalized() * len, GetRid());
        var exclude = rayQ.Ex;
        var space = GetWorld3D().DirectSpaceState;
        for (int i = 0; i < 16; i++)
        {
            using var hit = space.IntersectRay(q);
            if (hit.Count == 0) return null;
            using var colV = hit[PhysicsKeys.Collider];   // an object Variant carries a finalizable disposer
            var col = colV.AsGodotObject() as Node;
            int group = col != null && col.HasMeta(FieldBuilder.GroupMeta) ? (int)col.GetMeta(FieldBuilder.GroupMeta) : 0;
            if ((groups & (1u << group)) != 0) return ((Vector3)hit[PhysicsKeys.Position], (Vector3)hit[PhysicsKeys.Normal]);
            exclude.Add((Rid)hit[PhysicsKeys.Rid]);
            q.Exclude = exclude;
        }
        return null;
    }

    bool IsCubeRay(Vector3 from, Vector3 dir, float len) =>
        Ray(from, dir, len, ClimbGroups) is var (p, n) && Cubes.CubeAt(p - n) != null;

    // the facing as the client compares it: exactly +-1 on an axis after the snap (MS2 axes)
    bool FacingNegYOrNegX()
    {
        var f = CubeGrid.Ms2(Forward);
        return f.Y < -0.999f || f.X < -0.999f;
    }

    Vector3 Right3 => new Vector3(-Forward.Z, 0, Forward.X) * -1f;   // facing x up (MS2 UnitCross with +Z)

    void ClimbUpdate(Vector2 rawInput, bool jumpPressed, float dt)
    {
        // StateClimb::vfunc_8: no input for the first 100 ms; facing -Y or -X ignores the down key
        climbInputT += dt;
        var input = climbInputT >= MoveConfig.ClimbInputDelay ? rawInput : Vector2.Zero;
        if (FacingNegYOrNegX() && input.Y < 0) input.Y = 0;
        climbInput = input;
        if (jumpPressed)
        {
            var d = WorldDir(rawInput);
            if (rawInput.X == 0 && rawInput.Y > 0) d = Vector3.Zero;
            StartJump(d, d != Vector3.Zero, 0x25);
            return;
        }

        // vfunc_3: UpdatePhaseFromInput, MoveByPhase, FUN_1417bda60
        UpdateClimbPhase(input);
        ClimbMoveByPhase(dt);
        if (State != MoveState.Climb) return;
        ClimbCheck();
        if (State != MoveState.Climb) return;
        ClimbSequenceEvents();
    }

    void UpdateClimbPhase(Vector2 input)
    {
        if (csub is ClimbSub.TakeUp or ClimbSub.OverTop or ClimbSub.TakeDown or ClimbSub.DownLand) return;
        if (FacingNegYOrNegX()) input.X = -input.X;
        if (input == Vector2.Zero) csub = ClimbSub.Hang;
        else if (input.X == 0) csub = input.Y > 0 ? ClimbSub.Up : ClimbSub.Down;
        else if (input.Y == 0) csub = input.X > 0 ? ClimbSub.Left : ClimbSub.Right;
        else if (input.Y > 0) csub = input.X > 0 ? ClimbSub.UpLeft : ClimbSub.UpRight;
        else csub = input.X > 0 ? ClimbSub.DownLeft : ClimbSub.DownRight;
    }

    void ClimbMoveByPhase(float dt)
    {
        float v = MoveConfig.ClimbVelocityV * 100f, h = MoveConfig.ClimbVelocityH * 100f;
        switch (csub)
        {
            case ClimbSub.Up: ClimbUp(v, dt); break;
            case ClimbSub.Down: ClimbMove(Vector3.Down * v * dt); break;
            case ClimbSub.Left: ClimbSideways(true, h, dt); break;
            case ClimbSub.Right: ClimbSideways(false, h, dt); break;
            case ClimbSub.UpLeft: ClimbUp(v, dt); ClimbSideways(true, h, dt); break;
            case ClimbSub.UpRight: ClimbUp(v, dt); ClimbSideways(false, h, dt); break;
            case ClimbSub.DownLeft: ClimbMove(Vector3.Down * v * dt); ClimbSideways(true, h, dt); break;
            case ClimbSub.DownRight: ClimbMove(Vector3.Down * v * dt); ClimbSideways(false, h, dt); break;
            case ClimbSub.OverTop: ClimbMove((Vector3.Up * MoveConfig.ClimbOverUp + Forward * MoveConfig.ClimbOverFwd) * dt); break;
            case ClimbSub.TakeDown: ClimbMove(Vector3.Down * MoveConfig.ClimbOverUp * dt); break;
            case ClimbSub.Hang:
                climbHangT += dt;
                if (climbHangT >= MoveConfig.ClimbHangCheck)
                {
                    climbHangT = 0;
                    if (!IsWallInFront()) Enter(MoveState.Idle);
                }
                break;
        }
    }

    void ClimbUp(float v, float dt)
    {
        if (IsWallInFront()) ClimbMove(Vector3.Up * v * dt);
    }

    /// <summary>MoveLeft / MoveRight: only along a cube found 30 to that side.</summary>
    void ClimbSideways(bool left, float h, float dt)
    {
        var side = left ? -Right3 : Right3;
        var from = GlobalPosition + side * MoveConfig.ClimbSide + new Vector3(0, MoveConfig.ClimbLow, 0);
        if (!IsCubeRay(from, Forward, MoveConfig.ClimbRay)) return;
        ClimbMove(side * h * dt);
    }

    /// <summary>vfunc_68 with no gravity (the last argument 1): the controller's move, sliding along what it hits.</summary>
    void ClimbMove(Vector3 delta)
    {
        if (delta != Vector3.Zero) MoveStep(delta);
    }

    bool IsWallInFront() => Ray(GlobalPosition + new Vector3(0, MoveConfig.ClimbHigh, 0), Forward, MoveConfig.ClimbRay, ClimbGroups) != null;

    /// <summary>StateClimb::FUN_1417bda60: the bottom, the top of the wall, or still a wall ahead.</summary>
    void ClimbCheck()
    {
        var feet = GlobalPosition;
        switch (csub)
        {
            case ClimbSub.Down or ClimbSub.DownLeft or ClimbSub.DownRight:
                // GetGroundDistance(feet, 5): ground within 5 under the feet
                if (Ray(feet, Vector3.Down, 5f, GroundGroups) != null) { csub = ClimbSub.DownLand; return; }
                break;
            case ClimbSub.Up or ClimbSub.UpLeft or ClimbSub.UpRight:
                if (HasClimbableWallAhead())
                {
                    // rays at +99, and 5 to either side along MS2 x, find more group-0 wall: keep climbing
                    var o = GlobalPosition + new Vector3(0, MoveConfig.ClimbTopRay, 0);
                    if (Ray(o, Forward, MoveConfig.ClimbRay, TopGroups) != null || Ray(o + Vector3.Right * 5f, Forward, MoveConfig.ClimbRay, TopGroups) != null
                        || Ray(o + Vector3.Left * 5f, Forward, MoveConfig.ClimbRay, TopGroups) != null) return;
                    csub = ClimbSub.OverTop;
                    return;
                }
                break;
            case ClimbSub.Hang or ClimbSub.OverTop or ClimbSub.TakeUp or ClimbSub.TakeDown:
                return;
        }
        // FUN_1417bd310: a wall at +54 keeps the climb (facing its normal); none drops into StateFall
        if (Ray(feet + new Vector3(0, MoveConfig.ClimbLow, 0), Forward, MoveConfig.ClimbRay, ClimbGroups) is var (_, n)) FaceNow(-n);
        else Enter(MoveState.Fall);
    }

    /// <summary>HasClimbableWallAhead: a cube with no cube on it, whose top the hands (feet + 90) reach.</summary>
    bool HasClimbableWallAhead()
    {
        if (Ray(GlobalPosition + new Vector3(0, MoveConfig.ClimbLow, 0), Forward, MoveConfig.ClimbRay, ClimbGroups) is not var (p, n)) return false;
        if (Cubes.CubeAt(p - n) is not { } wall) return false;
        if (Cubes.Neighbour(wall, 0, 0, 1) != null) return false;
        var top = Cubes.Position(wall);
        if (GlobalPosition.Y + MoveConfig.ClimbHigh < top.Z + MoveConfig.CubeSize) return false;
        // FUN_1417bd420: no cube up and to the side within 100 of the hands
        var f = CubeGrid.Ms2(Forward);
        var offs = f.X > 0.999f ? new[] { (0, 1), (0, -1) } : new[] { (-1, 0), (1, 0) };
        var my = CubeGrid.Ms2(GlobalPosition);
        foreach (var (dx, dy) in offs)
        {
            if (Cubes.Neighbour(wall, dx, dy, 1) is not { } edge) continue;
            var e = Cubes.Position(edge);
            if (dy != 0) e.X = my.X; else e.Y = my.Y;
            if (new Vector2(e.X - my.X, e.Y - my.Y).Length() < 100f) return false;
        }
        return true;
    }

    /// <summary>TextKeyEvent ("upLanding") and EndOfSequence of the climb sequences.</summary>
    void ClimbSequenceEvents()
    {
        if (csub == ClimbSub.OverTop && !climbUpLanded && CurrentAnim == Anims?.Name(Act.ClimbUpLand))
        {
            float k = Anims.TextKey(CurrentAnim, "upLanding");
            if (k >= 0 && Anim.CurrentAnimationPosition >= k) { climbUpLanded = true; ClimbMove(Forward * 2f); }
        }
        if (Anim.IsPlaying() || Anims == null) return;
        string cur = CurrentAnim;
        if (cur == Anims.Name(Act.ClimbLUp) || cur == Anims.Name(Act.ClimbRUp))
        {
            climbAltUp = !climbAltUp;
            climbRestart = climbInput.Y != 0;
        }
        else if (cur == Anims.Name(Act.ClimbLDown) || cur == Anims.Name(Act.ClimbRDown))
        {
            climbAltSide = !climbAltSide;
            climbRestart = climbInput.Y != 0;
        }
        else if (cur == Anims.Name(Act.ClimbLCenter) || cur == Anims.Name(Act.ClimbRCenter)) climbRestart = climbInput.X != 0;
        else if (cur == Anims.Name(Act.ClimbUpLand) && csub == ClimbSub.OverTop) ClimbOverTop();
        else if (cur == Anims.Name(Act.ClimbUpTake) && csub == ClimbSub.TakeUp) csub = ClimbSub.Hang;
        else if (cur == Anims.Name(Act.ClimbDownTake) && csub == ClimbSub.TakeDown) csub = ClimbSub.Hang;
        else if (cur == Anims.Name(Act.ClimbDownLand) && csub == ClimbSub.DownLand) Enter(MoveState.Idle);
    }
    bool climbRestart;

    /// <summary>ClimbOverTop: onto the ground GetGroundDistance finds from 1 + 2 above the hands and 52 ahead (150 down at
    /// most). Its forward ray record (0x201, 77) only gives that point; it is never cast.</summary>
    void ClimbOverTop()
    {
        var pos = GlobalPosition;
        var origin = pos + new Vector3(0, MoveConfig.ClimbHigh + 1f + 2f, 0);
        var probe = origin + Forward * 52f;
        float dist = 0;
        if (Ray(probe, Vector3.Down, 150f, GroundGroups) is var (hp, _)) dist = probe.Y - hp.Y - 2f;
        GlobalPosition = new Vector3(pos.X, probe.Y - dist, pos.Z);
        Enter(MoveState.Idle);
    }

    void ClimbAnimation()
    {
        // StateClimb::vfunc_9
        Act a;
        switch (csub)
        {
            case ClimbSub.Hang: a = Act.ClimbIdle; climbAltSide = climbAltUp = false; break;
            case ClimbSub.Up or ClimbSub.UpLeft or ClimbSub.UpRight: a = climbAltUp ? Act.ClimbLUp : Act.ClimbRUp; break;
            case ClimbSub.Down or ClimbSub.DownLeft or ClimbSub.DownRight: a = climbAltSide ? Act.ClimbLDown : Act.ClimbRDown; break;
            case ClimbSub.Left: a = Act.ClimbLCenter; break;
            case ClimbSub.Right: a = Act.ClimbRCenter; break;
            case ClimbSub.OverTop: a = Act.ClimbUpLand; break;
            case ClimbSub.TakeUp: a = Act.ClimbUpTake; break;
            case ClimbSub.TakeDown: a = Act.ClimbDownTake; break;
            default: a = Act.ClimbDownLand; break;
        }
        bool restart = climbRestart && Anims?.Name(a) == CurrentAnim;
        climbRestart = false;
        PlayAct(a, restart: restart);
    }
}
