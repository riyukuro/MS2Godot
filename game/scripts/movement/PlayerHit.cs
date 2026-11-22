using Ms2.Ui.Skills;

namespace Ms2Godot;

// The own PC's hit reaction state (StateHit 0xd) and its push (PCBase::m_pushControl).
public partial class Player : IHitBody
{
    /// <summary>PCBase m_pushControl: the push a hit started (<see cref="PushControl.Start"/>). Entering StateIdle
    /// (StateIdle::vfunc_4, stateidle.cpp:187-189) or StateDead (statedead.cpp:150-153) resets it on the own PC.
    /// STILL-OPEN: the tick that moves the body by it (CPushControl::vfunc_1 from PCBase::vfunc_53) is not run yet.</summary>
    public PushControl Push { get; } = new();

    /// <summary>StateHit's own copy of its StateHitData (vfunc_4, statehit.cpp:62-80: m_f9c pause, m_fA4 duration, m_fA8
    /// distance, m_fB0 lift, m_nB4 down, m_nBc priority, m_strC0 animation), cleared by FUN_1417cb1d0 on entering.</summary>
    readonly StateHitData hitState = new();
    /// <summary>StateHit m_strAnim: the sequence it plays; empty when the body has none.</summary>
    string hitAnim = "";

    /// <summary>The client state the own PC is in for a hit's tests: StateSkill (0x10) while the own cast's sequence holds the
    /// body, else the move state's id.</summary>
    public int ClientStateId => skillSequence != null ? HitReaction.StateSkill : StateId(State);

    /// <summary>
    /// State::vfunc_14 (m_n14, 1 from State's ctor, state.cpp:569) of the current state: StateSkill's is whether the push
    /// runs with a speed above 0 (stateskill.cpp:1300); StateIdle's is 0 on water (m_n10 7, stateidle.cpp:493); StateWalk
    /// and StateHit keep 1; jump, fall, land, swim, ladder, rope, climb, dash, dead and revival set 0 in their ctors.
    /// </summary>
    public bool StateTakesHits => skillSequence != null ? Push.Speed > 0f : State switch
    {
        MoveState.Idle => !OnWater,
        MoveState.Walk or MoveState.Hit => true,
        _ => false,
    };

    /// <summary>StateHit::HasHitDuration (statehit.cpp:239) of the current state; false outside StateHit.</summary>
    public bool HitHasDuration => State == MoveState.Hit && !HitReaction.NearlyZero(hitState.Duration);
    /// <summary>StateHit::GetPushPriority (statehit.cpp:245) of the current state; 0 outside StateHit.</summary>
    public int HitPushPriority => State == MoveState.Hit ? hitState.Priority : 0;

    /// <summary>The current StateHit's data (a copy of what entered it).</summary>
    public StateHitData HitState => hitState;

    /// <summary>
    /// PC::vfunc_59(0xd, data) from the hit reaction (pc.cpp:943; vfunc_34's dot passes no data, :978) -> StateHit::vfunc_4
    /// (statehit.cpp:62): the state's fields are cleared, then copied from the data with the body's z.
    /// The vfunc_59 leaves StateSkill (State::vfunc_5), so the own cast's sequence ends.
    /// STILL-OPEN: the sequence it plays (Jump_FlyDamg_A with the lift, else the body's hit action vfunc_123(0x35) or the
    /// data's pushAnimation), the lift (GameObjectController::StartLift), the 70% timer, the end time and the escape keys are
    /// not ported; with no sequence, StateHit::vfunc_9 (statehit.cpp:148-152) goes on to StateIdle on the next update.
    /// </summary>
    /// <param name="force">vfunc_59's bForce: vfunc_33 passes the data's duration above 0.</param>
    public void EnterHit(StateHitData data, bool force)
    {
        if (DeadFlag) return;   // PC::vfunc_124 (pc.cpp:1199)
        if (StateKept(MoveState.Hit, force)) return;
        CancelSkillSequence();
        StopInteractAnim();
        Enter(MoveState.Hit);
        hitState.Clear();
        if (data != null)
        {
            hitState.SpeedRate = data.SpeedRate;
            hitState.PauseTime = data.PauseTime;
            hitState.Duration = data.Duration;
            hitState.Distance = data.Distance;
            hitState.UpDistance = data.UpDistance;
            hitState.Down = data.Down;
            hitState.Priority = data.Priority;
            hitState.Animation = data.Animation;
        }
        hitAnim = "";
    }

    /// <summary>PC::vfunc_59(1) from the hit reaction (pc.cpp:946-950): a push the PC does not react to, or a skill hit in
    /// state 0x2f, puts it in StateIdle; StateSkill's exit ends the own cast's sequence.</summary>
    public void ReactIdle()
    {
        if (DeadFlag) return;   // PC::vfunc_124 (pc.cpp:1199)
        if (StateKept(MoveState.Idle, false)) return;
        CancelSkillSequence();
        StopInteractAnim();
        Enter(MoveState.Idle);
    }

    /// <summary>CStateManager::ChangeState (statemanager.cpp:297): a change to the current state without bForce stays in it
    /// (State::vfunc_25 with no sub state returns true, state.cpp:751; StateIdle and StateHit keep it).</summary>
    bool StateKept(MoveState s, bool force) => !force && skillSequence == null && State == s;

    /// <summary>StateHit::vfunc_9 (statehit.cpp:148): with no sequence the state goes on to StateIdle (vfunc_59(1)).</summary>
    void HitUpdate()
    {
        if (hitAnim.Length == 0) Enter(MoveState.Idle);
    }

    /// <summary>The own PC's position in the client's z-up field coordinates (vfunc_18).</summary>
    public System.Numerics.Vector3 FieldPosition => new(GlobalPosition.X, -GlobalPosition.Z, GlobalPosition.Y);
}
