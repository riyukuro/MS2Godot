namespace Ms2.Audio;

/// <summary>
/// The stop rule of an effect's sound (CSoundEventEffect::vfunc_5 @0x141777520, soundeventmgr.cpp:612-626) over a ring of
/// voices: a sound stopped with its effect goes at once; with doNotStop the stop is only noted, and the sound goes 10 s
/// after it (m_dStopTime = now + 10.0) unless it ended before (CSoundEventEffect::vfunc_9 @0x1417771a0, :585-590). A
/// ticket names one sound in one voice: a voice taken by a newer sound drops the older ticket and its pending stop.
/// <see cref="Start"/> chooses the voice of a new event sound as the client's FMOD event system does (see there).
/// No allocation after the constructor.
/// </summary>
public sealed class EffectVoiceSlots
{
    /// <summary>The 10 s after a stop that a doNotStop sound plays on (soundeventmgr.cpp:617).</summary>
    public const double DoNotStopSeconds = 10.0;

    const int SlotBits = 8;
    readonly int[] tickets;
    readonly double[] stopAt;   // 0 = no stop pending
    readonly object[] events;   // the event a Start sound is an instance of, null for none
    readonly double[] endAt;    // when a Start sound ends (its start delay and its wave's length)
    readonly float[] audible;
    readonly int[] priority;
    int sequence;

    public EffectVoiceSlots(int count)
    {
        if (count < 1 || count > 1 << SlotBits) throw new ArgumentOutOfRangeException(nameof(count));
        tickets = new int[count];
        stopAt = new double[count];
        events = new object[count];
        endAt = new double[count];
        audible = new float[count];
        priority = new int[count];
    }

    /// <summary>The client's FMOD channel count: CSoundService::OnInit's EventSystem::init(100, ...)
    /// (soundservice.cpp:523-538).</summary>
    public const int ClientChannels = 100;

    /// <summary>FMOD_EVENTPROPERTY_MAX_PLAYBACKS_BEHAVIOR (fmod_event.h:187).</summary>
    public const int StealOldest = 1, StealNewest = 2, StealQuietest = 3, JustFail = 4, JustFailIfQuietest = 5;

    /// <summary>
    /// A new sound of event <paramref name="ev"/> at <paramref name="now"/>, playing until <paramref name="end"/>: its ticket,
    /// or 0 when it fails. The client gets every effect sound's instance with EventGroup::getEvent(name, FMOD_EVENT_DEFAULT)
    /// as the effect is made (CSoundEventMgr::FUN_14177b3d0, soundeventmgr.cpp:949, from CSoundEventEffect::vfunc_3, :456-574),
    /// so the event's own limits choose: an event has "Max playbacks" instances (FEV event record +0x2c), and with all of
    /// them still sounding its "Max playbacks behavior" (+0x78) steals the oldest, the newest or the quietest of them, fails,
    /// or fails when the new one would be the quietest (fmod_event.h:186-187; FMOD Event System Best Practices, "Use just
    /// fail if quietest"). Under the limit the sound takes a free voice; with none free it takes the voice of the least
    /// important sound (the highest event priority number, +0x28, 0 the most important), the oldest of those, and fails
    /// when every sounding one is more important. <paramref name="audibility"/> is the new sound's volume at the listener.
    /// <paramref name="stolen"/> is set when the voice still held a sounding one, which the caller stops.
    /// </summary>
    public int Start(object ev, int maxPlaybacks, int behavior, int eventPriority, float audibility, double now, double end, out bool stolen)
    {
        stolen = false;
        int slot = -1;
        if (ev != null && maxPlaybacks > 0)
        {
            int held = 0, oldest = -1, newest = -1, quietest = -1;
            for (int i = 0; i < tickets.Length; i++)
            {
                if (events[i] != ev || !Sounding(i, now)) continue;
                held++;
                if (oldest < 0 || tickets[i] >> SlotBits < tickets[oldest] >> SlotBits) oldest = i;
                if (newest < 0 || tickets[i] >> SlotBits > tickets[newest] >> SlotBits) newest = i;
                if (quietest < 0 || audible[i] < audible[quietest]) quietest = i;
            }
            if (held >= maxPlaybacks)
            {
                slot = behavior switch
                {
                    StealOldest => oldest,
                    StealNewest => newest,
                    StealQuietest => quietest,
                    JustFailIfQuietest => audibility < audible[quietest] ? -1 : quietest,
                    _ => -1,
                };
                if (slot < 0) return 0;
                stolen = true;
            }
        }
        if (slot < 0)
        {
            int least = -1;
            for (int i = 0; i < tickets.Length && slot < 0; i++)
            {
                if (!Sounding(i, now)) { slot = i; break; }
                if (least < 0 || priority[i] > priority[least]
                    || priority[i] == priority[least] && tickets[i] >> SlotBits < tickets[least] >> SlotBits) least = i;
            }
            if (slot < 0)
            {
                if (priority[least] < eventPriority) return 0;
                slot = least; stolen = true;
            }
        }
        int ticket = Take(slot);
        events[slot] = ev; endAt[slot] = end; audible[slot] = audibility; priority[slot] = eventPriority;
        return ticket;
    }

    /// <summary>The slot holds a sound that has not ended or been stopped at <paramref name="now"/>.</summary>
    public bool Sounding(int slot, double now) => tickets[slot] != 0 && now < endAt[slot];

    /// <summary>The voices sounding at <paramref name="now"/>, for the play log.</summary>
    public int SoundingCount(double now)
    {
        int n = 0;
        for (int i = 0; i < tickets.Length; i++) if (Sounding(i, now)) n++;
        return n;
    }

    /// <summary>The voices of event <paramref name="ev"/> sounding at <paramref name="now"/>, for the play log.</summary>
    public int SoundingOf(object ev, double now)
    {
        int n = 0;
        for (int i = 0; i < tickets.Length; i++) if (ev != null && events[i] == ev && Sounding(i, now)) n++;
        return n;
    }

    public int Count => tickets.Length;

    /// <summary>Voices with a doNotStop stop pending.</summary>
    public int Pending { get; private set; }

    /// <summary>A new sound in <paramref name="slot"/>: its ticket (never 0). The slot's older sound and its pending stop go.</summary>
    public int Take(int slot)
    {
        ClearPending(slot);
        sequence = sequence == int.MaxValue >> SlotBits ? 1 : sequence + 1;
        events[slot] = null; endAt[slot] = double.PositiveInfinity; audible[slot] = 0; priority[slot] = 0;
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
        events[slot] = null;
    }

    void ClearPending(int slot)
    {
        if (stopAt[slot] == 0) return;
        stopAt[slot] = 0;
        Pending--;
    }
}
