using Godot;
using Ms2Godot.Movement;
using Act = Ms2Godot.Movement.ActionAnims.Act;

namespace Ms2Godot;

/// <summary>
/// The local player (CharacterBody3D, Y-up, game units). Movement follows the client's actor states (StateIdle,
/// StateWalk, StateJump, StateFall, StateLand, StateSwim, StateLadder) on top of the PhysX capsule controller
/// (GameObjectController); the numbers are in <see cref="MoveConfig"/>.
/// The body is the client's capsule, moved by the client's own controller sweep (<see cref="NxController"/>), not by
/// MoveAndSlide.
/// </summary>
public partial class Player : CharacterBody3D
{
    /// <summary>The scene-tree group every player joins, so the HUD finds one without walking the tree.</summary>
    public const string Group = "player";
    public Player() { AddToGroup(Group); }

    public enum MoveState { Idle, Walk, Jump, Fall, Land, Swim, Ladder, Rope, Climb, Dash, Dead, Revival }

    public Node3D Visual;                 // the character root (faces +Z)
    /// <summary>The body's gender (0 male, 1 female) and its model root node name, for the player's effect dummies.</summary>
    public int Gender = 1;
    public string RootName;
    public AnimationPlayer Anim;
    public ActionAnims Anims;
    /// <summary>The equipped weapons' sheathed / hand rig (PC::FUN_141659000 / FUN_141657c80).</summary>
    public Net.EquipApplier.WeaponRig Weapons;
    /// <summary>PCBase m_unk_160 (vfunc_65 @0x14166ced0, SetEmotion @0x14166cee0, pcbase.cpp:1264-1285): battle stance flag.</summary>
    public bool InBattle { get; private set; }
    /// <summary>PC m_nD80: whether weapons are currently attached to Weapon_Hand_R/L_Point.</summary>
    public bool WeaponsHeld => Weapons?.WeaponsHeld ?? false;
    /// <summary>PC::vfunc_60 (@0x141658a30, pc.cpp:1737-1749): the weapon animation prefix while weapons are held.</summary>
    public string WeaponPrefix => Weapons?.ActivePrefix ?? "";
    float battleTimer = -1f;              // offline battle stance timeout (UserBattleDurationTick = 5000 ms); < 0 = indefinite
    bool prevAttackKey;
    public const float DefaultBattleStanceSeconds = 5f;

    /// <summary>
    /// PCBase::SetEmotion (@0x14166cee0, pcbase.cpp:1264-1279) + PC::FUN_141659000 (@0x141659000, pc.cpp:2491-2511):
    /// sets the battle stance flag and updates weapon attachment between sheathed and hand points.
    /// </summary>
    public void SetBattleStance(bool inBattle, float durationSeconds = -1f)
    {
        InBattle = inBattle;
        battleTimer = inBattle ? durationSeconds : -1f;
        UpdateWeaponStance();
    }

    /// <summary>
    /// State::vfunc_15 (@0x1417b8150, state.cpp:684): whether the current movement state allows weapons to remain in
    /// the hand nodes during battle stance (StateIdle::vfunc_15 @0x1417cd740, stateidle.cpp:210: not in water idle;
    /// StateWalk::vfunc_15 @0x141800a90, statewalk.cpp:376: running only, slow walk returns 0; StateJump @0x1417d6ac0,
    /// StateFall @0x1417c7aa0, StateLand @0x1417da780: 1; hit, spawn, revival, swim dash, climb, glide, pvp win/lose: 1; swim, ladder, rope, dash, ride: 0). The table is WeaponStance.StateHoldsWeapons, shared
    /// with the remote characters; its STILL-OPEN lists the client states that also answer 1 and are not modelled.
    /// </summary>
    public bool StateAllowsWeapons =>
        Mount == null && Ms2.Ui.Items.WeaponStance.StateHoldsWeapons(StateId(State), State == MoveState.Idle && OnWater ? 7 : SubOf(State), !WalkMode);

    /// <summary>
    /// STILL-OPEN, PC::FUN_141659000 and PlayAnimation steps not ported: the per-PlayAnimation re-apply of the sequence-filtered
    /// dummies (FUN_141659790, pc.cpp:2520-2555); cube mode clearing the held flag (pc.cpp:2491-2511, WeaponStance has the
    /// rule, the game never sets it); State::vfunc_16 forcing weapons on (state.cpp:690); StateIdle sub state 0xa calling
    /// SetEmotion(0) (stateidle.cpp:387).
    /// </summary>
    void UpdateWeaponStance() => Weapons?.SetState(InBattle, StateAllowsWeapons);
    public Climbables Climbs = new();
    public CubeGrid Cubes = new();
    /// <summary>fielddata checkClimb: wall climbing (skill 20000011) is allowed in this field.</summary>
    public bool CheckClimb = true;
    public Vector3 SpawnPoint;
    /// <summary>
    /// WorldService m_boundsMin / m_boundsMax (+0x820 / +0x82c): the box spanned by the field's MS2Bounding entities' positions
    /// (worldservice_1.cpp:10394-10405), in field units and axes (Z up). Before any MS2Bounding it is inverted, min FLT_MAX
    /// and max -FLT_MAX (worldservice_1.cpp:320-321, reset at 10125-10126), so the box test masks every axis.
    /// </summary>
    public Vector3 BoundsMin = new(float.MaxValue, float.MaxValue, float.MaxValue);
    public Vector3 BoundsMax = new(-float.MaxValue, -float.MaxValue, -float.MaxValue);
    public float Gravity = MoveConfig.SceneGravity;

    // input: x = right, y = forward (camera relative); set from the keyboard, or by a script when ScriptDriven
    public Vector2 Input2;
    public Vector2 MouseInput;            // click to move (MouseMove), used while there is no other direction
    public bool WalkMode, JumpHeld, ScriptDriven;
    public Vector3 CameraEuler;           // the camera mode's Euler rotation in degrees (CameraMath.Euler of the placed eye and look), for the live sync record
    public float CameraYaw;               // radians about +Y; camera forward = (-sin, 0, -cos)
    public string CurrentAnim = "";

    public MoveState State { get; private set; } = MoveState.Idle;
    /// <summary>PCBase::FUN_141675ed0: the entity in the controller's hit report is water (kind 8).</summary>
    public bool OnWater => hitGroup == MoveConfig.WaterGroup;
    public float GroundDistance { get; private set; }
    /// <summary>
    /// PCBase::FUN_141671480 (pcbase.cpp:2488), read by CGroundEffectDataManager::PlayEffects (groundeffectdata.cpp:275):
    /// the stored m_str958 (<see cref="GroundMaterialSnapshot"/>), refreshed every 200 ms, on landing and on a dash skid, not the live hit
    /// report. "" plays nothing.
    /// STILL-OPEN: the equipment's ground override is not ported. The set-effect update @0x1411ec480 (equipment.cpp:2790)
    /// stores the worn set option part's groundAttribute (setitemoptionpart.h:34) in m_str960 via FUN_141670e50, and
    /// FUN_141671480 (pcbase.cpp:2488) returns it instead of the touched ground when it is not empty, m_unk_9c4 != 8
    /// and, when vfunc_62() is true, vfunc_61() == 2.
    /// Other players keep their own: each remote PC replays its UserSync records through its own states and controller
    /// (Net.RemotePc, Ms2.Net.Replay.RemotePcStates; scenestategame.cpp:2509-2517), with PlayEffects' tests in
    /// Ms2.Net.Replay.GroundEffectGate (groundeffectdata.cpp:278-291) and the step keys of PCBase::TextKeyEvent.
    /// </summary>
    public string GroundMaterial => ground.Material;
    /// <summary>The last controller move touched something below (NXCC_COLLISION_DOWN).</summary>
    public bool Grounded => (nxHit & NxController.Hit.Down) != 0;
    /// <summary>The last landing's drop (the height the client sends in StateFallDamage; the server applies damage).</summary>
    public float LastFallHeight { get; private set; }
    public event Action<float> FallDamageHeight;
    /// <summary>StateLand::vfunc_4 (stateland.cpp:94): the landing plays the ground's "land" effects (CGroundEffectDataManager::PlayEffects).</summary>
    public event Action Landed;

    float stateTime, airTime, baseSpeed = MoveConfig.RunSpeed, yawDeg, targetYawDeg;
    Vector3 kVel;                         // GameObjectController m_kVelocity (horizontal)
    int hitGroup = -1;                    // GameObjectControllerHitReport m_entity: its collision group, -1 = none
    readonly GroundMaterialSnapshot ground = new();   // m_str958 and the hit report entity's MapAttribute
    ulong hitOwner;                       // the instance id of the last touched shape owner (its entity's metas are read once)
    NxController nx;
    NxController.Hit nxHit;               // GameObjectController m_nCollisionFlags of the last move
    bool fallTimerRunning;                // GameObjectController m_kFallTimer is running (airTime is its elapsed time)
    bool moveShapeHit;                    // GameObjectControllerHitReport m_bHit: ClearHit before the move, set by onShapeHit
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

    /// <param name="faces">the field's mesh face cache (Field.FaceGrids)</param>
    public static Player Create(Node3D visual, AnimationPlayer anim, Movement.NxController.FaceGrids faces)
    {
        // PC::vfunc_10 (pc.cpp:804-805): SetCollisionGroup(2), the controller's own group, and SetGroups(0x399), the
        // groups its moves collide with
        var p = new Player { Name = "Player", Visual = visual, Anim = anim, CollisionLayer = MoveConfig.PcLayer, CollisionMask = MoveConfig.PcGroups };
        // GameObjectController::CreateController: radius 25 and a capsule part of the height less two radii; the feet
        // (GetFeetPosition) are the capsule's bottom, and the sweep keeps them one skin above what they stand on
        var collision = new CollisionShape3D
        {
            Name = "Collision",
            Shape = new CapsuleShape3D { Radius = MoveConfig.Radius, Height = MoveConfig.Height },
            Position = new Vector3(0, MoveConfig.Height / 2, 0),
        };
        p.AddChild(collision);
        p.nx = new NxController(p, collision, faces) { ShapeHit = p.NoteHit };
        p.AddChild(visual);
        return p;
    }

    /// <summary>The longest frame the body steps at once: a hitch keeps its time (a 200 ms frame moves 200 ms), only a
    /// field load's frame is cut.</summary>
    const double MaxStepDt = 0.25;

    public override void _Ready()
    {
        // PCBase::vfunc_53 (pcbase.cpp:3733): the client steps the PC once per drawn frame with that frame's time, so the
        // body moves in _Process, before the camera (Field, priority 0) and the name tags / bars that read its position
        ProcessPriority = -1;
        MoveProof.Attach(this);
    }

    /// <summary>Back to StateIdle with no motion (after a teleport).</summary>
    public void ResetMotion()
    {
        jumping = false;
        kVel = Vector3.Zero;
        Velocity = Vector3.Zero;
        airTime = 0;
        fallTimerRunning = false;
        fallStartY = float.NaN;
        ClearHitReport();                 // FUN_1416629d0 clears the hit report after placing the PC
        Enter(MoveState.Idle);
    }

    /// <summary>
    /// PCBase::SetTargetYaw (pcbase.cpp:1431): only the yaw to turn to (m_f934); <see cref="TurnTowardTargetYaw"/> turns the
    /// facing there over the next frames. <see cref="Face"/> snaps.
    /// </summary>
    public void TurnTo(Vector3 dir)
    {
        if (dir.LengthSquared() < 1e-6f) return;
        targetYawDeg = Mathf.RadToDeg(Mathf.Atan2(dir.X, dir.Z));
    }

    /// <summary>PCBase::GetTargetYaw (m_f934, pcbase.cpp:1469): the yaw <see cref="TurnTo"/> set, in the
    /// <see cref="FacingRotZ"/> convention (MS2 Z rotation, degrees).</summary>
    public float TargetRotZ => targetYawDeg;

    public void Face(Vector3 dir)
    {
        if (dir.LengthSquared() < 1e-6f) return;
        yawDeg = targetYawDeg = Mathf.RadToDeg(Mathf.Atan2(dir.X, dir.Z));
        (Mount?.Visual ?? Visual).Rotation = new Vector3(0, Mathf.DegToRad(yawDeg), 0);
    }

    /// <summary>
    /// PCBase::vfunc_20().z: the facing as an MS2 Z rotation in degrees (forward = (sin z, -cos z) in MS2 axes); while riding
    /// it is the ridee's (pcbase.cpp:952), whose visual takes the facing here.
    /// </summary>
    /// <summary>The facing as a godot direction (the ridee's when riding), as <see cref="FacingRotZ"/> reads it.</summary>
    public Vector3 FacingDirection => (Mount?.Visual ?? Visual)?.GlobalBasis.Z ?? Forward;

    public float FacingRotZ
    {
        get
        {
            var f = (Mount?.Visual ?? Visual)?.GlobalBasis.Z ?? Forward;     // godot forward; MS2 (x, -z)
            return Mathf.RadToDeg(Mathf.Atan2(f.X, f.Z));
        }
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

    public override void _Process(double delta)
    {
        Step(delta);
        SeatOnMount();
    }

    void Step(double delta)
    {
        // WorldService::OnTick (worldservice_1.cpp:9679-9703) passes the scheduler's game-time delta with no clamp in the
        // client source; only a frame longer than a quarter second (a field load) is cut to it
        float dt = (float)Math.Min(delta, MaxStepDt);
        if (!ScriptDriven)
        {
            var k = MoveInput.Read();
            Input2 = k.Move;
            WalkMode = k.Walk;
            JumpHeld = k.Jump;
            // Offline attack key (defaultkey.xml keyAttack = X): StateSkill::vfunc_4 (@0x1417e5350, stateskill.cpp:432)
            // enters battle stance for UserBattleDurationTick (5 s); the key is read as MyPC reads it, not while
            // Unk_PCMove::FUN_141683ef0 holds input back (Ms2.Ui.InputBlock: a focused text field, the loading screen)
            bool attackKey = !Live && !Ms2.Ui.InputBlock.Active && Input.IsPhysicalKeyPressed(Key.X);
            if (attackKey && !prevAttackKey && GetViewport()?.GuiGetFocusOwner() == null)
                SetBattleStance(true, DefaultBattleStanceSeconds);
            prevAttackKey = attackKey;
        }
        if (InBattle && battleTimer > 0)
        {
            battleTimer -= dt;
            if (battleTimer <= 0) SetBattleStance(false);
        }
        // under a field move's loading screen the client's MyPC is gone (Ms2.Ui.FieldMoveCover): no key, click or script moves it
        if (Ms2.Ui.FieldMoveCover.Up) { Input2 = Vector2.Zero; MouseInput = Vector2.Zero; JumpHeld = false; }
        if (SkillHoldsMove()) { Input2 = Vector2.Zero; MouseInput = Vector2.Zero; JumpHeld = false; }   // the skill state (PlayerSkill)
        if (DeathHoldsMove) { Input2 = Vector2.Zero; MouseInput = Vector2.Zero; JumpHeld = false; }    // StateDead / StateRevival (PlayerDeath)
        var rawInput = Input2 != Vector2.Zero ? Input2 : MouseInput;
        var input = DiagonalHold(rawInput, dt);
        bool jumpPressed = JumpHeld && !prevJump;
        bool upPressed = input.Y > 0 && prevInput.Y <= 0, downPressed = input.Y < 0 && prevInput.Y >= 0;
        prevJump = JumpHeld;
        prevInput = input;
        var dir = WorldDir(input);
        // GameObject m_pos98: each state stores its move buffer's GetMoveDirection (stateidle.cpp:235, statejump.cpp:222,
        // statefall.cpp:170), the camera-relative input (unit_141682f80.cpp:313-335: x +1 left, y +1 forward), not the world move
        moveDir = MoveInputDir(input);
        bool hasInput = dir != Vector3.Zero;
        bool grounded = Grounded;
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
                // StateWalk::vfunc_3 sets the walk / run / ride speed before it moves (statewalk.cpp:171-201), and the
                // transitions come after it in the frame
                baseSpeed = Mount != null ? RideSpeed() : WalkMode ? MoveConfig.WalkSpeed : MoveConfig.RunSpeed;
                if (OnWater) { Enter(MoveState.Swim); break; }
                if (InAirFor(MoveConfig.WalkFallDelay)) { Enter(MoveState.Fall); break; }
                // StateWalk::vfunc_8 order: pressed up / down (ladder or rope), jump, then the held up / down key on a ladder
                if (input.Y > 0 && upPressed && (TryLadder(1) || TryRope(1))) break;
                if (input.Y < 0 && downPressed && TryLadder(2)) break;
                if (grounded && JumpHeld) { StartJump(dir, hasInput, 0x25, dir * baseSpeed); break; }
                if (input.Y > 0 && TryLadder(1)) break;
                if (input.Y < 0 && TryLadder(2)) break;
                if (!hasInput) { Enter(MoveState.Idle); break; }
                kVel = dir * baseSpeed;
                break;

            case MoveState.Swim:
                baseSpeed = Mount != null ? RideSpeed() : MoveConfig.SwimSpeed;      // StateSwim::vfunc_3 the same way (stateswim.cpp:141-166)
                if (!OnWater && grounded) { Enter(MoveState.Idle); break; }
                if (InAirFor(MoveConfig.IdleFallDelay)) { ClearHitReport(); Enter(MoveState.Idle); break; }   // StateSwim::vfunc_8 clears the report
                if (grounded && JumpHeld) { StartJump(dir, hasInput, 0x25, dir * baseSpeed); break; }
                if (!hasInput) { Enter(MoveState.Idle); break; }
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
                // StateJump::vfunc_8: nothing before IsJumpTimeElapsed (50 ms). A pressed up key takes a ladder or rope
                // (FUN_141659af0(1)), else a wall hit starts the climb.
                if (stateTime <= 0.05f) break;
                if (input.Y > 0 && upPressed)
                {
                    if (TryLadder(1) || TryRope(1)) break;
                    if (TryClimb()) break;
                    break;
                }
                // FUN_1417d6620: unless the jump had no input and is still rising, a ladder in reach with the up key
                // held (FUN_141659af0(0): IsActionKeyDown for the ladder, a rope needs the press) is taken from the air
                // (StateLadder sub 0); else a wall hit starts the climb
                if ((jumpHadInput || jumpTime >= jumpApex) && input.Y > 0 && TryLadder(3)) break;
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
                DashUpdate(input, upPressed, dt);
                break;

            case MoveState.Dead:
            case MoveState.Revival:
                DeathUpdate();
                break;
        }
        prevRawInput = rawInput;

        if (State is not (MoveState.Ladder or MoveState.Rope or MoveState.Climb or MoveState.Revival))
        {
            if (hasInput && State != MoveState.Dash) targetYawDeg = Mathf.RadToDeg(Mathf.Atan2(dir.X, dir.Z));
            var before = GlobalPosition;
            Integrate(dt);
            if (State == MoveState.Dash) DashAfterMove(before, dt);
        }
        else wallHit = false;
        // PC::vfunc_53 (pc.cpp:4436-4445) runs PCBase::vfunc_53 first, whose state update (pcbase.cpp:3732) moves the
        // controller; only then does it test m_timer970 and call vfunc_93(bExpired), whose down ray (pcbase.cpp:2466-2474)
        // runs every frame. So the 200 ms refresh reads the hit report and the ground left by this frame's move.
        ProbeGround();
        ground.Frame(dt, OnOrAboveGround);
        TurnTowardTargetYaw(dt);
        TickBlendHold(dt);
        UpdateWeaponStance();
        UpdateAnimation();

        StateSync(dt);                                   // the state sync and, offline, the server's out-of-bounds return
        PoseRideBox();
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
        if (DeadFlag && s is not (MoveState.Dead or MoveState.Revival)) return;   // PC::vfunc_124 (pc.cpp:1199), PlayerDeath
        var old = State;
        if (old == MoveState.Jump) { jumping = false; baseSpeed = jumpSavedSpeed; StateChangeOut?.Invoke(1, 6, new Vector3(GlobalPosition.X, -GlobalPosition.Z, GlobalPosition.Y)); }   // StateJump::vfunc_5 (statejump.cpp:157: SendStateChange(1, 6))
        if (old == MoveState.Ladder) { ladder = null; ClearHitReport(); }   // StateLadder::vfunc_5 clears the hit report
        if (old == MoveState.Rope) rope = null;
        if (old == MoveState.Dash && s != MoveState.Dash) DashExit();
        if (old is MoveState.Ladder or MoveState.Climb) StopJump();                // StateLadder / StateClimb::vfunc_5
        State = s;
        stateTime = 0;
        // StateLadder::vfunc_4 (stateladder.cpp:143), StateRope::vfunc_4 (statemanager.cpp:1225), StateClimb::vfunc_4
        // (state.cpp:1054), StateDash::vfunc_4 (state.cpp:1789)
        if (s is MoveState.Ladder or MoveState.Rope or MoveState.Climb or MoveState.Dash) StopJump();
        // StateLadder::vfunc_4 (stateladder.cpp:163-164), StateRope::vfunc_4 (statemanager.cpp:1238-1239), StateClimb::vfunc_4
        // (state.cpp:1040-1041): the held item goes, with RequestCube 0x12
        if (s == MoveState.Ladder) HeldStateEntered?.Invoke(Ms2.Ui.HeldItemState.Ladder);
        else if (s == MoveState.Rope) HeldStateEntered?.Invoke(Ms2.Ui.HeldItemState.Rope);
        else if (s == MoveState.Climb) HeldStateEntered?.Invoke(Ms2.Ui.HeldItemState.Climb);
        if (s == MoveState.Swim) swimRideTick = syncClock;                   // StateSwim::vfunc_4: m_nMode = the server tick
        if (s == MoveState.Fall) fallStartY = float.IsNaN(fallStartY) ? GlobalPosition.Y : fallStartY;
        if (s == MoveState.Land || (s == MoveState.Idle && old is MoveState.Fall or MoveState.Jump)) SendFallDamage();
        if (s == MoveState.Land)
        {
            // StateLand::vfunc_4 (stateland.cpp:76-104): the entity under the feet goes into the hit report, the ground
            // material is refreshed (vfunc_93(true)), the ground's "land" effects play, then on water (the hit report's
            // entity is kind 8, PCBase::FUN_141675ed0) straight on to StateIdle
            LandRay();
            ground.Refresh(OnOrAboveGround);
            Landed?.Invoke();
            if (OnWater) Enter(MoveState.Idle);
        }
    }

    /// <summary>
    /// GameObjectController::StopJump (@0x141240730, gameobjectcontrollerphysxhelpers.cpp:331-336): no jump, no controller
    /// velocity, and the fall timer reset (Timer::Reset, period 0; the next move off the ground starts it again).
    /// </summary>
    void StopJump()
    {
        jumping = false;
        kVel = Vector3.Zero;
        airTime = 0;
        fallTimerRunning = false;
    }

    /// <summary>PCBase::vfunc_99: in the air for longer than the delay and more than 0.3 m above the ground.</summary>
    bool InAirFor(float delay) => airTime > delay && GroundDistance > MoveConfig.FallMinHeight && !Grounded;

    /// <summary>
    /// PCBase::vfunc_93, run each frame by PC::vfunc_53: 0 while the controller is on the ground; else
    /// RayCastFixedDirection from the feet (vfunc_18 = GameObjectController::GetFeetPosition) straight down with length 0,
    /// which CastRay makes unlimited, on the default groups 0x389; FLT_MAX when it hits nothing.
    /// </summary>
    void ProbeGround()
    {
        if (Grounded) { GroundDistance = 0; return; }
        GroundDistance = Ray(GlobalPosition, Vector3.Down, MoveConfig.UnlimitedRay, GroundGroups) is var (p, _) ? GlobalPosition.Y - p.Y : float.MaxValue;
    }

    /// <summary>
    /// GameObjectControllerHitReport::onShapeHit keeps the entity of the last shape the controller touched; NxCharacter
    /// reports the hits of the side and down passes of a move (not the up pass of the auto-step), so after a move with
    /// a down part it is what the body stands on, else the last thing it ran into. It stays until cleared. Another
    /// controller goes to onControllerHit and leaves it alone.
    /// </summary>
    void NoteHit(GodotObject collider, int shape)
    {
        // onShapeHit (0x140cfade0) sets the entity of a touched shape, or clears it when the shape has no entity; a touched
        // controller goes to onControllerHit (0x140cfaec0), which only keeps the other controller
        if (collider is CharacterBody3D || collider is Node c && c.HasMeta(MoveConfig.ControllerMeta)) return;
        moveShapeHit = true;
        SetHitEntity(collider, shape);
    }

    /// <summary>The hit report's entity becomes the touched shape's (onShapeHit, SetEntity); the same owner as the last
    /// one keeps what was read from it.</summary>
    void SetHitEntity(GodotObject collider, int shape)
    {
        var owner = collider is CollisionObject3D co && shape >= 0 ? co.ShapeOwnerGetOwner(co.ShapeFindOwner(shape)) as Node : null;
        ulong id = owner?.GetInstanceId() ?? 0;
        if (id != 0 && id == hitOwner) return;
        hitOwner = id;
        // the entity's PropCollisionGroup: the shape's own when it differs from its body's (a GeneratePhysX cube box)
        hitGroup = owner != null && owner.HasMeta(FieldBuilder.KindMeta) ? (int)owner.GetMeta(FieldBuilder.KindMeta)
                 : collider is Node n && n.HasMeta(FieldBuilder.GroupMeta) ? (int)n.GetMeta(FieldBuilder.GroupMeta) : 0;
        hitFallReturn = FallReturnEntity(collider, shape);
        ground.Entity = FieldBuilder.GroundMaterial(owner);
    }

    /// <summary>m_unk_9a8 as vfunc_93 sets it: on the ground (FUN_141671a30), or the down ray from the feet hit something.</summary>
    bool OnOrAboveGround => Grounded || GroundDistance < float.MaxValue;

    /// <summary>
    /// StateLand::vfunc_4 (stateland.cpp:83-92): RayCastFixedDirection from 30 above the feet straight down, length 50, on
    /// the default groups; when it hits an entity other than the hit report's, GetHitReport()-&gt;SetEntity puts it there. So
    /// the landing's material and its water test name the entity under the feet even when the landing move touched only
    /// another controller or the report still held a wall.
    /// </summary>
    void LandRay()
    {
        if (RayHit(GlobalPosition + Vector3.Up * 30f, Vector3.Down, 50f, GroundGroups) is not var (col, shape)) return;
        var owner = col is CollisionObject3D co && shape >= 0 ? co.ShapeOwnerGetOwner(co.ShapeFindOwner(shape)) as Node : null;
        if (owner == null || !owner.HasMeta(FieldBuilder.GroundMeta)) return;   // kRay.m_hitEntity == 0: the report stays
        SetHitEntity(col, shape);
    }

    // ---- GameObjectController: jump and fall integration -------------------------------------------------------------

    /// <param name="frameVel">the velocity the old state's update moves by this frame. The frame runs the state's vfunc_3
    /// in CStateManager::Update (PCBase::vfunc_53, pcbase.cpp:3732; StateWalk's sets m_kVelocity = direction * speed and
    /// moves, statewalk.cpp:205-209) before MyPC::vfunc_53 calls the state's vfunc_8, whose transitions start the jump
    /// (mypc.cpp:2966); GameObjectController::StartJump's m_kVelocity = m_vDir
    /// (gameobjectcontrollerphysxhelpers.cpp:315) is replaced by StateJump::vfunc_3's SetKVelocity (statejump.cpp:206)
    /// before the next move. Our frame takes the transition first, so the jump frame moves by this.</param>
    void StartJump(Vector3 dir, bool hasInput, int sub, Vector3 frameVel = default)
    {
        // the old state's vfunc_5 runs first, then StateJump::vfunc_4: StopJump, then StartJump (statejump.cpp:126-128)
        Enter(MoveState.Jump);
        StopJump();
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
        kVel = frameVel;
        StateChangeOut?.Invoke(0, 6, new Vector3(GlobalPosition.X, -GlobalPosition.Z, GlobalPosition.Y));   // StateJump::vfunc_4 (statejump.cpp:134: SendStateChange(0, 6) with the position)
    }

    void Integrate(float dt)
    {
        var v = new Vector3(kVel.X, 0, kVel.Z);
        if (State is MoveState.Idle or MoveState.Land or MoveState.Dead) v = Vector3.Zero;
        if (jumping)
        {
            jumpTime += dt;
            float h = jumpV0 * jumpTime + 0.5f * Gravity * jumpTime * jumpTime;
            v.Y = (h - jumpH) / dt;
            jumpH = h;
        }
        else
        {
            // FUN_1412407f0: dz = g * fallTime * dt + g dt^2 / 2
            v.Y = Gravity * airTime + 0.5f * Gravity * dt;
            // and m_kVelocity * dt only when bMove = vfunc_99(0) && the state's move is ZERO (pcbase.cpp:1782): StateFall
            // moves by ZERO (statefall.cpp:158-159), so it drifts only after 100 ms of falling, more than 0.3 m above the
            // ground and not touching down (pcbase.cpp:2544-2556). StopJump's timer reset holds a warp's first frames.
            if (State == MoveState.Fall && !InAirFor(MoveConfig.IdleFallDelay)) { v.X = 0; v.Z = 0; }
        }
        Velocity = v;

        // GameObjectController::Move: the controller's up / side / down move by the frame's displacement
        var firstHit = MoveStep(v * dt, dt);

        // m_unk_c68 is taken from the first move's flags, before the world box's slide (pcbase.cpp:1835-1840 vs 1844-1862)
        wallHit = (firstHit & NxController.Hit.Sides) != 0 && (firstHit & NxController.Hit.Down) == 0 && v.Y < 0;
        if ((nxHit & NxController.Hit.Up) != 0 && jumping && jumpTime < jumpApex)
        {
            // the controller stays under the ceiling until the jump curve comes back down
        }
    }

    /// <summary>
    /// PCBase::vfunc_68 (@0x14166e100, pcbase.cpp:1820-1866): the controller's move, then the world box. Feet left outside
    /// the box's x or y sides, or above its top, take the frame's move back on those axes (WorldService::FUN_141bd36f0
    /// @0x141bd36f0, worldservice_1.cpp:7072-7083: there is no test against the floor, and the up/down part is dropped while
    /// the move goes down) with one more controller move. A mount moves the same body, as the client's Ridee is a PCBase.
    /// </summary>
    NxController.Hit MoveStep(Vector3 delta, float dt = 0)
    {
        ControllerMove(delta, dt);
        var first = nxHit;
        var p = GlobalPosition;                                                     // field axes: x, -z, y
        float fx = p.X, fy = -p.Z, fz = p.Y;
        var mask = Vector3.Zero;
        if (fx < BoundsMin.X || !(fx <= BoundsMax.X)) mask.X = 1;
        if (fy < BoundsMin.Y || !(fy <= BoundsMax.Y)) mask.Z = 1;
        if (!(fz <= BoundsMax.Z)) mask.Y = 1;
        if (mask.Y != 0 && delta.Y < 0) mask.Y = 0;
        if (mask == Vector3.Zero) return first;
        var slide = -delta * mask;
        if (slide != Vector3.Zero) ControllerMove(slide);
        return first;
    }

    /// <summary>
    /// GameObjectController::Move (@0x14123ff80, gameobjectcontrollerphysxhelpers.cpp:213-292): the controller's move by
    /// <paramref name="delta"/> without the ignored groups, then what it does with the result. A move that only hit
    /// something above, or only landed and touched another controller, leaves group 4 (the npcs) out of the next moves;
    /// a landing stops the jump and the fall timer unless that ignore is on (or the body stands against a controller),
    /// and the ignore ends on a landing clear of every shape. So a body landing on an npc keeps its fall and drops through
    /// it the next frame. The climb's moves (vfunc_68 with no gravity) pass <paramref name="dt"/> 0: they do not advance
    /// the fall time.
    /// </summary>
    void ControllerMove(Vector3 delta, float dt = 0)
    {
        moveShapeHit = false;                                                       // m_hitReport.ClearHit()
        nxHit = nx.Move(delta);
        bool other = nx.HitController;
        if (nxHit == NxController.Hit.Up) nx.IgnoreGroups = MoveConfig.ControllerHitIgnore;                    // :231
        if (nxHit == NxController.Hit.Down && other) nx.IgnoreGroups = MoveConfig.ControllerHitIgnore;         // :233
        bool stop = false;
        if ((nxHit & NxController.Hit.Down) != 0)
        {
            bool onController = false;
            if ((nxHit & NxController.Hit.Sides) != 0 && other)
            {
                onController = true;                                                // :239-247
                if (moveShapeHit) stop = true;
            }
            if (nx.IgnoreGroups != 0 && !other)
            {
                if (nx.Overlaps()) stop = true;                                     // :249-276
                else nx.IgnoreGroups = 0;
            }
            if (nx.IgnoreGroups == 0 && !onController) stop = true;                 // :278-283
        }
        else fallTimerRunning = true;                                               // :287-288, StartInfinite if not running
        if (stop)
        {
            // StopJump (@0x141240730): the jump ends and the fall timer is reset (StateJump then goes to StateLand)
            airTime = 0;
            fallTimerRunning = false;
            // (the climb's moves leave the jump and the state to the climb states)
            if (dt > 0 && jumping && jumpTime > dt) { jumping = false; Enter(MoveState.Land); }   // StateJump::vfunc_3: vfunc_59(4)
        }
        else if (fallTimerRunning) airTime += dt;
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
                // stateladder.cpp:433-463: GetGroundDistance from the feet each frame (length 0 = unlimited, groups 0x389,
                // physxhelper.cpp:475-489). On a hit: the landing when this step reaches the hit, lifted to 1 above it only
                // if the feet are already below; else a fall once the step passes the ladder's base. No hit: the landing
                // only when the feet are at the base (IsNearlyEqual, 1e-6), and the climb goes on otherwise.
                if (Ray(pos, Vector3.Down, MoveConfig.UnlimitedRay, GroundGroups) is var (hit, _))
                {
                    if (pos.Y - move <= hit.Y)
                    {
                        lsub = Ladder.DownLand;
                        if (pos.Y < hit.Y) GlobalPosition = new Vector3(pos.X, hit.Y + 1f, pos.Z);
                        PlayAct(Act.LadderDownLand, restart: true);
                        return;
                    }
                    if (pos.Y - move <= ladder.Position.Y) { Enter(MoveState.Fall); return; }
                }
                else if (Mathf.Abs(pos.Y - ladder.Position.Y) < 1e-6f)
                {
                    lsub = Ladder.DownLand;
                    PlayAct(Act.LadderDownLand, restart: true);
                    return;
                }
            }
            else ladder = next;
            GlobalPosition = pos - new Vector3(0, move, 0);
        }

    }

    // StateLadder::FUN_1417d99e0 (stateladder.cpp:580-597) casts on m_groups 0x201: groups 0 and 9
    readonly RayQuery topRay = RayQuery.CastRay(0x201);
    const uint CapsuleOverlapGroups = 0x389;

    Vector3? TopPoint()
    {
        var from = GlobalPosition + Forward * MoveConfig.LadderTopProbe + new Vector3(0, 500, 0);
        using var hit = GetWorld3D().DirectSpaceState.IntersectRay(topRay.Set(from, from + new Vector3(0, -1000, 0), GetRid()));
        return hit.Count == 0 ? null : (Vector3)hit[PhysicsKeys.Position];
    }

    bool Overlaps(Vector3 feet)
    {
        // GameObjectController::IsCapsuleOverlapping (gameobjectcontrollerphysxhelpers.cpp:538-558) is
        // checkOverlapCapsule(capsule, NX_ALL_SHAPES, 0x389), which has no sides: groups 0, 3, 7, 8 and 9, so neither an
        // npc capsule (group 4) nor a group 10/11 mesh stops it
        var t = new Transform3D(Basis.Identity, feet + new Vector3(0, 2, 0));
        return nx.OverlapsAt(t, CapsuleOverlapGroups);
    }

    // ---- animation -----------------------------------------------------------------------------------------------------

    string interactAnim;
    bool interactLoop;

    /// <summary>A non-loop interact motion played to its end (the state's EndOfSequence): StateReact 0x9e returns to idle
    /// on it (statereact.cpp:263-275).</summary>
    public event System.Action InteractAnimEnded;

    /// <summary>StateInteraction::FUN_1417d25e0 / vfunc_4 0x8b / 0x8c: plays a gather/use motion from the body's sequences.</summary>
    public void StartInteractAnim(string animName, bool loop)
    {
        if (string.IsNullOrEmpty(animName) || Anims == null || Anim == null) return;
        string key = Anims.Sequence(animName, loop);
        if (key == null) return;
        interactAnim = key;
        interactLoop = loop;
        PlaySequence(key, 1f, restart: true);
    }

    public void StopInteractAnim()
    {
        interactAnim = null;
        interactLoop = false;
    }

    void UpdateAnimation()
    {
        if (Mount != null) { RideAnimation(); return; }
        if (Anims == null) return;
        if (interactAnim != null)
        {
            if (State != MoveState.Idle || (!interactLoop && !Anim.IsPlaying()))
            {
                bool ended = State == MoveState.Idle;
                interactAnim = null;
                interactLoop = false;
                if (ended) InteractAnimEnded?.Invoke();
            }
            else return;
        }
        switch (State)
        {
            case MoveState.Idle:
                // StateIdle::PlayStandAnimation (@0x1417ce6f0, stateidle.cpp:518-531): BATTLE_IDLE (Attack_Idle_A) while
                // in battle stance on land, IDLE (Idle_A) otherwise
                PlayAct(OnWater ? Act.WaterIdle : InBattle ? Act.BattleIdle : Act.Idle);
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
                if (stateTime < 0.05f) PlayAct(Act.JumpLand, restart: CurrentAnim != Anims.Name(Act.JumpLand, WeaponPrefix));
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
        if (SkillSequencePlaying()) return;   // the own cast's motion plays to its end (PlayerSkill)
        var name = Anims?.Name(a, WeaponPrefix);
        if (name == null || (name == CurrentAnim && !restart)) return;   // HasAnimation(string) makes a StringName per call: ask only on a change
        if (Anim.HasAnimation(name)) PlaySequence(name, speed, restart);
    }

    // a BLEND transition's destination stays on its first frame until the blend ends: the time left, and the rate it then plays at
    float blendHold = -1, blendHoldSpeed = 1;

    /// <summary>
    /// Changes the body's sequence the way NiActorManager does when the target animation changes: it takes the KFM's
    /// transition from the current sequence to the new one (NiKFMTool::GetTransition, NiActorManager.cpp:624). A row with no
    /// transition of its own uses the file's DEFAULT_NONSYNC one (NiKFMTool::ReadBinary, NiKFMTool.cpp:1459-1466), BLEND 0.1
    /// in female.kfm and male.kfm; the jump's up to down and down to land rows are BLEND 0.00001, a cut.
    /// A BLEND with no blend pairs starts at once (NiActorManager.cpp:673-724, BlendFromSequence over the duration).
    /// NiControllerSequence::StartBlend (NiControllerSequence.cpp:1096-1117) activates the destination as a transition that
    /// holds its start frame (TRANSDEST, m_bHoldDestFrame) while the source plays on and eases out. When the blend ends the
    /// destination starts from that frame (NiControllerSequence.cpp:389-406). A CROSSFADE plays both. The same sequence
    /// again with restart, and the first sequence, start with no blend.
    /// </summary>
    public void PlaySequence(string name, float speed, bool restart)
    {
        var tr = string.IsNullOrEmpty(CurrentAnim) || name == CurrentAnim ? null : Anims?.Transition(CurrentAnim, name) ?? Anims?.DefaultTransition;
        blendHold = -1;
        if (tr == null) Anim.Play(name, 0, speed);
        else if (tr.Type == Ms2.Formats.Model.KfmFile.TransitionType.Blend)
        {
            Anim.Play(name, tr.Duration, 0);
            blendHold = tr.Duration; blendHoldSpeed = speed;
        }
        else Anim.Play(name, tr.Duration, speed);
        if (restart) Anim.Seek(0, true);
        CurrentAnim = name;
    }

    /// <summary>The end of a BLEND: the held destination starts from its first frame at its rate.</summary>
    void TickBlendHold(float dt)
    {
        if (blendHold < 0) return;
        blendHold -= dt;
        if (blendHold > 0) return;
        blendHold = -1;
        Anim.Play(CurrentAnim, 0, blendHoldSpeed);
        Anim.Seek(0, true);
    }
}
