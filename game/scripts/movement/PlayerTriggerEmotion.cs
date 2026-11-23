using Ms2.Ui.Guide;

namespace Ms2Godot;

/// <summary>
/// The own pc's StateTriggerEmotion (0x35, stateemotionidle.cpp:369-463, <see cref="Ms2.Ui.Guide.TriggerEmotion"/>), which
/// the trigger packet's guide type subs 7 and 8 enter (CTriggerService FUN_141863e50 / FUN_141864010). The state has no
/// frame of its own (State::vfunc_3 is empty, state.cpp:594): the body does not move and the move keys do nothing.
/// STILL-OPEN: State::vfunc_8 (state.cpp:634-640), a skill key casting from the state; State::vfunc_4's speed setup
/// (vfunc_10, state.cpp:600-611); other players in the state (their sync records); the own pc's sync record carries state
/// 0x35 sub 0 but no state data (vfunc_27, @0x1417c5680, is not read).
/// </summary>
public partial class Player
{
    readonly TriggerEmotion triggerEmotion = new();
    double triggerEmotionLeft = -1;   // seconds until the played sequence ends (its EndOfSequence); below 0 none plays

    /// <summary>The state's m_nFlag while it runs (State::vfunc_16 of the current state).</summary>
    public bool TriggerEmotionWeapons => State == MoveState.TriggerEmotion && triggerEmotion.Flag != 0;

    /// <summary>
    /// vfunc_59(0x35, data, 0, bForce) on the own pc. PC::vfunc_59 (pc.cpp:1189-1194) refuses it while dead (vfunc_124);
    /// CStateManager::ChangeState (statemanager.cpp:291-314) keeps the running state when it is already 0x35 and bForce is
    /// 0 (State::vfunc_25 returns true, state.cpp:751-756). Then vfunc_4 (@0x1417c4500, stateemotionidle.cpp:384-416):
    /// SendRequestCube with a held item, the data copied, the weapons in the hands with the flag (FUN_141657c80(true)),
    /// and the first name played at speed 1 without a loop.
    /// </summary>
    public void EnterTriggerEmotion(TriggerEmotionData data)
    {
        if (DeadFlag) return;
        if (State == MoveState.TriggerEmotion && !data.Force) return;
        Enter(MoveState.TriggerEmotion);
        HeldStateEntered?.Invoke(Ms2.Ui.HeldItemState.TriggerEmotion);
        string first = triggerEmotion.Enter(data, (long)(gameTime * 1000));
        UpdateWeaponStance();
        PlayTriggerEmotion(first);
    }

    /// <summary>PCBase::PlayAnimation (@0x14166dd20, pcbase.cpp:1521-1527) of a body sequence by name; the time to its end
    /// is kept for EndOfSequence. A name the body has no sequence for plays nothing and never ends.</summary>
    void PlayTriggerEmotion(string name)
    {
        triggerEmotionLeft = -1;
        if (string.IsNullOrEmpty(name) || Anims == null || Anim == null) return;
        string key = Anims.Sequence(name, false);
        if (key == null) return;
        PlaySequence(key, 1f, restart: true);
        triggerEmotionLeft = Anim.GetAnimation(key).Length + System.Math.Max(blendHold, 0f);
    }

    /// <summary>The state's frame: no move (vfunc_3 is empty). When the played sequence ends, EndOfSequence (@0x1417c47d0,
    /// stateemotionidle.cpp:431-460) plays the next name or goes back to StateIdle (vfunc_59(1)).</summary>
    void TriggerEmotionUpdate(float dt)
    {
        kVel = Godot.Vector3.Zero;
        if (triggerEmotionLeft < 0) return;
        triggerEmotionLeft -= dt;
        if (triggerEmotionLeft > 0) return;
        string next = triggerEmotion.EndOfSequence((long)(gameTime * 1000));
        if (next == null) { Enter(MoveState.Idle); return; }
        PlayTriggerEmotion(next);
    }

    /// <summary>vfunc_5 (@0x1417c4770, stateemotionidle.cpp:419-423): m_nFlag = 0.</summary>
    void TriggerEmotionExit()
    {
        triggerEmotion.Leave();
        triggerEmotionLeft = -1;
    }
}
