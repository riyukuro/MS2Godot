using Godot;
using Ms2.Net.Replay;
using Ms2Godot.Movement;

namespace Ms2Godot;

// The own cast's motion on the body.
public partial class Player : ISkillMotionOwner
{
    string skillSequence;   // the own cast's sequence while it plays; null otherwise
    // the record's motion data and move time, the move phases and the motion controller (the remote PCs' code), and the
    // sequence time its move keys were read up to
    readonly SkillMotion skillMotion = new();

    /// <summary>
    /// The own cast's motion (StateSkill plays the record's motion sequence at its sequenceSpeed): the body's &lt;name&gt;.kf, the
    /// same file the other players' casts play (ReplayField.SkillAnim). False when the body has no such sequence.
    /// <paramref name="move"/> is what the motion controller reads and <paramref name="rotZ"/> the record's rotation z
    /// (m_kRot.z): the state's entry (vfunc_4, stateskill.cpp:457-480) starts the controller of the motion's movetype on a
    /// new record (m_fDuration = m_fElapsed = 0), with no held actor.
    /// </summary>
    public bool PlaySkillSequence(string sequenceName, float speed, CastMove move, float rotZ)
    {
        var key = Anims?.Sequence(sequenceName, false);
        if (key == null || Anim == null) return false;
        // vfunc_59(0x10) leaves StateEmotion / StateEmotionIdle with their vfunc_5 (the face and the name tag offsets); the
        // skill state's end then goes to StateIdle
        if (State is MoveState.Emotion or MoveState.EmotionIdle) Enter(MoveState.Idle);
        PlaySequence(key, speed, true);
        skillSequence = key;
        skillSpeed = speed;
        skillHeld = false;
        kVel = Vector3.Zero;   // the state's enter, vfunc_4 (stateskill.cpp:437): the owner's velocity 0 (the vertical one is kept here)
        skillMotion.Record(move, speed, rotZ);
        skillMotion.Enter(this, false);
        return true;
    }

    /// <summary>The own cast's skill state holds the walk, jump and click-move input: the state moves by its motion and, for a
    /// moveType 2 / 3 skill, the input it reads itself (<see cref="SkillMotionMove"/>, stateskill.cpp:2855-2874). Asked
    /// each frame, so the sequence's end (<see cref="SkillSequenceEnding"/>) is seen in the frame it comes.</summary>
    bool SkillHoldsMove() => SkillSequencePlaying();

    /// <summary>The own cast's motion for this frame (StateSkill::FUN_1417edc20, stateskill.cpp:2844-2878), in Godot units:
    /// the sequence's move keys passed since the last read (TextKeyEvent :958-977), then the controller's step and, for a
    /// moveType 2 / 3 skill with move time left, <paramref name="rawInput"/> (the held move or click input, as the walk
    /// state reads it) at the owner's speed. Zero without a playing cast or with magicControl (m_n10 0x3c).
    /// STILL-OPEN: the client hands the two moves to vfunc_68 one after the other (each with its gravity step and
    /// collision); here they are one sweep. The hit pause (feature 0xca) and the ignored collision groups are not ported.</summary>
    Vector3 SkillMotionMove(Vector2 rawInput, float dt)
    {
        if (!SkillSequencePlaying()) return Vector3.Zero;
        SkillMotionKeys();
        if (skillMotion.NoMove) return Vector3.Zero;
        bool input = skillMotion.TakesInput;
        var dir = WorldDir(rawInput);
        skillMotion.Step(this, dt, Ms2N(dir));
        var d = FromMs2(skillMotion.Displacement);
        // PCBase::vfunc_95 (pcbase.cpp:2238-2243): the speed the last state set (m_unk_190.z; StateSkill sets none, so the
        // walk, run, ride or swim speed it came from) times vfunc_96 and m_f92c.
        // STILL-OPEN: vfunc_96 (the move speed stat's rate) and m_f92c are not kept for the own player (1 here).
        if (input) d += dir * (baseSpeed * dt);
        return d;
    }

    /// <summary>StateSkill::TextKeyEvent's move keys for the keys the own sequence passed since the last read.</summary>
    void SkillMotionKeys()
    {
        double pos = SkillSequencePosition;
        if (pos < 0 || Anims == null || !Anims.Kf.TryGetValue(skillSequence, out var kf)) return;
        skillMotion.ReadKeys(this, kf.TextKeys, pos);
    }

    // ---- ISkillMotionOwner (MS2 axes) --------------------------------------------------------------------------------

    static System.Numerics.Vector3 Ms2N(Vector3 g) => new(g.X, -g.Z, g.Y);
    static Vector3 FromMs2(System.Numerics.Vector3 p) => new(p.X, p.Z, -p.Y);

    System.Numerics.Vector3 ISkillMotionOwner.Position => Ms2N(GlobalPosition);
    bool ISkillMotionOwner.InAirFor(int ms) => fallTimerRunning && InAirFor(ms * 0.001f);
    bool ISkillMotionOwner.Jumping => jumping;
    float ISkillMotionOwner.CastKeyTime(string key) => skillSequence != null && Anims != null ? Anims.TextKey(skillSequence, key) : -1f;
    bool ISkillMotionOwner.LedgeAhead(System.Numerics.Vector3 dir, float dist, float height, float up) =>
        Net.RemotePc.LedgeAhead(GlobalPosition, dir, dist, height, up, skillBlocked ??= SkillBlocked);

    System.Func<Vector3, Vector3, float, bool> skillBlocked;
    bool SkillBlocked(Vector3 from, Vector3 dir, float len) => Ray(from, dir, len, MoveConfig.DownRayGroups) != null;

    /// <summary>The own cast's sequence while it plays, else null.</summary>
    public string SkillSequence => skillSequence;

    /// <summary>The own cast's sequence time in seconds (the kf's time, the one its text keys use), or -1 when it does not play.</summary>
    public double SkillSequencePosition =>
        skillSequence != null && (object)CurrentAnim == skillSequence && Anim != null ? Anim.CurrentAnimationPosition : -1;

    /// <summary>The own cast's sequence moved to <paramref name="time"/> seconds of the kf (a continue round's
    /// m_pSequence->SetOffset after the forced SetTargetAnimation, stateskill.cpp:932-937 and :710-714); nothing when it
    /// does not play. The next frame reads the move keys from 0 (<see cref="SkillMotion.Restarted"/>);
    /// <paramref name="loopRestart"/>: the loop's next round, which arms the loop's move phases again (FUN_1417f1a90).</summary>
    public void SeekSkillSequence(double time, bool loopRestart)
    {
        if (SkillSequencePosition < 0) return;
        if (skillHeld)
        {
            // the held end (EndOfSequence while charging or with a repeat left): the forced SetTargetAnimation plays it again
            skillHeld = false;
            Anim.Play(skillSequence, 0, skillSpeed);
        }
        Anim.Seek(time, true);
        skillMotion.Restarted(this, loopRestart);
    }

    /// <summary>
    /// The skill state's end step (StateSkill::FUN_1417ee120, stateskill.cpp:2885-2992), asked once when the own cast's
    /// sequence reaches its end, while the state still holds the body: true keeps the state (the HUD's next cast, or
    /// <see cref="HoldSkillSequence"/>); a new sequence it plays takes over at once, so no movement frame plays between.
    /// </summary>
    public System.Func<bool> SkillSequenceEnding;
    bool skillHeld;         // the ended sequence's last frame held: the skill state lasts past its sequence (HoldSkillSequence)
    bool skillEnding;       // SkillSequenceEnding runs
    float skillSpeed;       // the own cast's sequenceSpeed (PlaySkillSequence), for the held end's restart

    /// <summary>The skill state stays after its sequence ended (a cooling next cast queued, FUN_1417ee120's per-frame poll):
    /// the body keeps the last frame and the movement input stays held until <see cref="CancelSkillSequence"/>.</summary>
    public void HoldSkillSequence() { if (skillSequence != null) skillHeld = true; }

    /// <summary>True while the own cast's sequence ended and the skill state holds on (<see cref="HoldSkillSequence"/>).</summary>
    public bool SkillSequenceHeld => skillHeld && skillSequence != null;

    bool SkillSequenceRuns() =>
        skillSequence != null && (object)CurrentAnim == skillSequence && (skillHeld || blendHold >= 0 || Anim.IsPlaying());

    /// <summary>True while the own cast's sequence plays (PlayAct leaves the body to it); at its end the HUD's end step
    /// (<see cref="SkillSequenceEnding"/>) runs first, then it clears itself unless that kept the state.</summary>
    bool SkillSequencePlaying()
    {
        if (skillSequence == null) return false;
        if (SkillSequenceRuns()) return true;
        if ((object)CurrentAnim == skillSequence && !skillEnding && SkillSequenceEnding != null)
        {
            skillEnding = true;
            try { if (SkillSequenceEnding() && SkillSequenceRuns()) return true; }
            finally { skillEnding = false; }
            if (SkillSequenceRuns()) return true;   // the end step played the next cast's sequence
        }
        skillSequence = null;
        skillHeld = false;
        return false;
    }
    /// <summary>The own cast's sequence is cancelled (State::vfunc_7(1)): the movement animations take the body back.</summary>
    public void CancelSkillSequence() { skillSequence = null; skillHeld = false; }

    /// <summary>Client::Constants CancelSwing_KeyIsDown (constantsblocks.cpp:859, defined 0; 1 in the KMS and the GMS2
    /// table/constants.xml): the swing cancel keeps the held move direction, else only a pressed one.</summary>
    public static int CancelSwingKeyIsDown;

    // the swing cancel's input (StateSkill::FUN_1417ed7f0, stateskill.cpp:1519-1532), read while the skill holds the move
    Vector2 swingHeld, swingPressed, swingPrevMove;
    bool swingJumpPressed, swingPrevJump;
    bool swingJump;          // the HUD's swing cancel left the skill for the jump: it starts in the next movement frame
    Vector2 swingJumpPush;
    ulong swingJumpFrame;    // the movement frame count when it was asked: it is dropped after the next movement frame

    /// <summary>Unk_PCMove::GetMoveDirection while the skill state holds the move: the held move input (x left / right,
    /// y forward / back), zero outside the skill state.</summary>
    public Vector2 SwingHeld => swingHeld;

    /// <summary>Unk_PCMove::GetPressedMoveDirection since the last call (the axes whose key went down), then cleared.</summary>
    public Vector2 TakeSwingPressed() { var p = swingPressed; swingPressed = Vector2.Zero; return p; }

    /// <summary>IsActionKeyPressed(0x493e1) since the last call: the jump key went down in the skill state, then cleared.</summary>
    public bool TakeSwingJumpPressed() { bool j = swingJumpPressed; swingJumpPressed = false; return j; }

    /// <summary>vfunc_99(0) (pcbase.cpp:2544): the body falls, in the air for over 100 ms.</summary>
    public bool SwingFalling => fallTimerRunning && InAirFor(0.1f);

    void ReadSwingInput(bool skill, Vector2 move, bool jump)
    {
        if (!skill) { swingHeld = swingPressed = Vector2.Zero; swingJumpPressed = false; swingPrevMove = Vector2.Zero; swingPrevJump = jump; return; }
        swingHeld = move;
        var pressed = new Vector2(move.X != swingPrevMove.X ? move.X : 0, move.Y != swingPrevMove.Y ? move.Y : 0);
        if (pressed != Vector2.Zero) swingPressed = pressed;
        if (jump && !swingPrevJump) swingJumpPressed = true;
        swingPrevMove = move;
        swingPrevJump = jump;
    }

    /// <summary>The swing cancel leaves the skill state (StateSkill::FUN_1417ed7f0 :1550-1560, after SendSkillMode4) with the
    /// push <paramref name="push"/> (vfunc_22): for the walk (vfunc_7(2)) the held input then moves the body; for the jump
    /// (vfunc_7(6) without data: StateJump sub 0x21) the jump starts in the next movement frame the push's way.</summary>
    public void LeaveSkill(bool jump, Vector2 push)
    {
        CancelSkillSequence();
        swingJump = jump;
        swingJumpPush = push;
        swingJumpFrame = Engine.GetPhysicsFrames();
    }

    /// <summary>The kept swing jump, once, in the idle or walk state's frame after the skill let go.</summary>
    bool SwingJump(bool skill)
    {
        if (!swingJump) return false;
        swingJump = false;
        if (skill || Engine.GetPhysicsFrames() > swingJumpFrame + 1) return false;
        prevJump = JumpHeld;
        StartJump(WorldDir(swingJumpPush), swingJumpPush != Vector2.Zero, 0);
        return true;
    }
}
