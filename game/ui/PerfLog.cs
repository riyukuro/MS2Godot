using System.Diagnostics;
using Godot;

namespace Ms2Godot.Ui;

/// <summary>
/// Timing log for frame and window-open costs, on only with MS2_PERF set: each line starts with "perf:". Frame stats are
/// printed every <see cref="FrameWindow"/> frames: frames per second, mean and worst frame time, the garbage collections
/// of each generation and the bytes allocated over the window.
/// </summary>
public static class PerfLog
{
    public static readonly bool On = !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("MS2_PERF"));
    const int FrameWindow = 100;

    static int frames;
    static double sum, worst, spent0, ticks0, mouse0;   // As3View.SpentMs, TickMs and MouseMs at the window start
    static long retests0;                               // As3View.Retests at the window start
    static double retestMs0;                            // As3View.RetestMs at the window start
    static long hitTests0; static double hitMs0;        // As3View.HitTests and HitMs at the window start
    // As3View.Sync time and movie ticks at the last frame; the window's frames with a movie tick and without, and their sync time
    static double syncLast; static long tickLast;
    static int tickFrames; static double syncTick, syncIdle;
    static ulong lastTicks, lastFrame;
    static int gc0, gc1, gc2;
    static long allocated;

    public static void Print(string line)
    {
        if (!On) return;
        if (!buildPrinted) { buildPrinted = true; GD.Print("perf: build " + Build()); }
        GD.Print("perf: " + line);
    }

    static bool buildPrinted;

    /// <summary>MS2_PERF_MAXFPS (with MS2_PERF): the frame rate cap (Engine.MaxFps) the run measures at, with vsync off.</summary>
    public static void ApplyFpsCap()
    {
        if (!On || !int.TryParse(System.Environment.GetEnvironmentVariable("MS2_PERF_MAXFPS"), out int fps)) return;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = fps;
        Print($"frame rate cap {fps}, vsync off");
    }

    /// <summary>The configuration each measured assembly was built in and whether the JIT optimizes it (a Debug build
    /// without Optimize carries DebuggableAttribute with the JIT optimizer off): the game's, then the libraries'.</summary>
    static string Build()
    {
        static string Of(System.Reflection.Assembly a)
        {
            var config = a.GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false) is [System.Reflection.AssemblyConfigurationAttribute c, ..] ? c.Configuration : "?";
            bool opt = a.GetCustomAttributes(typeof(DebuggableAttribute), false) is not [DebuggableAttribute d, ..] || !d.IsJITOptimizerDisabled;
            return $"{a.GetName().Name} {config} {(opt ? "optimized" : "not optimized")}";
        }
        return string.Join(", ", new[] { typeof(PerfLog), typeof(Ms2.Avm2.Runtime), typeof(Ms2.Ui.UiStage), typeof(Ms2.Formats.Pack.PackSet), typeof(Ms2.Quest.QuestTable) }
            .Select(t => Of(t.Assembly)));
    }

    /// <summary>Call once per frame (from one node's _Process) with the label of the scene's current state.</summary>
    public static void Frame(string state)
    {
        if (!On) return;
        ulong frame = Engine.GetProcessFrames();   // the field and the HUD both call this: count each drawn frame once
        if (frame == lastFrame) return;
        lastFrame = frame;
        ulong now = Time.GetTicksUsec();
        if (lastTicks == 0) { lastTicks = now; syncLast = As3View.SyncMs; tickLast = As3View.Ticks; Reset(); return; }
        double ms = (now - lastTicks) / 1000.0;
        lastTicks = now;
        frames++; sum += ms; if (ms > worst) worst = ms;
        double sync = As3View.SyncMs - syncLast; syncLast = As3View.SyncMs;
        bool ticked = As3View.Ticks != tickLast; tickLast = As3View.Ticks;
        if (ticked) { tickFrames++; syncTick += sync; } else syncIdle += sync;
        if (frames < FrameWindow) return;
        Print($"frames [{state}] {frames / (sum / 1000.0):0.0} fps, mean {sum / frames:0.00} ms, worst {worst:0.0} ms, " +
              $"gc {GC.CollectionCount(0) - gc0}/{GC.CollectionCount(1) - gc1}/{GC.CollectionCount(2) - gc2}, " +
              $"alloc {(GC.GetTotalAllocatedBytes() - allocated) / 1048576.0 / (sum / 1000.0):0.0} MB/s, " +
              $"process {Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000:0.00} ms, draw calls {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):0}, " +
              $"movie views {(As3View.SpentMs - spent0) / frames:0.00} ms (ticks {(As3View.TickMs - ticks0) / frames:0.00} ms), " +
              $"mouse {(As3View.MouseMs - mouse0) / frames:0.000} ms/frame ({As3View.Retests - retests0} retests, {(As3View.RetestMs - retestMs0) / frames:0.000} ms/frame; {As3View.HitTests - hitTests0} hit tests, {(As3View.HitMs - hitMs0) / frames:0.000} ms/frame), " +
              $"sync {(syncTick + syncIdle) / frames:0.000} ms/frame, frames without a movie tick {100.0 * (frames - tickFrames) / frames:0}% " +
              $"(sync {(frames > tickFrames ? syncIdle / (frames - tickFrames) : 0):0.000} ms; with a tick {(tickFrames > 0 ? syncTick / tickFrames : 0):0.000} ms)");
        Reset();
    }

    static void Reset()
    {
        frames = 0; sum = 0; worst = 0;
        gc0 = GC.CollectionCount(0); gc1 = GC.CollectionCount(1); gc2 = GC.CollectionCount(2);
        allocated = GC.GetTotalAllocatedBytes();
        spent0 = As3View.SpentMs; ticks0 = As3View.TickMs; mouse0 = As3View.MouseMs; retests0 = As3View.Retests; retestMs0 = As3View.RetestMs; hitTests0 = As3View.HitTests; hitMs0 = As3View.HitMs;
        tickFrames = 0; syncTick = syncIdle = 0;
    }

    /// <summary>
    /// One memory line for <paramref name="label"/>, after a full blocking collection: process RSS (/proc/self/status
    /// VmRSS), the managed heap (live bytes; the GC's committed size), and Godot's object, node, resource and orphan-node
    /// counts and texture / buffer / video memory.
    /// </summary>
    public static void Memory(string label)
    {
        if (!On) return;
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long live = GC.GetTotalMemory(false);
        var info = GC.GetGCMemoryInfo(GCKind.FullBlocking);
        double rss = 0;
        try
        {
            foreach (var l in System.IO.File.ReadLines("/proc/self/status"))
                if (l.StartsWith("VmRSS:")) { rss = long.Parse(l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]) / 1024.0; break; }
        }
        catch (System.IO.IOException) { }
        double M(Performance.Monitor m) => Performance.GetMonitor(m);
        Print($"mem [{label}] rss {rss:0} MB, managed {live / 1048576.0:0.0} MB (committed {info.TotalCommittedBytes / 1048576.0:0}), " +
              $"objects {M(Performance.Monitor.ObjectCount):0} nodes {M(Performance.Monitor.ObjectNodeCount):0} " +
              $"resources {M(Performance.Monitor.ObjectResourceCount):0} orphans {M(Performance.Monitor.ObjectOrphanNodeCount):0}, " +
              $"texture {M(Performance.Monitor.RenderTextureMemUsed) / 1048576.0:0.0} MB buffer {M(Performance.Monitor.RenderBufferMemUsed) / 1048576.0:0.0} MB " +
              $"video {M(Performance.Monitor.RenderVideoMemUsed) / 1048576.0:0.0} MB, static {M(Performance.Monitor.MemoryStatic) / 1048576.0:0.0} MB");
    }

    /// <summary>Restart the frame window (a state change such as a window opening).</summary>
    public static void Restart() { if (On) { lastTicks = 0; } }

    /// <summary>A stopwatch for the named steps of one operation; <see cref="Step"/> prints the time since the last step.</summary>
    public sealed class Steps
    {
        readonly string what;
        readonly Stopwatch all = Stopwatch.StartNew(), step = Stopwatch.StartNew();
        readonly System.Text.StringBuilder line = new();
        readonly int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        readonly long bytes = GC.GetTotalAllocatedBytes();
        // the methods compiled meanwhile (all threads) and the compiler's time: the part of a first open a later open skips
        readonly long jitted = System.Runtime.JitInfo.GetCompiledMethodCount();
        readonly TimeSpan jitTime = System.Runtime.JitInfo.GetCompilationTime();
        public Steps(string what) { this.what = what; }
        public void Step(string name) { line.Append($" {name} {step.Elapsed.TotalMilliseconds:0.0}"); step.Restart(); }
        public void Extra(string name, double ms) => line.Append($" {name} {ms:0.0}");
        public double TotalMs => all.Elapsed.TotalMilliseconds;
        public void Done() => Print($"{what}: total {all.Elapsed.TotalMilliseconds:0.0} ms:{line}, gc {GC.CollectionCount(0) - g0}/{GC.CollectionCount(1) - g1}/{GC.CollectionCount(2) - g2}, " +
                                    $"alloc {(GC.GetTotalAllocatedBytes() - bytes) / 1048576.0:0.0} MB, " +
                                    $"jit {System.Runtime.JitInfo.GetCompiledMethodCount() - jitted} methods {(System.Runtime.JitInfo.GetCompilationTime() - jitTime).TotalMilliseconds:0.0} ms");
    }
}
