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
        skillHeld = false;
        skillHoldsMove = holdsMove;
        kVel = Vector3.Zero;   // the state's enter, vfunc_4 (stateskill.cpp:437): the owner's velocity 0 (the vertical one is kept here)
        return true;
    }

    /// <summary>The own cast's skill state holds the move, jump and click-move input (StateSkill moves by its motion only,
    /// stateskill.cpp:2855-2866): true while a sequence played with holdsMove plays. Asked each frame, so the sequence's end
    /// (<see cref="SkillSequenceEnding"/>) is seen in the frame it comes.</summary>
    bool SkillHoldsMove() => SkillSequencePlaying() && skillHoldsMove;

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

    /// <summary>
    /// The skill state's end step (StateSkill::FUN_1417ee120, stateskill.cpp:2885-2992), asked once when the own cast's
    /// sequence reaches its end, while the state still holds the body: true keeps the state (the HUD's next cast, or
    /// <see cref="HoldSkillSequence"/>); a new sequence it plays takes over at once, so no movement frame plays between.
    /// </summary>
    public System.Func<bool> SkillSequenceEnding;
    bool skillHeld;         // the ended sequence's last frame held: the skill state lasts past its sequence (HoldSkillSequence)
    bool skillEnding;       // SkillSequenceEnding runs

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
}
