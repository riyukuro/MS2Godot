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
    static double sum, worst;
    static ulong lastTicks;
    static int gc0, gc1, gc2;
    static long allocated;

    public static void Print(string line)
    {
        if (On) GD.Print("perf: " + line);
    }

    /// <summary>Call once per frame (from one node's _Process) with the label of the scene's current state.</summary>
    public static void Frame(string state)
    {
        if (!On) return;
        ulong now = Time.GetTicksUsec();
        if (lastTicks == 0) { lastTicks = now; Reset(); return; }
        double ms = (now - lastTicks) / 1000.0;
        lastTicks = now;
        frames++; sum += ms; if (ms > worst) worst = ms;
        if (frames < FrameWindow) return;
        Print($"frames [{state}] {frames / (sum / 1000.0):0.0} fps, mean {sum / frames:0.00} ms, worst {worst:0.0} ms, " +
              $"gc {GC.CollectionCount(0) - gc0}/{GC.CollectionCount(1) - gc1}/{GC.CollectionCount(2) - gc2}, " +
              $"alloc {(GC.GetTotalAllocatedBytes() - allocated) / 1048576.0 / (sum / 1000.0):0.0} MB/s, " +
              $"process {Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000:0.00} ms, draw calls {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):0}");
        Reset();
    }

    static void Reset()
    {
        frames = 0; sum = 0; worst = 0;
        gc0 = GC.CollectionCount(0); gc1 = GC.CollectionCount(1); gc2 = GC.CollectionCount(2);
        allocated = GC.GetTotalAllocatedBytes();
    }

    /// <summary>Restart the frame window (a state change such as a window opening).</summary>
    public static void Restart() { if (On) { lastTicks = 0; } }

    /// <summary>A stopwatch for the named steps of one operation; <see cref="Step"/> prints the time since the last step.</summary>
    public sealed class Steps
    {
        readonly string what;
        readonly Stopwatch all = Stopwatch.StartNew(), step = Stopwatch.StartNew();
        readonly System.Text.StringBuilder line = new();
        public Steps(string what) { this.what = what; }
        public void Step(string name) { line.Append($" {name} {step.Elapsed.TotalMilliseconds:0.0}"); step.Restart(); }
        public void Extra(string name, double ms) => line.Append($" {name} {ms:0.0}");
        public double TotalMs => all.Elapsed.TotalMilliseconds;
        public void Done() => Print($"{what}: total {all.Elapsed.TotalMilliseconds:0.0} ms:{line}");
    }
}
