using System.Numerics;

namespace Ms2.Net.Replay;

/// <summary>What a cast's motion controller reads of its owner (PCBase / GameObjectController, MS2 axes, game units).</summary>
public interface ISkillMotionOwner
{
    /// <summary>vfunc_18: the feet position.</summary>
    Vector3 Position { get; }
    /// <summary>vfunc_99(ms): the controller's fall timer runs and has passed <paramref name="ms"/>.</summary>
    bool InAirFor(int ms);
    /// <summary>GameObjectController::IsJumping.</summary>
    bool Jumping { get; }
    /// <summary>FUN_141b6fe90(m_pSequence, key, 1f): the time (s) of a text key in the cast's sequence, or -1 without it.</summary>
    float CastKeyTime(string key);
    /// <summary>
    /// PCBase::FUN_1416725c0 (pcbase.cpp): true when a skill move of <paramref name="dist"/> units along
    /// <paramref name="dir"/> (MS2 axes) from the feet raised by <paramref name="up"/> ends over a ledge: no hit along
    /// the direction or its flat part, and no ground within 10 + <paramref name="height"/> + 1 under the end point
    /// (raised by 10) nor under it moved by (0.1, 0.05). False for a zero direction.
    /// </summary>
    bool LedgeAhead(Vector3 dir, float dist, float height, float up);
}

/// <summary>
/// The motion side of StateSkill for any PC: the record's move time (CSkillRecord m_fDuration, m_fElapsed), the move
/// phases (m_bMove0 / m_bMove2 and their keys), and the motion controller by the motion's movetype (StateSkill::vfunc_4,
/// stateskill.cpp:457-480: 0 CLinearMotionController, 1 CArcMotionController, 2 CBlinkMotionController, 3
/// CMoveMotionController, any other a blink; motioncontroller.cpp). <see cref="Step"/> is FUN_1417edc20's controller part:
/// <see cref="Displacement"/> is what vfunc_3 hands vfunc_68, in units. Allocates nothing.
/// </summary>
public sealed class SkillMotion
{
    /// <summary>The motion data the controller reads.</summary>
    public CastMove Move;
    /// <summary>The record's rotation z (m_kRot.z, degrees).</summary>
    public float RotZ;
    /// <summary>GetMotionSpeedScale: the record's speed (StateSkill::m_fSpeed).</summary>
    public float Speed = 1f;
    /// <summary>The record's m_fDuration and m_fElapsed (m_fPad7c), seconds.</summary>
    public float Duration, Elapsed;
    /// <summary>StateSkill m_n10 == 0x3c: magicControl without a held actor, FUN_1417edc20 moves nothing.</summary>
    public bool NoMove;
    bool move0, move2;
    // the motion controller's m_kDir, m_fSpeed (m) and m_kMove (units)
    Vector3 dir, move; float speed;
    // CArcMotionController: m_dElapsed, m_kStartPos, m_kCtrlPos, m_kEndPos, m_kMove (the last point), m_kHeading
    float arcElapsed; Vector3 arcStart, arcCtrl, arcEnd, arcLast;

    /// <summary>The controller's last step (vfunc_3's move), in units.</summary>
    public Vector3 Displacement => move;
    /// <summary>The controller's direction (m_kDir).</summary>
    public Vector3 Direction => dir;

    /// <summary>A new record (CSkillRecord's constructor, skillrecord.cpp:164): m_fDuration = m_fElapsed = 0.</summary>
    public void Record(CastMove data, float speedScale, float rotZ)
    {
        Move = data;
        Speed = speedScale;
        RotZ = rotZ;
        Duration = Elapsed = 0f;
        KeyMark = KeysFromStart;   // the record's sequence is activated anew
    }

    /// <summary>
    /// StateSkill::vfunc_4 (stateskill.cpp:457-480): m_bMove0 = m_bMove2 = true; the controller by the motion's m_eMoveType
    /// and its vfunc_2 (CMotionController::vfunc_2 clears m_kDir and m_fSpeed, CArcMotionController::vfunc_2 lays its
    /// curve); m_n10 = 0x3b with a held actor (<paramref name="holding"/>), else 0x3c when magicControl is enabled.
    /// </summary>
    public void Enter(ISkillMotionOwner owner, bool holding)
    {
        move0 = move2 = true;
        dir = move = Vector3.Zero; speed = 0f;
        NoMove = !holding && Move.MagicControl;
        if (Move.MotionMoveType == 1) ArcStart(owner);
    }

    /// <summary>
    /// StateSkill::TextKeyEvent's move keys (stateskill.cpp:958-977): an armed phase's "move0" / "move2" starts the record's
    /// move time (m_fElapsed, m_fDuration, m_f80 cleared, then <see cref="CalcSkillDuration"/> to "move1" / "move3").
    /// True when the key was one (TextKeyEvent returns then).
    /// </summary>
    public bool TextKey(ISkillMotionOwner owner, string key)
    {
        if (move0 && key == "move0")
        {
            move0 = false;
            Elapsed = Duration = 0f;
            CalcSkillDuration(owner.CastKeyTime("move0"), owner.CastKeyTime("move1"));
            return true;
        }
        if (move2 && key == "move2")
        {
            move2 = false;
            Elapsed = Duration = 0f;
            CalcSkillDuration(owner.CastKeyTime("move2"), owner.CastKeyTime("move3"));
            return true;
        }
        return false;
    }

    /// <summary>
    /// The sequence's text keys the play passed this frame, in order: each key with <paramref name="after"/> &lt; time
    /// &lt;= <paramref name="upTo"/> goes to <see cref="TextKey"/>.
    /// </summary>
    public void TextKeys(ISkillMotionOwner owner, List<(double time, string text)> keys, double after, double upTo)
    {
        for (int i = 0; i < keys.Count; i++)
        {
            var (t, key) = keys[i];
            if (t > after && t <= upTo) TextKey(owner, key);
        }
    }

    /// <summary>The read mark that puts every key of the sequence still to come, a key at time 0 too: an activated sequence's
    /// first update throws its keys from 0 (NiControllerSequence::ThrowAllTextKeys with m_bNeedFirstTextKey set).</summary>
    public const double KeysFromStart = -1;

    /// <summary>The sequence time the own PC's keys were read up to (<see cref="ReadKeys"/>).</summary>
    public double KeyMark { get; private set; } = KeysFromStart;

    /// <summary>The own PC's TextKeyEvent feed for one frame: the keys after <see cref="KeyMark"/> up to
    /// <paramref name="upTo"/> (<see cref="TextKeys"/>), then the mark moves there.</summary>
    public void ReadKeys(ISkillMotionOwner owner, List<(double time, string text)> keys, double upTo)
    {
        TextKeys(owner, keys, KeyMark, upTo);
        KeyMark = upTo;
    }

    /// <summary>
    /// StateSkill's SetTargetAnimation(seq, m_fSpeed, true), Update and SetOffset (the loop's next round, loopend
    /// stateskill.cpp:932-937; the charging release's skip, vfunc_8 :710-714): the forced restart deactivates the sequence
    /// (NiActorManager::Reset) and the Update activates it again with an update that throws no keys (NiActorManager::Update,
    /// m_bNeedFirstTextKey stays set), so the next frame throws its keys from 0 up to the new time
    /// (<see cref="KeysFromStart"/>). <paramref name="loopRestart"/>: the loop's round, after which FUN_1417f1a90 runs
    /// (<see cref="ArmMovePhases"/>, :941).
    /// </summary>
    public void Restarted(ISkillMotionOwner owner, bool loopRestart)
    {
        KeyMark = KeysFromStart;
        if (loopRestart) ArmMovePhases(owner);
    }

    /// <summary>StateSkill::CalcSkillDuration (@0x1417ec210, stateskill.cpp:1259-1267): with both key times, the record's
    /// m_fDuration is their span at m_fSpeed, 0.05 s longer for a movingInMovekey skill. A missing key here is -1
    /// (<see cref="ISkillMotionOwner.CastKeyTime"/>), the client's -FLT_MAX.</summary>
    public void CalcSkillDuration(float start, float end)
    {
        if (start < 0f || end < 0f) return;
        Duration = (end - start) / Speed;
        if (Move.MovingInMovekey) Duration += 0.05f;
    }

    /// <summary>
    /// StateSkill::FUN_1417f1a90 (@0x1417f1a90, stateskill.cpp:2316-2354), after each restart of the loop: a "move0" or
    /// "move2" key inside the loop (loopstart..loopend) arms its phase again and clears the record's move time; the phase
    /// ("move0".."move1" when move0 is armed, else "move2".."move3") that spans exactly the loop takes its duration at once.
    /// </summary>
    public void ArmMovePhases(ISkillMotionOwner owner)
    {
        float m0 = owner.CastKeyTime("move0"), m2 = owner.CastKeyTime("move2");
        if (m0 < 0f && m2 < 0f) return;
        float ls = owner.CastKeyTime("loopstart"), le = owner.CastKeyTime("loopend");
        if (ls < 0f || le < 0f) return;
        bool any = false;
        if (m0 >= 0f && ls <= m0 && m0 <= le) { any = true; move0 = true; }
        if (m2 >= 0f && ls <= m2 && m2 <= le) { any = true; move2 = true; }
        if (!any) return;
        Duration = Elapsed = 0f;
        float start = move0 ? m0 : m2, end = owner.CastKeyTime(move0 ? "move1" : "move3");
        if (NearlyEqual(start, ls) && NearlyEqual(end, le)) CalcSkillDuration(start, end);
    }

    /// <summary>CSkillRecord::GetRemainingDuration: 0 without a duration or past it, else the time left.</summary>
    public float RemainingDuration => NearlyEqual(Duration, 0f) || Duration <= Elapsed ? 0f : Duration - Elapsed;

    /// <summary>FUN_1417edc20 (stateskill.cpp:2866-2874): a skill of stateAttr moveType 2 or 3 also takes the move input at
    /// the owner's speed, unless a movingInMovekey skill's record has no time left.</summary>
    public bool TakesInput =>
        (Move.SkillMoveType == 2 || Move.SkillMoveType == 3) && !(Move.MovingInMovekey && NearlyEqual(RemainingDuration, 0f));

    /// <summary>The owner's move direction (GameObject m_pos98, what the sync record's direction carries) in the skill state:
    /// the state's enter sets it to zero (StateSkill::vfunc_4, stateskill.cpp:438) and each frame of a skill of stateAttr
    /// moveType 2 or 3 sets it to the move buffer's direction (vfunc_8, :642-647), whether or not the record has move time
    /// left. False keeps it zero.</summary>
    public bool ReadsMoveDirection => Move.SkillMoveType == 2 || Move.SkillMoveType == 3;

    /// <summary>
    /// The controller's vfunc_5 by the motion's movetype: <see cref="Displacement"/> is what its vfunc_3 hands vfunc_68.
    /// <paramref name="input"/> is the move input's direction (MS2 axes, unit or zero) the move controller follows.
    /// </summary>
    public void Step(ISkillMotionOwner owner, float dt, Vector3 input)
    {
        switch (Move.MotionMoveType)
        {
            case 0: LinearStep(owner, dt, false, input); break;
            case 1: ArcStep(dt); break;
            case 3: LinearStep(owner, dt, true, input); break;
            default: MotionBase(dt); break;     // CBlinkMotionController::vfunc_5: the base only, m_kMove stays zero
        }
    }

    /// <summary>
    /// CSkillRecord::FUN_141759b60 / FUN_141759d20 (skillrecord.cpp:274-312): while the record has move time left, the
    /// step's share of <paramref name="rate"/> (units, x0.01 to meters) over m_fDuration; the step is added to m_fElapsed.
    /// </summary>
    float RecordStep(float dt, float rate)
    {
        if (!(Duration > 0f && Elapsed < Duration)) return 0f;
        float step = dt;
        if (Elapsed + step > Duration) step = Duration - Elapsed;
        Elapsed += step;
        if (Elapsed > Duration) Elapsed = Duration;
        return rate * 0.01f * (step / Duration);
    }

    /// <summary>
    /// CMotionController::vfunc_5 (@0x141488080): m_fSpeed is the record's step (keyMove, or movedistance when set); the
    /// direction is the record's m_kStart without a movedistance, else the record rotation's forward
    /// (GameObject::RotationToDirection) turned by moveangle about Z and, for a positive angle, tilted by it about
    /// forward x Z.
    /// STILL-OPEN: IsDashModel's equipment speed (FUN_1411e0f50) and the FUN_141489940 == 10 euler base are not ported.
    /// </summary>
    void MotionBase(float dt)
    {
        speed = RecordStep(dt, NearlyEqual(Move.MoveDistance, 0f) ? Move.KeyMove : Move.MoveDistance);
        dir = Unit(NearlyEqual(Move.MoveDistance, 0f) ? Move.Direction : TiltedForward());
    }

    /// <summary>The tilted base direction of CMotionController::vfunc_5 and CArcMotionController::vfunc_2: efd's
    /// MakeZRotation and MakeRotation (row-major, M * v) on (sin z, -cos z, 0).</summary>
    Vector3 TiltedForward()
    {
        const float r = 3.1415927f / 180f;
        float z = RotZ * r, a = Move.MoveAngle * r;
        var b = new Vector3(MathF.Sin(z), -MathF.Cos(z), 0f);
        float cs = MathF.Cos(a), sn = MathF.Sin(a);
        var d = new Vector3(cs * b.X + sn * b.Y, -sn * b.X + cs * b.Y, b.Z);
        if (a > 0f) d = Rotate(-a, Unit(Vector3.Cross(d, Vector3.UnitZ)), d);
        return d;
    }

    /// <summary>efd::Matrix3::MakeRotation(<paramref name="angle"/>, axis) times <paramref name="v"/>.</summary>
    static Vector3 Rotate(float angle, Vector3 axis, Vector3 v)
    {
        float cs = MathF.Cos(angle), sn = MathF.Sin(angle), om = 1f - cs, x = axis.X, y = axis.Y, z = axis.Z;
        return new Vector3(
            (x * x * om + cs) * v.X + (x * y * om + z * sn) * v.Y + (x * z * om - y * sn) * v.Z,
            (x * y * om - z * sn) * v.X + (y * y * om + cs) * v.Y + (y * z * om + x * sn) * v.Z,
            (x * z * om + y * sn) * v.X + (y * z * om - x * sn) * v.Y + (z * z * om + cs) * v.Z);
    }

    /// <summary>
    /// CLinearMotionController::vfunc_5 (@0x141488e80) and, with <paramref name="useInput"/>, CMoveMotionController::vfunc_5
    /// (@0x141489250, along the move input instead): no move while the PC is in the air (vfunc_99: 100 ms, the move
    /// controller 0) or jumping, or without a speed or direction; a step that would end over a ledge
    /// (FUN_1416725c0(dir, speed * 100, 750, 0)) clears the record's move time instead.
    /// </summary>
    void LinearStep(ISkillMotionOwner owner, float dt, bool useInput, Vector3 input)
    {
        MotionBase(dt);
        move = Vector3.Zero;
        if (owner.InAirFor(useInput ? 0 : 100) || owner.Jumping) return;
        if (NearlyEqual(speed, 0f)) return;
        if (useInput) dir = input;
        if (dir == Vector3.Zero) return;
        float dist = speed * 100f;
        if (!owner.LedgeAhead(dir, dist, 750f, 0f)) move = dir * dist;
        else Elapsed = Duration = 0f;
    }

    /// <summary>
    /// CArcMotionController::vfunc_2 (@0x141488520) at the state's entry: a 0.4 s quadratic bezier from the PC's place
    /// to movedistance along the tilted forward, its control point halfway and 100 higher.
    /// </summary>
    void ArcStart(ISkillMotionOwner owner)
    {
        arcElapsed = 0f;
        arcStart = arcLast = owner.Position;
        arcEnd = arcStart + TiltedForward() * Move.MoveDistance;
        arcCtrl = arcStart * 0.5f + arcEnd * 0.5f;
        arcCtrl.Z += 100f;
    }

    /// <summary>
    /// CArcMotionController::vfunc_5 (@0x141488ba0): the base step, then the record's elapsed again (FUN_141759d20, its
    /// speed unused here), and the move from the last bezier point to the one at elapsed / 0.4 (vfunc_3: m_kHeading,
    /// in meters there).
    /// </summary>
    void ArcStep(float dt)
    {
        MotionBase(dt);
        arcElapsed += dt;
        RecordStep(dt, 0f);
        float t = Math.Clamp(arcElapsed / 0.4f, 0f, 1f);
        var p = BezierPoint(arcStart, arcCtrl, arcEnd, t);
        var heading = p - arcLast;
        dir = Unit(heading);
        arcLast = p;
        move = heading;
    }

    /// <summary>GetBezierPoint (util.cpp:1191): a straight line when the control point is an end, else the quadratic
    /// bezier.</summary>
    static Vector3 BezierPoint(Vector3 a, Vector3 c, Vector3 b, float t)
    {
        if (a == c || b == c) return a + (b - a) * t;
        float u = 1f - t;
        return a * (u * u) + c * (2f * t * u) + b * (t * t);
    }

    /// <summary>IsNearlyEqual (@0x14028d470, commonutil.cpp): |a - b| &lt; 1e-6.</summary>
    static bool NearlyEqual(float a, float b) => MathF.Abs(a - b) < 1e-6f;

    static Vector3 Unit(Vector3 v)
    {
        float l = v.Length();
        return l > 1e-6f ? v / l : Vector3.Zero;    // efd::Point3::Unitize leaves a zero vector at zero
    }
}
