using Godot;

namespace Ms2Godot;

// The own cast's motion on the body.
public partial class Player
{
    string skillSequence;   // the own cast's sequence while it plays; null otherwise
    bool skillHoldsMove;    // the skill state does not take the move input (Ms2.Ui.Skills.SkillFollow.HoldsMove)

    /// <summary>
    /// The own cast's motion (StateSkill plays the record's motion sequence at its sequenceSpeed): the body's &lt;name&gt;.kf, the
    /// same file the other players' casts play (ReplayField.SkillAnim). False when the body has no such sequence.
    /// <paramref name="holdsMove"/>: the skill state does not read the move input while it plays (SkillFollow.HoldsMove,
    /// <see cref="SkillHoldsMove"/>). STILL-OPEN: a moveType 2 / 3 skill's own move (StateSkill::FUN_1417edc20,
    /// stateskill.cpp:2866-2874: the input at the owner's speed inside the skill state) goes through the walk state here.
    /// </summary>
    public bool PlaySkillSequence(string sequenceName, float speed, bool holdsMove)
    {
        var key = Anims?.Sequence(sequenceName, false);
        if (key == null || Anim == null) return false;
        PlaySequence(key, speed, true);
        skillSequence = key;
        skillHoldsMove = holdsMove;
        return true;
    }

    /// <summary>The own cast's skill state holds the move, jump and click-move input (StateSkill moves by its motion only,
    /// stateskill.cpp:2855-2866): true while a sequence played with holdsMove plays.</summary>
    bool SkillHoldsMove() => skillHoldsMove && SkillSequencePlaying();

    /// <summary>The own cast's sequence while it plays, else null.</summary>
    public string SkillSequence => skillSequence;

    /// <summary>The own cast's sequence time in seconds (the kf's time, the one its text keys use), or -1 when it does not play.</summary>
    public double SkillSequencePosition =>
        skillSequence != null && (object)CurrentAnim == skillSequence && Anim != null ? Anim.CurrentAnimationPosition : -1;

    /// <summary>The own cast's sequence moved to <paramref name="time"/> seconds of the kf (a continue round's
    /// m_pSequence->SetOffset, stateskill.cpp:937); nothing when it does not play.</summary>
    public void SeekSkillSequence(double time)
    {
        if (SkillSequencePosition >= 0) Anim.Seek(time, true);
    }

    /// <summary>True while the own cast's sequence plays (PlayAct leaves the body to it); clears itself when it ends.</summary>
    bool SkillSequencePlaying()
    {
        if (skillSequence == null) return false;
        if ((object)CurrentAnim == skillSequence && (blendHold >= 0 || Anim.IsPlaying())) return true;
        skillSequence = null;
        return false;
    }
    /// <summary>The own cast's sequence is cancelled (State::vfunc_7(1)): the movement animations take the body back.</summary>
    public void CancelSkillSequence() => skillSequence = null;
}
