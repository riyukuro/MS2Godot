using Godot;

namespace Ms2Godot;

// The own cast's motion on the body.
public partial class Player
{
    string skillSequence;   // the own cast's sequence while it plays; null otherwise

    /// <summary>
    /// The own cast's motion (StateSkill plays the record's motion sequence at its sequenceSpeed): the body's &lt;name&gt;.kf, the
    /// same file the other players' casts play (ReplayField.SkillAnim). False when the body has no such sequence.
    /// STILL-OPEN: the skill state's move, cancel and key-held rules (StateSkill) are not ported; the movement animations only
    /// wait until the sequence ends.
    /// </summary>
    public bool PlaySkillSequence(string sequenceName, float speed)
    {
        var key = Anims?.Sequence(sequenceName, false);
        if (key == null || Anim == null) return false;
        PlaySequence(key, speed, true);
        skillSequence = key;
        return true;
    }

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
