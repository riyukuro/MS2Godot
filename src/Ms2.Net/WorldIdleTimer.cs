namespace Ms2.Net;

/// <summary>
/// The world's idle timer (WorldService m_a70 interval, m_a74 deadline, m_a78 idle flag, worldservice_1.cpp:732-780). The server
/// sends the interval (WorldTimeSync, <see cref="Packets.WorldTimeSync"/>); every input the player makes resets the deadline
/// (<see cref="ResetIdleTimer"/>) and the world's per-frame update checks it (<see cref="CheckIdleTimeout"/>). When the
/// deadline passes the client tells the server it is idle (SyncWorld true) and tells it again with false at the next input, or when the
/// interval is switched off. The clock is GetTickCountMs2 (a 32-bit millisecond count): the deadline wraps with it, as in the client.
/// Callers (the client's): game start (WorldService::OnGameStart @0x141bcf9f0, worldservice_1.cpp:5774-5782), a chat line sent
/// (UIMessenger::SendChat @0x141a10660, uimessenger.cpp:4360), an action key down (Unk_PCMove::IsActionKeyDown @0x141682fe0,
/// unit_141682f80.cpp:233-255) and the world's update (worldservice_1.cpp:9824).
/// </summary>
public sealed class WorldIdleTimer
{
    readonly Func<uint> tick;
    readonly Action<bool> sendSyncWorld;
    uint interval;   // m_a70
    uint deadline;   // m_a74, 0 until the first reset
    bool idle;       // m_a78

    /// <param name="tick">GetTickCountMs2, milliseconds.</param>
    /// <param name="sendSyncWorld">WorldService::SendSyncWorld (@0x141bca110): the SyncWorld request with this flag.</param>
    public WorldIdleTimer(Func<uint> tick, Action<bool> sendSyncWorld)
    {
        this.tick = tick;
        this.sendSyncWorld = sendSyncWorld;
    }

    /// <summary>GetTickCountMs2 on the real clock.</summary>
    public static uint SystemTick() => unchecked((uint)Environment.TickCount64);

    /// <summary>The playing world's timer (WorldService::GetInstance()): set by the session, read by the places the client calls
    /// ResetIdleTimer from; null outside a session.</summary>
    public static WorldIdleTimer Current { get; set; }

    public int IntervalMs => unchecked((int)interval);
    public uint DeadlineTick => deadline;
    /// <summary>m_a78: the idle SyncWorld went out and no input has come since.</summary>
    public bool Idle => idle;

    /// <summary>WorldService::ResetIdleTimer (@0x141bca010, worldservice_1.cpp:732-743): nothing without an interval; else the
    /// deadline is now + interval, and a timer that was idle says it is back (SyncWorld false).</summary>
    public void ResetIdleTimer()
    {
        if (interval == 0) return;
        deadline = unchecked(tick() + interval);
        if (idle)
        {
            idle = false;
            sendSyncWorld(idle);
        }
    }

    /// <summary>WorldService::CheckIdleTimeout (@0x141bca090, worldservice_1.cpp:746-759): not once idle, not before a reset set the
    /// deadline or without an interval; at the deadline the timer is idle and SyncWorld true goes out.</summary>
    public void CheckIdleTimeout()
    {
        if (idle) return;
        if (deadline == 0 || interval == 0) return;
        if (tick() >= deadline)
        {
            idle = true;
            sendSyncWorld(idle);
        }
    }

    /// <summary>WorldService::SetIdleInterval (@0x141bca170, worldservice_1.cpp:771-780): stores the interval; switching it off while
    /// idle says SyncWorld false. The deadline is not touched: the next ResetIdleTimer sets it.</summary>
    public void SetIdleInterval(int intervalMs)
    {
        interval = unchecked((uint)intervalMs);
        if (interval == 0 && idle)
        {
            idle = false;
            sendSyncWorld(idle);
        }
    }
}
