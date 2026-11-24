namespace Ms2.Ui.Hud;

/// <summary>
/// WorldService's one field timer (m_804 the start server tick, m_800 the duration in ms, m_808 the auto-remove flag): the
/// Trigger timer kinds (CTriggerService::FUN_141862110, triggerservice.cpp:527) and the typing game (uitypinggamedialog.cpp:333)
/// set it; the trigger timer dialog (0x4e95) and the PvP observer (0x4e96) read it.
/// </summary>
public sealed class WorldTimer
{
    uint start, duration;
    int autoRemove;

    /// <summary>The start server tick, the duration in ms and the auto-remove flag last set.</summary>
    public (uint Start, uint Duration, int AutoRemove) State => (start, duration, autoRemove);

    /// <summary>WorldService::FUN_141bc6f60 (@0x141bc6f60): m_804 = start, m_800 = duration, m_808 = the flag.</summary>
    public void Set(int startTick, int durationMs, int flag)
    {
        start = (uint)startTick;
        duration = (uint)durationMs;
        autoRemove = flag;
    }

    /// <summary>
    /// WorldService::FUN_141bc6fb0 (@0x141bc6fb0): the remaining time at the server tick now, as minutes, seconds and
    /// hundredths (FUN_141b706a0: ms / 1000 / 60, ms / 1000 % 60, ms % 1000 / 10), and the auto-remove flag, which is cleared
    /// while time remains. All are 0 (the flag as set) without a duration or a server tick (false); before the start the
    /// whole duration; at or after the end 0 (true). The comparisons are unsigned, as the client's.
    /// </summary>
    public bool Get(int now, out int minutes, out int seconds, out int hundredths, out int flag)
    {
        minutes = seconds = hundredths = 0;
        flag = autoRemove;
        if (duration == 0) return false;
        uint tick = (uint)now;
        if (tick == 0) return false;
        if (start > tick)
        {
            Split(duration, out minutes, out seconds, out hundredths);
            return true;
        }
        uint elapsed = tick - start;
        if (elapsed >= duration) return true;
        uint remain = duration - elapsed;
        Split(remain, out minutes, out seconds, out hundredths);
        if (remain > 0) flag = 0;
        return true;
    }

    /// <summary>FUN_141b706a0 (@0x141b706a0): ms into minutes, seconds and hundredths.</summary>
    static void Split(uint ms, out int minutes, out int seconds, out int hundredths)
    {
        minutes = (int)(ms / 1000 / 60);
        seconds = (int)(ms / 1000 % 60);
        hundredths = (int)(ms % 1000 / 10);
    }
}
