namespace Ms2.Audio;

/// <summary>
/// The stop rule of an effect's sound (CSoundEventEffect::vfunc_5 @0x141777520, soundeventmgr.cpp:612-626) over a ring of
/// voices: a sound stopped with its effect goes at once; with doNotStop the stop is only noted, and the sound goes 10 s
/// after it (m_dStopTime = now + 10.0) unless it ended before (CSoundEventEffect::vfunc_9 @0x1417771a0, :585-590). A
/// ticket names one sound in one voice: a voice taken by a newer sound drops the older ticket and its pending stop.
/// No allocation after the constructor.
/// </summary>
public sealed class EffectVoiceSlots
{
    /// <summary>The 10 s after a stop that a doNotStop sound plays on (soundeventmgr.cpp:617).</summary>
    public const double DoNotStopSeconds = 10.0;

    const int SlotBits = 8;
    readonly int[] tickets;
    readonly double[] stopAt;   // 0 = no stop pending
    int sequence;

    public EffectVoiceSlots(int count)
    {
        if (count < 1 || count > 1 << SlotBits) throw new ArgumentOutOfRangeException(nameof(count));
        tickets = new int[count];
        stopAt = new double[count];
    }

    public int Count => tickets.Length;

    /// <summary>Voices with a doNotStop stop pending.</summary>
    public int Pending { get; private set; }

    /// <summary>A new sound in <paramref name="slot"/>: its ticket (never 0). The slot's older sound and its pending stop go.</summary>
    public int Take(int slot)
    {
        ClearPending(slot);
        sequence = sequence == int.MaxValue >> SlotBits ? 1 : sequence + 1;
        return tickets[slot] = (sequence << SlotBits) | slot;
    }

    /// <summary>The slot of a ticket that still names its voice's sound, else -1.</summary>
    public int SlotOf(int ticket)
    {
        if (ticket == 0) return -1;
        int slot = ticket & ((1 << SlotBits) - 1);
        return slot < tickets.Length && tickets[slot] == ticket ? slot : -1;
    }

    /// <summary>
    /// The effect's stop for the sound of <paramref name="ticket"/> at <paramref name="now"/> (seconds): its slot when it
    /// must stop now (the ticket is then dropped, so a sound still waiting for its start delay never starts), else -1 (a
    /// doNotStop stop noted, a second stop keeping the first time, or a ticket no longer held).
    /// </summary>
    public int Stop(int ticket, bool doNotStop, double now)
    {
        int slot = SlotOf(ticket);
        if (slot < 0) return -1;
        if (doNotStop)
        {
            if (stopAt[slot] == 0) { stopAt[slot] = now + DoNotStopSeconds; Pending++; }
            return -1;
        }
        Free(slot);
        return slot;
    }

    /// <summary>A slot whose doNotStop stop is due at <paramref name="now"/>, freed; -1 when none. Call until -1.</summary>
    public int NextDue(double now)
    {
        if (Pending == 0) return -1;
        for (int i = 0; i < stopAt.Length; i++)
            if (stopAt[i] != 0 && now >= stopAt[i]) { Free(i); return i; }
        return -1;
    }

    /// <summary>The slot's sound ended or was stopped: its ticket and pending stop go.</summary>
    public void Free(int slot)
    {
        ClearPending(slot);
        tickets[slot] = 0;
    }

    void ClearPending(int slot)
    {
        if (stopAt[slot] == 0) return;
        stopAt[slot] = 0;
        Pending--;
    }
}
