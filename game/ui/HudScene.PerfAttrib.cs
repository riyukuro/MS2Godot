using System.Text.Json.Nodes;
using Godot;

namespace Ms2Godot.Ui;

/// <summary>
/// MS2_PERF_GATE_ATTRIB=1 (a diagnostic, not part of the gate's counts): who creates the wrappers a live window leaves behind.
/// A marker node follows every C# node that processes (once for _Process, once for _PhysicsProcess); each marker adds the
/// DisposablesTracker's growth since the previous marker to the node type before it, and the first one takes what happened
/// outside the callbacks (signals, input, the engine's own calls). The window's session JSON gets the per-type totals under
/// "attrib". Wrappers a node creates in a callback it is not the owner of (a call into another node) count for the caller.
/// The same markers time the main thread and count its allocated bytes ("attrib_us", "attrib_bytes": per type, the window's
/// totals): a marker takes what ran since the marker before it in the same frame (the node's callback, and any engine
/// node processed between the two); the first marker of a frame puts what ran since the last frame's under "between frames"
/// (rendering, physics, input, the engine's own work).
/// </summary>
public partial class WrapperAttrib : Node
{
    static readonly Dictionary<string, long> Totals = new();
    static readonly Dictionary<string, long> Ticks = new(), Bytes = new();
    static readonly Dictionary<string, int> Markers = new();
    static long last, lastTicks, lastBytes;
    static ulong lastFrame;
    readonly string label;
    readonly bool physics;

    public WrapperAttrib() { }
    public WrapperAttrib(string label, bool physics) { this.label = label; this.physics = physics; }

    public override void _Ready() { SetProcess(!physics); SetPhysicsProcess(physics); ProcessPriority = 0; }
    public override void _Process(double delta) => Mark();
    public override void _PhysicsProcess(double delta) => Mark();

    void Mark()
    {
        long now = HudScene.GateWrapTotal();
        if (label != null) Totals[label] = Totals.GetValueOrDefault(label) + now - last;
        last = now;
        long ticks = System.Diagnostics.Stopwatch.GetTimestamp(), bytes = GC.GetAllocatedBytesForCurrentThread();
        ulong frame = Engine.GetProcessFrames();
        string at = frame != lastFrame ? "between frames" : label ?? "outside";
        Ticks[at] = Ticks.GetValueOrDefault(at) + ticks - lastTicks;
        Bytes[at] = Bytes.GetValueOrDefault(at) + bytes - lastBytes;
        lastTicks = System.Diagnostics.Stopwatch.GetTimestamp(); lastBytes = GC.GetAllocatedBytesForCurrentThread(); lastFrame = frame;
    }

    /// <summary>Marks after every processing C# node under <paramref name="root"/>.</summary>
    static bool installed;
    public static void Install(Node root)
    {
        if (installed) { Reset(); return; }   // the markers stay for the later windows
        installed = true;
        var nodes = new List<Node>();
        void Walk(Node n)
        {
            nodes.Add(n);
            foreach (var c in n.GetChildren()) Walk(c);
        }
        Walk(root);
        int count = 0;
        foreach (var n in nodes)
        {
            if (n is WrapperAttrib || n.GetParent() is not { } parent || n.GetScript().Obj == null) continue;
            string type = n.GetType().Name;
            if (n.IsProcessing()) { Place(parent, n, new WrapperAttrib(type + " process", false)); count++; Markers[type + " process"] = Markers.GetValueOrDefault(type + " process") + 1; }
            if (n.IsPhysicsProcessing()) { Place(parent, n, new WrapperAttrib(type + " physics", true)); count++; Markers[type + " physics"] = Markers.GetValueOrDefault(type + " physics") + 1; }
        }
        Reset();
        GD.Print($"perf gate attrib: {count} markers");
    }

    static void Reset()
    {
        Totals.Clear(); Ticks.Clear(); Bytes.Clear();
        last = HudScene.GateWrapTotal();
        lastTicks = System.Diagnostics.Stopwatch.GetTimestamp(); lastBytes = GC.GetAllocatedBytesForCurrentThread(); lastFrame = 0;
    }

    static void Place(Node parent, Node after, Node marker)
    {
        parent.AddChild(marker);
        parent.MoveChild(marker, after.GetIndex() + 1);
    }

    public static JsonObject Result()
    {
        var o = new JsonObject();
        foreach (var (k, v) in Totals.OrderByDescending(x => x.Value).Take(25)) o[k] = v;
        return o;
    }

    /// <summary>Microseconds per type over the window (all types).</summary>
    public static JsonObject ResultUs()
    {
        var o = new JsonObject();
        foreach (var (k, v) in Ticks.OrderByDescending(x => x.Value)) o[k] = Math.Round(v * 1e6 / System.Diagnostics.Stopwatch.Frequency, 1);
        return o;
    }

    /// <summary>The markers per type (the nodes of each type that processed when they were placed).</summary>
    public static JsonObject ResultMarkers()
    {
        var o = new JsonObject();
        foreach (var (k, v) in Markers) o[k] = v;
        return o;
    }

    /// <summary>Main-thread bytes allocated per type over the window (all types).</summary>
    public static JsonObject ResultBytes()
    {
        var o = new JsonObject();
        foreach (var (k, v) in Bytes.OrderByDescending(x => x.Value)) o[k] = v;
        return o;
    }
}
