using System.Collections.Generic;
using Godot;
using Ms2Godot.Ui;

namespace Ms2Godot.Tests;

/// <summary>
/// Windowed check (res://tests/mask_box_clip.tscn, needs a display, no game data): a one-rectangle mask keeps clipping its
/// content after the mask is hidden, shown and redrawn. A relayout re-shows the options dialog; its scroll pane's mask kept its
/// polygons, so it was not set again, its redraw left it unclipped, and the pane's rows drew over the HUD below the dialog. Quits with 0 when the content stays inside the box every time, else 1.
/// </summary>
public partial class MaskBoxClipCheck : Node
{
    public override async void _Ready()
    {
        var fails = new List<string>();
        var mask = new MaskShape();
        AddChild(mask);
        mask.Set(new List<Vector2[]> { new[] { new Vector2(20, 20), new Vector2(60, 20), new Vector2(60, 60), new Vector2(20, 60) } });
        mask.AddChild(new Polygon2D { Color = Colors.Red, Polygon = new[] { new Vector2(0, 0), new Vector2(120, 0), new Vector2(120, 120), new Vector2(0, 120) } });

        async System.Threading.Tasks.Task Check(string when)
        {
            for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var img = GetViewport().GetTexture().GetImage();
            var t = GetViewport().GetFinalTransform() * GetViewport().CanvasTransform;   // canvas point -> image pixel (the project stretches)
            bool Red(int x, int y) { var p = t * new Vector2(x, y); var c = img.GetPixel((int)p.X, (int)p.Y); return c.R > 0.9f && c.G < 0.1f && c.B < 0.1f; }
            if (!Red(40, 40)) fails.Add($"{when}: no content inside the box");
            if (Red(90, 90)) fails.Add($"{when}: content drawn outside the box");
        }
        await Check("first draw");
        mask.Visible = false;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        mask.Visible = true;
        await Check("after a hide and show");
        mask.QueueRedraw();
        await Check("after a redraw");
        GD.Print($"mask box clip: {(fails.Count == 0 ? "ok" : "FAIL " + string.Join("; ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
