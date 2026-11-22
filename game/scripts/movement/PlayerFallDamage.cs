using Godot;
using Act = Ms2Godot.Movement.ActionAnims.Act;

namespace Ms2Godot;

// The own PC's fall damage state (StateFallDamage 0x1f), entered on the server's fall damage reply.
public partial class Player
{
    /// <summary>The sequence StateFallDamage plays (m_strAnim), "" when the body has none.</summary>
    string fallDamageAnim = "";

    /// <summary>
    /// OnStateFallDamage (scenestategame.cpp:5823-5825): the own PC, when not dead, takes vfunc_59(0x1f). A mounted PC
    /// (state 0x21) hands the change to its ridee (PCBase::vfunc_59, pcbase.cpp:1106-1108), and only the PC registers
    /// 0x1f (pc.cpp:695), so a rider stays as it is. StateFallDamage::vfunc_4 (@0x1417c7c90, statefalldamage.cpp:51-70)
    /// plays FALL_DAMAGE (vfunc_123(0x3f)) once at m_f938, which stays 1 for the own PC.
    /// STILL-OPEN: the worn equipment's set name in front of the sequence (CEquipment::GetSetName) is not read.
    /// </summary>
    public void EnterFallDamage()
    {
        if (DeadFlag) return;          // IsDead, and PC::vfunc_124 (pc.cpp:1199)
        if (Mount != null) return;
        if (StateKept(MoveState.FallDamage, false)) return;
        CancelSkillSequence();
        StopInteractAnim();
        Enter(MoveState.FallDamage);
        kVel = Vector3.Zero;
        var name = Anims?.Name(Act.FallDamage, WeaponPrefix);
        fallDamageAnim = name != null && Anim != null && Anim.HasAnimation(name) ? name : "";
        if (fallDamageAnim.Length > 0) PlaySequence(fallDamageAnim, 1f, true);
    }

    /// <summary>
    /// StateFallDamage's frame. vfunc_3 (@0x1417c7fb0, :82-87) moves by a zero move, gravity only; EndOfSequence
    /// (@0x1417c8150, :105-110) goes to StateIdle when the FALL_DAMAGE sequence ends; vfunc_8 (@0x1417c8030, :90-101) faces
    /// the pressed move and with a held move (FUN_1416749c0) goes to StateWalk. A body without the sequence stays until a
    /// move. vfunc_63 is 0 while the PC holds state flags, which are not ported (STILL-OPEN); FUN_1417b9460 (the next
    /// queued skill) is the base cast poll, which the HUD's own cast covers.
    /// </summary>
    void FallDamageUpdate(bool hasInput)
    {
        kVel = Vector3.Zero;
        if (fallDamageAnim.Length > 0 && CurrentAnim == fallDamageAnim && !Anim.IsPlaying()) { Enter(MoveState.Idle); return; }
        if (hasInput) Enter(MoveState.Walk);
    }
}
