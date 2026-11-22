using System;
using Godot;
using Ms2.Net.Replay;
using Ms2.Ui.Skills;
using Ms2Godot.Movement;

namespace Ms2Godot.Net;

// Another PC's hit reaction state (StateHit 0xd) and its push (PCBase::m_pushControl). The client runs PC::vfunc_33/34/35
// on every PC, so a remote PC reacts to a hit as the own PC does (Player's PlayerHit), without the escape (StateHit::vfunc_8
// reads the own PC's keys) and without MyPC's fall start mark (FUN_141676060).
public sealed partial class RemotePc : IHitBody, IPushBody
{
    /// <summary>PCBase m_pushControl: the push a hit started (<see cref="PushControl.Start"/>), moved each frame by
    /// <see cref="PushControl.Tick"/> after the state's move (PCBase::vfunc_53, pcbase.cpp:3737). Only MyPC resets it on
    /// StateIdle / StateDead.</summary>
    public PushControl Push { get; } = new();

    /// <summary>A running push: CStateManager::FUN_1417dc4c0 (statemanager.cpp:597) skips the record's correction.</summary>
    public bool Pushed => Push.Running;

    /// <summary>StateHit's fields (statehit.cpp:62-80), its end time and the sequence it picked.</summary>
    readonly StateHitTimeline hit = new();
    /// <summary>GameObjectController's lift, which StateHit starts on a lifting hit (statehit.cpp:88-92).</summary>
    readonly LiftArc lift = new();
    /// <summary>StateHit m_strAnim: the body's key of the sequence it plays; empty when the body has none.</summary>
    string hitAnim = "";
    /// <summary>The client's name of the sequence playing (EndOfSequence compares names); its end is taken once per play.</summary>
    string hitPlaying = "";
    bool hitSeqEnded;
    /// <summary>The data the next StateHit::vfunc_4 copies (vfunc_59's data argument).</summary>
    StateHitData hitData;
    /// <summary>The game time StateHit reads (CMainSystem::GetGameTime): the frames' deltas, seconds.</summary>
    double gameTime;
    Predicate<string> hasSequence;

    void WireHit()
    {
        States.HitEntered = OnHitEntered;
        States.HitUpdate = OnHitUpdate;
        States.HitFrame = OnHitFrame;
        States.HitLeft = EndLift;
    }

    /// <summary>The client state id for a hit's tests (GetCurStateId).</summary>
    public int ClientStateId => States.Cur;

    /// <summary>
    /// State::vfunc_14 (m_n14, state.cpp:569) of the current state: StateSkill's is whether the push runs with a speed
    /// above 0 (stateskill.cpp:1300); StateIdle's is 0 on water (stateidle.cpp:493); StateWalk and StateHit keep 1, and
    /// StateHold sets 1 (stateholddata.cpp:148); the other states set 0 in their ctors.
    /// </summary>
    public bool StateTakesHits => States.Cur switch
    {
        RemotePcStates.StateSkill => Push.Speed > 0f,
        RemotePcStates.StateIdle => !OnWater,
        RemotePcStates.StateWalk or RemotePcStates.StateHit or RemotePcStates.StateHold => true,
        _ => false,
    };

    /// <summary>StateHit::HasHitDuration (statehit.cpp:239) of the current state; false outside StateHit.</summary>
    public bool HitHasDuration => States.Cur == RemotePcStates.StateHit && !HitReaction.NearlyZero(hit.Data.Duration);
    /// <summary>StateHit::GetPushPriority (statehit.cpp:245) of the current state; 0 outside StateHit.</summary>
    public int HitPushPriority => States.Cur == RemotePcStates.StateHit ? hit.Data.Priority : 0;

    /// <summary>The current StateHit's data (a copy of what entered it).</summary>
    public StateHitData HitState => hit.Data;
    /// <summary>The key of the sequence StateHit plays; empty when the body has none.</summary>
    public string HitAnim => hitAnim;
    public bool Lifted => lift.Active;

    /// <summary>
    /// PC::vfunc_59(0xd, data) from the hit reaction (pc.cpp:943): PC::vfunc_124 refuses a dead PC (pc.cpp:1199); a change
    /// to StateHit while in it without bForce keeps the state (CStateManager::ChangeState, statemanager.cpp:297). Then
    /// StateHit::vfunc_4 (@0x1417ca370, statehit.cpp:62-110), <see cref="OnHitEntered"/>.
    /// </summary>
    /// <param name="force">vfunc_59's bForce: vfunc_33 passes the data's duration above 0.</param>
    public void EnterHit(StateHitData data, bool force)
    {
        if (States.Dead) return;
        if (!force && States.Cur == RemotePcStates.StateHit) return;
        hitData = data;
        States.ChangeState(RemotePcStates.StateHit, 0, force);
    }

    /// <summary>PC::vfunc_59(1) from the hit reaction (pc.cpp:946-950): StateIdle, refused for a dead PC.</summary>
    public void ReactIdle()
    {
        if (States.Dead) return;
        States.ChangeState(RemotePcStates.StateIdle);
    }

    /// <summary>
    /// StateHit::vfunc_4 (@0x1417ca370, statehit.cpp:62-110): the fields are copied from the data with the body's z; a
    /// lifting hit ends any lift and starts its own (EndLift, StartLift(duration, lift, owner z); a started lift ends the
    /// jump), then the sequence: Jump_FlyDamg_A with the lift, else the body's hit action, replaced by the data's animation
    /// when the body has it; it plays at the data's speed from its start.
    /// STILL-OPEN: the body's hit action (vfunc_123(0x35)) is not in the action table, as for the own PC; the 70% timer
    /// (statehit.cpp:104) is not run.
    /// </summary>
    void OnHitEntered()
    {
        hit.Enter(hitData, gameTime, Position.Z);
        hitData = default;
        if (hit.Lifts)
        {
            EndLift();
            if (lift.Start(hit.Data.Duration, hit.Data.UpDistance, hit.OwnerZ)) jumping = false;
        }
        hasSequence ??= s => Anims?.Sequence(s, false) != null;
        PlayHitSequence(hit.PickSequence("", hasSequence), true, hit.Speed);
    }

    /// <summary>NiActorManager's play of the hit's sequence (PlayAnimation(name, speed, restart)): a body without it plays
    /// nothing and leaves the key empty.</summary>
    void PlayHitSequence(string seq, bool restart, float speed)
    {
        string key = seq.Length > 0 ? Anims?.Sequence(seq, false) : null;
        hitAnim = key ?? "";
        hitPlaying = key != null ? seq : "";
        hitSeqEnded = false;
        if (key == null) return;
        bool same = key == currentName;
        if (!same || restart || !Anim.IsPlaying()) Anim.Play(key, same ? 0 : Blend(key));
        if (restart) Anim.Seek(0, true);
        Anim.SpeedScale = speed;
        currentAct = -1; currentName = key; lastPos = -1;
    }

    /// <summary>
    /// StateHit's move. EndOfSequence (@0x1417caaf0, statehit.cpp:155-175) when its sequence ends: StateIdle, or the fly
    /// sequence again from its start at speed 1; vfunc_3 (@0x1417ca8a0, :116-130) after the move: at the end time Damg_C
    /// plays at speed 1 without restart when the data has down (a body without it stays in the state), else StateIdle.
    /// </summary>
    void OnHitUpdate(float dt)
    {
        if (!hitSeqEnded && hitAnim.Length > 0 && currentName == hitAnim && !Anim.IsPlaying())
        {
            hitSeqEnded = true;
            var end = hit.EndOfSequence(hitPlaying);
            if (end == HitStep.Idle) { States.ChangeState(RemotePcStates.StateIdle); return; }
            if (end == HitStep.ReplayFly) PlayHitSequence(hitPlaying, true, 1f);
        }
        var step = hit.Update(gameTime);
        if (step == HitStep.Idle) { States.ChangeState(RemotePcStates.StateIdle); return; }
        if (step == HitStep.Down && hitPlaying != StateHitTimeline.DownSequence && Anims?.Sequence(StateHitTimeline.DownSequence, false) != null)
            PlayHitSequence(StateHitTimeline.DownSequence, false, 1f);
    }

    /// <summary>StateHit::vfunc_9 (@0x1417caa80, statehit.cpp:148-152): no sequence goes on to StateIdle.</summary>
    void OnHitFrame()
    {
        if (hitAnim.Length == 0) States.ChangeState(RemotePcStates.StateIdle);
    }

    /// <summary>StateHit::vfunc_5 (@0x1417caa10, statehit.cpp:135-145) -> GameObjectController::EndLift (@0x141240d70): the
    /// lift ends and the fall timer is reset.</summary>
    void EndLift()
    {
        lift.End();
        airTime = 0;
        fallTimerRunning = false;
    }

    /// <summary>FUN_1412407f0's lift branch (gameobjectcontrollerphysxhelpers.cpp:365-381): the step up or down in field
    /// units, no horizontal part; 0 when it is not finite (Move's Unk_IsFinite).</summary>
    float LiftStep(float dt)
    {
        float step = lift.Step(dt, out _);
        return float.IsFinite(step) ? step : 0f;
    }

    // ---- IPushBody -----------------------------------------------------------------------------------------------------

    public System.Numerics.Vector3 FieldPosition => Position;

    public int PrevStateId => States.Prev;

    public bool RayHits(System.Numerics.Vector3 from, System.Numerics.Vector3 dir, float len) => Ray(G(from), G(dir), len) != null;

    public bool GroundBelow(System.Numerics.Vector3 from, float len, out float hitZ)
    {
        hitZ = 0f;
        if (Ray(G(from), len) is not var (p, _, _)) return false;
        hitZ = p.Y;
        return true;
    }

    /// <summary>PCBase::vfunc_106 (@0x141674750, pcbase.cpp:4188-4209): the controller's move without the npc group (the
    /// push's flags 0x10, GameObjectController::Move's nExcludeGroups), then the body faces against the move
    /// (kRot.z = -heading).</summary>
    public void PushMove(System.Numerics.Vector3 move, double dt)
    {
        var m = G(move);
        ControllerMove(m, 0f, MoveConfig.ControllerHitIgnore);
        if (m.X * m.X + m.Z * m.Z < 1e-6f) return;
        RotationZ = Mathf.RadToDeg(Mathf.Atan2(-m.X, -m.Z));
        States.TargetYaw = RotationZ;
    }
}
