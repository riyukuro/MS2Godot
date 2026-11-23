using System.Numerics;

namespace Ms2.Net.Replay;

/// <summary>
/// The body side of a remote PC that its state machine drives (PCBase / GameObjectController, MS2 axes, game units: 1 =
/// 1 cm; the client's PhysX speeds are metres per second, x100 here).
/// </summary>
public interface IRemotePc : ISkillMotionOwner
{
    /// <summary>vfunc_18 / vfunc_19: the feet position.</summary>
    new Vector3 Position { get; set; }
    /// <summary>vfunc_20().z / vfunc_21: the body's Z rotation in degrees.</summary>
    float RotationZ { get; set; }
    /// <summary>FUN_141671a30: the controller stands on something (its last move touched down).</summary>
    bool OnGround { get; }
    /// <summary>FUN_141675ed0: the entity in the controller's hit report is water (kind 8).</summary>
    bool OnWater { get; }
    /// <summary>GameObjectController::IsRising / GetJumpApexTime (s) / GetFallTime (ms); vfunc_99 and IsJumping are
    /// <see cref="ISkillMotionOwner"/>'s.</summary>
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
    /// <summary>StateSwim::FUN_1417fb370 (stateswim.cpp:304-310): a ridee that runs on water (Ridee::FUN_1417075e0), which
    /// swims at its row's run speed (sub 0x84); false for a PC.</summary>
    bool WaterRunner { get; }
    /// <summary>The row's run speed m_unk_180.m_z (ridee.cpp:431) without GetRunSpeed's ability rate: sub 0x84 swims at it
    /// (stateswim.cpp:147-153); a PC's is its run speed.</summary>
    float RowRunSpeed { get; }
    /// <summary>PCBase::vfunc_96 (@0x141671680, pcbase.cpp:2269-2275): move-speed stat rate (CalcMsiR, 1 at base).</summary>
    float MoveSpeedRate { get; }
    /// <summary>PCBase::vfunc_97 (@0x141671720, pcbase.cpp:2504-2519): scale animation rate (m_fScaleA8 / m_fScaleAC, 1 when equal).</summary>
    float ScaleAnimRate { get; }
    /// <summary>PCBase::m_f938: read by FUN_1416713c0 (@0x1416713c0, pcbase.cpp:2037-2041) and set by FUN_141670d80
    /// (@0x141670d80, pcbase.cpp:1995-1999); starts at 1f (pcbase.cpp:787) and is updated by State::vfunc_10.</summary>
    float AnimSpeed { get; set; }
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
    /// <summary>What StateHold reads of the held actor: WorldService::FUN_141bd1200(<paramref name="id"/>) and, on it,
    /// FUN_1416757c0(<paramref name="node"/>) (the node by name in its model).</summary>
    HoldTarget FindHoldTarget(int id, string node);
    /// <summary>PlayAnimation(name, rate, false, false): a sequence by its name; nothing when the sequence is playing or the
    /// body has no such sequence (false).</summary>
    bool PlaySequence(string name, float rate);
    /// <summary>SetTargetAnimation(seq, speed, true) (a forced restart: NiActorManager::Reset first), Update and
    /// SetOffset: the cast's sequence again from <paramref name="time"/> (s); the next frame throws its text keys from 0
    /// to the new time.</summary>
    void SeekCast(float time);
    /// <summary>GetPushControl()-&gt;m_pos18 != ZERO (FUN_1417dc4c0, statemanager.cpp:600): a push moves the PC, so a record
    /// does not correct its position.</summary>
    bool Pushed { get; }
}

/// <summary>
/// What the motion controller of a cast reads: the motion's SkillMotionData (movetype m_eMoveType, keyMove m_fAC,
/// movedistance m_fB8, moveangle), the skill's stateAttr moveType (m_nMoveType) and movingInMovekey, magicControl's
/// enable != 0, and the record's m_kStart (the SkillUse / SkillSync packet's direction, MS2 axes).
/// </summary>
public readonly record struct CastMove(int MotionMoveType, float KeyMove, float MoveDistance, float MoveAngle,
    int SkillMoveType, bool MovingInMovekey, bool MagicControl, Vector3 Direction);

/// <summary>The held actor's class as StateHold tests it (GetClassID).</summary>
public enum HoldKind
{
    /// <summary>No actor of the id.</summary>
    None,
    /// <summary>A PC (0x6200003 / 0x6200004): the hold is a seat on its mount.</summary>
    Pc,
    /// <summary>An npc (0x6200028).</summary>
    Npc,
    /// <summary>A ridee (0x6200045).</summary>
    Ridee,
    /// <summary>Any other actor.</summary>
    Other,
}

/// <summary>The held actor and its node (<see cref="IRemotePc.FindHoldTarget"/>).</summary>
public struct HoldTarget
{
    public HoldKind Kind;
    /// <summary>CNpc::GetNpcId of an npc.</summary>
    public int NpcId;
    /// <summary>IsDead.</summary>
    public bool Dead;
    /// <summary>The actor has the node.</summary>
    public bool HasNode;
    /// <summary>The node's world translation, MS2 axes.</summary>
    public Vector3 NodePosition;
    /// <summary>The node's world rotation as rows 0 of its matrix (m[0][0], m[0][1], m[0][2]), MS2 axes: what
    /// NiMatrix3::ToEulerAnglesXYZ reads for the y and z angles.</summary>
    public Vector3 NodeRow0;
    /// <summary>A PC target has a ridee (GetRidee, StateHold::FUN_1417cd060).</summary>
    public bool HasRidee;
    /// <summary>With <see cref="HasRidee"/>: the ridee's feet z (vfunc_18().z), MS2 axes.</summary>
    public float RideeZ;
    /// <summary>A PC target's ridee has the asker's seat: GetRidee, its CRideeData, and GetPassenger(FUN_141676790(the asker's
    /// object id)) all set.</summary>
    public bool HasSeat;
    /// <summary>With <see cref="HasSeat"/> and <see cref="HasNode"/>: Ridee::FUN_1417083a0's position for the asker on the
    /// node with the seat's translation, MS2 axes.</summary>
    public Vector3 SeatPosition;
    /// <summary>With <see cref="HasSeat"/> and <see cref="HasNode"/>: row 0 of the node's world rotation times the seat's
    /// EulerToMatrix(rotation), MS2 axes (what FUN_140a0b1f0 reads for the y and z angles).</summary>
    public Vector3 SeatRow0;
    /// <summary>With <see cref="HasSeat"/>: the ridee's GetCurStateId, FUN_14166cc80 (its sub), FUN_1416713c0 (its animation
    /// speed) and FUN_141676b70 (m_unk_b80 set, which only the own ridee's UpdateRunEffects sets, so false for a remote
    /// ridee: see RemoteLayer.Ride).</summary>
    public int RideeState, RideeSub;
    public float RideeAnimSpeed;
    public bool RideeSpIdle;
    /// <summary>With <see cref="HasSeat"/>: the seat's rider sequences.</summary>
    public RiderSequences Seat;
}

/// <summary>
/// A remote PC's CStateManager (statemanager.cpp) with the states that move it: the queue of sync records
/// (FUN_1417db2e0, FUN_1417dc230), the record's application (FUN_1417db550, FUN_1417dc4c0, FUN_1417dbd00), ChangeState
/// and, per frame, Update (statemanager.cpp:220-276) running the current state's vfunc_3 (move) and vfunc_9 (animation
/// and the transitions the PC takes by itself: a fall, a landing, the end of the landing). The input states' vfunc_8
/// runs for MyPC only (mypc.cpp:2966), so a remote PC changes state by its records and by its own vfunc_9 tests.
/// Ported states: StateIdle (1), StateWalk (2), StateLand (4), StateFall (5), StateJump (6), StateSwim (0x1b), and StateSkill
/// (0x10) as far as the queue and the next state go (<see cref="Cast"/>, <see cref="CastSequenceEnded"/>, <see cref="EndSkill"/>), and StateHold (0x20, on
/// the held actor's node: <see cref="Hold"/>).
/// STILL-OPEN: every other registered state (pc.cpp:668-721: crawl, ladder, rope, emotion, dash, climb, glide, swim
/// dash, ...) changes the id here and runs no update of its own; the records still place the PC (FUN_1417dbd00 snaps for
/// those states at once). A record's StateData (FUN_1417dc9a0 / vfunc_27, read by its vfunc_2) is read for StateHold only. The push
/// control (m_pushControl, statemanager.cpp:591), the marker the sender stood on (m_str88, FUN_1417dc4c0
/// statemanager.cpp:606-620), the cube cell (FUN_141678300) and the fixed Z rotation of PCBase::vfunc_21
/// (AdditionalCliList::TryGetFixedZRotation) are not ported. FUN_141676b70 (m_unk_b80, the ride/stunt idle and walk subs
/// 0xc / 0x17 / 0x27 / 0x87) is taken as 0. The viewer's camera local-target mode (CameraMgr::IsLocalTargetMode,
/// pcbase.cpp:1864-1869, which skips TurnTowardTargetYaw for a non-target PC while the viewer's camera is in local-target
/// mode) is not ported yet.
/// </summary>
public sealed class RemotePcStates
{
    public const int StateNone = 0, StateIdle = 1, StateWalk = 2, StateLand = 4, StateFall = 5, StateJump = 6,
        StateSkill = 0x10, StateHold = 0x20, StateRide = 0x21, StateSwim = 0x1b, StateDead = 0xc, StateHit = 0xd,
        StateStun = 0x12;

    // action ids (ActionType, actioncontainer.cpp) the ported states play through vfunc_123
    public const int ActIdle = 0, ActBattleIdle = 1, ActJumpDown = 2, ActJumpLand = 3, ActJumpUp = 4, ActJumpFall = 5,
        ActRun = 0x1c, ActWalk = 0x1d, ActTalkIdle = 0x2e, ActSpecialJumpUp = 0x2a, ActSpecialJumpDown = 0x2b,
        ActWaterIdle = 0x30, ActWaterSwim = 0x31, ActHoldIdle = 0x3e;

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
    /// <summary>m_n48 (StateWalk's run flag, set by vfunc_4 and vfunc_9).</summary>
    public bool WalkRun => walkRun != 0;
    /// <summary>Called when the state, its sub state or the walk's run flag changed: the three inputs of State::vfunc_15 (the
    /// weapon-hold answer; WeaponStance.StateHoldsWeapons in Ms2.Ui). Not per frame.</summary>
    public Action WeaponKeyChanged;
    (int cur, int sub, bool run) weaponKey;

    /// <summary>StateDead::vfunc_4 past State::vfunc_4 (statedead.cpp:107-146; FUN_1417b8370, DynamicCast&lt;PC&gt;, holds for
    /// any PC): the branch test (a PvP point, or the field's HasAutoRevivalType1: the revive in place) and the body part:
    /// the revive in place shows the body (vfunc_23(0)) and plays MODEPVP_DEAD's entry, the normal death hides it
    /// (vfunc_23(1)), plays DEAD (0x34) and builds the tomb (PC::FUN_14165cac0). The own-player part (FUN_1417b84b0 is MyPC
    /// only: the cube request, the push reset, the death or revive effect, the guide trigger) is not run for this PC.</summary>
    public Action DeadEntered;
    /// <summary>StateDead::vfunc_5 (@0x1417c29b0, statedead.cpp:177-186): the body shown again (vfunc_23(0)) and the shadow
    /// back on (vfunc_31(true)).</summary>
    public Action DeadLeft;
    /// <summary>GameObject::SetShadowVisible: StateRide::vfunc_4 turns the PC's simple shadow off and vfunc_5 back on
    /// (statemanager.cpp:1095, :1103).</summary>
    public Action<bool> ShadowVisible;
    /// <summary>StateSkill::vfunc_4's m_spRecord: called as the skill state is entered, before its entry runs.</summary>
    public Action SkillEntered;
    /// <summary>StateSkill::vfunc_5 (stateskill.cpp:512-535): called as the skill state is left, before its exit runs.</summary>
    public Action SkillExited;
    /// <summary>FUN_1417ecce0 for each attack FireAttacksAtPoint fires (the attack's index in the motion).</summary>
    public Action<int> AttackFired;
    /// <summary>A tag effect of the cast's motion plays on the caster (StateSkill::TextKeyEvent, stateskill.cpp:792-834): the
    /// key and the effect xml (<see cref="CastTextKey"/>).</summary>
    public Action<string, string> TagEffect;
    /// <summary>StateHit::vfunc_4 past State::vfunc_4 (@0x1417ca370, statehit.cpp:65-114): the hit's data, the lift and the
    /// sequence (the host's StateHit body, which keeps the data the reaction handed it).</summary>
    public Action HitEntered;
    /// <summary>StateHit::vfunc_3 (@0x1417ca8a0, statehit.cpp:116-130) after its vfunc_68(ZERO): the end time's down sequence
    /// or StateIdle, with the frame's seconds.</summary>
    public Action<float> HitUpdate;
    /// <summary>StateHit::vfunc_9 (@0x1417caa80, statehit.cpp:148-152): StateIdle when the state plays no sequence.</summary>
    public Action HitFrame;
    /// <summary>StateHit::vfunc_5 (@0x1417caa10, statehit.cpp:135-145): the lift ends.</summary>
    public Action HitLeft;
    /// <summary>StateHit m_bC8: false from the end of vfunc_4 (statehit.cpp:114) until vfunc_5 sets it (:144).</summary>
    bool hitTakesRecords = true;

    /// <summary>
    /// The current state's vfunc_32, whether it takes a record's state (FUN_1417dc4c0, statemanager.cpp:622): State's is
    /// true (state.h:97), StateStun's false (statestun.h:29), StateHit's its m_bC8 (@0x1417cb980, statehit.cpp:349), so a
    /// PC reacting to a hit keeps its own StateHit until that ends.
    /// STILL-OPEN: StateHit::vfunc_33(true) from CSceneStateGame::OnStateSkill's mode 6 (scenestategame.cpp:3493), which
    /// lets the hit take records again, is not ported (the packet is not read).
    /// </summary>
    public bool TakesRecords => Cur == StateHit ? hitTakesRecords : Cur != StateStun;

    void NotifyWeaponKey()
    {
        var key = (Cur, Sub, WalkRun);
        if (key == weaponKey) return;
        weaponKey = key;
        WeaponKeyChanged?.Invoke();
    }
    /// <summary>The queued records (for tests).</summary>
    public int Queued => requests.Count;
    /// <summary>The cast's record (PCBase::vfunc_103, the CSkillRecord): its skill id (GetSkillId) and its skill's
    /// m_blockA4.m_bChargingSkill, which <see cref="Apply"/> tests in StateSkill. StateSkill's m_b111 is the same flag
    /// (stateskill.cpp:766, m_spSkill->m_blockA4): it belongs to the skill, not to a level, as the table's flag does.
    public int SkillId { get; private set; }
    public bool ChargingSkill { get; private set; }
    Vector3 castPos;
    // the record's rotation z (m_kRot.z) and speed (m_fSpeed) live in the motion
    float castRotZ { get => skillMotion.RotZ; set => skillMotion.RotZ = value; }
    float castSpeed => skillMotion.Speed;
    // the cast record's m_nB0 / m_strB8 (the SkillUse packet's extra): the actor it holds after the cast
    int castHoldId; string castHoldNode = "";
    // the record's m_nMotion and its level's motion count (CSkillRecord::FUN_141759e40), and the time since
    // EndOfSequence marked an earlier motion's end (m_b48, m_fStartTime), -1 while it has not
    int castMotion, castMotionCount = 1; float castEndAge = -1f;
    // the loop of a motion (StateSkill m_nRepeat, m_bCharging, m_bLoop, m_nAttack, m_fLoopStart, m_fChargeTime != 0,
    // m_fSpeed) and the time since its "loopstart" (GetTickCountMs2() - m_n138, ms); the motion's loopCount and attack points
    int castRepeat, castAttack, castLoopCount; bool castCharging, castLoop, castLoopEnded;
    bool castInLoop;   // m_b117: inside the loop (a "loopstart" passed, no "loopend" since or a next round's)
    IReadOnlyDictionary<string, string> castTags;   // the motion's m_mapTagEffects
    float castLoopStart; double castLoopMs;
    IReadOnlyList<string> castPoints = System.Array.Empty<string>();
    // the record's motion data and move time, the move phases and the motion controller (shared with the own PC)
    readonly SkillMotion skillMotion = new();
    /// <summary>StateSkill::m_nRepeat (for tests): the loops left before the "loopend" key stops repeating.</summary>
    public int CastRepeat => castRepeat;

    /// <summary>StateHold's StateHoldData (m_spHold): m_nTargetId and m_strTargetName, the held actor and its node.</summary>
    public int HoldTargetId { get; private set; }
    public string HoldNode { get; private set; } = "";
    // the data the next entry of StateHold takes (vfunc_59's pData), and StateHold::m_kDir (the rotation before the hold)
    int pendingHoldId; string pendingHoldNode = ""; float holdDir;

    // StateWalk (statewalk.h:29-33: m_fSpeed1/m_fSpeed2 store vfunc_96/vfunc_97; m_n50 is only set by MyPC's vfunc_8)
    float walkSpeed1, walkSpeed2; int walkRun = 1; readonly int walkChanged = 0;
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

    // ---- the dead flag -----------------------------------------------------------------------------------------------

    /// <summary>
    /// PCBase m_unk_15c: PCBase::IsDead (@0x14166ce30, pcbase.cpp:1256), set by FUN_14166ce50 (pcbase.cpp:1262).
    /// StateSkill::PlayCastStartEffect returns at once for a dead owner (stateskill.cpp:1822).
    /// </summary>
    public bool Dead { get; private set; }

    /// <summary>
    /// OnAddUser (scenestategame.cpp:11185-11186): after the stats (vfunc_91), a PC with no current health
    /// (FUN_141670a00(4) == 0) takes FUN_14166ce50(1); OnRevival (scenestategame.cpp:7398) clears it for any actor.
    /// </summary>
    public void SetDead(bool dead) => Dead = dead;

    /// <summary>PCBase m_spB10: the state machine of the ridee this PC rides (set by PCBase::vfunc_120, cleared by
    /// vfunc_121 before its state change), null when not riding.</summary>
    public RemotePcStates Ridee { get; private set; }

    /// <summary>
    /// PC::vfunc_124 (@0x141653840, pc.cpp:1198-1205): a state goes through when the PC is not dead and has health
    /// <paramref name="hp"/> (or no max health <paramref name="hpMax"/>, FUN_141670a30(4)), or for the death 0xc, revival
    /// 0x18 and observer 0x3f states.
    /// </summary>
    public bool Allows(int state, long hp, long hpMax) =>
        (!Dead && (hpMax <= 0 || hp != 0)) || state == 0xc || state == 0x18 || state == 0x3f;

    /// <summary>
    /// PC::vfunc_59 (@0x1416537d0, pc.cpp:1189-1194): <see cref="Allows"/> (PC::vfunc_124), then PCBase::vfunc_59
    /// (pcbase.cpp:1097-1111): a PC in StateRide (0x21) with a ridee (m_spB10) passes the state to the ridee's vfunc_59 and
    /// keeps its own; else ChangeState. The PC is taken as loaded (m_unk_b4).
    /// </summary>
    public void ServerState(int state, long hp, long hpMax)
    {
        if (!Allows(state, hp, hpMax)) return;
        if (Cur == StateRide && Ridee != null) Ridee.ChangeState(state);
        else ChangeState(state);
    }

    /// <summary>PCBase::vfunc_120 (pcbase.cpp:3289-3343): m_spB10 = the ridee, then Ridee::vfunc_10 (ridee.cpp:461) runs the
    /// rider's vfunc_59(0x21) (<see cref="ServerState"/>), which a dead or 0-health PC refuses.</summary>
    public void RideOn(RemotePcStates ridee, long hp, long hpMax)
    {
        Ridee = ridee;
        ServerState(StateRide, hp, hpMax);
    }

    /// <summary>PCBase::vfunc_121 (pcbase.cpp:3405-3415): m_spB10 = 0, then vfunc_59(1) when the ridee stood on the ground,
    /// else vfunc_59(5) (<see cref="ServerState"/>).</summary>
    public void RideOff(bool ground, long hp, long hpMax)
    {
        Ridee = null;
        ServerState(ground ? StateIdle : StateFall, hp, hpMax);
    }

    /// <summary>
    /// CSceneStateGame::OnUserState (@0x141721940, scenestategame.cpp:5759-5771): vfunc_59(state); then state 12 runs
    /// vfunc_137 (<see cref="Die"/>) and any other state runs vfunc_59(state) again. A PC that took state 12 is already
    /// in it when vfunc_137 runs, so its dead flag is not set by it. Returns whether the dead flag was set.
    /// </summary>
    public bool UserState(int state, long hp, long hpMax)
    {
        ServerState(state, hp, hpMax);
        if (state == 12) return Die(hp);
        ServerState(state, hp, hpMax);
        return false;
    }

    /// <summary>
    /// PC::vfunc_137 (pc.cpp:4593-4607; OnUserState state 12 and OnDeadUser, scenestategame.cpp:6533-6544): not in the
    /// death state (0xc) and with no current health <paramref name="hp"/>: FUN_14166ce50(1). Returns whether it ran.
    /// m_n10f0 (set when a confirm count is kept) is the owner's (RemoteLayer). STILL-OPEN: m_nFF4 and the PvP time scale
    /// (IsMode1 / IsMode10).
    /// </summary>
    public bool Die(long hp)
    {
        if (Cur == 0xc || hp != 0) return false;
        Dead = true;
        return true;
    }

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
    /// In StateSkill (statemanager.cpp:403-421) the record is applied only when it names another skill (m_n64 not 0 and
    /// not the cast's id) or when it is not a skill record and the cast's skill is a charging skill; else it is dropped.
    /// </summary>
    void Apply(SyncRecord r)
    {
        MoveInput = r.Move;                          // vfunc_22(m_vel3c)
        CameraEuler = r.CameraEuler;                 // FUN_141678160(m_pos48)
        CameraMode = r.CameraMode;                   // FUN_1416781a0(m_n54)
        bool apply = Cur != StateSkill && r.State != StateSkill && r.State != StateLand;
        if (!apply && Cur == StateSkill)
        {
            if (r.SkillId != 0 && r.SkillId != SkillId) apply = true;
            if (r.State != StateSkill && ChargingSkill) apply = true;
        }
        if (apply) ApplyPc(r);
    }

    // ---- the skill state ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A cast with a sequence: OnSkillUse's record (CSkillRecord::Init, PCBase::vfunc_100 makes it vfunc_103's) then
    /// StateSkill::PlayCastStartEffect (@0x1417ea750, stateskill.cpp:1820-1828) enters StateSkill forced
    /// (vfunc_59(0x10, NULL, 0, 1)): ChangeState flushes the queue (its records tested against the new record when the PC
    /// is already in StateSkill), and a cast in the state leaves and enters it again. <paramref name="pos"/> (MS2 axes)
    /// and <paramref name="rotZ"/> (degrees) are the record's m_ptPos and m_ptRot.z, which StateSkill::vfunc_4 takes.
    /// </summary>
    /// <paramref name="holdId"/> and <paramref name="holdNode"/> are the record's m_nB0 / m_strB8 (OnSkillUse,
    /// scenestategame.cpp:3333-3340, from the packet's extra): the actor and node the cast's end holds.
    /// <paramref name="motion"/> and <paramref name="motionCount"/> are the record's m_nMotion and its level's
    /// GetMotionCount, which <see cref="CastSequenceEnded"/> compares.
    /// <paramref name="speed"/> is GetMotionSpeedScale (the record's speed, StateSkill::m_fSpeed); <paramref name="loopCount"/>
    /// and <paramref name="attackPoints"/> are the motion's SkillMotionData::m_nLoopCount and its attacks' m_strPoint.
    /// <paramref name="move"/> is what the motion controller reads (<see cref="CastMove"/>); <paramref name="tagEffects"/> the
    /// motion's tag effects (<see cref="CastTextKey"/>). The new record
    /// (CSkillRecord's constructor, skillrecord.cpp:164) starts at m_fDuration = m_fElapsed = 0.
    public void Cast(int skillId, bool chargingSkill, Vector3 pos, float rotZ, int holdId = 0, string holdNode = "",
        int motion = 0, int motionCount = 1, float speed = 1f, int loopCount = 0, IReadOnlyList<string> attackPoints = null,
        CastMove move = default, IReadOnlyDictionary<string, string> tagEffects = null)
    {
        skillMotion.Record(move, speed, rotZ);
        castTags = tagEffects;
        castInLoop = false;   // vfunc_4 (stateskill.cpp:411) and the sequence start (FUN_1417f0940 :2223)
        castLoopCount = loopCount;
        castPoints = attackPoints ?? System.Array.Empty<string>();
        castMotion = motion;
        castMotionCount = motionCount;
        SkillId = skillId;
        ChargingSkill = chargingSkill;
        castPos = pos;
        castRotZ = rotZ;
        castHoldId = holdId;
        castHoldNode = holdNode ?? "";
        ChangeState(StateSkill, 0, true);
    }

    /// <summary>
    /// The cast's sequence ended: StateSkill::EndOfSequence (@0x1417e9130, stateskill.cpp:1054-1072) sets m_b48 and
    /// m_fStartTime, and the next StateSkill::vfunc_3 runs FUN_1417ee120 (@0x1417ee120, stateskill.cpp:2885-2994). For a
    /// PC that is neither MyPC nor an npc, it ends the state (<see cref="EndSkill"/>) only when CSkillRecord::FUN_141759e40
    /// (skillrecord.cpp:315-323, m_nAttack 1) holds: the motion is the level's last (m_nMotion + 1 &gt;= GetMotionCount).
    /// After an earlier motion the PC stays in StateSkill, dropping the walk records (<see cref="Apply"/>), until the next
    /// motion's cast enters it again or, 5 s after the sequence end (m_fStartTime + 5 &lt; GetGameTime), EndSkill.
    /// Nothing when the PC has left StateSkill already. EndOfSequence returns first while the cast charges (m_bCharging)
    /// or has loops left (m_nRepeat &gt; 0): the "loopend" key (<see cref="CastTextKey"/>) takes the sequence back.
    /// </summary>
    public void CastSequenceEnded()
    {
        if (Cur != StateSkill) return;
        if (castCharging || castRepeat > 0) return;
        castLoop = true;
        castAttack = 0;
        if (castMotion + 1 >= castMotionCount) EndSkill();
        else castEndAge = 0f;
    }

    /// <summary>
    /// StateSkill::TextKeyEvent (@0x1417e7660, stateskill.cpp:778-1051) for a key of the cast's sequence at
    /// <paramref name="time"/> (s), what a PC that is neither MyPC nor an npc does of it (its record's m_n9c stays 0):
    /// "loopstart" (:844-867, m_b117 set) outside the loop marks the loop's start (m_fLoopStart, m_fChargeTime = 0,
    /// m_n138); "loopend" (:868-947) marks the charge time and, while the cast charges or has loops left, takes a loop
    /// off, turns the PC to the record's rotation (SetTargetYaw, vfunc_21) and plays the sequence again from the loop
    /// start plus the time it overran the loop by (ClampedElapsedAfter16ms of the time since "loopstart" past the loop's
    /// length at m_fSpeed, at most the length - 5 ms); the current attack's point and "charging_attack" (:980-998) end a
    /// charging skill's loop once no loop is left (m_bLoop), the point also takes the attacks of that point
    /// (FireAttacksAtPoint, @0x1417f0f10, stateskill.cpp:2272-2292, m_nAttack). Nothing outside StateSkill.
    /// First of all a key the motion's tag effects name (m_mapTagEffects, :792-834) plays its effect on the caster
    /// (<see cref="TagEffect"/>) unless <see cref="SkillTagEffects.Plays"/> says no; m_b117 follows the keys ("loopstart" on,
    /// "loopend" off then on again for a next round, a charging skill's "charging_attack" off).
    /// STILL-OPEN: the loop effect (record m_n88, FUN_1417598e0), the tag effect's handle (m_map138, kept for the state's
    /// exit), its sight range test (FUN_1417f4bc0, object kind 3), FireCurrentAttack's hits and the weapon sync
    /// (FUN_141663be0) are not ported.
    /// </summary>
    public void CastTextKey(string key, float time)
    {
        if (Cur != StateSkill) return;
        if (castTags != null && castTags.TryGetValue(key, out var fx) && SkillTagEffects.Plays(pc.CastKeyTime(key), castInLoop,
                false, castLoop, pc.CastKeyTime("loopstart"), pc.CastKeyTime("loopend"), pc.CastKeyTime("charging_attack")))
            TagEffect?.Invoke(key, fx);   // the record's m_n9c stays 0 for this PC
        if (key == "loopstart") castInLoop = true;   // :844, before the m_bLoop test
        if (key == "loopstart" && !castLoop)
        {
            castLoopStart = time;
            castLoopEnded = false;
            castLoopMs = 0;
        }
        if (key == "loopend")
        {
            castLoopEnded = true;
            castInLoop = false;   // :875
            if (castCharging || castRepeat > 0)
            {
                if (castRepeat > 0) castRepeat--;
                castInLoop = true;   // :905
                TargetYaw = castRotZ;
                pc.RotationZ = castRotZ;
                castAttack = 0;
                castCharging = false;
                // m_n134 = FUN_14028d260((m_fLoopEnd - m_fLoopStart) / m_fSpeed), m_nChargeTick (:907-912)
                uint loopMs = (uint)(long)((time - castLoopStart) / castSpeed * 1000f);
                uint tick = ClampedElapsedAfter16ms((uint)castLoopMs, loopMs);
                if (loopMs - 5u < tick) tick = loopMs - 5u;
                // m_n9c == 0: SetTargetAnimation(m_nLoopSequenceId, m_fSpeed, true), SetOffset(m_fLoopStart + tick * 0.001f)
                pc.SeekCast(castLoopStart + tick * 0.001f);
                skillMotion.ArmMovePhases(pc);
            }
        }
        // :958-977: the move phases' keys start the record's move time
        if (skillMotion.TextKey(pc, key)) return;
        // GetAttackDataAt(m_nAttack): null past the last attack. TextKeyEvent takes it before the key tests (:790), this
        // after the loopend reset above; the two differ only for an attack whose point is "loopend" (or "loopstart"),
        // and no skill of the client data has one (0 of the KMS2 and KMS2_DEV skills).
        string point = castAttack < castPoints.Count ? castPoints[castAttack] : null;
        if (point != null && key == point)
        {
            if (ChargingSkill && !castLoop && castRepeat == 0) castLoop = true;
            // FireAttacksAtPoint: the attacks from m_nAttack on with that same non-empty point
            string first = null;
            while (castAttack < castPoints.Count && castPoints[castAttack].Length > 0
                && (first == null || first == castPoints[castAttack]))
            {
                first = castPoints[castAttack];
                AttackFired?.Invoke(castAttack);
                castAttack++;
            }
            return;
        }
        if (key == "charging_attack" && ChargingSkill && !castLoop && castRepeat == 0)
        {
            castLoop = true;
            castInLoop = false;   // :996
        }
    }

    /// <summary>ClampedElapsedAfter16ms (stateskill.cpp): the time <paramref name="now"/> is past <paramref name="start"/>,
    /// less 16 ms and at most 50 ms; 0 within 16 ms.</summary>
    static uint ClampedElapsedAfter16ms(uint now, uint start)
    {
        uint e = now > start ? now - start : 0;
        return e > 16 ? System.Math.Min(e - 16, 50u) : 0;
    }

    /// <summary>
    /// OnSkillSync's update of the running cast (scenestategame.cpp:3395-3402): the record's rotation (m_kRot,
    /// <paramref name="rotZ"/>), PCBase::vfunc_22(<paramref name="move"/>), then in StateSkill FUN_1417ec630
    /// (@0x1417ec630, stateskill.cpp:1419-1488) with the packet's two flags: <paramref name="charge"/> is m_bCharging;
    /// charging after a "loopend" (m_fChargeTime != 0) turns the PC to the rotation and plays the sequence again from the
    /// loop start; not charging, <paramref name="start"/> outside the loop plays it from its "charging_attack" key and
    /// ends the loop (m_bLoop, m_nRepeat = 0, m_fLoopStart at that key).
    /// STILL-OPEN: the weapon sync (FUN_141663be0) is not ported.
    /// </summary>
    public void SkillSync(float rotZ, Vector2 move, bool charge, bool start)
    {
        castRotZ = rotZ;
        MoveInput = move;
        if (Cur != StateSkill) return;
        castCharging = charge;
        if (castCharging)
        {
            if (!castLoopEnded) return;
            TargetYaw = castRotZ;
            pc.RotationZ = castRotZ;
            castAttack = 0;
            castLoopEnded = false;
            castCharging = false;
            castInLoop = true;   // :1445
            pc.SeekCast(castLoopStart);
            skillMotion.ArmMovePhases(pc);
        }
        else if (!castLoop && start)
        {
            float at = pc.CastKeyTime("charging_attack");
            if (at < 0) return;       // STILL-OPEN: FUN_141b6fe90 gives -FLT_MAX for a missing key, SetOffset far before 0
            pc.SeekCast(at);
            castLoop = true;
            castCharging = false;
            castRepeat = 0;
            castLoopStart = at;
        }
    }

    /// <summary>
    /// StateSkill's end, FUN_1417f0070 (@0x1417f0070, stateskill.cpp:2810-2822): a record holding an actor (m_nB0) enters
    /// StateHold (0x20) with a StateHoldData of m_nB0 and m_strB8, any other StateIdle. Nothing when the PC has left
    /// StateSkill already.
    /// </summary>
    public void EndSkill()
    {
        if (Cur != StateSkill) return;
        if (castHoldId != 0) Hold(castHoldId, castHoldNode);
        else ChangeState(StateIdle);
    }

    /// <summary>OnSkillCancel (State::vfunc_7(1) -> StateSkill::vfunc_7, stateskill.cpp:576-592: vfunc_59(1)): StateSkill
    /// leaves for StateIdle. Nothing when the PC has left StateSkill already.</summary>
    public void CancelSkill()
    {
        if (Cur == StateSkill) ChangeState(StateIdle);
    }

    /// <summary>vfunc_59(0x20, StateHoldData) unforced: StateHold holding <paramref name="targetId"/> by its node
    /// <paramref name="node"/>.</summary>
    public void Hold(int targetId, string node)
    {
        pendingHoldId = targetId;
        pendingHoldNode = node ?? "";
        ChangeState(StateHold);
    }

    /// <summary>
    /// CStateManager::FUN_1417dc4c0 (@0x1417dc4c0, statemanager.cpp:597-637): with no push and the current state allowing
    /// it (IsPositionCorrectionAllowed), the position correction, the body rotation and the target yaw; then, unless the
    /// state refuses records (vfunc_32, <see cref="TakesRecords"/>) or the record is StateHit (0xd) or StateStun (0x12),
    /// the record's state through vfunc_59, forced when the state asks for it (vfunc_28).
    /// </summary>
    void ApplyPc(SyncRecord r)
    {
        if (!pc.Pushed && CorrectionAllowed(Cur))
        {
            Correct(r);
            pc.RotationZ = r.RotationZ;              // vfunc_21(m_dir2c)
            TargetYaw = r.TargetYaw;                 // SetTargetYaw(m_f58)
        }
        if (!TakesRecords) return;
        if (r.State == StateHit || r.State == StateStun) return;
        bool force = Cur == r.State && StateForces(r);
        // FUN_1417dc9a0 (vfunc_27) then StateData::vfunc_2: StateHoldData::vfunc_2 (@0x1417cbaf0) reads m_n90 / m_str98
        if (r.State == StateHold) { pendingHoldId = r.HoldTargetId; pendingHoldNode = r.HoldNode; }
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
        if (snap && (d.X != 0 || d.Y != 0 || dz != 0))
        {
            Snaps++;
            LastSnap = MathF.Sqrt(dist * dist + dz * dz);
        }
        if (snap) pc.Position = r.Position;
    }

    /// <summary>How many records moved the PC by putting it on the record's position (<see cref="Correct"/>), and how far
    /// the last one moved it: the live log's measure of a remote PC that jumps instead of walking.</summary>
    public int Snaps { get; private set; }
    public float LastSnap { get; private set; }

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
            NotifyWeaponKey();
            return;
        }
        if (state == StateSkill) FlushRequests();
        if (Cur == StateSkill) SkillExited?.Invoke();
        Exit(Cur, state);
        Prev = Cur;
        if (sub != 0) subs[state] = sub;
        Cur = state;
        StateChanged?.Invoke(state);
        if (state == StateSkill) SkillEntered?.Invoke();
        Enter(state);
        NotifyWeaponKey();
    }

    // ---- StateHold ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// StateHold::vfunc_4 (@0x1417cbcb0, stateholddata.cpp:161-185): StateMoveSkill::vfunc_4 (State::vfunc_4 -> State::vfunc_10
    /// sets m_f938 = 1f), StopJump, the StateHoldData taken, and the rotation kept (m_kDir) for vfunc_5.
    /// STILL-OPEN: StateMoveSkill::vfunc_38's skill level row of the data's skill (StateHoldData's 0x1312d1f level 1, or
    /// the record's m_n64 / m_n6c), whose absence falls back at once, is taken as present; FUN_1417ccd50's m_bRiding (read
    /// by vfunc_15 / vfunc_16 only), the shadow (vfunc_31(false)) and the hit report reset are not ported.
    /// </summary>
    void HoldEnter()
    {
        pc.AnimSpeed = 1f;
        pc.StopJump();
        HoldTargetId = pendingHoldId;
        HoldNode = pendingHoldNode;
        holdDir = pc.RotationZ;
    }

    /// <summary>
    /// StateHold::vfunc_3 (@0x1417cbfa0, stateholddata.cpp:204-248): with the held actor alive and its node found, an npc
    /// or any actor but a PC or a ridee puts the PC on the node's world translation turned by its world rotation
    /// (ToEulerAnglesXYZ times -57.2957764 through vfunc_21, only when the angles are unique); a PC target with the asker's
    /// seat on its ridee puts the PC on the seat (Ridee::FUN_1417083a0: vfunc_19 and vfunc_21 of FUN_140a0b1f0's angles,
    /// unique or not), a PC target without one leaves it; else the fallback state (StateMoveSkill m_nFallbackState 6,
    /// m_nFallbackSub 0x22: StateJump). No vfunc_68 runs: no gravity, no turn.
    /// STILL-OPEN: the x / y angles of vfunc_21 (the body keeps only its Z rotation); efd::FastATan2 is taken as the exact
    /// atan2.
    /// </summary>
    void HoldMove()
    {
        var t = pc.FindHoldTarget(HoldTargetId, HoldNode);
        if (!t.HasNode || t.Kind == HoldKind.None || t.Dead)
        {
            ChangeState(StateJump, 0x22);            // StateHold::ChangeState: vfunc_59(m_nFallbackState, 0, m_nFallbackSub, 0)
            return;
        }
        if (t.Kind == HoldKind.Pc)
        {
            if (!t.HasSeat) return;
            if (!EulerZ(t.SeatRow0, out float z)) z = 0f;    // the non-unique branch sets z to 0
            pc.Position = t.SeatPosition;
            pc.RotationZ = z * -57.2957764f;
            return;
        }
        if (t.Kind == HoldKind.Ridee) return;
        if (EulerZ(t.NodeRow0, out float zn)) pc.RotationZ = zn * -57.2957764f;
        pc.Position = t.NodePosition;
    }

    /// <summary>efd::Matrix3::ToEulerAnglesXYZ (Matrix3_Win32.cpp:938-951) from row 0: y = -asin(m[0][2]), and when y is
    /// inside (-pi/2, pi/2) by EE_RIGHT_ANGLE_EPSILON, z = atan2(m[0][1], m[0][0]) and true.</summary>
    public static bool EulerZ(Vector3 row0, out float z)
    {
        float y = -MathF.Asin(Math.Clamp(row0.Z, -1f, 1f));
        const float Edge = MathF.PI / 2 - 1e-3f;     // EE_HALF_PI - EE_RIGHT_ANGLE_EPSILON
        bool unique = y < Edge && y > -Edge;
        z = unique ? MathF.Atan2(row0.Y, row0.X) : 0f;
        return unique;
    }

    /// <summary>
    /// StateHold::vfunc_9 (@0x1417cc5a0, stateholddata.cpp:277-374): the held npc 0xa7e02a plays "Ride_Idle_U", the npcs
    /// 0xadf341 / 0xadf342 "Idle_A", a PC target its mount's passenger sequence, and any other actor HOLD_IDLE (0x3e,
    /// PlayAnimationById); all at FUN_1416713c0() without a restart.
    /// </summary>
    void HoldFrame()
    {
        var t = pc.FindHoldTarget(HoldTargetId, HoldNode);
        if (t.Kind == HoldKind.None) return;
        if (t.Kind == HoldKind.Npc)
        {
            if (t.NpcId == 0xa7e02a) { pc.PlaySequence("Ride_Idle_U", pc.AnimSpeed); return; }
            if (t.NpcId == 0xadf341 || t.NpcId == 0xadf342) { pc.PlaySequence("Idle_A", pc.AnimSpeed); return; }
        }
        else if (t.Kind == HoldKind.Pc)
        {
            if (t.HasSeat) pc.PlaySequence(StateRideAnim.ForPassenger(t.RideeState, t.RideeSub, t.RideeSpIdle, t.Seat), t.RideeAnimSpeed);
            return;
        }
        if (!pc.IsAction(ActHoldIdle)) pc.PlayAction(ActHoldIdle, pc.AnimSpeed, false);
    }

    // ---- the states ------------------------------------------------------------------------------------------------

    void Enter(int s)
    {
        switch (s)
        {
            case StateSkill:
                // StateSkill::vfunc_4 (stateskill.cpp:390-500): vfunc_22(ZERO), vfunc_19(m_ptPos), and, for a PC whose
                // FUN_141489940 is not 10, SetTargetYaw(m_ptRot.z) and vfunc_21(m_ptRot); the motion's start
                // (FUN_1417f0940, stateskill.cpp:2206-2212) clears m_b48
                castEndAge = -1f;
                // vfunc_4 (stateskill.cpp:398-402): m_bCharging, m_fChargeTime, m_fLoopStart cleared; FUN_1417f0940:
                // m_bLoop cleared, m_nRepeat = the motion's loop count, 20 more for another player's charging skill
                castCharging = castLoop = castLoopEnded = false;
                castLoopStart = 0f; castLoopMs = 0; castAttack = 0;
                castRepeat = castLoopCount + (ChargingSkill ? 0x14 : 0);
                MoveInput = Vector2.Zero;
                pc.Position = castPos;
                TargetYaw = castRotZ;
                pc.RotationZ = castRotZ;
                // vfunc_4 (stateskill.cpp:457-480): the move phases armed, the controller by the motion's movetype
                skillMotion.Enter(pc, castHoldId != 0);
                break;
            case StateHold:
                HoldEnter();
                break;
            case StateIdle:
                // StateIdle::vfunc_4 (stateidle.cpp:167-191): State::vfunc_4 -> State::vfunc_10 sets m_f938 = 1f;
                // sub 1 (0xc riding a stunt), 7 on water
                pc.AnimSpeed = 1f;
                subs[s] = 1;
                if (pc.OnWater) subs[s] = 7;
                // STILL-OPEN: the water idle sound (StartIdleSound) and FUN_14166f8f0(0, 1)
                break;
            case StateWalk:
                // StateWalk::vfunc_4 (@0x1417ff800, statewalk.cpp:101-154)
                walkSpeed1 = pc.MoveSpeedRate;                // vfunc_96 (statewalk.cpp:103)
                walkSpeed2 = pc.ScaleAnimRate;                // vfunc_97 (statewalk.cpp:104)
                if (pc.OnWater) { ChangeState(StateSwim); return; }
                WalkSpeedBySub();
                WalkGait();                                   // State::vfunc_4 -> StateWalk::vfunc_10 sets m_n48 and m_f938
                break;
            case StateLand:
                // StateLand::vfunc_4 (stateland.cpp:74-101): State::vfunc_4 resets m_f938 = 1f, sub 0x86, the ground
                // ray and refresh, "land", then on water straight to StateIdle
                pc.AnimSpeed = 1f;
                subs[s] = 0x86;
                pc.LandRefresh();
                pc.PlayEffects("land");
                if (pc.OnWater) ChangeState(StateIdle);
                break;
            case StateFall:
                // StateFall::vfunc_4 (@0x1417c6ef0, statefall.cpp:93-130): State::vfunc_4 (@0x1417b7e80, state.cpp:600-610)
                // calls State::vfunc_10 (@0x1417b8090, state.cpp:649-652) -> FUN_141670d80(1f); on water and on the ground
                // it lands at once, else plays the fall sequence at FUN_1416713c0() (statefall.cpp:128).
                // STILL-OPEN: the set name prefix of the fall sequence (CEquipment::GetSetName).
                pc.AnimSpeed = 1f;
                if (pc.OnWater && pc.OnGround) { ChangeState(StateLand); return; }
                pc.PlayAction(ActJumpFall, pc.AnimSpeed, false);
                break;
            case StateJump:
                pc.AnimSpeed = 1f;                            // State::vfunc_4 -> State::vfunc_10
                JumpEnter();
                break;
            case StateRide:
                ShadowVisible?.Invoke(false);                 // StateRide::vfunc_4 (statemanager.cpp:1095)
                break;
            case StateDead:
                // StateDead::vfunc_4 (@0x1417c2530, statedead.cpp:104-171): State::vfunc_4 -> State::vfunc_10 sets m_f938
                pc.AnimSpeed = 1f;
                DeadEntered?.Invoke();
                break;
            case StateHit:
                // StateHit::vfunc_4: State::vfunc_4 -> State::vfunc_10 sets m_f938; m_bC8 cleared last (nothing between
                // reads it)
                pc.AnimSpeed = 1f;
                hitTakesRecords = false;
                HitEntered?.Invoke();
                break;
            case StateSwim:
                // StateSwim::vfunc_4 (stateswim.cpp:89-129): State::vfunc_4 -> StateSwim::vfunc_10 sets m_f938; sub 0x84
                // (a remote ridee never has the RunX flag for 0x85) at the row's raw run speed m_z for a ridee that runs
                // on water (FUN_1417fb370), else at the swim speed
                pc.AnimSpeed = pc.Gait(EffectiveSpeed, pc.RunSpeed, pc.WalkSpeed, out _);
                subs[s] = 0x84;
                Speed = pc.WaterRunner ? pc.RowRunSpeed : pc.SwimSpeed;
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
            case StateDead:
                DeadLeft?.Invoke();                           // StateDead::vfunc_5
                break;
            case StateRide:
                ShadowVisible?.Invoke(true);                  // StateRide::vfunc_5 (statemanager.cpp:1103)
                break;
            case StateHit:
                HitLeft?.Invoke();                            // StateHit::vfunc_5: EndLift, then m_bC8 set
                hitTakesRecords = true;
                break;
            case StateHold:
                // StateHold::vfunc_5 (@0x1417cbe70, stateholddata.cpp:188-201): StopJump, the shadow back on
                // (vfunc_31(true)), the rotation from before the hold (vfunc_21(m_kDir)); the rest is MyPC's
                pc.StopJump();
                pc.RotationZ = holdDir;
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
            case StateDead:
                // StateIdle::vfunc_3 / StateLand::vfunc_3 / StateDead::vfunc_3: vfunc_68 with a zero move
                MoveBy(Vector3.Zero, dt);
                break;
            case StateWalk:
            {
                // StateWalk::vfunc_3 (statewalk.cpp:166-211): m_unk_158 is 0 (vfunc_63), the speed by the sub, then
                // the direction times vfunc_95() as the controller's velocity and the frame's move
                WalkSpeedBySub();
                var v = MoveDirection() * EffectiveSpeed;
                pc.SetVelocity(v);
                MoveBy(v * dt, dt);
                break;
            }
            case StateSwim:
            {
                // StateSwim::vfunc_3 (stateswim.cpp:141-174): sub 0x84's speed as at the enter
                Speed = pc.WaterRunner ? pc.RowRunSpeed : pc.SwimSpeed;
                var v = MoveDirection() * EffectiveSpeed;
                pc.SetVelocity(v);
                MoveBy(v * dt, dt);
                break;
            }
            case StateFall:
            {
                // StateFall::vfunc_3 (statefall.cpp:147-160): an air velocity while there is a direction, a zero move
                var d = MoveDirection();
                if (d != Vector3.Zero) pc.SetVelocity(d * (EffectiveSpeed * pc.AirSpeedScale));
                MoveBy(Vector3.Zero, dt);
                break;
            }
            case StateJump:
                JumpMove(dt);
                break;
            case StateHold:
                HoldMove();
                break;
            case StateHit:
                MoveBy(Vector3.Zero, dt);                     // StateHit::vfunc_3: vfunc_68(ZERO)
                HitUpdate?.Invoke(dt);
                break;
            case StateSkill:
                // StateSkill::vfunc_3 (@0x1417e6820, stateskill.cpp:605-631): on water (FUN_141675ed0) the cast gives way
                // to StateSwim (vfunc_7(0x1b)); else FUN_1417edc20 moves the PC
                // STILL-OPEN: SendSkillMode4 before the swim (a remote PC sends nothing here), and the npc-node follow
                // of FUN_1417efbe0 (feature 0xae, m_nB0) that skips FUN_1417edc20, are not ported.
                if (pc.OnWater) { ChangeState(StateSwim); break; }
                SkillMotion(dt);
                castLoopMs += dt * 1000.0;
                // FUN_1417ee120's tail: 5 s after an earlier motion's sequence end the state ends
                if (castEndAge >= 0f && (castEndAge += dt) > 5f) EndSkill();
                break;
        }
    }

    /// <summary>
    /// StateSkill::FUN_1417edc20 (@0x1417edc20, stateskill.cpp:2844-2878): nothing while m_n10 is 0x3c (magicControl
    /// without a held actor); else the motion controller's step (vfunc_5) and its displacement through vfunc_68, then for
    /// a skill of stateAttr moveType 2 or 3 the move input at the PC's speed through vfunc_68 again, unless a
    /// movingInMovekey skill's record has no time left. The controller's displacement is in meters (PhysX units); here
    /// in units (x100), as the walk moves.
    /// STILL-OPEN: the hit pause (feature 0xca: m_f80 grows and nothing moves), the motion's ignoreCollisionGroups and
    /// vfunc_6 flags for vfunc_68, and the lift test of the linear and move controllers are not ported.
    /// </summary>
    void SkillMotion(float dt)
    {
        if (skillMotion.NoMove) return;
        bool input = skillMotion.TakesInput;
        skillMotion.Step(pc, dt, MoveDirection());
        MoveBy(skillMotion.Displacement, dt);
        // CalcMoveDirectionNoYaw after the controller's vfunc_68 (stateskill.cpp:2866-2869), whose turn toward the target
        // yaw a camera mode 0xe direction reads
        if (input) MoveBy(MoveDirection() * (EffectiveSpeed * dt), dt);
    }

    /// <summary>The cast's move time (m_fDuration, m_fElapsed, for tests).</summary>
    public float CastDuration => skillMotion.Duration;
    public float CastElapsed => skillMotion.Elapsed;

    void MoveBy(Vector3 move, float dt)
    {
        pc.Move(move, dt);
        // PCBase::vfunc_68 (@0x14166e100, pcbase.cpp:1864-1869): skipped for a non-target PC while the viewer's camera
        // (CameraMgr::IsLocalTargetMode) is in local-target mode.
        // STILL-OPEN: viewer camera local-target mode is not ported yet, so TurnTowardTargetYaw always runs.
        TurnTowardTargetYaw(dt);
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
            case StateHold: HoldFrame(); break;
            case StateHit: HitFrame?.Invoke(); break;
            case StateSwim:
                // StateSwim::vfunc_9 (@0x1417fac90, stateswim.cpp:228-263), no ridee: StateSwim::vfunc_10
                // (@0x1417fafb0, stateswim.cpp:266-270) sets m_f938 and PlayAnimation passes FUN_1416713c0().
                // STILL-OPEN: the costume's swim name (PC::GetSwimAnimName).
                if (!pc.IsAction(ActWaterSwim))
                {
                    pc.AnimSpeed = pc.Gait(EffectiveSpeed, pc.SwimSpeed, pc.WalkSpeed, out _);
                    pc.PlayAction(ActWaterSwim, pc.AnimSpeed, false);
                }
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
                if (!pc.IsAction(key)) pc.PlayAction(key, pc.AnimSpeed, false);
                break;
            }
            case 7:
                // PlayBoreWaitAnimation (stateidle.cpp:573-597). STILL-OPEN: PC::GetSwimAnimName's costume name.
                if (!pc.IsAction(ActWaterIdle)) pc.PlayAction(ActWaterIdle, pc.AnimSpeed, false);
                break;
            case 6:
                if (!pc.IsAction(ActTalkIdle)) pc.PlayAction(ActTalkIdle, pc.AnimSpeed, false);   // FUN_1417ce9f0 (stateidle.cpp:555-561)
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

    /// <summary>PCBase::vfunc_95 (@0x141671620, pcbase.cpp:2238-2243): FUN_141671940() * vfunc_96() * m_f92c (m_f92c is 1).</summary>
    float EffectiveSpeed => Speed * pc.MoveSpeedRate;

    /// <summary>PCBase::vfunc_97 (@0x141671720, pcbase.cpp:2504-2519): animation rate from current vs base scale.</summary>
    public static float ScaleRate(float cur, float baseScale)
    {
        float rate = 1f;
        if (cur != baseScale)
        {
            if (baseScale / 2f > cur) rate = 1.5f;
            else if (baseScale / 2f <= cur && cur < baseScale) rate = 2f - cur / baseScale;
            else if (cur > baseScale && baseScale * 2f >= cur) rate = 1f - (cur - baseScale) / (baseScale * 2f);
            else if (baseScale * 2f < cur) rate = 0.5f;
        }
        return rate;
    }

    void WalkGait()
    {
        pc.AnimSpeed = pc.Gait(EffectiveSpeed, pc.RunSpeed, pc.WalkSpeed, out bool run);
        walkRun = run ? 1 : 0;
        NotifyWeaponKey();
    }

    /// <summary>
    /// StateWalk::vfunc_9 (@0x141800580, statewalk.cpp:322-373): water goes to StateSwim; a fall timer past sqrt(3 / |g|) s
    /// falls; else the run (0x1c) or, at the walk sub or a walking gait, the walk (0x1d) sequence, restarting when vfunc_96
    /// (MoveSpeedRate) or vfunc_97 (ScaleAnimRate) changed.
    /// STILL-OPEN: the cube guide sequences (0x38 / 0x39) and the ride's 0x1f.
    /// </summary>
    void WalkFrame()
    {
        if (pc.OnWater) { ChangeState(StateSwim); return; }
        int delay = Ms(MathF.Sqrt(3f / MathF.Abs(pc.Gravity / 100f)));
        if (pc.InAirFor(delay)) { ChangeState(StateFall); return; }
        if (subs[StateWalk] == 0x19 && walkRun != 0) { walkRun = 0; NotifyWeaponKey(); }
        int key = walkRun == 0 ? ActWalk : ActRun;
        bool changed = !(pc.MoveSpeedRate == walkSpeed1 && pc.ScaleAnimRate == walkSpeed2 && walkChanged == 0);
        if (!pc.IsAction(key) || changed)
        {
            pc.AnimSpeed = pc.Gait(EffectiveSpeed, pc.RunSpeed, pc.WalkSpeed, out bool run);
            walkRun = run ? 1 : 0;
            NotifyWeaponKey();
            walkSpeed1 = pc.MoveSpeedRate;
            walkSpeed2 = pc.ScaleAnimRate;
            pc.PlayAction(key, pc.AnimSpeed, changed);
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
        pc.StartJump(pc.JumpHeight, jumpDir, EffectiveSpeed);
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
        pc.SetVelocity(jumpDir * EffectiveSpeed * pc.AirSpeedScale);
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
        if (limit <= pc.FallTimeMs) { pc.PlayAction(ActJumpFall, pc.AnimSpeed, false); return; }
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
