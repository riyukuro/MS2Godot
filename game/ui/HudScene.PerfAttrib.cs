using System.Text.Json.Nodes;
using Godot;

namespace Ms2Godot.Ui;

/// <summary>
/// MS2_PERF_GATE_ATTRIB=1 (a diagnostic, not part of the gate's counts): who creates the wrappers a live window leaves behind.
/// A marker node follows every C# node that processes (once for _Process, once for _PhysicsProcess); each marker adds the
/// DisposablesTracker's growth since the previous marker to the node type before it, and the first one takes what happened
/// outside the callbacks (signals, input, the engine's own calls). The window's session JSON gets the per-type totals under
/// "attrib". Wrappers a node creates in a callback it is not the owner of (a call into another node) count for the caller.
/// </summary>
public partial class WrapperAttrib : Node
{
    static readonly Dictionary<string, long> Totals = new();
    static long last;
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
    }

    /// <summary>Marks after every processing C# node under <paramref name="root"/>.</summary>
    public static void Install(Node root)
    {
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
            if (n.IsProcessing()) { Place(parent, n, new WrapperAttrib(type + " process", false)); count++; }
            if (n.IsPhysicsProcessing()) { Place(parent, n, new WrapperAttrib(type + " physics", true)); count++; }
        }
        Totals.Clear();
        last = HudScene.GateWrapTotal();
        GD.Print($"perf gate attrib: {count} markers");
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
}
