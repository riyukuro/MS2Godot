using Godot;
using Act = Ms2Godot.Movement.ActionAnims.Act;

namespace Ms2Godot;

/// <summary>
/// A state the server put the own pc into (OnUserState, vfunc_59(state) with no state data) on the body: the body's own move
/// state for a state it has one for, or the sequence, hide and pause the state runs (Ms2.Ui.OwnStateExit) while the body
/// stands.
/// </summary>
public partial class Player
{
    /// <summary>
    /// ChangeState's vfunc_5 of the body's state and the class's enter with no data (Ms2.Ui.OwnStateExit.BodyEntryOf): to
    /// StateIdle (an at-once class, or a state run by OwnStateExit while the body stands), or the body's own state of that id.
    /// The death 0xc and revival 0x18 are the caller's (EnterDead, EnterRevival).
    /// </summary>
    public void EnterServerState(Ms2.Ui.BodyEntry entry)
    {
        if (DeadFlag) return;   // PC::vfunc_124 (pc.cpp:1199)
        switch (entry)
        {
            case Ms2.Ui.BodyEntry.Idle:
                ReactIdle();
                break;
            case Ms2.Ui.BodyEntry.Land:
                // StateLand::vfunc_4 (stateland.cpp:74-104): the land sequence; on water straight on to StateIdle (the update)
                if (StateKept(MoveState.Land, false)) break;
                LeaveForServerState();
                Enter(MoveState.Land);
                break;
            case Ms2.Ui.BodyEntry.Fall:
                // StateFall::vfunc_4 (statefall.cpp:93-131): JUMP_FALL; on the ground the next frame lands
                if (StateKept(MoveState.Fall, false)) break;
                LeaveForServerState();
                Enter(MoveState.Fall);
                break;
            case Ms2.Ui.BodyEntry.Jump:
                // StateJump::vfunc_4 (statejump.cpp:95-146): sub 0 becomes 0x21, a jump with no move
                if (StateKept(MoveState.Jump, false)) break;
                LeaveForServerState();
                StartJump(Vector3.Zero, false, 0);
                break;
            case Ms2.Ui.BodyEntry.Ladder:
                // StateLadder::vfunc_4 (stateladder.cpp:153-201): the ladder in reach for side 3, else StateIdle
                if (StateKept(MoveState.Ladder, false)) break;
                LeaveForServerState();
                if (!TryLadder(3)) Enter(MoveState.Idle);
                break;
            case Ms2.Ui.BodyEntry.Rope:
                // StateRope::vfunc_4 (statemanager.cpp:1236, 1260): the rope in reach (FUN_141659af0(3)), else StateIdle
                if (StateKept(MoveState.Rope, false)) break;
                LeaveForServerState();
                if (!TryRope(3)) Enter(MoveState.Idle);
                break;
            case Ms2.Ui.BodyEntry.Hit:
                EnterHit(null, false);   // StateHit::vfunc_4 with no data: no lift, HIT
                break;
            case Ms2.Ui.BodyEntry.Dash:
                if (StateKept(MoveState.Dash, false)) break;
                LeaveForServerState();
                StartDash();
                break;
            case Ms2.Ui.BodyEntry.Revival:
                EnterRevival();
                break;
            case Ms2.Ui.BodyEntry.Swim:
                // StateSwim::vfunc_4 (stateswim.cpp:89-130) sets the swim speed; the body swims only on water
                if (!OnWater || StateKept(MoveState.Swim, false)) break;
                LeaveForServerState();
                Enter(MoveState.Swim);
                break;
            case Ms2.Ui.BodyEntry.Climb:
                if (StateKept(MoveState.Climb, false)) break;
                TryClimb();
                break;
            case Ms2.Ui.BodyEntry.FallDamage:
                EnterFallDamage();
                break;
        }
    }

    /// <summary>The old state's vfunc_5 as another state is entered: the own cast's sequence and an interact motion end.</summary>
    void LeaveForServerState()
    {
        CancelSkillSequence();
        StopInteractAnim();
    }

    string stateSequence;
    bool stateSequenceLoops;
    int stateSequencePlays;
    bool animationPaused;

    /// <summary>
    /// The sequence a server-set state plays on the body (PCBase::PlayAnimation from its enter, frame or EndOfSequence): held
    /// over the body's own sequences while it is set. <paramref name="action"/> names an action (the weapon prefix taken)
    /// rather than a sequence. <paramref name="plays"/> rises with each new play, which starts the sequence over. A name the
    /// body lacks plays nothing (the body's current sequence stays). Null ends it.
    /// </summary>
    public void SetStateSequence(string name, bool loop, bool action, int plays)
    {
        if (name == null) { stateSequence = null; return; }
        if (name == stateSequence && plays == stateSequencePlays) return;
        stateSequencePlays = plays;
        string key = action ? (name == Ms2.Ui.OwnStateExit.TalkAction ? Anims?.Name(Act.Idle, WeaponPrefix) : null) : Anims?.Sequence(name, loop);
        if (key == null || Anim == null || !Anim.HasAnimation(key)) { stateSequence = name; stateSequenceLoops = loop; return; }
        CancelSkillSequence();
        StopInteractAnim();
        stateSequence = name;
        stateSequenceLoops = loop;
        PlaySequence(key, 1f, restart: true);
    }

    /// <summary>A server-set state's sequence holds the body's animation.</summary>
    bool StateSequenceHolds => stateSequence != null;

    /// <summary>GameObject::FUN_14123d7a0("stun", 1 / 0) (gameobject.cpp:902-909): the body's animation paused while any
    /// key is set (StateStun sub 0x41).</summary>
    public void SetAnimationPaused(bool paused)
    {
        if (paused == animationPaused || Anim == null) return;
        animationPaused = paused;
        if (paused) Anim.Pause();
        else Anim.Play();
    }

    /// <summary>The hide reason of StateSpawn (AddHideReason(10), statespawn.cpp:65-75): the body and its shadow hidden.</summary>
    public void SetSpawnHidden(bool hidden) => SetHideReason(Ms2.Ui.OwnStateExit.SpawnHideReason, hidden);
}
