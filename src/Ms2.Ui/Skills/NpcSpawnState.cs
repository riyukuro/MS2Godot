namespace Ms2.Ui.Skills;

/// <summary>
/// An npc's StateSpawn (0x11, statespawn.cpp): entered from a sync block of the spawn state (CNpc::ApplySyncBlock,
/// unit_14108e1e0_npcactorandvibrate.cpp:975-984) with the sequence of the field's spawn point (<see cref="Enter"/>), it hides
/// the npc and its shadow for a delay drawn from the object id, then shows it, plays the sequence and goes to the idle state
/// once the sequence's length has passed. Times are the game clock's (Timer, timer.cpp: milliseconds of clock 0xe001001b).
/// The host plays the npc data's regenEffect on <see cref="Step.Show"/> (:114-127; FUN_1417b8480 is the owner as a CNpc).
/// </summary>
public sealed class NpcSpawnState
{
    /// <summary>What a frame of the state does (<see cref="Update"/>).</summary>
    public enum Step
    {
        /// <summary>The delay runs; the npc stays hidden.</summary>
        Wait,
        /// <summary>Nothing to do this frame.</summary>
        None,
        /// <summary>The delay ended: the npc is shown (RemoveHideReason(10)) and <see cref="Animation"/>, if any, plays forced at 1.</summary>
        Show,
        /// <summary>The time reached the sequence's length: the npc goes to the idle state (vfunc_59(1)).</summary>
        Idle,
        /// <summary><see cref="Show"/> and <see cref="Idle"/> in the same frame (a point without a sequence, or a sequence of no length).</summary>
        ShowAndIdle,
    }

    /// <summary>The hide reason the state sets (AddHideReason(10)).</summary>
    public const int HideReason = 10;

    /// <summary>Whether the state runs (entered and not left).</summary>
    public bool Active { get; private set; }
    /// <summary>The spawn point's sequence (StateSpawnData m_str); null when the block named no point of the field.</summary>
    public string Animation { get; private set; }
    /// <summary>The delay in ms (100..399), from <see cref="DelayMs"/>.</summary>
    public int Delay { get; private set; }

    double end;            // m_timer's end, ms
    float elapsed;         // m_fElapsed
    float duration;        // m_fDuration

    /// <summary>Unk_Random(objectId).FUN_1407684c0() % 300 + 100 (vfunc_4, :65-77): the hide time in ms.</summary>
    public static int DelayMs(int objectId) => (int)(new Ms2.Quest.QuestRandom(objectId).Next() % 300) + 100;

    /// <summary>
    /// vfunc_4 (@0x1417f6140): the state is entered at <paramref name="nowMs"/> with the point's sequence (null without one);
    /// the npc and its shadow are hidden by the caller, and the delay's timer starts.
    /// </summary>
    public void Enter(int objectId, string animation, double nowMs)
    {
        Active = true;
        Animation = animation;
        elapsed = 0; duration = 0;
        Delay = DelayMs(objectId);
        end = nowMs + Delay;
    }

    /// <summary>vfunc_5 (@0x1417f6260): the state is left; the caller shows the npc and its shadow.</summary>
    public void Leave()
    {
        Active = false;
        elapsed = 0; duration = 0;
    }

    /// <summary>
    /// vfunc_3 (@0x1417f62f0): one frame of <paramref name="dt"/> seconds at <paramref name="nowMs"/>. While the timer runs, nothing.
    /// On the first frame after it (the time still 0) the npc is shown and the sequence plays, its length from
    /// <paramref name="length"/> (GetAnimationDuration, 0 for a sequence the body lacks); then the time grows by
    /// <paramref name="dt"/>, and at the length the state ends in the idle state.
    /// </summary>
    public Step Update(float dt, double nowMs, Func<string, float> length)
    {
        if (!Active) return Step.None;
        if (nowMs < end) return Step.Wait;
        bool show = false;
        if (MathF.Abs(elapsed) < 1e-5f)
        {
            show = true;
            if (!string.IsNullOrEmpty(Animation)) duration = length(Animation);
        }
        elapsed += dt;
        if (elapsed >= duration)
        {
            Leave();
            return show ? Step.ShowAndIdle : Step.Idle;
        }
        return show ? Step.Show : Step.None;
    }
}
