using System.Numerics;

namespace Ms2.Ui.Skills;

/// <summary>Where the own skill state's swing cancel (<see cref="SkillSwingCancel.Step"/>) leaves to.</summary>
public enum SwingExit : byte
{
    /// <summary>The skill state stays.</summary>
    None,
    /// <summary>SendSkillMode4, then vfunc_7(2): StateWalk (stateskill.cpp:1556-1560).</summary>
    Walk,
    /// <summary>SendSkillMode4, then vfunc_7(6): StateJump (stateskill.cpp:1551-1555).</summary>
    Jump,
}

/// <summary>What the swing cancel reads in a frame (StateSkill::FUN_1417ed7f0 @0x1417ed7f0, stateskill.cpp:1510-1563).</summary>
public struct SwingInput
{
    /// <summary>The owner may act (vfunc_63, PCBase m_unk_158 0: no state flag held, pcbase.cpp:1248).</summary>
    public bool CanAct;
    /// <summary>vfunc_99(0) (pcbase.cpp:2544): the owner falls, in the air for over 100 ms.</summary>
    public bool Falling;
    /// <summary>Unk_PCMove::GetMoveDirection: the held move input (camera-relative: x left / right, y forward / back).</summary>
    public Vector2 Held;
    /// <summary>Unk_PCMove::GetPressedMoveDirection: only the axes whose key went down this frame.</summary>
    public Vector2 Pressed;
    /// <summary>Client::Constants CancelSwing_KeyIsDown (1 in the KMS and the GMS2 table/constants.xml): the held direction counts.</summary>
    public bool KeyIsDown;
    /// <summary>IsActionKeyPressed(0x493e1): the jump action's key was pressed.</summary>
    public bool JumpPressed;
    /// <summary>The running skill is a continue skill (m_bContinue) and its key (m_nA4) is down (:1533-1538).</summary>
    public bool ContinueKeyHeld;
    /// <summary>The state holds a queued charge record (m_spCharge, :1539-1540).</summary>
    public bool Charging;
    /// <summary>The next cast's key (m_spNext's m_nA4), or without one the running record's key, is down (:1543-1547).</summary>
    public bool ExitKeyHeld;
    /// <summary>The state's m_n10 is 0x3c (the skill's magicControl enable, vfunc_4 :496-497): no exit (:1549).</summary>
    public bool MagicControl;
}

/// <summary>
/// StateSkill::FUN_1417ed7f0 @0x1417ed7f0 (stateskill.cpp:1505-1563), the swing cancel, run at the end of each frame of the
/// own skill state (vfunc_8, :725), after the key poll. While the motion is in its input window (m_b4a, "inputcombo" to
/// "endInputCombo", or m_b114, the motion's selfInputCombo key passed) the move input is kept as the push (m_ptPush: the held
/// direction with CancelSwing_KeyIsDown, else the pressed one; a zero direction keeps the last) and a jump press is kept
/// (m_b110). A continue skill whose key is held, or a queued charge record, has no push. From the "combo" or "selfcombo" key
/// or the sequence's end on (m_b49, m_b116, m_b112), with the key let go, the push goes to the owner (vfunc_22) and, unless
/// the state has magicControl (m_n10 0x3c), a kept jump leaves for StateJump, else a push leaves for StateWalk, each after
/// SendSkillMode4. The kept values reset when a motion starts (FUN_1417f0940 :2215-2216, <see cref="Reset"/>).
/// Outside the window nothing is kept, so a move or a jump then does not cut the motion short.
/// </summary>
public struct SkillSwingCancel
{
    /// <summary>m_ptPush: the kept move input.</summary>
    public Vector2 Push;
    /// <summary>m_b110: a jump press was kept.</summary>
    public bool Jump;

    /// <summary>FUN_1417f0940 (:2215-2216): a motion starts.</summary>
    public void Reset() { Push = Vector2.Zero; Jump = false; }

    /// <summary>One frame with the state's combo flags <paramref name="w"/>.</summary>
    public SwingExit Step(in SkillFollow.Window w, in SwingInput i)
    {
        if (!i.CanAct || i.Falling) return SwingExit.None;            // :1515-1518
        if (w.InputCombo || w.SelfInputCombo)                         // :1519-1532
        {
            var dir = i.KeyIsDown ? i.Held : i.Pressed;
            if (dir != Vector2.Zero) Push = dir;
            if (i.JumpPressed) Jump = true;
        }
        if (i.ContinueKeyHeld) Push = Vector2.Zero;                   // :1533-1538
        if (i.Charging) Push = Vector2.Zero;                          // :1539-1540
        if (!w.Combo && !w.SelfCombo && !w.Ended) return SwingExit.None;   // :1541-1542
        if (i.ExitKeyHeld) return SwingExit.None;                     // :1543-1547
        if (i.MagicControl) return SwingExit.None;                    // :1549
        if (Jump) return SwingExit.Jump;                              // :1551-1555
        return Push != Vector2.Zero ? SwingExit.Walk : SwingExit.None;   // :1556-1560 (FUN_1416749c0: the push set by vfunc_22)
    }
}
