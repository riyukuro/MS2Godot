using Godot;
using Ms2.Net.Replay;
using Ms2Godot.Movement;
using Act = Ms2Godot.Movement.ActionAnims.Act;

namespace Ms2Godot.Net;

/// <summary>
/// Another player's body, driven by its own state machine (<see cref="RemotePcStates"/>) as the client's remote PC is:
/// its controller (GameObjectController::CreateController, the capsule and NxCharacter sweep of the local player,
/// <see cref="NxController"/>) moves and collides, its hit report keeps the touched entity (water, ground material), and its
/// animation's "run*" / "walk*" text keys play steps (PCBase::TextKeyEvent, pcbase.cpp:997-1008).
/// STILL-OPEN: the world box slide of PCBase::vfunc_68 (WorldService::FUN_141bd36f0), the moving platform carry
/// (pcbase.cpp:1794-1816), the pushes of WorldService::FUN_141bc8420 and the TRANSDEST hold frame of a KFM blend
/// (the local player's PlaySequence) are not ported for remote PCs. A hit's StateHit, lift and push are (RemotePcHit).
/// </summary>
public sealed partial class RemotePc : IRemotePc
{
    /// <summary>The PC's root: the controller body, the visual under it; the name tags and bars hang on it.</summary>
    public readonly CharacterBody3D Body;
    public readonly Node3D Visual;
    public readonly AnimationPlayer Anim;
    public readonly ActionAnims Anims;
    /// <summary>The mount this body is (a Ridee, <see cref="CreateRidee"/>), whose actions and speeds are the riding row's;
    /// null for a PC.</summary>
    public readonly Mount Mount;
    public readonly RemotePcStates States;
    readonly NxController nx;
    readonly GroundMaterialSnapshot ground = new();
    readonly RayQuery rayQ = RayQuery.CastRay();

    /// <summary>The job group (PC::GetJobGroupId) and battle stance (vfunc_65, m_unk_160) PlayEffects reads.</summary>
    public int JobGroup;
    public bool BattleStance { get; set; }
    /// <summary>A step's or landing's ground effects that passed PlayEffects' tests: action, ground material, feet (Godot)
    /// and facing (radians about up).</summary>
    public Action<string, string, Vector3, float> Effects;

    // GameObjectController
    readonly float gravity;
    Vector3 kVel;                          // m_kVelocity (Godot axes)
    float airTime; bool fallTimerRunning;  // m_kFallTimer
    bool jumping; float jumpV0, jumpApex, jumpTime, jumpH;
    NxController.Hit nxHit; bool moveShapeHit;
    float groundDistance = float.MaxValue;
    // GameObjectControllerHitReport
    int hitGroup = -1; ulong hitOwner;
    // the sequence the states asked for
    int currentAct = -1; string currentName; double lastPos = -1;

    RemotePc(CharacterBody3D body, Node3D visual, AnimationPlayer anim, ActionAnims anims, Mount mount, float gravity, NxController.FaceGrids faces)
    {
        Body = body; Visual = visual; Anim = anim; Anims = anims; Mount = mount; this.gravity = gravity;
        var collision = body.GetNode<CollisionShape3D>("Collision");
        nx = new NxController(body, collision, faces) { ShapeHit = NoteHit };
        States = new RemotePcStates(this, (int)body.GetInstanceId());
        States.ShadowVisible = shown => FieldShadows.SetShadowVisible(body, shown);
        anim.AnimationFinished += OnAnimationFinished;
        WireHit();
    }

    /// <summary>
    /// A remote PC at <paramref name="feet"/> (Godot axes). PC::vfunc_10 (pc.cpp:804-805): collision group 2 and the 0x399
    /// sweep, the capsule of GameObjectController::CreateController (the local player's, Player.Create); then the pending
    /// StateFall (pcbase.cpp:792, :924). <paramref name="faces"/> is the field's mesh face cache (Field.FaceGrids), shared
    /// with the player's and the other remote PCs' controllers.
    /// </summary>
    public static RemotePc Create(string name, Node3D visual, AnimationPlayer anim, ActionAnims anims, float gravity, Vector3 feet, float rotZ, NxController.FaceGrids faces) =>
        Create(name, visual, anim, anims, null, gravity, feet, rotZ, faces);

    /// <summary>
    /// Another player's mount at <paramref name="feet"/>: Ridee::FUN_1417043a0 (@0x1417043a0) gives it the PC's model size
    /// (25, 105) and Ridee::vfunc_10 (@0x141704750) the PC's collision group 2 and 0x399 sweep, so it is a PC body with the
    /// mount's model. Ridee::vfunc_123 plays idleXMotion / runXMotion for actions 0x1e / 0x1f and the action table's
    /// sequence otherwise; its walk, run and swim speeds are the row's (m_unk_180).
    /// Its run speed is Ridee::GetRunSpeed's (<see cref="RunSpeed"/>); a remote ridee never has the RunX flag, so
    /// GetRideSpeed's runXSpeed is not used. STILL-OPEN: the row's box for remotes.
    /// </summary>
    public static RemotePc CreateRidee(string name, Mount mount, float gravity, Vector3 feet, float rotZ, NxController.FaceGrids faces) =>
        Create(name, mount.Visual, mount.Anim, null, mount, gravity, feet, rotZ, faces);

    static RemotePc Create(string name, Node3D visual, AnimationPlayer anim, ActionAnims anims, Mount mount, float gravity, Vector3 feet, float rotZ, NxController.FaceGrids faces)
    {
        var body = new CharacterBody3D { Name = name, CollisionLayer = MoveConfig.PcLayer, CollisionMask = MoveConfig.PcGroups };
        body.AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new CapsuleShape3D { Radius = MoveConfig.Radius, Height = MoveConfig.Height },
            Position = new Vector3(0, MoveConfig.Height / 2, 0),
        });
        body.AddChild(visual);
        body.Position = feet;
        var pc = new RemotePc(body, visual, anim, anims, mount, gravity, faces) { RotationZ = rotZ };
        pc.States.TargetYaw = rotZ;
        return pc;
    }

    /// <summary>Starts the state machine once the body is in the tree (its first move needs the space).</summary>
    public void Spawn() => States.Spawn();

    /// <summary>One drawn frame: PCBase::vfunc_53 (pcbase.cpp:3728-3743): the states, then the push (:3737); then the
    /// frame's text keys.</summary>
    public void Tick(float dt)
    {
        gameTime += dt;
        ProbeGround();
        States.Update(dt);
        Push.Tick(dt, this);
        TextKeys();
    }

    static Vector3 G(System.Numerics.Vector3 p) => new(p.X, p.Z, -p.Y);
    static System.Numerics.Vector3 M(Vector3 g) => new(g.X, -g.Z, g.Y);

    // ---- IRemotePc ---------------------------------------------------------------------------------------------------

    public System.Numerics.Vector3 Position
    {
        get => M(Body.Position);
        set { Body.Position = G(value); }        // vfunc_19 -> FUN_14166d260: SetFeetPosition
    }

    float rotZ;
    public float RotationZ
    {
        get => rotZ;
        set { rotZ = value; Visual.Rotation = new Vector3(0, Mathf.DegToRad(value), 0); }
    }

    /// <summary>GameObjectController::IsOnGround (gameobjectcontrollerphysxhelpers.cpp:487-490): the last move touched down
    /// and the controller is neither jumping nor lifted.</summary>
    public bool OnGround => (nxHit & NxController.Hit.Down) != 0 && !jumping && !lift.Active;
    /// <summary>The ridee this PC rides (PCBase::GetRidee), set at the ride on and cleared at the ride off; null for a ridee.</summary>
    public RemotePc Ridee { get; set; }
    /// <summary>The swim exit's water test of FUN_141675ed0 (pcbase.cpp:4328-4345): a mounted PC answers with its ridee's test,
    /// else the PC's own ground hit.</summary>
    public bool OnWater => Ridee is { } r ? r.OnWater : hitGroup == MoveConfig.WaterGroup;
    /// <summary>PCBase::vfunc_99 as the local player has it: the fall timer past the delay, more than 0.3 m above the ground
    /// (the down ray) and not touching down.</summary>
    public bool InAirFor(int ms) => fallTimerRunning && airTime * 1000f > ms && groundDistance > MoveConfig.FallMinHeight && !OnGround;
    public bool Jumping => jumping;
    public bool Rising => jumping && jumpTime < jumpApex;
    public float JumpApexTime => jumpApex;
    public int FallTimeMs => (int)(airTime * 1000f);

    /// <summary>GameObjectController::StopJump (@0x141240730): the jump ends, the velocity and fall timer are reset.</summary>
    public void StopJump()
    {
        jumping = false;
        kVel = Vector3.Zero;
        airTime = 0;
        fallTimerRunning = false;
    }

    /// <summary>GameObjectController::StartJump(h): v0 = sqrt(2 * 2h * -g), apex time v0 / -g; m_kVelocity = the direction
    /// times the speed (gameobjectcontrollerphysxhelpers.cpp:315), replaced by StateJump::vfunc_3 before the move.</summary>
    public void StartJump(float height, System.Numerics.Vector3 dir, float speed)
    {
        jumpV0 = Mathf.Sqrt(2f * (2f * height) * -gravity);
        jumpApex = jumpV0 / -gravity;
        jumpTime = 0; jumpH = 0;
        jumping = true;
        kVel = G(dir * speed);
    }

    public void SetVelocity(System.Numerics.Vector3 v) => kVel = G(v);

    /// <summary>
    /// PCBase::vfunc_68 (@0x14166e100, pcbase.cpp:1763-1823): standing still on an entity with no fall time takes no
    /// velocity; else GameObjectController's velocity part (the jump curve, or gravity g t dt + g dt^2 / 2 with the
    /// horizontal velocity only when the fall timer runs and the state's move is zero, the local player's Integrate),
    /// with the horizontal part dropped outside StateWalk / StateFall / StateJump; then the controller's move.
    /// </summary>
    public void Move(System.Numerics.Vector3 moveMs2, float dt)
    {
        var move = G(moveMs2);
        bool zero = move == Vector3.Zero;
        bool noVelocity = OnGround && !fallTimerRunning && hitOwner != 0 && zero;
        var disp = move;
        if (!noVelocity)
        {
            var v = Vector3.Zero;
            if (!jumping && lift.Active) v.Y = LiftStep(dt);   // FUN_1412407f0's lift branch: the step only
            else if (jumping)
            {
                jumpTime += dt;
                float h = jumpV0 * jumpTime + 0.5f * gravity * jumpTime * jumpTime;
                v.Y = h - jumpH;
                jumpH = h;
                v.X = kVel.X * dt; v.Z = kVel.Z * dt;
            }
            else
            {
                v.Y = (gravity * airTime + 0.5f * gravity * dt) * dt;
                if (InAirFor(0) && zero) { v.X = kVel.X * dt; v.Z = kVel.Z * dt; }
            }
            int s = States.Cur;
            if (s != RemotePcStates.StateWalk && s != RemotePcStates.StateFall && s != RemotePcStates.StateJump) { v.X = 0; v.Z = 0; }
            disp += v;
        }
        if (disp != Vector3.Zero) ControllerMove(disp, dt);
    }

    /// <summary>
    /// GameObjectController::Move (@0x14123ff80, gameobjectcontrollerphysxhelpers.cpp:213-292), as the local player's
    /// ControllerMove: an up-only move or a landing on another controller leaves group 4 out of the next moves; a landing
    /// stops the jump and the fall timer unless that ignore holds; a move with no down part starts the fall timer.
    /// </summary>
    /// <param name="exclude">Move's nExcludeGroups (:220): groups left out of this move only.</param>
    void ControllerMove(Vector3 delta, float dt, uint exclude = 0)
    {
        moveShapeHit = false;
        if (exclude != 0 && (exclude & ~nx.IgnoreGroups) != 0)
        {
            uint keep = nx.IgnoreGroups;
            nx.IgnoreGroups = keep | exclude;
            nxHit = nx.Move(delta);
            nx.IgnoreGroups = keep;
        }
        else nxHit = nx.Move(delta);
        bool other = nx.HitController;
        if (nxHit == NxController.Hit.Up) nx.IgnoreGroups = MoveConfig.ControllerHitIgnore;
        if (nxHit == NxController.Hit.Down && other) nx.IgnoreGroups = MoveConfig.ControllerHitIgnore;
        bool stop = false;
        if ((nxHit & NxController.Hit.Down) != 0)
        {
            bool onController = false;
            if ((nxHit & NxController.Hit.Sides) != 0 && other)
            {
                onController = true;
                if (moveShapeHit) stop = true;
            }
            if (nx.IgnoreGroups != 0 && !other)
            {
                if (nx.Overlaps()) stop = true;
                else nx.IgnoreGroups = 0;
            }
            if (nx.IgnoreGroups == 0 && !onController) stop = true;
        }
        else fallTimerRunning = true;
        if (stop)
        {
            airTime = 0;
            fallTimerRunning = false;
            if (jumping && jumpTime > dt) jumping = false;     // StopJump; StateJump::vfunc_3 then lands
        }
        else if (fallTimerRunning) airTime += dt;
    }

    /// <summary>The ground ray of PCBase::vfunc_93 (the local player's ProbeGround): 0 on the ground, else the unlimited
    /// down ray on groups 0x389, FLT_MAX with no hit.</summary>
    void ProbeGround()
    {
        if (OnGround) { groundDistance = 0; return; }
        groundDistance = Ray(Body.GlobalPosition, 0f) is var (p, _, _) ? Body.GlobalPosition.Y - p.Y : float.MaxValue;
    }

    /// <summary>The nearest hit straight down from <paramref name="from"/> on groups 0x389 (CPhysXHelper's default down
    /// groups), <paramref name="len"/> 0 = unlimited: its point, collider and shape.</summary>
    (Vector3 pos, ulong colId, int shape)? Ray(Vector3 from, float len) => Ray(from, Vector3.Down, len);

    /// <summary>The nearest hit from <paramref name="from"/> along the unit <paramref name="dir"/> on groups 0x389
    /// (CPhysXHelper::RayCastFromPoint's default groups), <paramref name="len"/> 0 = unlimited.</summary>
    (Vector3 pos, ulong colId, int shape)? Ray(Vector3 from, Vector3 dir, float len)
    {
        var h = rayQ.Nearest(Body.GetWorld3D().DirectSpaceState, from, from + dir * (len > 0 ? len : MoveConfig.UnlimitedRay), Body.GetRid(), MoveConfig.DownRayGroups);
        return h is { } hit ? (hit.Position, hit.ColliderId, hit.Shape) : null;
    }

    /// <summary>
    /// PCBase::FUN_1416725c0 (@0x1416725c0, pcbase.cpp:4006-4033) for the skill controllers: no hit along the direction
    /// nor along its flat part (RayCastFromPoint, groups 0x389), and no ground (GetGroundDistance, a down ray of
    /// 10 + height + 1) under the end point raised by 10, nor under it moved by (0.1, 0.05): the move ends over a ledge.
    /// The flat direction (length cos tilt) is made unit length here with the same distance, as CPhysXHelper::CastRay
    /// normalizes the ray's direction and keeps m_maxDist (physxhelper.cpp:198-207); a vertical move casts no flat ray
    /// (CastRay returns on a zero direction).
    /// STILL-OPEN: each IntersectRay returns a Godot Dictionary (up to 4 per moving remote PC per frame, as Ray does).
    /// </summary>
    public bool LedgeAhead(System.Numerics.Vector3 dir, float dist, float height, float up) =>
        LedgeAhead(Body.GlobalPosition, dir, dist, height, up, blocked ??= Blocked);

    System.Func<Vector3, Vector3, float, bool> blocked;
    bool Blocked(Vector3 from, Vector3 dir, float len) => Ray(from, dir, len) != null;

    /// <summary><see cref="LedgeAhead(System.Numerics.Vector3, float, float, float)"/> for any PC's feet (Godot axes):
    /// <paramref name="blocked"/>(from, unit dir, len) is the PC's RayCastFromPoint on groups 0x389 (len &gt; 0). The own
    /// player's skill motion asks it too (Player.LedgeAhead).</summary>
    public static bool LedgeAhead(Vector3 feet, System.Numerics.Vector3 dir, float dist, float height, float up,
        System.Func<Vector3, Vector3, float, bool> blocked)
    {
        float l = dir.Length();
        if (l <= 1e-6f) return false;
        dir /= l;
        if (dist < 0f) { dir = -dir; dist = -dist; }
        var from = feet + Vector3.Up * up;
        var d = G(dir);
        if (blocked(from, d, dist)) return false;
        var flat = new Vector3(d.X, 0f, d.Z);
        float fl = flat.Length();
        if (fl > 1e-6f && blocked(from, flat / fl, dist)) return false;
        var end = from + d * dist + Vector3.Up * 10f;
        float len = 10f + height + 1f;
        if (blocked(end, Vector3.Down, len)) return false;
        end += G(new System.Numerics.Vector3(0.1f, 0.05f, 0f));
        return !blocked(end, Vector3.Down, len);
    }

    /// <summary>onShapeHit (0x140cfade0): a touched shape's entity goes into the report (another controller's touch goes to
    /// onControllerHit and leaves it).</summary>
    void NoteHit(GodotObject collider, int shape)
    {
        if (collider is CharacterBody3D || collider is Node c && c.HasMeta(MoveConfig.ControllerMeta)) return;
        moveShapeHit = true;
        SetHitEntity(collider, shape);
    }

    void SetHitEntity(GodotObject collider, int shape)
    {
        var owner = collider is CollisionObject3D co && shape >= 0 ? co.ShapeOwnerGetOwner(co.ShapeFindOwner(shape)) as Node : null;
        ulong id = owner?.GetInstanceId() ?? 0;
        if (id != 0 && id == hitOwner) return;
        hitOwner = id;
        hitGroup = owner != null && owner.HasMeta(FieldBuilder.KindMeta) ? (int)owner.GetMeta(FieldBuilder.KindMeta)
                 : collider is Node n && n.HasMeta(FieldBuilder.GroupMeta) ? (int)n.GetMeta(FieldBuilder.GroupMeta) : 0;
        ground.Entity = FieldBuilder.GroundMaterial(owner);
    }

    /// <summary>PCBase::vfunc_121's report copy (pcbase.cpp:3364-3366): the ridee's touched entity, when it has one, goes
    /// into the rider's report.</summary>
    public void TakeHitReport(RemotePc ridee)
    {
        if (ridee.hitOwner == 0) return;
        hitOwner = ridee.hitOwner;
        hitGroup = ridee.hitGroup;
        ground.Entity = ridee.ground.Entity;
    }

    bool OnOrAboveGround => OnGround || groundDistance < float.MaxValue;

    /// <summary>StateLand::vfunc_4 (stateland.cpp:83-93): RayCastFixedDirection from 30 above the feet, length 50; a hit
    /// entity other than the report's goes into the report; then vfunc_93(true).</summary>
    public void LandRefresh()
    {
        if (Ray(Body.GlobalPosition + Vector3.Up * 30f, 50f) is var (_, colId, shape))
        {
            var col = GodotObject.InstanceFromId(colId);
            var owner = col is CollisionObject3D co && shape >= 0 ? co.ShapeOwnerGetOwner(co.ShapeFindOwner(shape)) as Node : null;
            if (owner != null && owner.HasMeta(FieldBuilder.GroundMeta)) SetHitEntity(col, shape);
        }
        ground.Refresh(OnOrAboveGround);
    }

    public void RefreshGround() => ground.Refresh(OnOrAboveGround);

    /// <summary>m_str958, the stored ground material.</summary>
    public string GroundMaterial => ground.Material;

    /// <summary>
    /// CGroundEffectDataManager::PlayEffects (groundeffectdata.cpp:275-305) for this PC: its tests
    /// (<see cref="GroundEffectGate.Allows"/>; this PC is not MyPC, not flagged by vfunc_23), then the effect files.
    /// STILL-OPEN: COptimizer::IsUpdateEnabled is taken as on: m_nUpdateOn2 comes from the field's setting
    /// (WorldService::FUN_141bcda90, worldservice_1.cpp:6695, m_298->m_1e4) and m_nUpdateOn goes off for a class 3/4 boss
    /// (unit_14108e1e0_npcactorandvibrate.cpp:537); neither is ported.
    /// </summary>
    public void PlayEffects(string action)
    {
        string g = ground.Material;
        if (!GroundEffectGate.Allows(true, JobGroup, BattleStance, action, g, false, true, false)) return;
        Effects?.Invoke(action, g, Body.GlobalPosition, Visual.Rotation.Y);
    }

    public bool IsAction(int action) => currentAct == action && Anim.IsPlaying();
    /// <summary>ActionAnims names an action only once its sequence is in the (shared) library, so no per-call name lookup
    /// on the player (a string to StringName conversion each call).</summary>
    public bool HasAction(int action) => ActionKey(action) != null;

    string ActionKey(int action) => Mount == null ? Anims?.Name((Act)action, WeaponPrefix) : action switch
    {
        0x1e => Mount.SequenceKey(Mount.Data.IdleXMotion, true),
        0x1f => Mount.SequenceKey(Mount.Data.RunXMotion, true),
        _ => Mount.Key(ActionAnims.TypeName(action), ActionAnims.Loops(action)),
    };

    /// <summary>The blend into <paramref name="to"/>: NiActorManager's KFM transition from the current sequence (the local
    /// player's PlaySequence), or a mount's 0.15 s (<see cref="Mount.Play"/>).</summary>
    double Blend(string to)
    {
        if (currentName == null || to == currentName) return 0;
        if (Anims == null) return MountBlend;
        return (Anims.Transition(currentName, to) ?? Anims.DefaultTransition)?.Duration ?? 0;
    }
    const double MountBlend = 0.15;

    /// <summary>PC::vfunc_60 (@0x141658a30, pc.cpp:1737-1749): the weapon action's prefix while weapons are held, "" otherwise.
    /// Every action resolves through it (CAnimationManager::PlayAnimation, animationmanager.cpp:248-265).</summary>
    public string WeaponPrefix { get; private set; } = "";

    /// <summary>The prefix changed (weapons drawn or sheathed): the running action switches to its prefixed or plain sequence.</summary>
    public void SetWeaponPrefix(string prefix)
    {
        prefix ??= "";
        if (prefix == WeaponPrefix) return;
        WeaponPrefix = prefix;
        if (currentAct >= 0) PlayAction(currentAct, (float)Anim.SpeedScale, false);
    }

    /// <summary>
    /// A cast's motion sequence outside the action table (RemoteLayer.Casts.cs): the PC enters StateSkill
    /// (<see cref="RemotePcStates.Cast"/>, which first flushes the queued records), then StateSkill::vfunc_4 plays the
    /// sequence at the record's speed. Its end (<see cref="OnAnimationFinished"/>) is StateSkill::EndOfSequence
    /// (<see cref="RemotePcStates.CastSequenceEnded"/>): the last motion (<paramref name="motion"/> + 1 &gt;= <paramref name="motionCount"/>)
    /// leaves the state, an earlier one waits in it; the next state plays its own action with the default transition from the cast.
    /// </summary>
    public void PlaySkill(string name, float speed, int skillId, bool chargingSkill, System.Numerics.Vector3 pos, float rotZ,
        int holdId = 0, string holdNode = "", int motion = 0, int motionCount = 1, int loopCount = 0,
        IReadOnlyList<string> attackPoints = null, CastMove move = default)
    {
        States.Cast(skillId, chargingSkill, pos, rotZ, holdId, holdNode, motion, motionCount, speed, loopCount, attackPoints, move);
        bool again = name == currentName;
        Anim.Play(name, 0.05, speed);
        if (again) Anim.Seek(0, true);           // the same sequence again (a forced re-entry): Play alone would go on
        Anim.SpeedScale = 1f;
        currentAct = -1; currentName = name; castSpeed = speed;
        lastPos = FromStart;                     // an activated sequence throws its keys from 0 (m_bNeedFirstTextKey)
                                                 // STILL-OPEN: not checked against NiControllerSequence
    }

    // the cast's play speed (SeekCast plays it again at it) and the "from the sequence start" mark of lastPos: the next
    // frame throws the keys in [0, pos] (NiControllerSequence::ThrowAllTextKeys after an activation)
    float castSpeed = 1f;
    const double FromStart = -1e-9;

    public float CastKeyTime(string key) => Anims?.TextKey(currentAct < 0 ? currentName : null, key) ?? -1f;

    /// <summary>
    /// StateSkill's SetTargetAnimation(seq, m_fSpeed, true) + SetOffset(time): the cast's sequence again (played anew when
    /// it already ended) from <paramref name="time"/>; the forced restart reactivates it, so the next frame throws the keys
    /// from 0 to the new time ("loopstart" again).
    /// </summary>
    public void SeekCast(float time)
    {
        if (currentAct >= 0 || currentName == null) return;
        if (!Anim.IsPlaying() || Anim.CurrentAnimation != currentName) Anim.Play(currentName, 0, castSpeed);
        Anim.Seek(Math.Max(time, 0f), true);
        seeked = true;
    }
    bool seeked;

    public bool PlayAction(int action, float rate, bool restart)
    {
        var name = ActionKey(action);
        if (name == null) return false;
        if (name == currentName && !restart && Anim.IsPlaying()) { Anim.SpeedScale = rate; currentAct = action; return true; }
        Anim.Play(name, Blend(name));
        Anim.SpeedScale = rate;
        currentAct = action; currentName = name; lastPos = -1;
        return true;
    }

    /// <summary>
    /// A body sequence by name outside the action table (StateHold::vfunc_9's PlayAnimationByName, looping): the default
    /// transition from the current sequence; nothing when it is already playing or the body has none.
    /// </summary>
    public bool PlaySequence(string name, float rate)
    {
        if (!ReferenceEquals(name, seqName)) { seqName = name; seqKey = Anims?.Sequence(name, true); }
        return PlayKey(seqKey, rate);
    }
    string seqName, seqKey;

    /// <summary>A loaded sequence by its AnimationPlayer key, outside the action table: the default transition from the
    /// current sequence; nothing when it is already playing or the body lacks it.</summary>
    bool PlayKey(string key, float rate)
    {
        if (key == null) return false;      // a key from ActionAnims is a loaded sequence
        if (key == currentName && Anim.IsPlaying()) { Anim.SpeedScale = rate; return true; }
        Anim.Play(key, Blend(key));
        Anim.SpeedScale = rate;
        currentAct = -1; currentName = key; lastPos = -1;
        return true;
    }

    /// <summary>
    /// StateDead::vfunc_4's body part (statedead.cpp:107-146) for another PC: the revive in place (<paramref name="inPlace"/>)
    /// shows the body (vfunc_23(0)) and plays MODEPVP_DEAD (0x40) entry <paramref name="reviveAnim"/> once (PlayAnimation
    /// with rate 1); the normal death hides the body (vfunc_23(1): the entity's IsVisible cleared) and plays DEAD (0x34,
    /// vfunc_123); both then turn the shadow off (vfunc_31(false)). STILL-OPEN: vfunc_23's tails are not ported.
    /// </summary>
    public void EnterDead(bool inPlace, int reviveAnim)
    {
        SetBodyShown(inPlace);
        if (inPlace) PlayKey(Anims?.NameAt(Act.ModePvpDead, reviveAnim), 1f);
        else PlayAction((int)Act.Dead, 1f, false);
        FieldShadows.SetShadowEnabled(Body, false);
    }

    /// <summary>StateDead::vfunc_5 (statedead.cpp:177-185): the body shown (vfunc_23(0)) and the shadow on (vfunc_31(true);
    /// the state is still StateDead there, CStateManager::ChangeState statemanager.cpp:301-310, so PC::vfunc_31's state 0x3f
    /// case does not apply).</summary>
    public void LeaveDead()
    {
        SetBodyShown(true);
        FieldShadows.SetShadowEnabled(Body, true);
    }

    /// <summary>PC::vfunc_23(!shown): the character root hidden or shown (StateDead::vfunc_5 shows it, statedead.cpp:181).</summary>
    public void SetBodyShown(bool shown)
    {
        if (Visual.Visible != shown) Visual.Visible = shown;
    }

    /// <summary>PCBase m_unk_c08, the hide reasons (bit 2: a hideRider mount).</summary>
    int hideReasons;
    public bool IsHidden => hideReasons != 0;

    /// <summary>PCBase::AddHideReason (@0x14166ca20) / RemoveHideReason (@0x14166ca80): nothing when the bit already has that
    /// value, else ApplyHideReasons (@0x14166cb30): vfunc_23(1) while any reason is set, vfunc_23(0) when none is. Its
    /// re-show of the ridee for reason 2 alone is the port's already: the rider's visual does not carry the mount. The
    /// simple shadow follows: SetShadowVisible(false) while hidden, (true) when shown (pcbase.cpp:1188-1203).</summary>
    public void SetHideReason(int reason, bool on)
    {
        int bit = 1 << reason;
        if (((hideReasons & bit) != 0) == on) return;
        hideReasons = on ? hideReasons | bit : hideReasons & ~bit;
        SetBodyShown(hideReasons == 0);
        FieldShadows.SetShadowVisible(Body, hideReasons == 0);
    }

    /// <summary>The hide reason of a hideRider mount (pcbase.cpp:3331-3333).</summary>
    public const int HideReasonRide = 2;

    // PCBase m_unk_1c0: the stats by kind (0 total, 1 base, 2 current) and id
    readonly long[][] stats = { new long[Ms2.Net.Replay.MoveSpeedRate.StatCount], new long[Ms2.Net.Replay.MoveSpeedRate.StatCount],
        new long[Ms2.Net.Replay.MoveSpeedRate.StatCount] };

    /// <summary>PCBase::GetStat: kind 0 total, 1 base, 2 current.</summary>
    public long Stat(int kind, int stat) => (uint)kind < 3 && (uint)stat < (uint)stats[0].Length ? stats[kind][stat] : 0;

    /// <summary>PCBase::SetStat (@0x141670540): the total as given (FUN_1416708a0), the base (FUN_1416705c0) and the current
    /// (SetCurrentStat) held to 0 below and the total above. Each also goes on to the PC's ridee (m_spB10); RemoteLayer
    /// passes it on (<see cref="RemoteLayer"/>'s SetStat).</summary>
    public void SetStat(int kind, int stat, long value)
    {
        if ((uint)kind >= 3 || (uint)stat >= (uint)stats[0].Length) return;
        if (kind != 0)
        {
            if (value < 0) value = 0;
            if (value > stats[0][stat]) value = stats[0][stat];
        }
        stats[kind][stat] = value;
    }

    /// <summary>The held actor's lookup (RemoteLayer: FUN_141bd1200 and FUN_1416757c0 over the field's users and npcs).</summary>
    public Func<int, string, HoldTarget> HoldLookup;
    public HoldTarget FindHoldTarget(int id, string node) => HoldLookup?.Invoke(id, node) ?? default;

    public float Gait(float speed, float run, float walk, out bool isRun) => ActionAnims.GaitRate(speed, run, walk, out isRun) * ScaleAnimRate;

    // a mount's are its row's, in units/s (row * 100; Ridee::FUN_1417043a0's m_unk_180 y / z / w)
    public float WalkSpeed => Mount == null ? MoveConfig.WalkSpeed : Mount.Data.WalkSpeed * 100f;
    /// <summary>A mount's is Ridee::GetRunSpeed @0x1417071d0 (ridee.cpp:1028-1037): the row's run speed times 1 + the rider's
    /// GetAbilityRate(0xae) (<see cref="RiderAbilityRate"/>), with FUN_1411f0e30's rounding.</summary>
    public float RunSpeed => Mount == null ? MoveConfig.RunSpeed
        : (float)((double)Mount.Data.RunSpeed * (double)(1.0f + (RiderAbilityRate?.Invoke(Player.RunXSameSpeedAbility) ?? 0f)) + 0.000001) * 100f;
    public float SwimSpeed => Mount == null ? MoveConfig.SwimSpeed : Mount.Data.SwimSpeed * 100f;
    /// <summary>The row's run speed with no ability rate (m_unk_180.m_z, ridee.cpp:431), StateSwim sub 0x84's.</summary>
    public float RowRunSpeed => Mount == null ? MoveConfig.RunSpeed : Mount.Data.RunSpeed * 100f;
    /// <summary>Ridee::FUN_1417075e0 (ridee.cpp:1091-1103): a riding type 1 or 2, or the rider's enabled buff of
    /// eventBuffType 3 (<see cref="RiderEventBuff"/>); false for a PC.</summary>
    public bool WaterRunner => Mount != null && (Mount.Data.Type is 1 or 2 || RiderEventBuff?.Invoke(3) == true);
    /// <summary>A ridee's rider's CEquipment::GetAbilityRate of a special ability; null reads 0.</summary>
    public Func<int, float> RiderAbilityRate;
    /// <summary>A ridee's rider's HasEnabledWithEventBuffType; null reads false.</summary>
    public Func<int, bool> RiderEventBuff;
    /// <summary>PCBase::vfunc_96: a ridee's CalcRmsiR of its current mount speed, a PC's CalcMsiR of its current movement
    /// speed (<see cref="Ms2.Net.Replay.MoveSpeedRate"/>).</summary>
    public float MoveSpeedRate => Mount != null
        ? Ms2.Net.Replay.MoveSpeedRate.Ridee(stats[2][Ms2.Net.Replay.MoveSpeedRate.MountSpeedStat])
        : Ms2.Net.Replay.MoveSpeedRate.Pc(stats[2][Ms2.Net.Replay.MoveSpeedRate.MovementSpeedStat]);
    // STILL-OPEN: the scale easing (PCBase::vfunc_97, pcbase.cpp:2504-2519, m_fScaleA8 / m_fScaleAC) is taken as 1f.
    public float ScaleAnimRate => 1f;
    public float AnimSpeed { get; set; } = 1f;
    public float Gravity => gravity;
    public float JumpHeight => MoveConfig.JumpParam;
    public float AirSpeedScale => MoveConfig.FallMoveSpeed;
    public float TurnSpeed => MoveConfig.TurnSpeedDeg;

    // ---- animation events ----------------------------------------------------------------------------------------------

    /// <summary>NiActorManager's end of a sequence that does not loop: the state's EndOfSequence (a cast's sequence, played
    /// outside the action table, is StateSkill::EndOfSequence).</summary>
    void OnAnimationFinished(StringName name)
    {
        if (name != currentName) return;
        if (currentAct >= 0) States.SequenceEnded(currentAct);
        else if (States.Cur == RemotePcStates.StateSkill) States.CastSequenceEnded();
    }

    /// <summary>
    /// The text keys the current sequence passed this frame (NiActorManager's text key callbacks -> PCBase::TextKeyEvent,
    /// pcbase.cpp:997-1008): a "run*" or "walk*" key, not in StateNone and not class 0x6200028, plays that key's ground
    /// effects. A sequence without such keys plays no steps.
    /// </summary>
    /// A cast's sequence (played outside the action table) also hands each key to StateSkill::TextKeyEvent
    /// (<see cref="RemotePcStates.CastTextKey"/>); a key that takes the sequence back (<see cref="SeekCast"/>) lets the
    /// frame's later keys go on (the throw's range is already taken) and the next frame throw from the sequence start.
    void TextKeys()
    {
        if (currentName == null) return;
        var keys = Mount != null ? Mount.TextKeys(currentName) : Anims != null && Anims.Kf.TryGetValue(currentName, out var kf) ? kf.TextKeys : null;
        if (keys == null || keys.Count == 0) return;
        double pos = Anim.CurrentAnimationPosition, len = Anim.CurrentAnimationLength;
        if (lastPos < 0 && lastPos != FromStart) { lastPos = pos; return; }
        bool cast = currentAct < 0 && States.Cur == RemotePcStates.StateSkill;
        seeked = false;
        foreach (var (t, key) in keys)
        {
            if (!Crossed(lastPos, pos, t, len)) continue;
            if (GroundEffectGate.StepKey(key, States.Cur, ClassPc)) PlayEffects(key);
            if (cast) States.CastTextKey(key, (float)t);
        }
        if (seeked) { lastPos = FromStart; seeked = false; }
        else lastPos = pos;
    }

    /// <summary>PC's class id (efd::DynamicCast&lt;PC&gt;, class 0x6200003, worldservice_1.cpp:6149).</summary>
    const int ClassPc = 0x6200003;

    static bool Crossed(double from, double to, double t, double len) => to >= from ? from < t && t <= to : (t > from || t <= to);

    /// <summary>Stops listening before the body is freed.</summary>
    public void Free()
    {
        Anim.AnimationFinished -= OnAnimationFinished;
        Body.QueueFree();
    }
}
