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
    /// (the SP idle for subs 0xc / 0x87), anything else plays the SP idle when the ridee's m_unk_b80 is set
    /// (<paramref name="spIdle"/>, FUN_141676b70: the RunX flag of the own ridee; a remote ridee's is never set), else rideAniPC.
    /// </summary>
    public static string For(int state, int sub, in RiderSequences s, bool spIdle = false) => state switch
    {
        RemotePcStates.StateWalk or RemotePcStates.StateSwim => sub is 0x17 or 0x85 ? s.SpRun : s.Run,
        RemotePcStates.StateJump => sub == 0x27 ? s.SpJump : s.Jump,
        RemotePcStates.StateLand => s.Idle,
        RemotePcStates.StateIdle => sub is 0xc or 0x87 ? s.SpIdle : s.Idle,
        _ => spIdle ? s.SpIdle : s.Pc,
    };

    /// <summary>
    /// StateHold::vfunc_9 (@0x1417cc5a0, stateholddata.cpp:309-370) for a PC target: the passenger's sequence of its seat by
    /// the owner's ridee's state and sub. Walk (2) or swim (0x1b) runs (the SP run for subs 0x17 / 0x85), the jump (6)
    /// jumps (the SP jump for sub 0x27), idle (1) and land (4) stand (the SP idle for subs 0xc / 0x87), anything else plays
    /// the SP idle when the ridee's m_unk_b80 is set (<paramref name="spIdle"/>, FUN_141676b70), else rideAniPC.
    /// </summary>
    public static string ForPassenger(int state, int sub, bool spIdle, in RiderSequences s) => state switch
    {
        RemotePcStates.StateWalk or RemotePcStates.StateSwim => sub is 0x17 or 0x85 ? s.SpRun : s.Run,
        RemotePcStates.StateJump => sub == 0x27 ? s.SpJump : s.Jump,
        RemotePcStates.StateIdle or RemotePcStates.StateLand => sub is 0xc or 0x87 ? s.SpIdle : s.Idle,
        _ => spIdle ? s.SpIdle : s.Pc,
    };
}
