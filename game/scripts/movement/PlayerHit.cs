using System;
using Godot;
using Ms2.Ui.Skills;
using Ms2Godot.Movement;

namespace Ms2Godot;

// The own PC's hit reaction state (StateHit 0xd) and its push (PCBase::m_pushControl).
public partial class Player : IHitBody, IPushBody
{
    /// <summary>PCBase m_pushControl: the push a hit started (<see cref="PushControl.Start"/>), moved each frame by
    /// <see cref="PushControl.Tick"/> after the state's move (PCBase::vfunc_53, pcbase.cpp:3737). Entering StateIdle
    /// (StateIdle::vfunc_4, stateidle.cpp:187-189) or StateDead (statedead.cpp:150-153) resets it on the own PC.</summary>
    public PushControl Push { get; } = new();

    /// <summary>StateHit's fields (statehit.cpp:62-80), its end time and the sequence it picked.</summary>
    readonly StateHitTimeline hit = new();
    /// <summary>GameObjectController's lift, which StateHit starts on a lifting hit (statehit.cpp:88-92).</summary>
    readonly LiftArc lift = new();
    /// <summary>StateHit m_strAnim: the body's key of the sequence it plays; empty when the body has none.</summary>
    string hitAnim = "";
    /// <summary>The client's name of the sequence playing (EndOfSequence compares names); its end is taken once per play.</summary>
    string hitPlaying = "";
    bool hitSeqEnded;
    /// <summary>The game time StateHit reads (CMainSystem::GetGameTime): the frames' scaled deltas, seconds.</summary>
    double gameTime;
    /// <summary>The previous move state, for GetPrevState (the push tick's StateHold test).</summary>
    MoveState prevState;
    Predicate<string> hasSequence;

    /// <summary>Client::Constants EscapeHitMethod* (constantsblocks.cpp:803-806), read once like StateHit::vfunc_8's
    /// statics (statehit.cpp:184-187); the field loads them from table/constants.xml.</summary>
    public static EscapeHitMethods EscapeHit = new();

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
    public bool HitHasDuration => State == MoveState.Hit && !HitReaction.NearlyZero(hit.Data.Duration);
    /// <summary>StateHit::GetPushPriority (statehit.cpp:245) of the current state; 0 outside StateHit.</summary>
    public int HitPushPriority => State == MoveState.Hit ? hit.Data.Priority : 0;

    /// <summary>The current StateHit's data (a copy of what entered it).</summary>
    public StateHitData HitState => hit.Data;

    /// <summary>
    /// PC::vfunc_59(0xd, data) from the hit reaction (pc.cpp:943; vfunc_34's dot passes no data, :978) -> StateHit::vfunc_4
    /// (@0x1417ca370, statehit.cpp:62-110): the fields are cleared, then copied from the data with the body's z; a lifting
    /// hit ends any lift and starts its own (EndLift, StartLift(duration, lift, owner z); a started lift ends the jump),
    /// then the sequence: Jump_FlyDamg_A with the lift, else the body's hit action, replaced by the data's animation when
    /// the body has it; it plays at the data's speed from its start. The vfunc_59 leaves StateSkill (State::vfunc_5), so
    /// the own cast's sequence ends.
    /// STILL-OPEN: the body's hit action (vfunc_123(0x35), action 53) is not in the action table, so a hit without a
    /// custom animation or a lift has no sequence and StateHit::vfunc_9 goes on to StateIdle; the 70% timer
    /// (statehit.cpp:104, read by vfunc_36) is not run, as vfunc_36's callers are not ported.
    /// </summary>
    /// <param name="force">vfunc_59's bForce: vfunc_33 passes the data's duration above 0.</param>
    public void EnterHit(StateHitData data, bool force)
    {
        if (DeadFlag) return;   // PC::vfunc_124 (pc.cpp:1199)
        if (StateKept(MoveState.Hit, force)) return;
        CancelSkillSequence();
        StopInteractAnim();
        Enter(MoveState.Hit);
        hit.Enter(data, gameTime, FieldPosition.Z);
        if (hit.Lifts)
        {
            EndLift();
            if (lift.Start(hit.Data.Duration, hit.Data.UpDistance, hit.OwnerZ)) jumping = false;
        }
        hasSequence ??= s => Anims?.Sequence(s, false) != null;
        string seq = hit.PickSequence("", hasSequence);
        PlayHitSequence(seq, true, hit.Speed);
    }

    void PlayHitSequence(string seq, bool restart, float speed)
    {
        string key = seq.Length > 0 ? Anims?.Sequence(seq, false) : null;
        hitAnim = key ?? "";
        hitPlaying = key != null ? seq : "";
        hitSeqEnded = false;
        if (key != null) PlaySequence(key, speed, restart);
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

    /// <summary>
    /// StateHit's frame. EndOfSequence (@0x1417caaf0, statehit.cpp:155-175) when its sequence ends: on to StateIdle, or the
    /// fly sequence again from its start; vfunc_3 (@0x1417ca8a0, :116-130) after the move: at the end time Damg_C plays
    /// when the data has down (no restart; a body without it stays in the state), else StateIdle; vfunc_8 (@0x1417cad10,
    /// :179-199) the escape by the client's constants; vfunc_9 (:148-152) StateIdle with no sequence.
    /// </summary>
    /// <param name="held">the held keys' move (Unk_PCMove::GetMoveDirection).</param>
    /// <param name="pressed">the keys pressed this frame (GetPressedMoveDirection).</param>
    void HitUpdate(Vector2 held, Vector2 pressed, bool jumpPressed)
    {
        kVel = Vector3.Zero;   // vfunc_3: vfunc_68(ZERO)
        if (!hitSeqEnded && hitAnim.Length > 0 && CurrentAnim == hitAnim && !Anim.IsPlaying())
        {
            hitSeqEnded = true;
            var end = hit.EndOfSequence(hitPlaying);
            if (end == HitStep.Idle) { Enter(MoveState.Idle); return; }
            if (end == HitStep.ReplayFly) PlayHitSequence(hitPlaying, true, 1f);   // PlayAnimation("Jump_FlyDamg_A", 1.0f, true): not the data's speed
        }
        var step = hit.Update(gameTime);
        if (step == HitStep.Idle) { Enter(MoveState.Idle); return; }
        if (step == HitStep.Down && hitPlaying != StateHitTimeline.DownSequence)
        {
            // PlayAnimation("Damg_C", 1, false): the speed is 1, not the data's
            string key = Anims?.Sequence(StateHitTimeline.DownSequence, false);
            if (key != null)
            {
                hitAnim = key;
                hitPlaying = StateHitTimeline.DownSequence;
                hitSeqEnded = false;
                PlaySequence(key, 1f, false);
            }
        }
        // vfunc_8: vfunc_63 is 0 while the PC holds state flags (m_unk_158, AddStateFlags pcbase.cpp:1233); the flags are
        // not ported, so the PC can always act here (STILL-OPEN)
        var move = EscapeHit.MoveKeyIsDown != 0 ? held : pressed;
        var dir = WorldDir(move);
        switch (EscapeHit.Escape(lift.Active, lift.Falling, true, dir != Vector3.Zero, InAirFor(0f), jumpPressed))
        {
            case HitEscape.Walk: Enter(MoveState.Walk); return;
            case HitEscape.Jump: StartJump(dir, dir != Vector3.Zero, 0); return;   // vfunc_59(6) without data: StateJump sub 0x21
            case HitEscape.Skill: break;   // STILL-OPEN: State::FUN_1417b9460 (the next queued skill) is not ported
        }
        if (hitAnim.Length == 0) Enter(MoveState.Idle);
    }

    /// <summary>StateHit::vfunc_5 (@0x1417caa10, statehit.cpp:135-145): leaving the state ends the lift.</summary>
    void LeaveHit() => EndLift();

    /// <summary>GameObjectController::EndLift (@0x141240d70): the lift ends and the fall timer is reset.</summary>
    void EndLift()
    {
        lift.End();
        airTime = 0;
        fallTimerRunning = false;
    }

    /// <summary>FUN_1412407f0's lift branch in the frame's move: the step up or down in field units (no horizontal part),
    /// 0 when it is not finite (Move's Unk_IsFinite). The first fall marks the fall start (MyPC::FUN_141676060).</summary>
    float LiftStep(float dt)
    {
        float step = lift.Step(dt, out bool startedFalling);
        if (startedFalling && float.IsNaN(fallStartY)) fallStartY = GlobalPosition.Y;
        return float.IsFinite(step) ? step : 0f;
    }

    /// <summary>StateHit's hold on the fall damage (PCBase::SendFallDamage, pcbase.cpp:4360, FUN_1417cb160).</summary>
    bool FallDamageHeld => State == MoveState.Hit && !hit.FallDamageAllowed(gameTime);

    /// <summary>The own PC's position in the client's z-up field coordinates (vfunc_18).</summary>
    public System.Numerics.Vector3 FieldPosition => new(GlobalPosition.X, -GlobalPosition.Z, GlobalPosition.Y);

    static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Z, -v.Y);

    public int PrevStateId => StateId(prevState);

    public bool RayHits(System.Numerics.Vector3 from, System.Numerics.Vector3 dir, float len) =>
        Ray(ToGodot(from), ToGodot(dir), len, GroundGroups) != null;

    public bool GroundBelow(System.Numerics.Vector3 from, float len, out float hitZ)
    {
        hitZ = 0f;
        if (Ray(ToGodot(from), Vector3.Down, len == 0f ? MoveConfig.UnlimitedRay : len, GroundGroups) is not var (p, _)) return false;
        hitZ = p.Y;
        return true;
    }

    /// <summary>PCBase::vfunc_106 (@0x141674750, pcbase.cpp:4188-4209): the controller's move without the npc group (the
    /// push's flags 0x10, GameObjectController::Move's nExcludeGroups), then the body faces against the move
    /// (kRot.z = -heading).</summary>
    public void PushMove(System.Numerics.Vector3 move, double dt)
    {
        var m = ToGodot(move);
        ControllerMove(m, 0f, MoveConfig.ControllerHitIgnore);
        Face(new Vector3(-m.X, 0, -m.Z));
    }
}
