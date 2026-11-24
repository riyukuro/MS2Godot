using System;
using Godot;
using Ms2.Ui.Skills;

namespace Ms2Godot;

/// <summary>
/// The own pc's StateEmotion (0xb, stateemotionidle.cpp:196-352) and StateEmotionIdle (0xf, :646-870), which an emotion skill
/// enters (PCBase::FUN_14166ed70, <see cref="EmotionCast"/>): the skill's paramStr names the emotion of the face item's group
/// (m_spD10, <see cref="Ms2.Ui.FaceEmotion.Group"/>); the face plays it, the name tag and the HP bar move by its offsetNameTag
/// and the body plays its animations (PCBase::GetEmotionAnimation): anim once, then idleAnim with the bore animations.
/// STILL-OPEN: the state effects (State::FUN_1417b92a0, StateEffectMgr::GetEffects, state.cpp:933-938); vfunc_63 (the state
/// flags) before StateEmotionIdle's input; a mounted pc (PCBase::vfunc_59 hands the change to the ridee, pcbase.cpp:1106-1108,
/// which is not ported, so nothing is entered); the up key's ride off in StateEmotionIdle (RideOffActionTakeLR, :730-754);
/// the crawl key (0x493e5, state 3), which the port does not bind.
/// </summary>
public partial class Player
{
    SkillData emotionRow;               // m_spData2's row (StateEmotionData m_nEmotion)
    string emotionAnim = "";           // m_strAnim: the row's paramStr
    double emotionLeft = -1;           // seconds until the played sequence ends (EndOfSequence); below 0 none ends
    double emotionPeriod = -1;         // a looped sequence's length: its EndOfSequence comes again each pass, below 0 once
    readonly EmotionIdleSub emotionIdle = new();

    /// <summary>IsEmotionLearned(id, 1) of the own skill system (CSkillSystem, skillslot.cpp:2803); null checks nothing (offline).</summary>
    public Func<int, bool> EmotionLearned;
    /// <summary>Client::Constants BoreWaitingTick (ms) and EmotionBoreAnimProbability (clientconstants.h +0xa00, +0x29c8).</summary>
    public int BoreWaitingTickMs = 20000, EmotionBoreChance = 100;
    /// <summary>The client's s_random draw (FUN_140767ff0), for the bore pick.</summary>
    public Func<uint> EmotionRandom = () => (uint)Random.Shared.Next();

    /// <summary>m_f170 / m_f174 (PCBase::FUN_141670330 / FUN_141670360): added to OffsetPcNametag (vfunc_89) and OffsetPcHpbar
    /// (FUN_141670390); the emotion states set them to the row's offsetNameTag and back to 0 on their exit.</summary>
    public float NameTagOffset { get; private set; }
    public float HpBarOffset { get; private set; }

    /// <summary>The emotion skill id of StateEmotion / StateEmotionIdle (StateEmotionData m_nEmotion, the sync record's m_n64), else 0.</summary>
    public int EmotionId => State is MoveState.Emotion or MoveState.EmotionIdle && emotionRow != null ? (int)emotionRow.Id : 0;
    /// <summary>StateEmotion's m_strAnim (Unk_PCBaseState0b::FUN_14167f950, read by FUN_14166ed70) in 0xb, else null.</summary>
    public string EmotionAnim => State == MoveState.Emotion ? emotionAnim : null;
    /// <summary>StateEmotionIdle's m_n10 (0x6f, 0x70-0x72) for the sync record's sub state.</summary>
    byte EmotionIdleSubId => (byte)emotionIdle.Sub;

    /// <summary>
    /// vfunc_59(0xb, StateEmotionData(id), 0, 1) from FUN_14166ed70: refused while dead (PC::vfunc_124); entered again when
    /// already in 0xb (bForce), which runs the exit first. Then StateEmotion::vfunc_4 @0x1417c3990 (:208-241): SendRequestCube
    /// with a held item; a row that is no main type 0xe keeps the state with nothing played (vfunc_3 then leaves for
    /// StateIdle); else m_strAnim = paramStr, the name tag offsets, the face (FUN_14166ef10(anim, 1, 0, false)) and the
    /// emotion's anim (kind 0) played from its start.
    /// </summary>
    public void EnterEmotion(SkillData row)
    {
        if (DeadFlag || Mount != null || row == null) return;
        CancelSkillSequence();
        StopInteractAnim();
        Enter(MoveState.Emotion);
        HeldStateEntered?.Invoke(Ms2.Ui.HeldItemState.Emotion);
        emotionRow = row;
        emotionAnim = "";
        emotionLeft = -1;
        if (row.MainType != EmotionCast.MainTypeEmotion) return;
        emotionAnim = row.ParamStr;
        NameTagOffset = HpBarOffset = row.OffsetNameTag;
        FaceEmotion?.Play(emotionAnim, false, FaceMs);
        emotionLoop = EmotionAnimation(0);
        if (emotionLoop != null) PlayEmotion(emotionLoop, true);
    }

    string emotionLoop;                // StateEmotion's m_strLoop, null when the emotion has no anim

    string EmotionAnimation(int kind) => EmotionCast.Animation(FaceEmotion?.Group, emotionAnim, kind);

    /// <summary>
    /// StateEmotionIdle::vfunc_4 @0x1417c5d10 (:657-688), from StateEmotion's EndOfSequence: the timer reset, m_n10 = 0x6f, a
    /// row that is no main type 0xe goes to StateIdle; else the face, the name tag offsets and the idleAnim played from its start.
    /// </summary>
    void EnterEmotionIdle()
    {
        var row = emotionRow;
        Enter(MoveState.EmotionIdle);
        emotionRow = row;
        emotionIdle.Enter();
        emotionLeft = -1;
        if (row == null || row.MainType != EmotionCast.MainTypeEmotion) { Enter(MoveState.Idle); return; }
        emotionAnim = row.ParamStr;
        FaceEmotion?.Play(emotionAnim, false, FaceMs);
        NameTagOffset = HpBarOffset = row.OffsetNameTag;
        if (EmotionAnimation(1) is { } idle) PlayEmotion(idle, true);
    }

    /// <summary>PCBase::PlayAnimation(name, 1, bForce, false): the body's sequence of that name with the kf's own cycle;
    /// without bForce the sequence playing now is left as it is. A name the body lacks plays nothing. EndOfSequence comes
    /// at a clamped sequence's end and, INFERRED (NiActorManager's END_OF_SEQUENCE is engine code, not in the decomp), at
    /// the end of each pass of a looped one: all 126 KMS emotions whose anim loops have an idleAnim, which StateEmotion
    /// reaches only from its EndOfSequence. A back-and-forth sequence (no emotion's) gets none.</summary>
    void PlayEmotion(string name, bool force)
    {
        if (Anims == null || Anim == null) return;
        string key = Anims.Sequence(name, false);
        if (key == null) return;
        if (!force && key == CurrentAnim) return;
        var a = Anim.GetAnimation(key);
        bool clamp = Anims.Kf.TryGetValue(key, out var kf) && kf.Cycle == 2;
        a.LoopMode = kf == null || kf.Cycle == 0 ? Animation.LoopModeEnum.Linear
            : kf.Cycle == 1 ? Animation.LoopModeEnum.Pingpong : Animation.LoopModeEnum.None;
        PlaySequence(key, 1f, restart: true);
        bool loop = kf == null || kf.Cycle == 0;
        emotionLeft = clamp || loop ? a.Length + Math.Max(blendHold, 0f) : -1;
        emotionPeriod = loop && a.Length > 0 ? a.Length : -1;
    }

    /// <summary>The sequence played by the state ended this frame (NiActorManager's EndOfSequence), once.</summary>
    bool EmotionSequenceEnded(float dt)
    {
        if (emotionLeft < 0) return false;
        emotionLeft -= dt;
        if (emotionLeft > 0) return false;
        emotionLeft = emotionPeriod > 0 ? Math.Max(emotionLeft + emotionPeriod, 0.001) : -1;
        return true;
    }

    /// <summary>
    /// StateEmotion's frame. vfunc_8 @0x1417c3e00 (:265-297), the input: the held move goes to StateWalk (FUN_1416749c0); a
    /// pressed up key takes a ladder or rope (TryUseChairOrBed(1)), a pressed down key a ladder (TryUseChairOrBed(2)); the
    /// jump key on the ground jumps (vfunc_59(6, 0, 0x25)); else the base cast poll (FUN_1417b9460, the HUD's own cast). The
    /// facing (vfunc_22 of the move) is Step's shared line. vfunc_3 @0x1417c3fe0 (:300-322): an emotion no longer learned or
    /// without anim goes to StateIdle; else a zero move. EndOfSequence @0x1417c4150 (:325-336): with an idleAnim on to
    /// StateEmotionIdle, else StateIdle.
    /// </summary>
    void EmotionUpdate(Vector2 input, bool upPressed, bool downPressed, Vector3 dir, bool hasInput, float dt)
    {
        kVel = Vector3.Zero;
        if (hasInput) { Enter(MoveState.Walk); return; }
        if (input.Y > 0 && upPressed && (TryLadder(1) || TryRope(1))) return;
        if (input.Y < 0 && downPressed && TryLadder(2)) return;
        if (Grounded && JumpHeld) { StartJump(dir, hasInput, 0x25); return; }
        if (emotionRow != null && EmotionLearned != null && !EmotionLearned((int)emotionRow.Id)) { Enter(MoveState.Idle); return; }
        if (emotionLoop == null) { Enter(MoveState.Idle); return; }
        if (!EmotionSequenceEnded(dt)) return;
        if (EmotionAnimation(1) != null) EnterEmotionIdle();
        else Enter(MoveState.Idle);
    }

    /// <summary>
    /// StateEmotionIdle's frame. vfunc_8 @0x1417c6230 (:720-803): a pressed up key takes a ladder or rope, a pressed down key
    /// a ladder, the jump key on the ground jumps, the held move goes to StateWalk; then the bore timer
    /// (<see cref="EmotionIdleSub.Tick"/>). vfunc_3 @0x1417c6070 (:691-706): an emotion no longer learned goes to StateIdle;
    /// else a zero move. vfunc_9 @0x1417c6810 (:806-829), each frame: the sub's kind is played without bForce, so a bore
    /// animation starts the frame after its pick and idleAnim again after its EndOfSequence (:832-840, back to 0x6f).
    /// </summary>
    void EmotionIdleUpdate(Vector2 input, bool upPressed, bool downPressed, Vector3 dir, bool hasInput, float dt)
    {
        kVel = Vector3.Zero;
        if (input.Y > 0 && upPressed && (TryLadder(1) || TryRope(1))) return;
        if (input.Y < 0 && downPressed && TryLadder(2)) return;
        if (Grounded && JumpHeld) { StartJump(dir, hasInput, 0x25); return; }
        if (hasInput) { Enter(MoveState.Walk); return; }
        emotionIdle.Tick((long)(gameTime * 1000), BoreWaitingTickMs, EmotionBoreChance, EmotionRandom, k => EmotionAnimation(k) != null);
        if (emotionRow != null && EmotionLearned != null && !EmotionLearned((int)emotionRow.Id)) { Enter(MoveState.Idle); return; }
        if (EmotionSequenceEnded(dt)) emotionIdle.EndOfSequence();
        if (EmotionAnimation(emotionIdle.Kind) is { } next) PlayEmotion(next, false);
    }

    /// <summary>StateEmotion::vfunc_5 @0x1417c3cb0 (:244-252): m_n10 0, the name tag offsets 0 and, unless the next state is
    /// StateEmotionIdle, the face rests (FUN_14166f8f0(0, 1)). StateEmotionIdle::vfunc_5 @0x1417c61a0 (:709-717): the data
    /// and anim cleared, the face rests, the offsets 0.</summary>
    void EmotionExit(MoveState old, MoveState next)
    {
        NameTagOffset = HpBarOffset = 0;
        emotionLeft = -1;
        if (old == MoveState.EmotionIdle || next != MoveState.EmotionIdle) FaceEmotion?.ToRest(true, FaceMs);
        if (old == MoveState.EmotionIdle) { emotionRow = null; emotionAnim = ""; }
    }
}
