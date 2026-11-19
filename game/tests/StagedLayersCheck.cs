using System.Diagnostics;
using Godot;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/staged_layers.tscn): <see cref="StagedLayers"/> hides the visible canvas layers below a
/// staged node, also the ones added after the first hide, leaves a layer outside the node and a layer that was already
/// hidden alone, shows again only what it hid, and a frame boundary's hide does not walk the node's subtree (20000
/// nodes below the field: under 0.2 ms per hide). Quits with 0 when it holds, else 1.
/// </summary>
public partial class StagedLayersCheck : Node
{
    public override void _Ready()
    {
        var field = new Node3D { Name = "Field" };
        AddChild(field);
        var outside = new CanvasLayer { Name = "Outside" };
        AddChild(outside);
        Node3D parent = field;
        for (int i = 0; i < 20000; i++)
        {
            var n = new Node3D();
            (i % 100 == 0 ? field : parent).AddChild(n);
            parent = n;
        }
        var top = new CanvasLayer { Name = "Top" };
        field.AddChild(top);
        var deep = new CanvasLayer { Name = "Deep" };
        parent.AddChild(deep);
        var off = new CanvasLayer { Name = "Off", Visible = false };
        field.AddChild(off);

        var layers = new StagedLayers(field);
        layers.Hide();
        bool ok = !top.Visible && !deep.Visible && !off.Visible && outside.Visible;

        var late = new Node3D();                  // a subtree with a layer enters after the first hide
        var lateLayer = new CanvasLayer { Name = "Late" };
        late.AddChild(lateLayer);
        parent.AddChild(late);
        top.Visible = true;                       // a layer shown again by its owner while staged
        layers.Hide();
        ok &= !lateLayer.Visible && !top.Visible && outside.Visible;

        const int reps = 200;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < reps; i++) layers.Hide();
        double perHide = sw.Elapsed.TotalMilliseconds / reps;
        sw.Restart();
        for (int i = 0; i < 20; i++) field.FindChildren("*", nameof(CanvasLayer), true, false);
        double perSearch = sw.Elapsed.TotalMilliseconds / 20;
        ok &= perHide < 0.2;

        layers.Restore();
        ok &= top.Visible && deep.Visible && lateLayer.Visible && !off.Visible && outside.Visible;
        var after = new CanvasLayer { Name = "After" };      // no longer listening
        field.AddChild(after);
        ok &= layers.Tracked == 0 && after.Visible;

        GD.Print($"staged layers: hide {perHide:0.000} ms per frame boundary (a subtree search {perSearch:0.000} ms) -> {(ok ? "ok" : "FAIL")}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
