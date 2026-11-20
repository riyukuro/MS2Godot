using System.Numerics;

namespace Ms2.Net.Replay;

/// <summary>
/// The body side of a remote PC that its state machine drives (PCBase / GameObjectController, MS2 axes, game units: 1 =
/// 1 cm; the client's PhysX speeds are metres per second, x100 here).
/// </summary>
public interface IRemotePc
{
    /// <summary>vfunc_18 / vfunc_19: the feet position.</summary>
    Vector3 Position { get; set; }
    /// <summary>vfunc_20().z / vfunc_21: the body's Z rotation in degrees.</summary>
    float RotationZ { get; set; }
    /// <summary>FUN_141671a30: the controller stands on something (its last move touched down).</summary>
    bool OnGround { get; }
    /// <summary>FUN_141675ed0: the entity in the controller's hit report is water (kind 8).</summary>
    bool OnWater { get; }
    /// <summary>vfunc_99(ms): the controller's fall timer runs and has passed <paramref name="ms"/>.</summary>
    bool InAirFor(int ms);
    /// <summary>GameObjectController::IsJumping / IsRising / GetJumpApexTime (s) / GetFallTime (ms).</summary>
    bool Jumping { get; }
    bool Rising { get; }
    float JumpApexTime { get; }
    int FallTimeMs { get; }
    /// <summary>GameObjectController::StopJump and StartJump(height, direction, speed).</summary>
    void StopJump();
    void StartJump(float height, Vector3 dir, float speed);
    /// <summary>GameObjectController::SetKVelocity (units/s).</summary>
    void SetVelocity(Vector3 v);
    /// <summary>PCBase::vfunc_68's controller part (pcbase.cpp:1763-1862): gravity, the move and the world box, by
    /// <paramref name="move"/> (units) over <paramref name="dt"/> seconds; the turn toward the target yaw is
    /// <see cref="RemotePcStates"/>'s.</summary>
    void Move(Vector3 move, float dt);
    /// <summary>StateLand::vfunc_4 (stateland.cpp:83-93): the ray from 30 above the feet, 50 down, puts a hit entity in the
    /// hit report, then vfunc_93(true) refreshes the stored ground material.</summary>
    void LandRefresh();
    /// <summary>vfunc_93(true) (PCBase, pcbase.cpp:2458-2483): the stored ground material (m_str958) from the hit report's
    /// entity. PC::vfunc_53 calls it every 200 ms (pc.cpp:4441-4445); vfunc_93(false) leaves it.</summary>
    void RefreshGround();
    /// <summary>CGroundEffectDataManager::PlayEffects(this, action) after its gates (<see cref="GroundEffectGate"/>).</summary>
    void PlayEffects(string action);
    /// <summary>FUN_14166de90(action, current): the current sequence is the action's.</summary>
    bool IsAction(int action);
    /// <summary>HasAnimSequence(vfunc_123(action)): the body has the action's sequence.</summary>
    bool HasAction(int action);
    /// <summary>PlayAnimation(vfunc_123(action), rate, restart): false when the body has no such sequence.</summary>
    bool PlayAction(int action, float rate, bool restart);
    /// <summary>PC::vfunc_115 through StateWalk::vfunc_10 (statewalk.cpp:387-390): the gait rate of
    /// <paramref name="speed"/> and whether it is the run (m_n48).</summary>
    float Gait(float speed, float run, float walk, out bool isRun);
    /// <summary>FUN_141671920's speeds (m_unk_180): y walk, z run, w swim (units/s).</summary>
    float WalkSpeed { get; }
    float RunSpeed { get; }
    float SwimSpeed { get; }
    /// <summary>GameObjectController::GetGravity(true) (units/s^2, negative).</summary>
    float Gravity { get; }
    /// <summary>FUN_14166da60: the jump height (units).</summary>
    float JumpHeight { get; }
    /// <summary>The constant StateFall / StateJump scale the air velocity by (Client::Constants, statefall.cpp:155,
    /// statejump.cpp:205).</summary>
    float AirSpeedScale { get; }
    /// <summary>PCBase::GetTurnSpeed (degrees/s).</summary>
    float TurnSpeed { get; }
    /// <summary>vfunc_65 (m_unk_160): the battle stance.</summary>
    bool BattleStance { get; }
}

/// <summary>
/// A remote PC's CStateManager (statemanager.cpp) with the states that move it: the queue of sync records
/// (FUN_1417db2e0, FUN_1417dc230), the record's application (FUN_1417db550, FUN_1417dc4c0, FUN_1417dbd00), ChangeState
/// and, per frame, Update (statemanager.cpp:220-276) running the current state's vfunc_3 (move) and vfunc_9 (animation
/// and the transitions the PC takes by itself: a fall, a landing, the end of the landing). The input states' vfunc_8
/// runs for MyPC only (mypc.cpp:2966), so a remote PC changes state by its records and by its own vfunc_9 tests.
/// Ported states: StateIdle (1), StateWalk (2), StateLand (4), StateFall (5), StateJump (6), StateSwim (0x1b).
/// STILL-OPEN: every other registered state (pc.cpp:668-721: crawl, ladder, rope, emotion, skill, dash, climb, glide, swim
/// dash, ...) changes the id here and runs no update of its own; the records still place the PC (FUN_1417dbd00 snaps for
/// those states at once). A record's StateData (FUN_1417dc9a0 / vfunc_27, read by its vfunc_2) is not read. The push
/// control (m_pushControl, statemanager.cpp:591), the marker the sender stood on (m_str88, FUN_1417dc4c0
/// statemanager.cpp:606-620), the cube cell (FUN_141678300) and the fixed Z rotation of PCBase::vfunc_21
/// (AdditionalCliList::TryGetFixedZRotation) are not ported. FUN_141676b70 (m_unk_b80, the ride/stunt idle and walk subs
/// 0xc / 0x17 / 0x27 / 0x87) is taken as 0.
/// </summary>
public sealed class RemotePcStates
{
    public const int StateNone = 0, StateIdle = 1, StateWalk = 2, StateLand = 4, StateFall = 5, StateJump = 6,
        StateSkill = 0x10, StateRide = 0x21, StateSwim = 0x1b;

    // action ids (ActionType, actioncontainer.cpp) the ported states play through vfunc_123
    public const int ActIdle = 0, ActBattleIdle = 1, ActJumpDown = 2, ActJumpLand = 3, ActJumpUp = 4, ActJumpFall = 5,
        ActRun = 0x1c, ActWalk = 0x1d, ActTalkIdle = 0x2e, ActSpecialJumpUp = 0x2a, ActSpecialJumpDown = 0x2b,
        ActWaterIdle = 0x30, ActWaterSwim = 0x31;

    /// <summary>The ids PC::vfunc_10's RegisterState calls name (pc.cpp:668-721) and StateNone (statemanager.cpp:197):
    /// ChangeState returns on any other (statemanager.cpp:304).</summary>
    static readonly bool[] Registered = MakeRegistered();
    static bool[] MakeRegistered()
    {
        var r = new bool[256];
        foreach (int id in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0xb, 0xc, 0xd, 0xe, 0xf, 0x10, 0x11, 0x12, 0x14, 0x15, 0x16,
                     0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20, 0x21, 0x22, 0x25, 0x26, 0x28, 0x29, 0x2a, 0x2b, 0x2c,
                     0x2d, 0x2f, 0x35, 0x36, 0x37, 0x39, 0x3a, 0x3b, 0x3c, 0x3d, 0x3e, 0x3f, 0x40, 0x45, 0x46, 0x47 })
            r[id] = true;
        return r;
    }

    readonly IRemotePc pc;
    readonly List<SyncRecord> requests = new();   // m_requests (std::list, oldest first)
    readonly int[] subs = new int[256];           // each State object's m_n10, kept across entries as the client's are
    readonly Random rng;

    /// <summary>m_nCur / m_nPrev.</summary>
    public int Cur { get; private set; }
    public int Prev { get; private set; }
    /// <summary>The current state's m_n10.</summary>
    public int Sub => subs[Cur];
    /// <summary>GameObject m_pos98: the move buffer direction (x left, y forward).</summary>
    public Vector2 MoveInput { get; private set; }
    /// <summary>PCBase m_posBF4 / m_unk_c00: the sender's camera Euler rotation and camera mode, for vfunc_67.</summary>
    public Vector3 CameraEuler { get; private set; }
    public int CameraMode { get; private set; }
    /// <summary>PCBase m_f934 (SetTargetYaw).</summary>
    public float TargetYaw { get; set; }
    /// <summary>m_unk_190.m_z (FUN_141671960 / vfunc_95): the move speed the states set (units/s).</summary>
    public float Speed { get; private set; }
    /// <summary>Called with each state entered, before its vfunc_4 (which may enter another in the same frame).</summary>
    public Action<int> StateChanged;
    /// <summary>The queued records (for tests).</summary>
    public int Queued => requests.Count;

    // StateWalk
    float walkSpeed1, walkSpeed2; int walkRun = 1, walkChanged;
    // StateJump
    Vector3 jumpDir; int jumpFirstSteer; float jumpSavedSpeed; bool jumpHadInput; static int jumpToggle;
    static readonly int[] JumpSubs = { 0x21, 0x23, 0x22, 0x24 };   // m_aSub (@0x1417d5160, statejump.cpp:82-85): {0x21, 0x23}, +2 {0x22, 0x24}

    public RemotePcStates(IRemotePc owner, int seed = 0)
    {
        pc = owner;
        rng = new Random(seed);
        Speed = owner.RunSpeed;                      // PCBase::vfunc_10: FUN_141671960(m_unk_180.m_z) (pcbase.cpp:916)
        TargetYaw = owner.RotationZ;                 // and SetTargetYaw(vfunc_20().z) (pcbase.cpp:915)
        subs[StateWalk] = 0x18;
    }

    /// <summary>
    /// The first state: PCBase's ctor leaves StateFall (5) pending in m_unk_c48 (pcbase.cpp:792) and PCBase::vfunc_10
    /// applies it through FUN_1416795e0 (pcbase.cpp:924, :3557-3569), so a PC added to the field falls and lands.
    /// </summary>
    public void Spawn() => ChangeState(StateFall);

    // ---- the queue -------------------------------------------------------------------------------------------------

    /// <summary>
    /// CStateManager::FUN_1417db2e0 (@0x1417db2e0, statemanager.cpp:355-386): the record goes to the back; then, newest
    /// first, from the newest record with a non-zero time the times are summed, and the record that takes the sum past
    /// 100 ms is dropped with every older one.
    /// </summary>
    public void Push(SyncRecord r)
    {
        requests.Add(r);
        int i = requests.Count - 1;
        while (i >= 0 && requests[i].Time == 0f) i--;
        if (i < 0) return;                           // no record with a time: erase(begin, rend().base()) erases nothing
        float sum = requests[i].Time;
        i--;
        for (; i >= 0; i--)
        {
            sum += requests[i].Time;
            if (sum > 0.1f) break;                   // FUN_14028d280(100): 100 ms in seconds
        }
        if (i >= 0) requests.RemoveRange(0, i + 1);
    }

    /// <summary>
    /// CStateManager::FUN_1417dc230 (@0x1417dc230, statemanager.cpp:519-560): the front record is counted down by the
    /// frame and played once; a record shorter than the frame is dropped for the next one when there is one (which is
    /// returned at once, without the played test), else it is held at 0.
    /// </summary>
    SyncRecord Next(float delta)
    {
        if (requests.Count == 0) return null;
        var r = requests[0];
        if (r.Time >= delta)
        {
            r.Time -= delta;
            if (r.Fired) return null;
            r.Fired = true;
            return r;
        }
        if (requests.Count == 1)
        {
            r.Time = 0f;
            if (r.Fired) return null;
            r.Fired = true;
            return r;
        }
        requests.RemoveAt(0);
        return requests[0];
    }

    /// <summary>CStateManager::FlushRequests (@0x1417dc110, statemanager.cpp:504-515): every queued record is applied
    /// and the state run for its time.</summary>
    void FlushRequests()
    {
        var all = requests.ToArray();
        requests.Clear();
        foreach (var r in all)
        {
            Apply(r);
            StateMove(r.Time);
            StateFrame(r.Time);
        }
    }

    // ---- the frame -------------------------------------------------------------------------------------------------

    /// <summary>
    /// PCBase::vfunc_53 (pcbase.cpp:3728-3743) / CStateManager::Update (@0x1417daaa0, statemanager.cpp:220-276): the
    /// queue's record is applied, then the state's vfunc_3 and vfunc_9 run with the frame time (the MyPC-only key and
    /// position checks are skipped: the owner is not class 0x6200004). Then PC::vfunc_53's 200 ms ground refresh
    /// (pc.cpp:4441-4445) runs through <see cref="IRemotePc.RefreshGround"/>.
    /// </summary>
    public void Update(float dt)
    {
        var r = Next(dt);
        if (r != null) Apply(r);
        StateMove(dt);
        StateFrame(dt);
        // PC::vfunc_53: m_timer970 expired (elapsed >= 200 ms, timer.cpp:167) or not yet started -> Start(200), vfunc_93(true)
        groundMs += dt * 1000.0;
        if (!groundStarted || groundMs >= GroundRefreshMs)
        {
            groundStarted = true;
            groundMs = 0;
            pc.RefreshGround();
        }
    }

    /// <summary>m_timer970's period: Timer::Start(200) in PC::vfunc_53 (pc.cpp:4443).</summary>
    public const double GroundRefreshMs = 200;
    double groundMs; bool groundStarted;

    /// <summary>
    /// CStateManager::FUN_1417db550 (@0x1417db550, statemanager.cpp:390-436), the PC branch: the move direction, the
    /// camera rotation and mode, m_f92c = 1, then the record is applied unless the PC or the record is in StateSkill
    /// (0x10) or the record is StateLand (4): the remote PC's landing is its own.
    /// STILL-OPEN: the StateSkill branch (statemanager.cpp:403-421: a different skill id or a charging skill) needs the
    /// skill states; this queue never enters 0x10.
    /// </summary>
    void Apply(SyncRecord r)
    {
        MoveInput = r.Move;                          // vfunc_22(m_vel3c)
        CameraEuler = r.CameraEuler;                 // FUN_141678160(m_pos48)
        CameraMode = r.CameraMode;                   // FUN_1416781a0(m_n54)
        if (Cur != StateSkill && r.State != StateSkill && r.State != StateLand) ApplyPc(r);
    }

    /// <summary>
    /// CStateManager::FUN_1417dc4c0 (@0x1417dc4c0, statemanager.cpp:587-637): with no push and the current state allowing
    /// it (IsPositionCorrectionAllowed), the position correction, the body rotation and the target yaw; then, unless the
    /// state refuses records (vfunc_32) or the record is StateHit (0xd) or StateStun (0x12), the record's state through
    /// vfunc_59, forced when the state asks for it (vfunc_28).
    /// </summary>
    void ApplyPc(SyncRecord r)
    {
        if (CorrectionAllowed(Cur))
        {
            Correct(r);
            pc.RotationZ = r.RotationZ;              // vfunc_21(m_dir2c)
            TargetYaw = r.TargetYaw;                 // SetTargetYaw(m_f58)
        }
        if (r.State == 0xd || r.State == 0x12) return;
        bool force = Cur == r.State && StateForces(r);
        // PCBase::vfunc_59 (pcbase.cpp:1098-1112): ready (m_unk_b4) and not riding (state 0x21 hands it to the ridee)
        ChangeState(r.State, r.Sub, force);
    }

    /// <summary>CStateManager::IsPositionCorrectionAllowed (@0x1417dc070, statemanager.cpp:482-496).</summary>
    public static bool CorrectionAllowed(int state) => state is not (0xe or 0x20 or 0x2b or 0x3a or 0x3b or 0x3c or 0x45);

    /// <summary>
    /// CStateManager::FUN_1417dbd00 (@0x1417dbd00, statemanager.cpp:440-460): the PC is put on the record's position
    /// unless the record is a moving state (2, 0x1b, 0x1c, 0x1e, 5, 3, 1, 0xd, 0x41, 0x46) less than 100 away on the
    /// ground plane; a height difference over 200 (400 for StateGlide 0x1e) or a glide record for a PC not gliding puts
    /// it there in any case.
    /// </summary>
    void Correct(SyncRecord r)
    {
        bool snap = true;
        var d = pc.Position - r.Position;
        float dz = d.Z;
        float dist = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
        int s = r.State;
        if ((s == 2 || s == 0x1b || s == 0x1c || s == 0x1e || s == 5 || s == 3 || s == 1 || s == 0xd || s == 0x41 || s == 0x46) && dist < 100f)
            snap = false;
        float limit = s == 0x1e ? 400f : 200f;
        if (MathF.Abs(dz) > limit) snap = true;
        if (s == 0x1e && Cur != 0x1e) snap = true;
        if (snap) pc.Position = r.Position;
    }

    /// <summary>vfunc_28 of the current state (State::vfunc_28 is false; StateJump's is m_n10 != the record's sub,
    /// statejump.cpp:342-345).</summary>
    bool StateForces(SyncRecord r) => Cur == StateJump && subs[StateJump] != r.Sub;

    /// <summary>
    /// CStateManager::ChangeState (@0x1417dafa0, statemanager.cpp:300-325): an unregistered id is ignored; the same state
    /// unforced only takes the sub (State::vfunc_25: if (n) vfunc_2(n)); else the old state's vfunc_5, the sub, then the new
    /// state's vfunc_4. StateSkill first flushes the queue.
    /// </summary>
    public void ChangeState(int state, int sub = 0, bool force = false)
    {
        if (state < 0 || state > 255 || !Registered[state]) return;
        if (state == Cur && !force)
        {
            if (sub != 0) subs[Cur] = sub;
            return;
        }
        if (state == StateSkill) FlushRequests();
        Exit(Cur, state);
        Prev = Cur;
        if (sub != 0) subs[state] = sub;
        Cur = state;
        StateChanged?.Invoke(state);
        Enter(state);
    }

    // ---- the states ------------------------------------------------------------------------------------------------

    void Enter(int s)
    {
        switch (s)
        {
            case StateIdle:
                // StateIdle::vfunc_4 (stateidle.cpp:167-191): sub 1 (0xc riding a stunt), 7 on water
                subs[s] = 1;
                if (pc.OnWater) subs[s] = 7;
                // STILL-OPEN: the water idle sound (StartIdleSound) and FUN_14166f8f0(0, 1)
                break;
            case StateWalk:
                // StateWalk::vfunc_4 (statewalk.cpp:101-154)
                walkSpeed1 = walkSpeed2 = 0;                  // vfunc_96 / vfunc_97 (the speed pair the change test reads)
                if (pc.OnWater) { ChangeState(StateSwim); return; }
                WalkSpeedBySub();
                WalkGait();                                   // State::vfunc_4 -> StateWalk::vfunc_10 sets m_n48
                break;
            case StateLand:
                // StateLand::vfunc_4 (stateland.cpp:74-101): sub 0x86, the ground ray and refresh, "land", then on water
                // straight to StateIdle
                subs[s] = 0x86;
                pc.LandRefresh();
                pc.PlayEffects("land");
                if (pc.OnWater) ChangeState(StateIdle);
                break;
            case StateFall:
                // StateFall::vfunc_4 (statefall.cpp:93-130): on water and on the ground it lands at once, else the fall
                // sequence. STILL-OPEN: the set name prefix of the fall sequence (CEquipment::GetSetName).
                if (pc.OnWater && pc.OnGround) { ChangeState(StateLand); return; }
                pc.PlayAction(ActJumpFall, 1f, false);
                break;
            case StateJump:
                JumpEnter();
                break;
            case StateSwim:
                // StateSwim::vfunc_4 (stateswim.cpp:89-129): with no ridee (FUN_1417fb370 is 0) sub 0x84 at the swim speed
                subs[s] = 0x84;
                Speed = pc.SwimSpeed;
                break;
        }
    }

    void Exit(int s, int next)
    {
        switch (s)
        {
            case StateWalk:
                // StateWalk::vfunc_5 (statewalk.cpp:157-163)
                subs[s] = 0x18;
                Speed = pc.RunSpeed;
                break;
            case StateJump:
                Speed = jumpSavedSpeed;                       // StateJump::vfunc_5 (statejump.cpp:148-158)
                break;
            case StateSwim:
                Speed = pc.RunSpeed;                          // StateSwim::vfunc_5 (stateswim.cpp:132-138)
                break;
        }
    }

    /// <summary>The current state's vfunc_3, then PCBase::vfunc_68's turn toward the target yaw for the states that move.</summary>
    void StateMove(float dt)
    {
        switch (Cur)
        {
            case StateIdle:
            case StateLand:
                // StateIdle::vfunc_3 / StateLand::vfunc_3: vfunc_68 with a zero move
                MoveBy(Vector3.Zero, dt);
                break;
            case StateWalk:
            {
                // StateWalk::vfunc_3 (statewalk.cpp:166-211): m_unk_158 is 0 (vfunc_63), the speed by the sub, then
                // the direction times the speed as the controller's velocity and the frame's move
                WalkSpeedBySub();
                var v = MoveDirection() * Speed;
                pc.SetVelocity(v);
                MoveBy(v * dt, dt);
                break;
            }
            case StateSwim:
            {
                // StateSwim::vfunc_3 (stateswim.cpp:141-174)
                Speed = pc.SwimSpeed;
                var v = MoveDirection() * Speed;
                pc.SetVelocity(v);
                MoveBy(v * dt, dt);
                break;
            }
            case StateFall:
            {
                // StateFall::vfunc_3 (statefall.cpp:147-160): an air velocity while there is a direction, a zero move
                var d = MoveDirection();
                if (d != Vector3.Zero) pc.SetVelocity(d * (Speed * pc.AirSpeedScale));
                MoveBy(Vector3.Zero, dt);
                break;
            }
            case StateJump:
                JumpMove(dt);
                break;
        }
    }

    void MoveBy(Vector3 move, float dt)
    {
        pc.Move(move, dt);
        TurnTowardTargetYaw(dt);                     // pcbase.cpp:1863-1868 (no local target camera for a remote)
    }

    /// <summary>The current state's vfunc_9.</summary>
    void StateFrame(float dt)
    {
        switch (Cur)
        {
            case StateIdle: IdleFrame(); break;
            case StateWalk: WalkFrame(); break;
            case StateLand: LandFrame(); break;
            case StateFall:
                // StateFall::vfunc_9 (statefall.cpp:193-226): touching down lands (the chair test is MyPC's)
                if (pc.OnGround) ChangeState(StateLand);
                break;
            case StateJump: JumpFrame(); break;
            case StateSwim:
                // StateSwim::vfunc_9 (stateswim.cpp:228-263), no ridee: the swim sequence. STILL-OPEN: the costume's swim
                // name (PC::GetSwimAnimName) and the vfunc_10 rate.
                if (!pc.IsAction(ActWaterSwim)) pc.PlayAction(ActWaterSwim, 1f, false);
                break;
        }
    }

    /// <summary>
    /// StateIdle::vfunc_9 (stateidle.cpp:294-455): a PC not on water whose fall timer runs falls; the water sub is set by
    /// MyPC's branch only, so a remote keeps the sub it entered with or a record gave. Sub 1 plays the stand (the battle
    /// idle in the battle stance), 7 the water idle, 6 the talk idle.
    /// STILL-OPEN: the bore subs 2-5 (FUN_141679170), 8 / 9 (cube modes), 0xa-0x16 and the equip-change subs.
    /// </summary>
    void IdleFrame()
    {
        if (!pc.OnWater && pc.InAirFor(0)) { ChangeState(StateFall); return; }
        switch (subs[StateIdle])
        {
            case 1:
            {
                int key = pc.BattleStance ? ActBattleIdle : ActIdle;     // PlayStandAnimation (stateidle.cpp:518-531)
                if (!pc.IsAction(key)) pc.PlayAction(key, 1f, false);
                break;
            }
            case 7:
                // PlayBoreWaitAnimation (stateidle.cpp:573-597). STILL-OPEN: PC::GetSwimAnimName's costume name.
                if (!pc.IsAction(ActWaterIdle)) pc.PlayAction(ActWaterIdle, 1f, false);
                break;
            case 6:
                if (!pc.IsAction(ActTalkIdle)) pc.PlayAction(ActTalkIdle, 1f, false);   // FUN_1417ce9f0 (stateidle.cpp:555-561)
                break;
        }
    }

    /// <summary>StateWalk's speed by its sub (statewalk.cpp:171-204): 0x19 walk, 0x18 / 0x17 run, 0x1a 15 m/s.</summary>
    void WalkSpeedBySub()
    {
        switch (subs[StateWalk])
        {
            case 0x19: Speed = pc.WalkSpeed; break;
            case 0x18: case 0x17: Speed = pc.RunSpeed; break;
            case 0x1a: Speed = 1500f; break;
        }
    }

    void WalkGait()
    {
        pc.Gait(Speed, pc.RunSpeed, pc.WalkSpeed, out bool run);
        walkRun = run ? 1 : 0;
    }

    /// <summary>
    /// StateWalk::vfunc_9 (statewalk.cpp:322-373): water goes to StateSwim; a fall timer past sqrt(3 / |g|) s falls; else
    /// the run (0x1c) or, at the walk sub or a walking gait, the walk (0x1d) sequence, again when the speed changed.
    /// STILL-OPEN: the cube guide sequences (0x38 / 0x39) and the ride's 0x1f.
    /// </summary>
    void WalkFrame()
    {
        if (pc.OnWater) { ChangeState(StateSwim); return; }
        int delay = Ms(MathF.Sqrt(3f / MathF.Abs(pc.Gravity / 100f)));
        if (pc.InAirFor(delay)) { ChangeState(StateFall); return; }
        if (subs[StateWalk] == 0x19) walkRun = 0;
        int key = walkRun == 0 ? ActWalk : ActRun;
        bool changed = !(Speed == walkSpeed1 && pc.RunSpeed == walkSpeed2 && walkChanged == 0);
        if (!pc.IsAction(key) || changed)
        {
            float rate = pc.Gait(Speed, pc.RunSpeed, pc.WalkSpeed, out bool run);
            walkRun = run ? 1 : 0;
            walkSpeed1 = Speed;
            walkSpeed2 = pc.RunSpeed;
            pc.PlayAction(key, rate, changed);
        }
    }

    /// <summary>
    /// StateLand::vfunc_9 (stateland.cpp:127-162): the landing sequence (JUMP_LAND), or StateIdle when the body has none;
    /// StateLand::EndOfSequence (stateland.cpp:165-186) goes to StateIdle when it ends (<see cref="SequenceEnded"/>).
    /// STILL-OPEN: StateLandData's own sequence name and the cube guide's 0x3c.
    /// </summary>
    void LandFrame()
    {
        if (!pc.HasAction(ActJumpLand)) { ChangeState(StateIdle); return; }
        if (!pc.IsAction(ActJumpLand)) pc.PlayAction(ActJumpLand, 1f, false);
    }

    /// <summary>NiActorManager's end of sequence (PCBase -> CStateManager::OnEndOfSequence -> the state's EndOfSequence).</summary>
    public void SequenceEnded(int action)
    {
        if (Cur == StateLand && action == ActJumpLand) ChangeState(StateIdle);   // stateland.cpp:165-186
    }

    /// <summary>
    /// StateJump::vfunc_4 (statejump.cpp:95-130): the sub (0x21 when none, a random variant of 0x25), the move direction,
    /// the speed it restores on exit, whether there was a direction (FUN_1416749c0), then StopJump and StartJump.
    /// </summary>
    void JumpEnter()
    {
        int sub = subs[StateJump];
        if (sub == 0) sub = 0x21;
        else if (sub == 0x25)
        {
            jumpToggle = (jumpToggle + 1) % 2;
            sub = rng.Next(2) != 0 ? JumpSubs[2 + jumpToggle] : JumpSubs[jumpToggle];
        }
        subs[StateJump] = sub;
        jumpDir = MoveDirection();
        jumpFirstSteer = 1;
        jumpSavedSpeed = Speed;
        jumpHadInput = MoveInput != Vector2.Zero;
        pc.StopJump();
        pc.StartJump(pc.JumpHeight, jumpDir, Speed);
    }

    /// <summary>
    /// StateJump::vfunc_3 (statejump.cpp:175-214): while the controller jumps, a first direction after a jump from standing
    /// sets the speed to 1 m/s; the air velocity is the direction times the speed and the scale; once the jump has ended
    /// the PC lands. STILL-OPEN: the jump-to target (FUN_14166dbb0, m_pos920).
    /// </summary>
    void JumpMove(float dt)
    {
        if (!pc.Jumping) { ChangeState(StateLand); return; }
        var world = MoveDirection();
        if (!jumpHadInput && jumpFirstSteer != 0 && world != Vector3.Zero)
        {
            jumpDir = world;
            jumpFirstSteer = 0;
            Speed = 100f;                            // FUN_141671960(1.0f)
        }
        else if (world != Vector3.Zero) jumpDir = world;
        pc.SetVelocity(jumpDir * Speed * pc.AirSpeedScale);
        MoveBy(Vector3.Zero, dt);
    }

    /// <summary>
    /// StateJump::vfunc_9 (statejump.cpp:273-341): on the ground it lands; past sqrt(12 / |g|) s plus the apex time of
    /// falling the fall sequence; else rising the jump up (the special one for subs 0x22 / 0x24 when the body has it) and
    /// coming down the jump down. STILL-OPEN: the cube guide's 0x3a / 0x3b and the stunt subs' 0x46 / 0x47.
    /// </summary>
    void JumpFrame()
    {
        int limit = Ms(MathF.Sqrt(12f / MathF.Abs(pc.Gravity / 100f))) + Ms(pc.JumpApexTime);
        if (pc.OnGround) { ChangeState(StateLand); return; }
        if (limit <= pc.FallTimeMs) { pc.PlayAction(ActJumpFall, 1f, false); return; }
        int sub = subs[StateJump];
        if (sub is not (0x21 or 0x22 or 0x23 or 0x24)) return;
        bool special = sub is 0x22 or 0x24;
        int anim = pc.Rising
            ? (special && pc.HasAction(ActSpecialJumpUp) ? ActSpecialJumpUp : ActJumpUp)
            : (special && pc.HasAction(ActSpecialJumpDown) ? ActSpecialJumpDown : ActJumpDown);
        if (!pc.IsAction(anim)) pc.PlayAction(anim, 1f, false);
    }

    /// <summary>FUN_14028d260: seconds to whole milliseconds (truncated).</summary>
    static int Ms(float s) => (int)(s * 1000f);

    // ---- direction and turn ----------------------------------------------------------------------------------------

    /// <summary>
    /// PCBase::CalcMoveDirection(m_pos98) (@0x141674a10, pcbase.cpp:2828-2840) with the axes of PCBase::vfunc_67
    /// (@0x14166cf40, pcbase.cpp:1310-1340): camera mode 0xe takes the body's facing; else column 0 of
    /// FromEulerAnglesXYZ(the sender's camera rotation), flattened (components under 0.001 dropped) and unit length; the
    /// second axis is -(first x Z); the result is y times the first plus x times the second, unit length.
    /// STILL-OPEN: camera mode 0x11's fixed axes (CameraMgr FUN_140af0210).
    /// </summary>
    public Vector3 MoveDirection()
    {
        var input = MoveInput;
        if (input == Vector2.Zero) return Vector3.Zero;
        Vector3 a;
        if (CameraMode == 0xe)
        {
            // -UNIT_Y * MakeZRotation(z): the row vector times Gamebryo's (transposed) rotation, (sin z, -cos z, 0)
            float z = pc.RotationZ * 3.1415927f / 180f;
            a = new Vector3(MathF.Sin(z), -MathF.Cos(z), 0);
        }
        else
        {
            const float r = 3.1415927f / 180f;
            float cx = MathF.Cos(CameraEuler.X * r), sx = MathF.Sin(CameraEuler.X * r);
            float cy = MathF.Cos(CameraEuler.Y * r), sy = MathF.Sin(CameraEuler.Y * r);
            float cz = MathF.Cos(CameraEuler.Z * r), sz = MathF.Sin(CameraEuler.Z * r);
            // column 0 of MakeXRotation(x) * MakeYRotation(y) * MakeZRotation(z) (Gamebryo's transposed rotations)
            a = new Vector3(cy * cz, sx * sy * cz - cx * sz, 0);
            if (MathF.Abs(a.X) < 0.001f) a.X = 0;
            if (MathF.Abs(a.Y) < 0.001f) a.Y = 0;
            a = Unit(a);
        }
        var b = -Vector3.Cross(a, Vector3.UnitZ);
        return Unit(input.Y * a + input.X * b);
    }

    static Vector3 Unit(Vector3 v)
    {
        float l = v.Length();
        return l > 1e-6f ? v / l : Vector3.Zero;    // efd::Point3::Unitize leaves a zero vector at zero
    }

    /// <summary>PCBase::TurnTowardTargetYaw (@0x14166d630, pcbase.cpp:1390-1414): the body turns toward the target yaw at
    /// the turn speed, the short way, and stops on it; the result is normalised (FUN_140291bc0).</summary>
    void TurnTowardTargetYaw(float dt)
    {
        float z = pc.RotationZ;
        if (z == TargetYaw) return;
        float target = Normalize(TargetYaw);
        TargetYaw = target;
        float step = pc.TurnSpeed * dt;
        if (z < target)
        {
            if (target - z > 180f) z -= step; else z += step;
            if (z > target) z = target;
        }
        else if (z > target)
        {
            if (z - target > 180f) z += step; else z -= step;
            if (z < target) z = target;
        }
        pc.RotationZ = Normalize(z);
    }

    /// <summary>NormalizeDegrees: into [0, 360).</summary>
    static float Normalize(float d)
    {
        while (d < 0f) d += 360f;
        while (d >= 360f) d -= 360f;
        return d;
    }
}
