using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using Ms2Godot.Ui;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/guide_layer.tscn): the guide event movie (UIGuideEvent) draws over every dialog. The client
/// loads it into CommonDepth layer 0x23 = GuideEvent (uiguideeventarrow.cpp:102), over the dialog layers (25, 26, 29) and the
/// modal message box (34), under the system text (36), the tooltip (37) and the mouse cursor (39). Opens the HUD
/// (res://ui/hud.tscn) and checks that
/// <list type="bullet">
/// <item>the guide movie's canvas layer is above every other HUD canvas layer that holds a dialog (the HUD's, the dialogs'
/// popup and system menu layers, the message box, the massive event);</item>
/// <item>the tooltip and the cursor are above the guide;</item>
/// <item>hiding CommonDepth.GuideEvent hides the guide movie and showing it draws it again.</item>
/// </list>
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class GuideLayerCheck : Node
{
    public override void _Ready()
    {
        FieldContents.ShowInputGuide = false;
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, new GuideLayerDriver());
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://ui/hud.tscn");
    }
}

/// <summary>The check's driver, at the tree root so it outlives the scene change to the HUD.</summary>
public partial class GuideLayerDriver : Node
{
    int wait;
    readonly ulong startMs = Time.GetTicksMsec();

    static void Collect(Node n, List<CanvasLayer> into)
    {
        if (n is CanvasLayer c) into.Add(c);
        for (int i = 0, count = n.GetChildCount(); i < count; i++) Collect(n.GetChild(i), into);
    }

    static CanvasLayer LayerOf(Node n)
    {
        for (var p = n; p != null; p = p.GetParent()) if (p is CanvasLayer c) return c;
        return null;
    }

    public override void _Process(double delta)
    {
        if (Time.GetTicksMsec() - startMs > 240_000) { Finish(new List<string> { "timed out" }); return; }
        if (GetTree().CurrentScene is not HudScene hud || !Field.LoadComplete) return;
        if (++wait < 40) return;
        var failures = new List<string>();
        var guide = typeof(HudScene).GetField("guideEvent", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(hud) as Node;
        var gl = guide == null ? null : LayerOf(guide);
        if (gl == null) { Finish(new List<string> { "the guide movie is in no canvas layer" }); return; }
        var layers = new List<CanvasLayer>();
        Collect(GetTree().Root, layers);
        var tooltip = TooltipLayer.Instance;
        var cursor = MouseCursorLayer.Instance;
        foreach (var l in layers)
        {
            if (l == gl || l == tooltip || l == cursor || l.Layer >= 100) continue;     // 100: the loading shot and the cursor
            if (l.Layer >= gl.Layer && l.GetChildCount() > 0)
                failures.Add($"canvas layer {l.Name} ({l.Layer}) holds movies and is not under the guide's ({gl.Layer})");
        }
        if (tooltip == null || tooltip.Layer <= gl.Layer) failures.Add($"the tooltip layer ({tooltip?.Layer}) is not over the guide's ({gl.Layer})");
        if (cursor == null || cursor.Layer <= gl.Layer) failures.Add($"the cursor layer ({cursor?.Layer}) is not over the guide's ({gl.Layer})");
        MovieLayerHost.Layers.SetVisible(Ms2.Ui.CommonDepth.GuideEvent, false);
        if (gl.Visible) failures.Add("the guide layer draws with CommonDepth.GuideEvent hidden");
        MovieLayerHost.Layers.SetVisible(Ms2.Ui.CommonDepth.GuideEvent, true);
        if (!gl.Visible) failures.Add("the guide layer stays hidden with CommonDepth.GuideEvent shown");
        Finish(failures, gl.Layer);
    }

    void Finish(List<string> failures, int layer = -1)
    {
        SetProcess(false);
        GD.Print($"guide layer: {(failures.Count == 0 ? "ok" : "FAIL: " + string.Join("; ", failures))} (guide canvas layer {layer})");
        GetTree().Quit(failures.Count == 0 ? 0 : 1);
    }
}
