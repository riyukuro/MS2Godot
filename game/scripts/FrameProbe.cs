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
    long last, total, max; int frames, gc0; double ms; long objs;

    static readonly AllocSampler Sampler = System.Environment.GetEnvironmentVariable("MS2_FRAMEPROBE") == "sites" ? new AllocSampler() : null;

    public FrameProbe() { ProcessPriority = int.MinValue; ProcessMode = ProcessModeEnum.Always; }

    public override void _Process(double delta)
    {
        long now = GC.GetAllocatedBytesForCurrentThread();
        if (++frames <= Warm) { last = now; gc0 = GC.CollectionCount(0); return; }
        long d = now - last; last = now;
        total += d; if (d > max) max = d; ms += delta * 1000;
        int n = frames - Warm;
        if (n % Window != 0) return;
        objs = (long)Performance.GetMonitor(Performance.Monitor.ObjectCount);
        GD.Print($"frameprobe frames {n} bytes/frame avg {total / Window} max {max} objects {objs} frame ms {ms / Window:F2} gen0 {GC.CollectionCount(0) - gc0}");
        Census("window " + n);
        Sampler?.Print();
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
            foreach (var kv in System.Linq.Enumerable.Take(System.Linq.Enumerable.OrderByDescending(byType, k => k.Value), 10)) GD.Print($"  census {kv.Key} {kv.Value}");   // perf-ok: env-gated probe, one census per 120 frames
        }
    }

    /// <summary>MS2_FRAMEPROBE=sites: the runtime's allocation ticks (one per ~100 KB) by type (the runtime delivers them on its own thread, so no stack), printed per window with the window's top 12. Samples, so only large sites show; it is a finder, not a counter.</summary>
    sealed class AllocSampler : System.Diagnostics.Tracing.EventListener
    {
        readonly System.Collections.Generic.Dictionary<string, long> bytes = new();
        readonly object gate = new();
        int armed = 1;

        protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource src)
        {
            if (src.Name == "Microsoft-Windows-DotNETRuntime")
                EnableEvents(src, System.Diagnostics.Tracing.EventLevel.Verbose, (System.Diagnostics.Tracing.EventKeywords)0x1);
        }

        protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs e)
        {
            if (e.EventName == null || !e.EventName.StartsWith("GCAllocationTick") || System.Threading.Volatile.Read(ref armed) == 0) return;
            string type = "?"; long amount = 0;
            for (int i = 0; i < e.PayloadNames.Count; i++)
            {
                if (e.PayloadNames[i] == "TypeName") type = e.Payload[i]?.ToString();
                else if (e.PayloadNames[i] == "AllocationAmount64") amount = Convert.ToInt64(e.Payload[i]);
            }
            string site = "";
            lock (gate) { var k = type + " @ " + site; bytes.TryGetValue(k, out var c); bytes[k] = c + amount; }
        }

        public void Print()
        {
            System.Threading.Volatile.Write(ref armed, 1);
            string[] top;
            lock (gate)
            {
                top = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(System.Linq.Enumerable.Take(System.Linq.Enumerable.OrderByDescending(bytes, k => k.Value), 12), k => $"  sites {k.Value / 1024} KB {k.Key}"));
                bytes.Clear();
            }
            foreach (var l in top) GD.Print(l);
        }
    }
}
