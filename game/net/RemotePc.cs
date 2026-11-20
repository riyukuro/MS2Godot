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
/// (pcbase.cpp:1794-1816), the pushes (WorldService::FUN_141bc8420) and the TRANSDEST hold frame of a KFM blend
/// (the local player's PlaySequence) are not ported for remote PCs.
/// </summary>
public sealed class RemotePc : IRemotePc
{
    /// <summary>The PC's root: the controller body, the visual under it; the name tags and bars hang on it.</summary>
    public readonly CharacterBody3D Body;
    public readonly Node3D Visual;
    public readonly AnimationPlayer Anim;
    public readonly ActionAnims Anims;
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

    RemotePc(CharacterBody3D body, Node3D visual, AnimationPlayer anim, ActionAnims anims, float gravity)
    {
        Body = body; Visual = visual; Anim = anim; Anims = anims; this.gravity = gravity;
        var collision = body.GetNode<CollisionShape3D>("Collision");
        nx = new NxController(body, collision) { ShapeHit = NoteHit };
        States = new RemotePcStates(this, (int)body.GetInstanceId());
        anim.AnimationFinished += OnAnimationFinished;
    }

    /// <summary>
    /// A remote PC at <paramref name="feet"/> (Godot axes). PC::vfunc_10 (pc.cpp:804-805): collision group 2 and the 0x399
    /// sweep, the capsule of GameObjectController::CreateController (the local player's, Player.Create); then the pending
    /// StateFall (pcbase.cpp:792, :924).
    /// </summary>
    public static RemotePc Create(string name, Node3D visual, AnimationPlayer anim, ActionAnims anims, float gravity, Vector3 feet, float rotZ)
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
        var pc = new RemotePc(body, visual, anim, anims, gravity) { RotationZ = rotZ };
        pc.States.TargetYaw = rotZ;
        return pc;
    }

    /// <summary>Starts the state machine once the body is in the tree (its first move needs the space).</summary>
    public void Spawn() => States.Spawn();

    /// <summary>One drawn frame: PCBase::vfunc_53 (pcbase.cpp:3728-3743), then the frame's text keys.</summary>
    public void Tick(float dt)
    {
        ProbeGround();
        States.Update(dt);
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
    /// and the controller is not jumping (no lift here).</summary>
    public bool OnGround => (nxHit & NxController.Hit.Down) != 0 && !jumping;
    public bool OnWater => hitGroup == MoveConfig.WaterGroup;
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
            if (jumping)
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
    void ControllerMove(Vector3 delta, float dt)
    {
        moveShapeHit = false;
        nxHit = nx.Move(delta);
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
    (Vector3 pos, GodotObject col, int shape)? Ray(Vector3 from, float len)
    {
        var q = rayQ.Set(from, from + Vector3.Down * (len > 0 ? len : MoveConfig.UnlimitedRay), Body.GetRid());
        var exclude = rayQ.Ex;
        var space = Body.GetWorld3D().DirectSpaceState;
        for (int i = 0; i < 16; i++)
        {
            using var hit = space.IntersectRay(q);
            if (hit.Count == 0) return null;
            var col = hit[PhysicsKeys.Collider].AsGodotObject();
            int group = col is Node n && n.HasMeta(FieldBuilder.GroupMeta) ? (int)n.GetMeta(FieldBuilder.GroupMeta) : 0;
            if ((MoveConfig.DownRayGroups & (1u << group)) != 0) return ((Vector3)hit[PhysicsKeys.Position], col, (int)hit[PhysicsKeys.Shape]);
            exclude.Add((Rid)hit[PhysicsKeys.Rid]);
            q.Exclude = exclude;
        }
        return null;
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

    bool OnOrAboveGround => OnGround || groundDistance < float.MaxValue;

    /// <summary>StateLand::vfunc_4 (stateland.cpp:83-93): RayCastFixedDirection from 30 above the feet, length 50; a hit
    /// entity other than the report's goes into the report; then vfunc_93(true).</summary>
    public void LandRefresh()
    {
        if (Ray(Body.GlobalPosition + Vector3.Up * 30f, 50f) is var (_, col, shape))
        {
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
    public bool HasAction(int action) => Anims?.Name((Act)action) != null;

    public bool PlayAction(int action, float rate, bool restart)
    {
        var name = Anims?.Name((Act)action);
        if (name == null) return false;
        if (name == currentName && !restart && Anim.IsPlaying()) { Anim.SpeedScale = rate; currentAct = action; return true; }
        // NiActorManager's KFM transition from the current sequence (the local player's PlaySequence), as a blend time
        var tr = currentName == null || name == currentName ? null : Anims.Transition(currentName, name) ?? Anims.DefaultTransition;
        Anim.Play(name, tr?.Duration ?? 0);
        Anim.SpeedScale = rate;
        currentAct = action; currentName = name; lastPos = -1;
        return true;
    }

    public float Gait(float speed, float run, float walk, out bool isRun) => ActionAnims.GaitRate(speed, run, walk, out isRun) * ScaleAnimRate;

    public float WalkSpeed => MoveConfig.WalkSpeed;
    public float RunSpeed => MoveConfig.RunSpeed;
    public float SwimSpeed => MoveConfig.SwimSpeed;
    // STILL-OPEN: remote PC move-speed stat (PCBase::vfunc_96 -> CalcMsiR, pcbase.cpp:2269-2275) and scale easing
    // (PCBase::vfunc_97, pcbase.cpp:2504-2519, m_fScaleA8 / m_fScaleAC) are not replicated yet, taken as 1f.
    public float MoveSpeedRate => 1f;
    public float ScaleAnimRate => 1f;
    public float AnimSpeed { get; set; } = 1f;
    public float Gravity => gravity;
    public float JumpHeight => MoveConfig.JumpParam;
    public float AirSpeedScale => MoveConfig.FallMoveSpeed;
    public float TurnSpeed => MoveConfig.TurnSpeedDeg;

    // ---- animation events ----------------------------------------------------------------------------------------------

    /// <summary>NiActorManager's end of a sequence that does not loop: the state's EndOfSequence.</summary>
    void OnAnimationFinished(StringName name)
    {
        if (currentAct >= 0 && name == currentName) States.SequenceEnded(currentAct);
    }

    /// <summary>
    /// The text keys the current sequence passed this frame (NiActorManager's text key callbacks -> PCBase::TextKeyEvent,
    /// pcbase.cpp:997-1008): a "run*" or "walk*" key, not in StateNone and not class 0x6200028, plays that key's ground
    /// effects. A sequence without such keys plays no steps.
    /// </summary>
    void TextKeys()
    {
        if (currentName == null || Anims == null || !Anims.Kf.TryGetValue(currentName, out var kf) || kf.TextKeys.Count == 0) return;
        double pos = Anim.CurrentAnimationPosition, len = Anim.CurrentAnimationLength;
        if (lastPos < 0) { lastPos = pos; return; }
        foreach (var (t, key) in kf.TextKeys)
            if (Crossed(lastPos, pos, t, len) && GroundEffectGate.StepKey(key, States.Cur, ClassPc)) PlayEffects(key);
        lastPos = pos;
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
