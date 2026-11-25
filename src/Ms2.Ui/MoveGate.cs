namespace Ms2.Ui;

/// <summary>
/// Unk_PCMove::FUN_141683ef0 (@0x141683ef0, unit_141682f80.cpp:495-518), the own pc's move gate: the move and action key
/// reads (IsKeyDown, IsActionKeyDown) and the action key's polls (TaxiStationAction, HoldAction, HoldPCAction,
/// ReleasePCAction and TreeWateringAction vfunc_1, actionkeyobject.cpp:776, 2211, 2257, 2315) go through it. For the own pc
/// it fails when:
/// <list type="bullet">
/// <item>the pc is dead (IsDead);</item>
/// <item>CInputKeyHandler::FUN_14116d7a0(0xf) holds (a focused text field, a field move's loading screen);</item>
/// <item>vfunc_63 (the slot at 0x1f8) is 0: the pc holds state flags (PCBase m_unk_158, AddStateFlags);</item>
/// <item>with m_ready 0, the additional list stuns (AdditionalCliList::FUN_1409709b0, m_motion.m_nStun);</item>
/// <item>the pc is in cube mode 1 (PC::FUN_1416596d0, m_nCubeModeFFC == 1);</item>
/// <item>the camera mode refuses (CameraMgr::FUN_140aed3e0, the mode's vfunc_34).</item>
/// </list>
/// STILL-OPEN: what writes m_ready (+0x50) is not known; it is taken as 0, so a stun always closes the gate.
/// STILL-OPEN: only LocalTargetCameraMode overrides vfunc_34 (its m_108.m_59); that mode has no port, so the camera passes.
/// </summary>
public static class MoveGate
{
    /// <summary>PCBase::vfunc_63 (@0x14166ce00, pcbase.cpp:1248): the pc can act while it holds no state flag.</summary>
    public static bool CanAct(uint stateFlags) => stateFlags == 0;

    /// <summary>FUN_141683ef0 for the own pc, in the client's order.</summary>
    public static bool Passes(bool dead, bool inputBlocked, uint stateFlags, bool stunned, int cubeMode, bool cameraAllowsMove = true)
    {
        if (dead) return false;
        if (inputBlocked) return false;
        if (!CanAct(stateFlags)) return false;
        if (stunned) return false;                  // m_ready 0
        if (cubeMode == 1) return false;
        return cameraAllowsMove;
    }
}
