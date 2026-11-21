using Godot;
using Ms2Godot.Movement;
using Act = Ms2Godot.Movement.ActionAnims.Act;

namespace Ms2Godot;

// The own player's death and revival states (StateDead 0xc, StateRevival 0x18), entered by the server's packets only.
public partial class Player
{
    string deadSequence;    // the sequence StateDead::vfunc_4 picked: DEAD, or the revive-in-place MODEPVP_DEAD entry

    /// <summary>
    /// PC::vfunc_124 (pc.cpp:1199) refuses every state but death and revival (and observer 0x3f): set by the live session from
    /// MyCharacter.DeathOnlyStates. A player the own FieldAddUser added dead (scenestategame.cpp:11185: FUN_14166ce50(1), no
    /// vfunc_59) has the flag and no state: CStateManager starts at m_nCur 0 (statemanager.cpp:195), nothing registered there.
    /// Here the body stays in the state it had and takes no other; the move, jump and click-move input is not read (MyPC's
    /// input paths return while IsDead, mypc.cpp:1346, 2210, 4518). The server's Revival clears it.
    /// STILL-OPEN: the client's state 0 plays no sequence and runs no vfunc_3 (no gravity); this body keeps its idle loop and
    /// its gravity.
    /// </summary>
    public bool DeadFlag;

    /// <summary>True in StateDead or StateRevival, or with <see cref="DeadFlag"/>: the move, jump and click-move input is not read.</summary>
    public bool DeathHoldsMove => DeadFlag || State is MoveState.Dead or MoveState.Revival;

    /// <summary>
    /// MyPC::vfunc_137's vfunc_59(0xc) (mypc.cpp:1346-1348) -> StateDead::vfunc_4 (@0x1417c2530, statedead.cpp:104-171).
    /// <paramref name="reviveInPlace"/>: a PvP point or a field with HasAutoRevivalType1 plays MODEPVP_DEAD (0x40) entry
    /// <paramref name="reviveAnim"/> (the PvP team, or the constant ShadowWorldAutoReviveDeadAction) instead of DEAD (0x34);
    /// the normal case hides the body (vfunc_23(1): PC::vfunc_23 clears the entity's IsVisible 0xa0b6b179 and hides the
    /// equipment) and the live session builds the tombstone (PC::FUN_14165cac0, <see cref="Movement.PlayerTombstone"/>); the
    /// revive-in-place case shows it (vfunc_23(0)). The state has no vfunc_8 and no exit of its own:
    /// it lasts until the server's Revival (<see cref="EnterRevival"/>). Entering it again (each DeadUser) does nothing:
    /// CStateManager::ChangeState (statemanager.cpp:297) returns for the current state, State::vfunc_25 answering true.
    /// The camera is not touched: neither the state (vfunc_4, vfunc_3, vfunc_5) nor the camera modes test the death, so the
    /// field camera keeps following the player's position (Field.UpdateCamera) with the body hidden, and zoom and rotation
    /// stay live.
    /// STILL-OPEN: vfunc_31 (the shadow) has no Godot counterpart on the player; vfunc_23's tails and random target
    /// effect are not hidden; the death and revive effects of the common resource and the guide trigger 8 are not played here.
    /// </summary>
    public void EnterDead(bool reviveInPlace, int reviveAnim)
    {
        if (State == MoveState.Dead) return;
        CancelSkillSequence();          // vfunc_59 leaves StateSkill (State::vfunc_5)
        StopInteractAnim();
        deadSequence = reviveInPlace ? Anims?.NameAt(Act.ModePvpDead, reviveAnim) : Anims?.Name(Act.Dead, WeaponPrefix);
        Enter(MoveState.Dead);
        kVel = Vector3.Zero;
        SetBodyShown(reviveInPlace);
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
        SetBodyShown(true);
        CancelSkillSequence();
        Enter(MoveState.Revival);
        kVel = Vector3.Zero;
        var name = Anims?.Name(Act.Revival, WeaponPrefix);
        if (name == null || Anim == null || !Anim.HasAnimation(name)) { Enter(MoveState.Idle); return; }
        PlaySequence(name, 1f, true);
    }

    /// <summary>
    /// OnUserState's vfunc_59(state) (scenestategame.cpp:5766) for a state other than 0xc and 0x18 that PC::vfunc_124
    /// (pc.cpp:1199) lets in: the death and revival states are left. The other user states have no body state here, so the
    /// body goes to idle. Outside StateDead and StateRevival it does nothing.
    /// </summary>
    public void LeaveDeath()
    {
        if (State is not (MoveState.Dead or MoveState.Revival)) return;
        deadSequence = null;
        SetBodyShown(true);
        Enter(MoveState.Idle);
    }

    /// <summary>PC::vfunc_23(!shown) on the body: the character root hidden or shown. Leaving StateDead shows it again
    /// (StateDead::vfunc_5, statedead.cpp:181: vfunc_23(0)), for the revival and any other next state.</summary>
    void SetBodyShown(bool shown)
    {
        if (Visual != null && Visual.Visible != shown) Visual.Visible = shown;
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
