namespace Ms2.Avm2;

public sealed partial class Runtime
{
    /// <summary>
    /// The movie's elapsed time in ms, MovieImpl::TimeElapsed (microseconds there): the interval timers (setTimeout,
    /// setInterval and flash.utils.Timer) run on it. It moves by the time each <see cref="Flash.Player.Advance"/> is handed
    /// (MovieImpl::Advance, sfr_gfx_01i.cpp:3986: TimeElapsed += deltaT*1e6), so a game frame that runs no movie frame still
    /// moves it. getTimer reads real time instead (Player.ASTimerMs).
    /// </summary>
    public double TimerMs;
    /// <summary>Wall clock for new Date(); a fixed value when <see cref="FixedWallClock"/> is set (tests, scans).</summary>
    public DateTime? FixedWallClock;
    public double WallClockMs() => ((FixedWallClock ?? DateTime.UtcNow).ToUniversalTime() - DateTime.UnixEpoch).TotalMilliseconds + (FixedWallClock != null ? TimerMs : 0);

    /// <summary>
    /// AS3::IntervalTimer (sfr_as3_15.cpp:3589/3596): a setTimeout / setInterval function with its arguments, or the core
    /// of a running flash.utils.Timer (TimerObj, with its repeat count taken at start). Times are whole microseconds, as
    /// the client's: Interval = delay * 1000, InvokeTime the next due time on <see cref="TimerMs"/>.
    /// </summary>
    internal sealed class IntervalTimer
    {
        public int Id;
        public AsFunction Function;
        public object[] Params;
        public Flash.AsTimer TimerObj;
        public long Interval, InvokeTime;
        public uint CurrentCount, RepeatCount;   // unsigned, as the client's: a negative repeatCount is a very large one
        public bool Timeout, Active = true;
    }

    // MovieImpl::IntervalTimers: one list in the order the timers were added, for the functions and the Timer objects.
    // STILL-OPEN: the client keeps one list, one elapsed time and one id counter per group root movie, shared by every swf
    // loaded into it, so all of a group's dialogs' timers fire in one add order in one pass. Here each dialog's Runtime has
    // its own, run in the scene order of the movie views. getTimer already counts from one start (Player.ASTimerMs); the
    // timer list does not. Only observable between two dialogs' timers due in the same advance; no case found.
    readonly List<IntervalTimer> intervalTimers = new();
    int lastIntervalTimerId;

    long TimeElapsedUs => (long)Math.Round(TimerMs * 1000);   // TimerMs is a sum of doubles; the client's count is whole

    /// <summary>
    /// MovieImpl::AddIntervalTimer (@0x14256bd60, sfr_gfx_01i.cpp:1117) and IntervalTimer::Start (sfr_as3_19.cpp:9990):
    /// the id is the movie's next (one counter for timeouts, intervals and Timers), the timer goes at the end of the list,
    /// and it is due one interval from now, with no floor on that first interval.
    /// </summary>
    internal IntervalTimer AddIntervalTimer(IntervalTimer t)
    {
        t.Id = ++lastIntervalTimerId;
        intervalTimers.Add(t);
        t.InvokeTime = TimeElapsedUs + t.Interval;
        return t;
    }

    /// <summary>
    /// setTimeout / setInterval (GlobalObjectCPP::setInterval @0x14242a0c0, sfr_as3_04.cpp:2921; setTimeout
    /// sfr_as3_05.cpp:1923): the delay is the argument as a UInt32 (Convert2UInt32), the extra arguments go to the call.
    /// The function value is kept as given; one that is not a function is not called (the client's call throws, and the
    /// error is printed and dropped).
    /// STILL-OPEN: the ArgumentError for fewer than 2 arguments (eWrongArgumentCountError) is not thrown here.
    /// </summary>
    public int AddTimeout(AsFunction f, uint delay, object[] args, bool repeat) =>
        AddIntervalTimer(new IntervalTimer { Function = f, Params = args, Interval = (long)delay * 1000, Timeout = !repeat }).Id;

    /// <summary>MovieImpl::ClearIntervalTimer (@0x14256e0d0, sfr_gfx_01i.cpp:1127): the first timer with the id stops; the
    /// next advance takes it off the list. clearTimeout and clearInterval both come here, so either clears any timer.</summary>
    public void ClearTimeout(int id)
    {
        for (int i = 0; i < intervalTimers.Count; i++)
            if (intervalTimers[i].Id == id) { intervalTimers[i].Active = false; break; }
    }

    /// <summary>
    /// MovieImpl::ShutdownTimersForMovieDef (@0x142576cc0, sfr_gfx_01i.cpp:2403): each Timer core whose Timer object's
    /// class comes from the movie definition <paramref name="movieKey"/> stops (see Player). The client puts a null in its
    /// place; here it stays stopped in its place until the next advance takes it off, so a loop over the list in progress
    /// keeps its positions.
    /// </summary>
    internal void ShutdownTimersForMovieDef(string movieKey)
    {
        foreach (var t in intervalTimers)
            if (t.TimerObj is { } o && string.Equals(o.Class?.MovieKey, movieKey, StringComparison.OrdinalIgnoreCase)) t.Active = false;
    }

    /// <summary>
    /// The time part of MovieImpl::Advance (sfr_gfx_01i.cpp:3986-4022), before its frame: the elapsed time moves on by
    /// <paramref name="ms"/>, each timer that was on the list at the start and is still running is invoked once
    /// (<see cref="Invoke"/>), and the stopped ones are taken off after. A timer added by a callback first runs at the next
    /// advance. <paramref name="frameMs"/> is the movie's frame time (1000/fps).
    /// The client counts each advance's time as UInt64(deltaT*1e6f), cut to whole microseconds per advance; here the time is
    /// summed as a double and read to the nearest microsecond.
    /// </summary>
    public void AdvanceClock(double ms, double frameMs)
    {
        TimerMs += ms;
        int n = intervalTimers.Count;
        if (n == 0) return;
        long now = TimeElapsedUs, frameUs = (long)((float)(frameMs * 0.001) * 1000000f);
        bool inactive = false;
        advanceDepth++;
        try
        {
            for (int i = 0; i < n; i++)
            {
                var t = intervalTimers[i];
                if (t.Active) Invoke(t, now, frameUs);
                if (!t.Active) inactive = true;
            }
        }
        finally { advanceDepth--; }
        // only the outermost pass takes the stopped ones off: a callback that advances the movie again (a nested pass)
        // must not move the entries under the pass that is still running
        if (inactive && advanceDepth == 0) intervalTimers.RemoveAll(static t => !t.Active);
    }

    int advanceDepth;

    /// <summary>
    /// IntervalTimer::Invoke (@0x142512fc0, sfr_as3_18.cpp:7219): once the invoke time is reached, a Timer's event
    /// (while under its repeat count) or the function, an exception printed and dropped. Then a timeout, or a Timer at its
    /// repeat count, stops; otherwise the next invoke time is a whole number of intervals past the current one, the missed
    /// ones skipped, with the interval at least a tenth of the frame time. So a timer fires at most once per advance.
    /// </summary>
    void Invoke(IntervalTimer t, long now, long frameUs)
    {
        if (now < t.InvokeTime) return;
        if (t.TimerObj != null)
        {
            if (t.RepeatCount == 0 || t.CurrentCount < t.RepeatCount)
            {
                ++t.CurrentCount;
                Guard("timer", null, t.TimerObj, static o => o.ExecuteEvent());
            }
        }
        else if (t.Function != null) Guard("timeout", null, t, static x => x.Function.Call(null, x.Params));
        if (t.Timeout || (t.RepeatCount != 0 && t.CurrentCount >= t.RepeatCount)) t.Active = false;
        else
        {
            long interval = t.Interval < frameUs / 10 ? frameUs / 10 : t.Interval;
            if (interval == 0) t.InvokeTime = now;
            else t.InvokeTime += (now - t.InvokeTime + interval) / interval * interval;
        }
    }

    readonly Dictionary<AsClass, AsClass> vectorTypes = new();

    /// <summary>applytype: Vector.&lt;T&gt; gives a Vector class bound to T.</summary>
    public object ApplyType(object factory, object[] types)
    {
        if (factory != VectorClass) throw TypeError("applytype on a non-generic type");
        var t = types.Length > 0 ? types[0] as AsClass : null;
        if (t == null) return VectorClass;
        if (vectorTypes.TryGetValue(t, out var c)) return c;
        var v = VectorClass;
        c = new AsClass
        {
            Runtime = this, Name = "__AS3__.vec.Vector.<" + t.Name + ">", LocalName = "Vector.<" + t.LocalName + ">", Ns = v.Ns, Super = v.Super,
            InstanceTraits = v.InstanceTraits, Traits = v.Traits, Slots = v.Slots, Prototype = v.Prototype, Allocator = v.Allocator,
            Ctor = v.Ctor, IsNative = true, TypeArg = t, Class = ClassClass,
        };
        c.CallAsFunction = a => a.Length > 0 && a[0] is AsArray src ? new AsArray(this, src.Items, c) : new AsArray(this, null, c);
        vectorTypes[t] = c;
        return c;
    }
}
