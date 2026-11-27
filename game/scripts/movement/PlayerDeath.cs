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
    /// Both cases turn the shadow off (vfunc_31(false)); leaving StateDead turns it on (StateDead::vfunc_5, statedead.cpp:182).
    /// STILL-OPEN: vfunc_23's tails and random target effect are not hidden; the guide trigger 8 is not played. The live session plays the MyPC death effect
    /// (dead_Start_MyPC, or dead_Start_MyPC_PvP in place), not attached to the body and not removed early.
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
        FieldShadows.SetShadowEnabled(this, false);
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
        FieldShadows.SetShadowEnabled(this, true);   // StateDead::vfunc_5
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
        FieldShadows.SetShadowEnabled(this, true);   // StateDead::vfunc_5
        Enter(MoveState.Idle);
    }

    /// <summary>PC::vfunc_23(!shown) on the body: the character root hidden or shown. Leaving StateDead shows it again
    /// (StateDead::vfunc_5, statedead.cpp:181: vfunc_23(0)), for the revival and any other next state.</summary>
    void SetBodyShown(bool shown)
    {
        if (Visual != null && Visual.Visible != shown) Visual.Visible = shown;
    }

    /// <summary>PCBase m_unk_c08, the hide reasons (bit 2: a hideRider mount).</summary>
    int hideReasons;
    public bool IsHidden => hideReasons != 0;

    /// <summary>PCBase::AddHideReason (@0x14166ca20) / RemoveHideReason (@0x14166ca80): nothing when the bit already has that
    /// value, else ApplyHideReasons (@0x14166cb30): vfunc_23(1) while any reason is set, vfunc_23(0) when none is. Its
    /// re-show of the ridee for reason 2 alone is the port's already: the rider's visual does not carry the mount. The
    /// simple shadow follows: SetShadowVisible(false) while hidden, (true) when shown (pcbase.cpp:1188-1203).</summary>
    public void SetHideReason(int reason, bool on)
    {
        int bit = 1 << reason;
        if (((hideReasons & bit) != 0) == on) return;
        hideReasons = on ? hideReasons | bit : hideReasons & ~bit;
        SetBodyShown(hideReasons == 0);
        FieldShadows.SetShadowVisible(this, hideReasons == 0);
    }

    /// <summary>The hide reason of a hideRider mount (pcbase.cpp:3331-3333).</summary>
    public const int HideReasonRide = 2;

    /// <summary>PCBase::FUN_14166cb00 (@0x14166cb00): the hide reason's bit is set.</summary>
    public bool HasHideReason(int reason) => (hideReasons & (1 << reason)) != 0;

    /// <summary>The hide reason of the field's HideMyPC property (field property 3).</summary>
    public const int HideReasonField = 1;

    /// <summary>UIHudEntity::Entity::vfunc_8 (uihudentity.cpp:200-210, called with IsDead from UINameTag::Entity::vfunc_8,
    /// uinametag.cpp:334): a body hidden by vfunc_23(1) (GameObject m_unk_b0, FUN_14123c660) that is not dead has no name
    /// tag, unless it rides a ridee that is shown (a hideRider mount alone keeps the tag).</summary>
    public bool HidesNameTag => Visual is { Visible: false } && !DeadFlag && State != MoveState.Dead
        && Mount?.Visual is not { Visible: true };

    /// <summary>FieldPropertyHideMyPCCli::vfunc_4 (@0x140b27700) / vfunc_5 (@0x140b27790), fieldpropertycli.cpp:146-171, on
    /// the local player: AddHideReason(1) or RemoveHideReason(1), then the same on its ridee when it rides.</summary>
    public void SetHideMyPC(bool on)
    {
        SetHideReason(HideReasonField, on);
        Mount?.SetHidden(on);
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
