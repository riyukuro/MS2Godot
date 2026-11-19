using System;
using Godot;

namespace Ms2Godot;

/// <summary>Diagnostic, off unless MS2_FRAMEPROBE is set: managed bytes allocated on the main thread per frame, the Godot object
/// count and the frame time, printed every 120 frames after a warm-up. It runs first in the frame, so the difference between two
/// calls is everything the frame did (all _Process and _PhysicsProcess calls, signals, input).</summary>
public sealed partial class FrameProbe : Node
{
    public static bool Enabled => System.Environment.GetEnvironmentVariable("MS2_FRAMEPROBE") != null;
    const int Warm = 300, Window = 120;
    static readonly System.Collections.Generic.Dictionary<string, long> acc = new();
    public static void Add(string k, long b) { acc.TryGetValue(k, out var v); acc[k] = v + b; }
    long last, total, max; int frames, gc0; double ms; long objs;

    public FrameProbe() { ProcessPriority = int.MinValue; ProcessMode = ProcessModeEnum.Always; }

    public override void _Process(double delta)
    {
        long now = GC.GetAllocatedBytesForCurrentThread();
        if (++frames <= Warm) { acc.Clear(); last = now; gc0 = GC.CollectionCount(0); return; }
        long d = now - last; last = now;
        total += d; if (d > max) max = d; ms += delta * 1000;
        int n = frames - Warm;
        if (n % Window != 0) return;
        objs = (long)Performance.GetMonitor(Performance.Monitor.ObjectCount);
        GD.Print($"frameprobe frames {n} bytes/frame avg {total / Window} max {max} objects {objs} frame ms {ms / Window:F2} gen0 {GC.CollectionCount(0) - gc0}");
        foreach (var kv in System.Linq.Enumerable.Take(System.Linq.Enumerable.OrderByDescending(acc, k => k.Value), 14)) GD.Print($"  site {kv.Key} {kv.Value / Window} B/frame");
        Census("window " + n);
        acc.Clear();
        total = 0; max = 0; ms = 0; gc0 = GC.CollectionCount(0);
    }

    /// <summary>Prints how many wrappers GodotSharp's DisposablesTracker holds, by type (its dictionaries are what rebuilds every
    /// entry when they grow). Reads the tracker by reflection, so it only works on the SDK's current internals.</summary>
    public static void Census(string label)
    {
        var tracker = typeof(GodotObject).Assembly.GetType("Godot.DisposablesTracker");
        if (tracker == null) return;
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        foreach (var prop in new[] { "GodotObjectInstances", "OtherInstances" })
        {
            if (tracker.GetProperty(prop, F)?.GetValue(null) is not System.Collections.IDictionary dict) continue;
            var byType = new System.Collections.Generic.Dictionary<string, int>();
            int live = 0, total = dict.Count;
            foreach (System.Collections.DictionaryEntry e in dict)
            {
                var wr = e.Key;
                object o = null;
                var m = wr.GetType().GetMethod("TryGetTarget");
                var args = new object[1];
                if (m != null && (bool)m.Invoke(wr, args)) o = args[0];
                if (o == null) continue;
                live++;
                var n = o.GetType().Name;
                byType.TryGetValue(n, out var c); byType[n] = c + 1;
            }
            GD.Print($"census {label} {prop}: entries {total} live {live}");
            foreach (var kv in System.Linq.Enumerable.Take(System.Linq.Enumerable.OrderByDescending(byType, k => k.Value), 10)) GD.Print($"  census {kv.Key} {kv.Value}");
        }
    }
}
