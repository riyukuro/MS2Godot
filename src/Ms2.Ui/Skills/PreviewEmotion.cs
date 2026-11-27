namespace Ms2.Ui.Skills;

/// <summary>
/// The skill preview avatar's emotion: UIAvatarPC::FUN_1418bce80 (@0x1418bce80, uiavatarbase.cpp:1509) hands the row to
/// PCBase::FUN_14166ed70 (pcbase.cpp:3674-3685) of the dialog's clone pc, which enters StateEmotion (0xb,
/// stateemotionidle.cpp:196-352) and from its EndOfSequence StateEmotionIdle (0xf, :646-870) or StateIdle. The same states the own
/// pc runs (the game's Player emotion part) without their input step (vfunc_8: the clone takes no keys):
/// <list type="bullet">
/// <item>Enter (StateEmotion::vfunc_4 @0x1417c3990, :208-241): skipped while 0xb already plays the row's paramStr
/// (<see cref="EmotionCast.Enters"/>); else entered again (the exit first). A row that is no main type 0xe plays nothing and
/// leaves for StateIdle at the next frame (vfunc_3, :300-322); else the face plays the paramStr and the body the emotion's anim
/// (kind 0) from its start.</item>
/// <item>EndOfSequence (@0x1417c4150, :325-336): with an idleAnim (kind 1) on to StateEmotionIdle, else StateIdle.</item>
/// <item>StateEmotionIdle::vfunc_4 (@0x1417c5d10, :657-688): the face again and the idleAnim from its start; vfunc_9
/// (@0x1417c6810, :806-829) plays it each frame without bForce.</item>
/// <item>The exits (vfunc_5 @0x1417c3cb0 :244-252, @0x1417c61a0 :709-717): the face rests unless 0xb goes on to 0xf.</item>
/// </list>
/// The host plays the sequences (<see cref="Play"/>, with the kf's own cycle) and the face (<see cref="Face"/>,
/// <see cref="FaceRest"/>). STILL-OPEN: the bore animations (StateEmotionIdle::vfunc_8's timer, :777-802, sits in the input
/// step; whether the clone runs it is not traced, so the idleAnim plays on); vfunc_3's IsEmotionLearned (the own skill system's);
/// the state effects (State::FUN_1417b92a0).
/// </summary>
public sealed class PreviewEmotion
{
    /// <summary>PCBase::PlayAnimation(name, 1, bForce, false): the host plays the body's sequence of that name with its kf's
    /// cycle and gives its length (s) and cycle (0 a loop, 1 back and forth, 2 clamped); null when the body has none, or when
    /// without bForce it plays already.</summary>
    public Func<string, bool, (double Length, int Cycle)?> Play = (_, _) => null;
    /// <summary>PCBase::GetEmotionAnimation(name, kind) of the avatar's emotion group (<see cref="EmotionCast.Animation"/>).</summary>
    public Func<string, int, string> Animation = (_, _) => null;
    /// <summary>PCBase::FUN_14166ef10(name, 1, 0, false): the face plays the emotion.</summary>
    public Action<string> Face = _ => { };
    /// <summary>PCBase::FUN_14166f8f0(0, 1): the face rests.</summary>
    public Action FaceRest = () => { };
    /// <summary>StateIdle: the body's idle.</summary>
    public Action Idle = () => { };

    /// <summary>The state: 0 (StateIdle), <see cref="EmotionCast.StateEmotion"/> or <see cref="EmotionCast.StateEmotionIdle"/>.</summary>
    public int State { get; private set; }
    /// <summary>m_strAnim: the row's paramStr while an emotion state plays, else "".</summary>
    public string Anim { get; private set; } = "";

    SkillData row;
    string loop;                       // StateEmotion's m_strLoop, null when the emotion has no anim
    double left = -1;                  // seconds until the played sequence ends; below 0 none ends
    double period = -1;                // a looped sequence's length: its EndOfSequence comes each pass

    /// <summary>FUN_14166ed70 then StateEmotion::vfunc_4.</summary>
    public void Enter(SkillData skill)
    {
        if (!EmotionCast.Enters(skill, State, State == EmotionCast.StateEmotion ? Anim : null)) return;
        if (State != 0) Exit(EmotionCast.StateEmotion);
        State = EmotionCast.StateEmotion;
        row = skill;
        Anim = "";
        loop = null;
        left = -1;
        if (skill.MainType != EmotionCast.MainTypeEmotion) return;
        Anim = skill.ParamStr ?? "";
        Face(Anim);
        loop = Animation(Anim, 0);
        if (loop != null) PlaySequence(loop, true);
    }

    /// <summary>The frame: StateEmotion's vfunc_3 and EndOfSequence, StateEmotionIdle's vfunc_9.</summary>
    public void Tick(double dt)
    {
        if (State == EmotionCast.StateEmotion)
        {
            if (loop == null) { ToIdle(); return; }
            if (!Ended(dt)) return;
            if (Animation(Anim, 1) != null) EnterIdle();
            else ToIdle();
        }
        else if (State == EmotionCast.StateEmotionIdle)
        {
            Ended(dt);
            if (Animation(Anim, 1) is { } idle) PlaySequence(idle, false);
        }
    }

    void EnterIdle()
    {
        var r = row;
        Exit(EmotionCast.StateEmotionIdle);
        State = EmotionCast.StateEmotionIdle;
        row = r;
        left = -1;
        if (r == null || r.MainType != EmotionCast.MainTypeEmotion) { ToIdle(); return; }
        Anim = r.ParamStr ?? "";
        Face(Anim);
        if (Animation(Anim, 1) is { } idle) PlaySequence(idle, true);
    }

    void ToIdle()
    {
        if (State != 0) Exit(0);
        State = 0;
        Idle();
    }

    void Exit(int next)
    {
        int old = State;
        left = -1;
        if (old == EmotionCast.StateEmotionIdle || next != EmotionCast.StateEmotionIdle) FaceRest();
        if (old == EmotionCast.StateEmotionIdle) { row = null; Anim = ""; }
    }

    /// <summary>EndOfSequence comes at a clamped sequence's end and at the end of each pass of a looped one (as the own pc's).</summary>
    void PlaySequence(string name, bool force)
    {
        if (Play(name, force) is not { } p) return;
        bool looped = p.Cycle == 0;
        left = p.Cycle == 2 || looped ? p.Length : -1;
        period = looped && p.Length > 0 ? p.Length : -1;
    }

    bool Ended(double dt)
    {
        if (left < 0) return false;
        left -= dt;
        if (left > 0) return false;
        left = period > 0 ? Math.Max(left + period, 0.001) : -1;
        return true;
    }
}
