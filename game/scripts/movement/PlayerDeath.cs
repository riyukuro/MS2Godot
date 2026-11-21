using Godot;
using Ms2Godot.Movement;
using Act = Ms2Godot.Movement.ActionAnims.Act;

namespace Ms2Godot;

// The own player's death and revival states (StateDead 0xc, StateRevival 0x18), entered by the server's packets only.
public partial class Player
{
    string deadSequence;    // the sequence StateDead::vfunc_4 picked: DEAD, or the revive-in-place MODEPVP_DEAD entry

    /// <summary>True in StateDead or StateRevival: the move, jump and click-move input is not read.</summary>
    public bool DeathHoldsMove => State is MoveState.Dead or MoveState.Revival;

    /// <summary>
    /// MyPC::vfunc_137's vfunc_59(0xc) (mypc.cpp:1346-1348) -> StateDead::vfunc_4 (@0x1417c2530, statedead.cpp:104-171).
    /// <paramref name="reviveInPlace"/>: a PvP point or a field with HasAutoRevivalType1 plays MODEPVP_DEAD (0x40) entry
    /// <paramref name="reviveAnim"/> (the PvP team, or the constant ShadowWorldAutoReviveDeadAction) instead of DEAD (0x34);
    /// the normal case's tombstone (PC::FUN_14165cac0) is not built here. The state has no vfunc_8 and no exit of its own:
    /// it lasts until the server's Revival (<see cref="EnterRevival"/>). Entering it again (each DeadUser) does nothing:
    /// CStateManager::ChangeState (statemanager.cpp:297) returns for the current state, State::vfunc_25 answering true.
    /// STILL-OPEN: vfunc_23 (the fade flag 0xa0b6b179) and vfunc_31 (the shadow) have no Godot counterpart on the player;
    /// the death and revive effects of the common resource and the guide trigger 8 are not played here.
    /// </summary>
    public void EnterDead(bool reviveInPlace, int reviveAnim)
    {
        if (State == MoveState.Dead) return;
        CancelSkillSequence();          // vfunc_59 leaves StateSkill (State::vfunc_5)
        StopInteractAnim();
        deadSequence = reviveInPlace ? Anims?.NameAt(Act.ModePvpDead, reviveAnim) : Anims?.Name(Act.Dead, WeaponPrefix);
        Enter(MoveState.Dead);
        kVel = Vector3.Zero;
        if (deadSequence != null && Anim != null && Anim.HasAnimation(deadSequence)) PlaySequence(deadSequence, 1f, true);
    }

    /// <summary>
    /// OnRevival's vfunc_59(0x18) (scenestategame.cpp:7400) -> StateRevival: vfunc_4 plays REVIVAL (0x33); the sequence's end
    /// (EndOfSequence, vfunc_59(1)) goes to StateIdle. A body without the sequence goes straight on to idle. The state has no
    /// vfunc_3: the controller does not move while it plays.
    /// </summary>
    public void EnterRevival()
    {
        deadSequence = null;
        CancelSkillSequence();
        Enter(MoveState.Revival);
        kVel = Vector3.Zero;
        var name = Anims?.Name(Act.Revival, WeaponPrefix);
        if (name == null || Anim == null || !Anim.HasAnimation(name)) { Enter(MoveState.Idle); return; }
        PlaySequence(name, 1f, true);
    }

    /// <summary>StateDead::vfunc_3 (statedead.cpp): vfunc_68(zero, t), gravity only; StateRevival: the sequence's end.</summary>
    void DeathUpdate()
    {
        kVel = Vector3.Zero;
        if (State != MoveState.Revival) return;
        var name = Anims?.Name(Act.Revival, WeaponPrefix);
        if (stateTime > 0.05f && (CurrentAnim != name || (blendHold < 0 && !Anim.IsPlaying()))) Enter(MoveState.Idle);
    }
}
