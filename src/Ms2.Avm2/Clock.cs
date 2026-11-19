namespace Ms2.Avm2;

public sealed partial class Runtime
{
    /// <summary>Milliseconds since the movie started (getTimer); advanced by the player, so runs are reproducible.</summary>
    public double TimerMs;
    /// <summary>Wall clock for new Date(); a fixed value when <see cref="FixedWallClock"/> is set (tests, scans).</summary>
    public DateTime? FixedWallClock;
    public double WallClockMs() => ((FixedWallClock ?? DateTime.UtcNow).ToUniversalTime() - DateTime.UnixEpoch).TotalMilliseconds + (FixedWallClock != null ? TimerMs : 0);

    sealed class Timeout { public int Id; public AsFunction F; public double Due, Every; public object[] Args; public bool Repeat; }
    readonly List<Timeout> timeouts = new();
    int nextTimeout = 1;

    public int AddTimeout(AsFunction f, double ms, object[] args, bool repeat)
    {
        if (f == null) return 0;
        var t = new Timeout { Id = nextTimeout++, F = f, Due = TimerMs + Math.Max(0, ms), Every = Math.Max(1, ms), Args = args, Repeat = repeat };
        timeouts.Add(t);
        return t.Id;
    }

    public void ClearTimeout(int id) => timeouts.RemoveAll(t => t.Id == id);

    /// <summary>Advances the clock and runs the setTimeout / setInterval callbacks that are due.</summary>
    public void AdvanceClock(double ms)
    {
        TimerMs += ms;
        bool due = false;
        foreach (var t in timeouts) if (t.Due <= TimerMs) { due = true; break; }
        if (!due) return;
        foreach (var t in timeouts.Where(t => t.Due <= TimerMs).OrderBy(t => t.Due).ToList())
        {
            if (!timeouts.Contains(t)) continue;
            if (t.Repeat) t.Due += t.Every; else timeouts.Remove(t);
            Guard("timeout", () => t.F.Call(null, t.Args));
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
