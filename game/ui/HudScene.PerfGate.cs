using System.Text.Json.Nodes;
using Godot;
using Ms2Godot.Net;

namespace Ms2Godot.Ui;

/// <summary>
/// MS2_PERF_GATE=&lt;file.json&gt;: one session of the perf regression gate (tools/perfgate/run.sh). After the field is
/// loaded, on a fixed frame schedule (run with --fixed-fps, so every frame's delta is the same whatever its wall time):
/// <list type="bullet">
/// <item>the field load: wall time, bytes allocated (all threads), collections, engine object count and JIT-compiled
/// methods, from process start;</item>
/// <item>the field idle (no dialog open) and the HUD with three dialogs open: bytes (main thread and all threads) and
/// collections per frame, and the field effects' bytes per effect frame (<see cref="EffectInstance.Count"/>);</item>
/// <item>each of <see cref="GateDialogs"/>: the first open this session, then <see cref="GateRepeats"/> repeat opens; each
/// open's bytes, collections, JIT-compiled methods and time from just before its toggle to the end of the <see cref="GateOpenFrames"/>th
/// frame after it, the engine objects it added, and the objects still there 35 frames after its close.</item>
/// </list>
/// MS2_PERF_GATE_CASE names the session ("install": the first after setup; "session": a later one). The file is written
/// and the scene quits at the end.
/// </summary>
public partial class HudScene
{
    static readonly string GatePath = System.Environment.GetEnvironmentVariable("MS2_PERF_GATE") is { Length: > 0 } g ? g : null;
    static readonly string[] GateDialogs = { "inventory", "myinfo", "skill", "quest", "worldmap", "shop", "options" };
    const int GateRepeats = 3, GateOpenFrames = 3, GateSettle = 300, GateIdle = 300, GateQuiet = 15, GatePer = 100 + GateRepeats * 100;

    /// <summary>
    /// The upper HUD's clock in a gate session: a fixed time, as offline it is the local time and its once-a-minute redraw
    /// would land in whichever window the wall clock reaches the minute in.
    /// </summary>
    static readonly DateTime GateClock = new(2026, 1, 1, 12, 0, 0);

    int gateFrame = -1;
    readonly JsonObject gateOut = new();
    readonly JsonArray gateOpens = new();
    GateMark gateOpen, gateIdle;
    string gateIdleName;
    readonly List<double> gateFrameMs = new();
    long gateLastTick;

    /// <summary>The counters a measured window starts from.</summary>
    readonly record struct GateMark(long Main, long Total, int Gc0, int Gc1, int Gc2, double Objects, double Nodes, long Ticks,
        long FxBytes, long FxInstances, long FxTicks, long Jit, long JitTicks, long JitAll)
    {
        public static GateMark Now() => new(GC.GetAllocatedBytesForCurrentThread(), GC.GetTotalAllocatedBytes(true),
            GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
            Performance.GetMonitor(Performance.Monitor.ObjectCount), Performance.GetMonitor(Performance.Monitor.ObjectNodeCount),
            System.Diagnostics.Stopwatch.GetTimestamp(), EffectInstance.CountBytes, EffectInstance.CountInstances, EffectInstance.CountTicks,
            System.Runtime.JitInfo.GetCompiledMethodCount(true), System.Runtime.JitInfo.GetCompilationTime(true).Ticks,
            System.Runtime.JitInfo.GetCompiledMethodCount(false));

        public static double Ms(long from, long to) => (to - from) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    static JsonObject GateDelta(GateMark a, GateMark b) => new()
    {
        ["main_bytes"] = b.Main - a.Main,
        ["total_bytes"] = b.Total - a.Total,
        ["gc0"] = b.Gc0 - a.Gc0, ["gc1"] = b.Gc1 - a.Gc1, ["gc2"] = b.Gc2 - a.Gc2,
        ["objects"] = (long)(b.Objects - a.Objects),
        ["nodes"] = (long)(b.Nodes - a.Nodes),
        // methods the main thread compiled (the ones its frames waited for), and every thread's (tier-up included)
        ["jit_methods"] = b.Jit - a.Jit,
        ["jit_all_methods"] = b.JitAll - a.JitAll,
        ["jit_ms"] = Math.Round(TimeSpan.FromTicks(b.JitTicks - a.JitTicks).TotalMilliseconds, 3),
        ["ms"] = Math.Round(GateMark.Ms(a.Ticks, b.Ticks), 3),
    };

    void GateStep()
    {
        gateFrame++;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (gateIdleName != null && gateLastTick != 0) gateFrameMs.Add(GateMark.Ms(gateLastTick, now));
        gateLastTick = now;
        if (gateFrame == 0)
        {
            EffectInstance.Count = true;
            var m = GateMark.Now();
            gateOut["case"] = System.Environment.GetEnvironmentVariable("MS2_PERF_GATE_CASE") ?? "session";
            gateOut["field"] = new JsonObject
            {
                ["map"] = "02000001_tw_tria",   // the offline field hud.tscn loads without --field
                ["ms"] = Math.Round((DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds, 1),
                ["total_bytes"] = m.Total,
                ["gc0"] = m.Gc0, ["gc1"] = m.Gc1, ["gc2"] = m.Gc2,
                ["objects"] = (long)m.Objects,
                ["nodes"] = (long)m.Nodes,
                ["jit_methods"] = m.Jit,
                ["jit_all_methods"] = m.JitAll,
                ["jit_ms"] = Math.Round(TimeSpan.FromTicks(m.JitTicks).TotalMilliseconds, 3),
            };
            GatePregrowTrackers();   // after the field's numbers: they stay the player's
            return;
        }
        int t = gateFrame - GateSettle;
        if (t == -GateQuiet) GateCollect();
        if (t < 0) return;
        if (t == 0) GateIdleStart("idle");
        if (t == GateIdle) GateIdleEnd();   // its collection is also the first open's (GateQuiet frames before it)
        t -= GateIdle + GateQuiet;
        if (t < 0) return;
        int i = t / GatePer, s = t % GatePer;
        if (i < GateDialogs.Length)
        {
            string d = GateDialogs[i];
            int k = s / 100, r = s % 100;   // k = 0: the first open; 1..GateRepeats: repeats; each 100 frames: open, close at 50
            // each open starts GateQuiet frames after a collection (the previous open's kept-objects one, or the idle's):
            // the engine objects that collection's finalizers queued are freed by then, outside the window
            if (r == 0) { gateOpen = GateMark.Now(); PerfToggle(d, true); }
            else if (r == GateOpenFrames)
            {
                var o = GateDelta(gateOpen, GateMark.Now());
                // the engine objects the open left alive: counted after a collection, so the wrappers a collection in
                // the window happened to finalize (or not) do not move the count
                GateCollect();
                o["objects"] = (long)(Performance.GetMonitor(Performance.Monitor.ObjectCount) - gateOpen.Objects);
                o["nodes"] = (long)(Performance.GetMonitor(Performance.Monitor.ObjectNodeCount) - gateOpen.Nodes);
                o["dialog"] = d;
                o["kind"] = k == 0 ? "first" : "repeat";
                gateOpens.Add(o);
            }
            else if (r == 50) PerfToggle(d, false);
            else if (r == 100 - GateQuiet)
            {
                GateCollect();
                ((JsonObject)gateOpens[^1])["kept_objects"] = (long)(Performance.GetMonitor(Performance.Monitor.ObjectCount) - gateOpen.Objects);
            }
            return;
        }
        t -= GateDialogs.Length * GatePer;
        if (t == 0) { ToggleInventory(); ToggleMyInfo(); ToggleSkillBook(); }
        else if (t == 100 - GateQuiet) GateCollect();
        else if (t == 100) GateIdleStart("idle_3_open");
        else if (t == 100 + GateIdle) { GateIdleEnd(); CloseAllDialogs(); skillBook?.Close(); }
        else if (t == 100 + GateIdle + 30)
        {
            gateOut["opens"] = gateOpens;
            System.IO.File.WriteAllText(GatePath, gateOut.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"perf gate: {GatePath}");
            GetTree().Quit(0);
        }
    }

    /// <summary>
    /// GodotSharp's DisposablesTracker holds every wrapper in a ConcurrentDictionary, whose growth rebuilds every entry
    /// (some 3 MB at the field's ~60000 wrappers). The count crosses the growth point once a session, in whichever of
    /// the first few opens it reaches it. The gate grows both tables up front, past twice their count (they never
    /// shrink), so that one-time engine cost lands outside every measured window.
    /// </summary>
    static void GatePregrowTrackers()
    {
        var t = typeof(GodotObject).Assembly.GetType("Godot.DisposablesTracker");
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        void Grow<T>(string name) where T : class
        {
            if (t?.GetProperty(name, F)?.GetValue(null) is not System.Collections.Concurrent.ConcurrentDictionary<WeakReference<T>, byte> d)
            {
                GD.PushWarning($"perf gate: no DisposablesTracker.{name}; its growth may land in a window");
                return;
            }
            var keys = new WeakReference<T>[d.Count + 1024];
            for (int i = 0; i < keys.Length; i++) d.TryAdd(keys[i] = new WeakReference<T>(null), 0);
            foreach (var k in keys) d.TryRemove(k, out _);
        }
        Grow<GodotObject>("GodotObjectInstances");
        Grow<IDisposable>("OtherInstances");
    }

    void GateIdleStart(string name) { gateIdleName = name; gateIdle = GateMark.Now(); gateFrameMs.Clear(); }

    /// <summary>
    /// A full collection with the finalizers run: every measured window starts from no garbage (so its collections are
    /// its own allocation's, not the ones before it), and the object counts are of live objects.
    /// </summary>
    static void GateCollect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    void GateIdleEnd()
    {
        var m = GateMark.Now();
        var o = GateDelta(gateIdle, m);
        GateCollect();
        o["objects"] = (long)(Performance.GetMonitor(Performance.Monitor.ObjectCount) - gateIdle.Objects);
        o["nodes"] = (long)(Performance.GetMonitor(Performance.Monitor.ObjectNodeCount) - gateIdle.Nodes);
        o["frames"] = GateIdle;
        long fxInst = m.FxInstances - gateIdle.FxInstances;
        o["fx_effect_frames"] = fxInst;
        o["fx_bytes"] = m.FxBytes - gateIdle.FxBytes;
        o["fx_us_per_effect"] = fxInst == 0 ? 0 : Math.Round(GateMark.Ms(gateIdle.FxTicks, m.FxTicks) * 1000 / fxInst, 3);
        gateFrameMs.Sort();
        o["frame_ms_median"] = gateFrameMs.Count == 0 ? 0 : Math.Round(gateFrameMs[gateFrameMs.Count / 2], 3);
        o["frame_ms_worst"] = gateFrameMs.Count == 0 ? 0 : Math.Round(gateFrameMs[^1], 3);
        gateOut[gateIdleName] = o;
        gateIdleName = null;
    }
}
