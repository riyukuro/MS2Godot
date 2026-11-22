namespace Ms2.Ui;

/// <summary>
/// The own PC's current state id as PCBase::GetCurStateId gives it (the state machine's current state, gostate.h ids), for the
/// readers that want the real state rather than the interaction gates' view (<see cref="InteractKey.StateId"/> maps every move
/// other than idle and walk to -1). The hit candidate's state (SkillDamage +0x98, read by the arrow fill's stateHitIgnore filter)
/// is one of them.
/// </summary>
public static class OwnPcState
{
    /// <summary>StateSkill's id (vfunc_59(0x10), HudScene's own cast).</summary>
    public const int StateSkill = 0x10;
    /// <summary>StateRide's id while mounted.</summary>
    public const int StateRide = 0x21;

    /// <summary>
    /// The state id: riding 0x21; a state the server put the PC in (<paramref name="serverState"/> other than the spawn, idle
    /// and walk ids 0, 1, 2) is kept; 0x10 while the own skill state runs; else the move state's own id
    /// (<paramref name="moveStateId"/>: idle 1, walk 2, land 4, fall 5, jump 6, ladder 8, rope 9, dash 20, swim 27, climb 29).
    /// </summary>
    public static int CurStateId(int serverState, bool mounted, bool skillStateRuns, int moveStateId)
    {
        if (mounted) return StateRide;
        if (serverState is not (0 or 1 or 2)) return serverState;
        return skillStateRuns ? StateSkill : moveStateId;
    }
}
