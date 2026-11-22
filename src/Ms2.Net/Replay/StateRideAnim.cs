namespace Ms2.Net.Replay;

/// <summary>
/// The rider sequences of a riding row (RideeData::Rider, FUN_140782110: rideAniPC, then rideAniPC_Idle / _Run / _Jump /
/// _SP_Idle / _SP_Run / _SP_Jump, each falling back to rideAniPC when empty).
/// </summary>
public readonly record struct RiderSequences(string Pc, string Idle, string Run, string Jump, string SpIdle, string SpRun, string SpJump);

/// <summary>
/// StateRide::vfunc_9 (@0x1417e0780, statemanager.cpp:1119-1185): the rider's sequence by its ridee's state and sub, played
/// at the ridee's animation speed (FUN_1416713c0, m_f938).
/// </summary>
public static class StateRideAnim
{
    /// <summary>
    /// The sequence for a ridee in <paramref name="state"/> with sub <paramref name="sub"/>: walk (2) or swim (0x1b) runs
    /// (the SP run for subs 0x17 / 0x85), the jump (6) jumps (the SP jump for sub 0x27), land (4) stands, idle (1) stands
    /// (the SP idle for subs 0xc / 0x87), anything else plays rideAniPC.
    /// STILL-OPEN: the SP idle of the last branch (the ridee's m_unk_b80, FUN_141676b70) is taken as unset.
    /// </summary>
    public static string For(int state, int sub, in RiderSequences s) => state switch
    {
        RemotePcStates.StateWalk or RemotePcStates.StateSwim => sub is 0x17 or 0x85 ? s.SpRun : s.Run,
        RemotePcStates.StateJump => sub == 0x27 ? s.SpJump : s.Jump,
        RemotePcStates.StateLand => s.Idle,
        RemotePcStates.StateIdle => sub is 0xc or 0x87 ? s.SpIdle : s.Idle,
        _ => s.Pc,
    };
}
