using Godot;
using Ms2Godot.Movement;
using Act = Ms2Godot.Movement.ActionAnims.Act;

namespace Ms2Godot;

/// <summary>
/// The local player (CharacterBody3D, Y-up, game units). Movement follows the client's actor states (StateIdle,
/// StateWalk, StateJump, StateFall, StateLand, StateSwim, StateLadder) on top of the PhysX capsule controller
/// (GameObjectController); the numbers are in <see cref="MoveConfig"/>.
/// The body is a flat-bottomed cylinder: Godot has no controller step offset, so a step-up test runs after a
/// blocked move, and a capsule's round bottom slides back off a 30 riser edge.
/// </summary>
public partial class Player : CharacterBody3D
{
    public enum MoveState { Idle, Walk, Jump, Fall, Land, Swim, Ladder, Rope, Climb, Dash }

    public Node3D Visual;                 // the character root (faces +Z)
    public AnimationPlayer Anim;
    public ActionAnims Anims;
    public Climbables Climbs = new();
    public CubeGrid Cubes = new();
    /// <summary>fielddata checkClimb: wall climbing (skill 20000011) is allowed in this field.</summary>
    public bool CheckClimb = true;
    public Vector3 SpawnPoint;
    /// <summary>Below this height the player goes back to the spawn point (a stand-in: the return is the server's).</summary>
    public float FallReturnY = float.NegativeInfinity;
    public float Gravity = MoveConfig.SceneGravity;

    // input: x = right, y = forward (camera relative); set from the keyboard, or by a script when ScriptDriven
    public Vector2 Input2;
    public Vector2 MouseInput;            // click to move (MouseMove), used while there is no other direction
    public bool WalkMode, JumpHeld, ScriptDriven;
    public float CameraYaw;               // radians about +Y; camera forward = (-sin, 0, -cos)
    public string CurrentAnim = "";

    public MoveState State { get; private set; } = MoveState.Idle;
    /// <summary>PCBase::FUN_141675ed0: the entity in the controller's hit report is water (kind 8).</summary>
    public bool OnWater => hitGroup == MoveConfig.WaterGroup;
    public float GroundDistance { get; private set; }
    /// <summary>The last landing's drop (the height the client sends in StateFallDamage; the server applies damage).</summary>
    public float LastFallHeight { get; private set; }
    public event Action<float> FallDamageHeight;

    float stateTime, airTime, baseSpeed = MoveConfig.RunSpeed, yawDeg, targetYawDeg;
    Vector3 kVel;                         // GameObjectController m_kVelocity (horizontal)
    int hitGroup = -1;                    // GameObjectControllerHitReport m_entity: its collision group, -1 = none
    float fallStartY = float.NaN;         // PCBase m_posC10
    Vector2 prevInput, prevRawInput;
    bool wallHit;                         // PCBase m_unk_c68: a side hit in the air while descending
    bool prevJump;
    float diagHold;
    Vector2 diagDir;

    // jump (GameObjectController + StateJump)
    bool jumping;
    float jumpTime, jumpV0, jumpApex, jumpH;
    Vector3 jumpDir;
    bool jumpHadInput, jumpFirstSteer;
    float jumpSavedSpeed = MoveConfig.RunSpeed;
    int jumpSub;                          // 0x21 Jump, 0x22 JumpSpecial, 0x23 Jump2, 0x24 JumpSpecial2
    static int jumpToggle;
    static readonly Random rng = new();

    // ladder (StateLadder)
    enum Ladder { Idle = 0x2a, Up = 0x2b, Down = 0x2c, UpLand = 0x2d, DownLand = 0x2e, TakeUp = 0x2f, TakeDown = 0x30, TakeAir = 0x31 }
    Ladder lsub;
    Climbables.Climb ladder;
    bool upFlip, downFlip, stepRunning, stepBegan;
    float stepT, stepDur, stepPrevRatio;
    Vector3 la, lb, lc;
    float lt, ldur;
    bool lthree;                          // lerp through the middle point

    public static Player Create(Node3D visual, AnimationPlayer anim)
    {
        var p = new Player { Name = "Player", Visual = visual, Anim = anim };
        p.FloorMaxAngle = Mathf.DegToRad(MoveConfig.SlopeLimitDeg);
        p.FloorSnapLength = MoveConfig.StepOffset;
        p.SafeMargin = MoveConfig.SkinWidth;
        p.AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new CylinderShape3D { Radius = MoveConfig.Radius, Height = MoveConfig.Height },
            Position = new Vector3(0, MoveConfig.Height / 2, 0),
        });
        p.AddChild(visual);
        return p;
    }

    public override void _Ready() => MoveProof.Attach(this);

    /// <summary>Back to StateIdle with no motion (after a teleport).</summary>
    public void ResetMotion()
    {
        jumping = false;
        kVel = Vector3.Zero;
        Velocity = Vector3.Zero;
        airTime = 0;
        fallStartY = float.NaN;
        hitGroup = -1;                    // FUN_1416629d0 clears the hit report after placing the PC
        Enter(MoveState.Idle);
    }

    public void Face(Vector3 dir)
    {
        if (dir.LengthSquared() < 1e-6f) return;
        yawDeg = targetYawDeg = Mathf.RadToDeg(Mathf.Atan2(dir.X, dir.Z));
        (Mount?.Visual ?? Visual).Rotation = new Vector3(0, Mathf.DegToRad(yawDeg), 0);
    }

    Vector3 Forward => new(Mathf.Sin(Mathf.DegToRad(yawDeg)), 0, Mathf.Cos(Mathf.DegToRad(yawDeg)));

    /// <summary>PCBase::CalcMoveDirection: input y along the camera's flat forward, x along its side, unit length.</summary>
    Vector3 WorldDir(Vector2 input)
    {
        if (input == Vector2.Zero) return Vector3.Zero;
        var fwd = new Vector3(-Mathf.Sin(CameraYaw), 0, -Mathf.Cos(CameraYaw));
        var right = new Vector3(-fwd.Z, 0, fwd.X);
        return (right * input.X + fwd * input.Y).Normalized();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (!ScriptDriven)
        {
            var k = MoveInput.Read();
            Input2 = k.Move;
            WalkMode = k.Walk;
            JumpHeld = k.Jump;
        }
        var rawInput = Input2 != Vector2.Zero ? Input2 : MouseInput;
        var input = DiagonalHold(rawInput, dt);
        bool jumpPressed = JumpHeld && !prevJump;
        bool upPressed = input.Y > 0 && prevInput.Y <= 0, downPressed = input.Y < 0 && prevInput.Y >= 0;
        prevJump = JumpHeld;
        prevInput = input;
        var dir = WorldDir(input);
        bool hasInput = dir != Vector3.Zero;
        bool grounded = IsOnFloor();
        ProbeGround();
        stateTime += dt;

        switch (State)
        {
            case MoveState.Idle:
                kVel = Vector3.Zero;
                if (input.Y > 0 && upPressed && (TryLadder(1) || TryRope(1))) break;
                if (input.Y < 0 && downPressed && TryLadder(2)) break;
                if (grounded && JumpHeld) { StartJump(dir, hasInput, 0x25); break; }
                if (hasInput) { Enter(OnWater ? MoveState.Swim : MoveState.Walk); break; }
                if (!OnWater && InAirFor(MoveConfig.IdleFallDelay)) Enter(MoveState.Fall);
                break;

            case MoveState.Walk:
                if (OnWater) { Enter(MoveState.Swim); break; }
                if (InAirFor(MoveConfig.WalkFallDelay)) { Enter(MoveState.Fall); break; }
                // StateWalk::vfunc_8 order: pressed up / down (ladder or rope), jump, then the held up / down key on a ladder
                if (input.Y > 0 && upPressed && (TryLadder(1) || TryRope(1))) break;
                if (input.Y < 0 && downPressed && TryLadder(2)) break;
                if (grounded && JumpHeld) { StartJump(dir, hasInput, 0x25); break; }
                if (input.Y > 0 && TryLadder(1)) break;
                if (input.Y < 0 && TryLadder(2)) break;
                if (!hasInput) { Enter(MoveState.Idle); break; }
                baseSpeed = Mount != null ? RideSpeed() : WalkMode ? MoveConfig.WalkSpeed : MoveConfig.RunSpeed;
                kVel = dir * baseSpeed;
                break;

            case MoveState.Swim:
                if (!OnWater && grounded) { Enter(MoveState.Idle); break; }
                if (InAirFor(MoveConfig.IdleFallDelay)) { hitGroup = -1; Enter(MoveState.Idle); break; }   // StateSwim::vfunc_8 clears the report
                if (grounded && JumpHeld) { StartJump(dir, hasInput, 0x25); break; }
                if (!hasInput) { Enter(MoveState.Idle); break; }
                baseSpeed = Mount != null ? RideSpeed() : MoveConfig.SwimSpeed;
                kVel = dir * baseSpeed;
                break;

            case MoveState.Jump:
                // StateJump::vfunc_3: the launch direction, replaced by any held direction; a standing jump steers at 1 m/s
                if (hasInput)
                {
                    if (!jumpHadInput && jumpFirstSteer) { jumpFirstSteer = false; baseSpeed = MoveConfig.AirSteerSpeed; }
                    jumpDir = dir;
                }
                kVel = jumpDir * baseSpeed * MoveConfig.FallMoveSpeed;
                // IsJumpTimeElapsed (50 ms): the up key takes a ladder or rope; else a wall hit starts the climb
                if (input.Y > 0 && upPressed && stateTime > 0.05f && (TryLadder(1) || TryRope(1))) break;
                if (TryClimb()) break;
                break;

            case MoveState.Fall:
                if (hasInput) kVel = dir * baseSpeed * MoveConfig.FallMoveSpeed;
                if (input.Y > 0 && TryLadder(3)) break;
                if (TryClimb()) break;
                if (grounded) Enter(MoveState.Land);
                break;

            case MoveState.Land:
                kVel = Vector3.Zero;
                if (OnWater) { Enter(MoveState.Idle); break; }
                if (hasInput) { Enter(MoveState.Walk); break; }
                if (grounded && JumpHeld) { StartJump(dir, hasInput, 0x25); break; }
                // StateLand::EndOfSequence (or no landing sequence)
                if (stateTime > 0.05f && (!Anim.IsPlaying() || CurrentAnim != Anims?.Name(Act.JumpLand))) Enter(MoveState.Idle);
                break;

            case MoveState.Ladder:
                LadderUpdate(input, jumpPressed, dt);
                break;

            case MoveState.Rope:
                RopeUpdate(rawInput, jumpPressed, dt);
                break;

            case MoveState.Climb:
                ClimbUpdate(rawInput, jumpPressed, dt);
                break;

            case MoveState.Dash:
                DashUpdate(input, upPressed);
                break;
        }
        prevRawInput = rawInput;

        if (State is not (MoveState.Ladder or MoveState.Rope or MoveState.Climb))
        {
            if (hasInput && State != MoveState.Dash) targetYawDeg = Mathf.RadToDeg(Mathf.Atan2(dir.X, dir.Z));
            var before = GlobalPosition;
            Integrate(dt);
            if (State == MoveState.Dash) DashAfterMove(before, dt);
        }
        else wallHit = false;
        TurnTowardTargetYaw(dt);
        UpdateAnimation();

        if (GlobalPosition.Y < FallReturnY)              // fell out of the field: back to the spawn point
        {
            GlobalPosition = SpawnPoint;
            Velocity = Vector3.Zero;
            Enter(MoveState.Idle);
        }
    }

    /// <summary>Unk_PCMove::UpdateMoveDirection: releasing one key of a diagonal keeps the diagonal for 50 ms.</summary>
    Vector2 DiagonalHold(Vector2 input, float dt)
    {
        // armed by the raw key transition (last frame's keys, not this hold's own output), so it runs once for 50 ms
        if (prevRawInput.X != 0 && prevRawInput.Y != 0 && input != prevRawInput && input != Vector2.Zero &&
            (input.X == prevRawInput.X || input.Y == prevRawInput.Y))
        {
            diagHold = MoveConfig.DiagonalHold;
            diagDir = prevRawInput;
        }
        if (diagHold > 0)
        {
            diagHold -= dt;
            if (input.X == diagDir.X || input.Y == diagDir.Y) return diagDir;
            diagHold = 0;
        }
        return input;
    }

    void Enter(MoveState s)
    {
        var old = State;
        if (old == MoveState.Jump) { jumping = false; baseSpeed = jumpSavedSpeed; }   // StateJump::vfunc_5
        if (old == MoveState.Ladder) { ladder = null; hitGroup = -1; }   // StateLadder::vfunc_5 clears the hit report
        if (old == MoveState.Rope) rope = null;
        if (old == MoveState.Dash && s != MoveState.Dash) DashExit();
        State = s;
        stateTime = 0;
        if (s == MoveState.Fall) fallStartY = float.IsNaN(fallStartY) ? GlobalPosition.Y : fallStartY;
        if (s == MoveState.Land || (s == MoveState.Idle && old is MoveState.Fall or MoveState.Jump)) SendFallDamage();
    }

    /// <summary>PCBase::vfunc_99: in the air for longer than the delay and more than 0.3 m above the ground.</summary>
    bool InAirFor(float delay) => airTime > delay && GroundDistance > MoveConfig.FallMinHeight && !IsOnFloor();

    /// <summary>
    /// PCBase::vfunc_93, run each frame by PC::vfunc_53: 0 while the controller is on the ground; else
    /// RayCastFixedDirection from the feet (vfunc_18 = GameObjectController::GetFeetPosition) straight down with length 0,
    /// which CastRay makes unlimited, on the default groups 0x389; FLT_MAX when it hits nothing.
    /// </summary>
    void ProbeGround()
    {
        if (IsOnFloor()) { GroundDistance = 0; return; }
        GroundDistance = Ray(GlobalPosition, Vector3.Down, MoveConfig.UnlimitedRay, GroundGroups) is var (p, _) ? GlobalPosition.Y - p.Y : float.MaxValue;
    }

    /// <summary>
    /// GameObjectControllerHitReport::onShapeHit keeps the entity of the last shape the controller touched; NxCharacter
    /// reports the hits of the side and down passes of a move (not the up pass of the auto-step), so after a move with
    /// a down part it is what the body stands on, else the last thing it ran into. It stays until cleared. Another
    /// controller goes to onControllerHit and leaves it alone.
    /// </summary>
    void UpdateHitReport(Vector3 wantedHoriz, float dy)
    {
        for (int i = 0; i < GetSlideCollisionCount(); i++)
            NoteHit(GetSlideCollision(i).GetCollider());
        if (dy >= 0) return;
        // the down pass: the frame's drop, plus the step offset it undoes when the move had a side part
        float down = -dy + (wantedHoriz.LengthSquared() > 0 ? MoveConfig.StepOffset : 0);
        var col = new KinematicCollision3D();
        if (TestMove(GlobalTransform, Vector3.Down * Mathf.Max(down, 2 * SafeMargin), col)) NoteHit(col.GetCollider());
    }

    void NoteHit(GodotObject collider)
    {
        // onShapeHit (0x140cfade0) sets the entity of a touched shape, or clears it when the shape has no entity; a touched
        // controller goes to onControllerHit (0x140cfaec0), which only keeps the other controller
        if (collider is CharacterBody3D || collider is Node c && c.HasMeta(MoveConfig.ControllerMeta)) return;
        hitGroup = collider is Node n && n.HasMeta("ms2_group") ? (int)n.GetMeta("ms2_group") : 0;
    }

    // ---- GameObjectController: jump and fall integration -------------------------------------------------------------

    void StartJump(Vector3 dir, bool hasInput, int sub)
    {
        // StateJump::vfunc_4: the jump variant
        if (sub == 0x25)
        {
            jumpToggle = (jumpToggle + 1) % 2;
            jumpSub = rng.Next(2) != 0 ? (jumpToggle == 0 ? 0x22 : 0x24) : (jumpToggle == 0 ? 0x21 : 0x23);
        }
        else jumpSub = 0x21;
        jumpDir = dir;
        jumpHadInput = hasInput;
        jumpFirstSteer = true;
        jumpSavedSpeed = baseSpeed;
        fallStartY = GlobalPosition.Y;
        // GameObjectController::StartJump(h): v0 = sqrt(2 * 2h * -g), apex time = v0 / -g
        jumpV0 = Mathf.Sqrt(2f * (2f * MoveConfig.JumpParam) * -Gravity);
        jumpApex = jumpV0 / -Gravity;
        jumpTime = 0;
        jumpH = 0;
        jumping = true;
        Enter(MoveState.Jump);
    }

    void Integrate(float dt)
    {
        var v = new Vector3(kVel.X, 0, kVel.Z);
        if (State is MoveState.Idle or MoveState.Land) v = Vector3.Zero;
        if (jumping)
        {
            jumpTime += dt;
            float h = jumpV0 * jumpTime + 0.5f * Gravity * jumpTime * jumpTime;
            v.Y = (h - jumpH) / dt;
            jumpH = h;
            FloorSnapLength = 0;
        }
        else
        {
            // FUN_1412407f0: dz = g * fallTime * dt + g dt^2 / 2
            v.Y = Gravity * airTime + 0.5f * Gravity * dt;
            FloorSnapLength = MoveConfig.StepOffset;
        }
        Velocity = v;

        var start = GlobalTransform;
        var wantedHoriz = new Vector3(v.X, 0, v.Z) * dt;
        MoveAndSlide();
        // NxCharacter (SweepTest::moveCharacter): every move with a side part goes up by the step offset, sideways, then
        // down, unless it moves upward; so any riser under the step offset is climbed, whatever the approach angle.
        if (v.Y <= 0 && IsOnWall() && wantedHoriz.Length() > 0.01f && StepPath(start, wantedHoriz) is { } stepped &&
            stepped.Origin.Y > GlobalPosition.Y + 1f)
            GlobalTransform = stepped;
        UpdateHitReport(wantedHoriz, v.Y * dt);

        wallHit = IsOnWall() && !IsOnFloor() && v.Y < 0;
        if (IsOnFloor())
        {
            airTime = 0;
            // GameObjectController::Move: a hit below stops the jump (StateJump then goes to StateLand)
            if (jumping && jumpTime > dt) { jumping = false; Enter(OnWater ? MoveState.Idle : MoveState.Land); }
        }
        else airTime += dt;
        if (IsOnCeiling() && jumping && jumpTime < jumpApex)
        {
            // the controller stays under the ceiling until the jump curve comes back down
        }
    }

    void TryStepUp(Vector3 horiz)
    {
        if (StepPath(GlobalTransform, horiz) is { } t && t.Origin.Y > GlobalPosition.Y + 1f) GlobalTransform = t;
    }

    /// <summary>The up / side / down passes of NxCharacter's move from <paramref name="t0"/>: the up pass is cut short by a
    /// ceiling, the side pass slides along what it hits, the down pass must land on a walkable floor (the slope limit). The
    /// client's controller is CLIMB_EASY, so there is no constrainedClimbingMode height test. Null when it does not land.</summary>
    Transform3D? StepPath(Transform3D t0, Vector3 horiz)
    {
        var col = new KinematicCollision3D();
        var up = Vector3.Up * (MoveConfig.StepOffset + 2 * SafeMargin);   // a 30 riser must pass with the margin
        var t = TestMove(t0, up, col) ? t0.Translated(col.GetTravel()) : t0.Translated(up);
        float rise = t.Origin.Y - t0.Origin.Y;
        if (rise < 1f) return null;
        var rem = horiz;
        for (int i = 0; i < 4 && rem.LengthSquared() > 1e-4f; i++)
        {
            if (!TestMove(t, rem, col)) { t = t.Translated(rem); break; }
            t = t.Translated(col.GetTravel());
            var n = col.GetNormal();
            n.Y = 0;
            if (n.LengthSquared() < 1e-6f) break;
            n = n.Normalized();
            rem = col.GetRemainder();
            rem.Y = 0;
            rem -= n * rem.Dot(n);
        }
        if (!TestMove(t, Vector3.Down * rise * 1.5f, col)) return null;
        if (col.GetNormal().Y < Mathf.Cos(FloorMaxAngle)) return null;
        return t.Translated(col.GetTravel());
    }

    /// <summary>PCBase::SendFallDamage: the height from the fall start to the landing; skipped on water.</summary>
    void SendFallDamage()
    {
        if (float.IsNaN(fallStartY)) return;
        float h = fallStartY - GlobalPosition.Y;
        fallStartY = float.NaN;
        if (OnWater || h <= 0) return;
        LastFallHeight = h;
        FallDamageHeight?.Invoke(h);
    }

    void TurnTowardTargetYaw(float dt)
    {
        float diff = Mathf.Wrap(targetYawDeg - yawDeg, -180f, 180f);
        float step = MoveConfig.TurnSpeedDeg * dt;
        yawDeg = Mathf.Abs(diff) <= step ? targetYawDeg : yawDeg + Mathf.Sign(diff) * step;
        (Mount?.Visual ?? Visual).Rotation = new Vector3(0, Mathf.DegToRad(yawDeg), 0);
    }

    // ---- StateLadder ---------------------------------------------------------------------------------------------------

    bool TryLadder(int side)
    {
        if (Mount != null) return false;
        var l = Climbs.Find(GlobalPosition, side);
        if (l == null) return false;
        Enter(MoveState.Ladder);
        jumping = false;
        kVel = Vector3.Zero;
        Velocity = Vector3.Zero;
        ladder = l;
        // face the ladder: rotation z + 180
        var faceDir = Climbables.Direction(l.YawDeg + 180f);
        yawDeg = targetYawDeg = Mathf.RadToDeg(Mathf.Atan2(faceDir.X, faceDir.Z));
        var basePos = l.Position - faceDir * l.Extrude;
        basePos.Y = RungHeight(l);
        var pos = GlobalPosition;
        var mid = new Vector3(basePos.X, pos.Y, basePos.Z);
        upFlip = downFlip = false;
        stepRunning = false;
        if (side == 1) { lsub = Ladder.TakeUp; Lerp(pos, pos, basePos, MoveConfig.LadderTakeUpTime, false); }
        else if (side == 2) { lsub = Ladder.TakeDown; Lerp(pos, mid, basePos, MoveConfig.LadderTakeDownTime, true); }
        else { lsub = Ladder.TakeAir; Lerp(pos, pos, basePos, MoveConfig.LadderTakeAirTime, false); }
        return true;
    }

    /// <summary>StateLadder::FUN_1417d9880: the rung 56 or 131 above the ladder base nearest the hands (feet + 90), less 5.</summary>
    float RungHeight(Climbables.Climb l)
    {
        float above = GlobalPosition.Y + 90f;
        float near = Mathf.Abs(above - l.Position.Y + 56f), far = Mathf.Abs(above - l.Position.Y + 131f);
        return (near < far ? l.Position.Y + 56f : l.Position.Y + 131f) - 5f;
    }

    void Lerp(Vector3 a, Vector3 b, Vector3 c, float dur, bool three)
    {
        la = a; lb = b; lc = c; lt = 0; ldur = dur; lthree = three;
    }

    Vector3 LerpAt(float r)
    {
        if (!lthree) return la + (lc - la) * r;
        return r <= 0.5f ? la + (lb - la) * (r * 2f) : lb + (lc - lb) * ((r - 0.5f) * 2f);
    }

    void LadderUpdate(Vector2 input, bool jumpPressed, float dt)
    {
        if (jumpPressed)
        {
            // StateLadder::vfunc_8: jump off
            var d = WorldDir(input);
            StartJump(d, d != Vector3.Zero, 0x21);
            return;
        }
        var pos = GlobalPosition;
        switch (lsub)
        {
            case Ladder.TakeUp or Ladder.TakeDown or Ladder.TakeAir or Ladder.UpLand:
            {
                lt += dt;
                float r = Mathf.Min(lt / ldur, 1f);
                GlobalPosition = LerpAt(r);
                if (r >= 1f)
                {
                    if (lsub == Ladder.UpLand) { Enter(MoveState.Idle); return; }
                    lsub = Ladder.Idle;
                }
                return;
            }
            case Ladder.DownLand:
                if (!Anim.IsPlaying()) Enter(MoveState.Idle);
                return;
        }

        // EndOfSequence of a climb cycle: the other hand next time; the next cycle starts at once while the key is held
        if (lsub is Ladder.Up or Ladder.Down && !stepRunning && stepBegan && !Anim.IsPlaying())
        {
            if (lsub == Ladder.Up) upFlip = !upFlip; else downFlip = !downFlip;
            lsub = Ladder.Idle;
        }
        // StateLadder::vfunc_3: a climb cycle per up / down key; 75 units over 200 ms from the sequence's "begin" key
        if (lsub == Ladder.Idle)
        {
            if (input.Y > 0) { lsub = Ladder.Up; stepRunning = false; stepBegan = false; }
            else if (input.Y < 0) { lsub = Ladder.Down; stepRunning = false; stepBegan = false; }
            else return;
            PlayAct(lsub == Ladder.Up ? (upFlip ? Act.LadderLUp : Act.LadderRUp) : (downFlip ? Act.LadderLDown : Act.LadderRDown), restart: true);
        }

        float rate = 1f;
        if (!stepBegan)
        {
            float begin = Anims?.TextKey(CurrentAnim, "begin") ?? -1;
            if (begin < 0 || Anim.CurrentAnimationPosition >= begin)
            {
                stepBegan = stepRunning = true;
                stepT = 0;
                stepPrevRatio = 0;
                stepDur = MoveConfig.LadderStepTime / rate;
            }
        }
        float move = 0;
        if (stepRunning)
        {
            stepT += dt;
            float r = Mathf.Min(stepT / stepDur, 1f);
            move = MoveConfig.LadderStep * (r - stepPrevRatio);
            stepPrevRatio = r;
            if (r >= 1f) stepRunning = false;
        }

        if (lsub == Ladder.Up)
        {
            var next = Climbs.Find(pos, 1, ladder);
            if (next == null)
            {
                // the top: ray down from 75 ahead and 500 up
                var top = TopPoint();
                if (top is Vector3 tp)
                {
                    tp.Y += 5f;
                    var p2 = pos + new Vector3(0, move, 0);
                    if ((p2 - tp).Length() <= MoveConfig.LadderTopReach && !Overlaps(tp))
                    {
                        lsub = Ladder.UpLand;
                        Lerp(p2, new Vector3(p2.X, tp.Y, p2.Z), tp, MoveConfig.LadderLandTime, true);
                        PlayAct(Act.LadderUpLand, restart: true);
                        GlobalPosition = p2;
                        return;
                    }
                }
            }
            else ladder = next;
            GlobalPosition = pos + new Vector3(0, move, 0);
        }
        else if (lsub == Ladder.Down)
        {
            var next = Climbs.Find(pos, 2, ladder);
            if (next == null)
            {
                float groundY = pos.Y - GroundDistance;
                if (GroundDistance < float.MaxValue && pos.Y - move <= groundY)
                {
                    lsub = Ladder.DownLand;
                    GlobalPosition = new Vector3(pos.X, groundY + 1f, pos.Z);
                    PlayAct(Act.LadderDownLand, restart: true);
                    return;
                }
                if (pos.Y - move <= ladder.Position.Y) { Enter(MoveState.Fall); return; }
            }
            else ladder = next;
            GlobalPosition = pos - new Vector3(0, move, 0);
        }

    }

    Vector3? TopPoint()
    {
        var from = GlobalPosition + Forward * MoveConfig.LadderTopProbe + new Vector3(0, 500, 0);
        var q = PhysicsRayQueryParameters3D.Create(from, from + new Vector3(0, -1000, 0));
        q.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
        return hit.Count == 0 ? null : (Vector3)hit["position"];
    }

    bool Overlaps(Vector3 feet)
    {
        var t = new Transform3D(Basis.Identity, feet + new Vector3(0, 2, 0));
        return TestMove(t, Vector3.Up * 0.1f);
    }

    // ---- animation -----------------------------------------------------------------------------------------------------

    void UpdateAnimation()
    {
        if (Mount != null) { RideAnimation(); return; }
        if (Anims == null) return;
        switch (State)
        {
            case MoveState.Idle:
                PlayAct(OnWater ? Act.WaterIdle : Act.Idle);
                break;
            case MoveState.Walk:
            {
                // StateWalk::vfunc_9 + PC::vfunc_115: walk or run sequence and its rate by the speed
                float rate = ActionAnims.GaitRate(baseSpeed, MoveConfig.RunSpeed, MoveConfig.WalkSpeed, out bool run);
                PlayAct(run ? Act.Run : Act.Walk, speed: rate);
                break;
            }
            case MoveState.Swim:
                PlayAct(Act.WaterSwim);
                break;
            case MoveState.Jump:
            {
                // StateJump::vfunc_9: up while rising, down after the apex, the fall loop after 12/|g| s + apex
                bool special = jumpSub is 0x22 or 0x24;
                if (airTime >= MoveConfig.JumpFallAnimDelay + Mathf.Round(jumpApex * 1000f) / 1000f) PlayAct(Act.JumpFall);
                else if (jumpTime < jumpApex) PlayAct(special && Anims.Has(Act.SpecialJumpUp) ? Act.SpecialJumpUp : Act.JumpUp);
                else PlayAct(special && Anims.Has(Act.SpecialJumpDown) ? Act.SpecialJumpDown : Act.JumpDown);
                break;
            }
            case MoveState.Fall:
                PlayAct(Act.JumpFall);
                break;
            case MoveState.Land:
                if (stateTime < 0.05f) PlayAct(Act.JumpLand, restart: CurrentAnim != Anims.Name(Act.JumpLand));
                break;
            case MoveState.Ladder:
                switch (lsub)
                {
                    case Ladder.Idle: PlayAct(Act.LadderIdle); break;
                    case Ladder.TakeUp: PlayAct(Act.LadderUpTake); break;
                    case Ladder.TakeDown: PlayAct(Act.LadderDownTake); break;
                }
                break;
            case MoveState.Rope:
                RopeAnimation();
                break;
            case MoveState.Climb:
                ClimbAnimation();
                break;
            case MoveState.Dash:
                DashAnimation();
                break;
        }
    }

    void PlayAct(Act a, float speed = 1f, bool restart = false)
    {
        var name = Anims?.Name(a);
        if (name == null || !Anim.HasAnimation(name)) return;
        if (name != CurrentAnim || restart)
        {
            Anim.Play(name, 0.15, speed);
            if (restart) Anim.Seek(0, true);
            CurrentAnim = name;
        }
    }
}
