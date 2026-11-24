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
                // StateLadder::vfunc_4 (stateladder.cpp:153-202): the ladder in reach for side 3, else StateIdle; the
                // shadow is turned off after either (the ladder's own enter turns it off too)
                if (StateKept(MoveState.Ladder, false)) break;
                LeaveForServerState();
                if (!TryLadder(3)) { Enter(MoveState.Idle); FieldShadows.SetShadowVisible(this, false); }
                break;
            case Ms2.Ui.BodyEntry.Rope:
                // StateRope::vfunc_4 (statemanager.cpp:1236-1262): the rope in reach (FUN_141659af0(3)), else StateIdle;
                // the shadow is turned off after either
                if (StateKept(MoveState.Rope, false)) break;
                LeaveForServerState();
                if (!TryRope(3)) { Enter(MoveState.Idle); FieldShadows.SetShadowVisible(this, false); }
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
                // StateClimb::vfunc_4 (state.cpp:1029-1057) with no data: StateMoveSkill's enter enters StateIdle at once
                // (statemanager.cpp:670-676, 720-723), then the rest of the enter turns the shadow off (state.cpp:1038),
                // which stays off as StateIdle runs
                if (StateKept(MoveState.Climb, false)) break;
                ReactIdle();
                FieldShadows.SetShadowVisible(this, false);
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
        string key = action ? ActionKey(name) : Anims?.Sequence(name, loop);
        if (key == null || Anim == null || !Anim.HasAnimation(key)) { stateSequence = name; stateSequenceLoops = loop; return; }
        CancelSkillSequence();
        StopInteractAnim();
        stateSequence = name;
        stateSequenceLoops = loop;
        PlaySequence(key, 1f, restart: true);
    }

    /// <summary>A server-set state's action name to the body's sequence (vfunc_123): StateTalk's IDLE, TALK_UP, TALK_IDLE and
    /// LISTEN_IDLE.</summary>
    string ActionKey(string name) => name switch
    {
        Ms2.Ui.OwnStateExit.TalkAction => Anims?.Name(Act.Idle, WeaponPrefix),
        Ms2.Ui.OwnStateExit.TalkUpAction => Anims?.Name(Act.TalkUp, WeaponPrefix),
        Ms2.Ui.OwnStateExit.TalkIdleAction => Anims?.Name(Act.TalkIdle, WeaponPrefix),
        Ms2.Ui.OwnStateExit.ListenIdleAction => Anims?.Name(Act.ListenIdle, WeaponPrefix),
        _ => null,
    };

    /// <summary>The length in seconds of a server-set state's action (<see cref="ActionKey"/>), null when the body has none.</summary>
    public double? ActionSeconds(string name) => ActionKey(name) is { } key && Anim != null && Anim.HasAnimation(key) ? Anim.GetAnimation(key).Length : null;

    /// <summary>The body has the sequences of vfunc_123(0x2d) TALK_UP and vfunc_123(0x2e) TALK_IDLE (StateTalk's bFirst and
    /// bSecond, statetalk.cpp:110-123).</summary>
    public bool HasTalkUp => TalkUpSequence != null;
    /// <summary>The body's sequence of TALK_UP, null when it has none.</summary>
    public string TalkUpSequence => Anims?.Name(Act.TalkUp, WeaponPrefix);
    public bool HasTalkIdle => Anims?.Name(Act.TalkIdle, WeaponPrefix) != null;

    /// <summary>The own body's simple shadow shows (GameObject::SetShadowVisible's flag), for the checks.</summary>
    public bool ShadowShown => GetNodeOrNull<BlobShadow>(FieldShadows.ShadowNode) is not { Off: true };
    /// <summary>The own body has a simple shadow at this shadow level.</summary>
    public bool HasSimpleShadow => GetNodeOrNull<BlobShadow>(FieldShadows.ShadowNode) != null;

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
