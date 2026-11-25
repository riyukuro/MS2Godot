using Godot;
using Ms2.Ui.Hud;
using Ms2Godot.Ui;

namespace Ms2Godot.Tests;

/// <summary>
/// Check of the software cursor under a message box (res://tests/mouse_cursor_modal.tscn, GitHub issue #1). The cursor movie
/// must see every mouse motion (GFxManager::HandleWindowMessage, gfxmanager.cpp:942-956: the last loaded movie, the cursor
/// movie, answers WM_MOUSEMOVE first, GFxMovieMouseCursor::vfunc_14 @0x1422f2bd0) while a message box is shown, which takes
/// the mouse over its own shapes. For every box type (0..0x24), modal and modaless: a box is shown, the mouse moves to points
/// over the box, and the cursor movie's stage mouse must follow (checked against the map from window to stage the same
/// movie made with no box up). Quits with 0 when every point holds, else 1.
/// </summary>
public partial class MouseCursorModalCheck : Node
{
    int step, wait, failures, checks, kinds;
    int type = -1;
    bool modaless;
    (double X, double Y) a, b;
    static readonly Vector2 PA = new(200, 150), PB = new(900, 500);
    static readonly Vector2[] Points = { new(640, 360), new(660, 380), new(620, 340) };
    ulong startMs;

    public override void _Ready()
    {
        GetTree().Root.Size = new Vector2I(1280, 720);
        startMs = Time.GetTicksMsec();
        MessageBoxLayer.Ensure(this);
        TooltipLayer.Ensure(this);
        MouseCursorLayer.Ensure(this);
    }

    static void Move(Vector2 p) =>
        Input.ParseInputEvent(new InputEventMouseMotion { Position = p, GlobalPosition = p, Relative = new Vector2(1, 1) });

    static (double X, double Y) Mouse()
    {
        var pl = MouseCursorLayer.Instance?.Movie?.Player;
        return pl == null ? (double.NaN, double.NaN) : (pl.MouseX, pl.MouseY);
    }

    void Fail(string why) { failures++; GD.Print("mouse cursor modal: FAIL " + why); }

    public override void _Process(double delta)
    {
        if (Time.GetTicksMsec() - startMs > 240_000) { Fail("timed out at step " + step); Finish(); return; }
        if (++wait < 4) return;
        wait = 0;
        switch (step)
        {
            case 0:
                if (MouseCursorLayer.Cursor == null || MessageBoxLayer.Instance == null || MessageBoxLayer.LayerMovie == null) { wait = 0; return; }
                if (GetTree().Root.GetChildren().OfType<Node>().Last() != MouseCursorLayer.Instance) { Fail("the cursor layer is not the root's last child"); }
                Move(PA); step = 1; return;
            case 1:
                a = Mouse(); Move(PB); step = 2; return;
            case 2:
                b = Mouse();
                if (!(b.X > a.X && b.Y > a.Y)) { Fail($"the cursor movie did not follow the mouse with no box up ({a} {b})"); Finish(); return; }
                step = 3; return;
            case 3:
                NextKind();
                return;
            case 4:   // the box is up: move over it
                if (MessageBoxLayer.Instance is not { Shown: true }) { Fail($"type {type}: the box is not shown"); step = 3; return; }
                if (pointIndex == 0) kinds++;
                if (pointIndex >= Points.Length) { MessageBoxQueue.Clear(0); step = 3; return; }
                Move(Points[pointIndex]); step = 5; return;
            case 5:
            {
                var p = Points[pointIndex++];
                var want = (a.X + (p.X - PA.X) * (b.X - a.X) / (PB.X - PA.X), a.Y + (p.Y - PA.Y) * (b.Y - a.Y) / (PB.Y - PA.Y));
                var got = Mouse();
                checks++;
                if (System.Math.Abs(got.X - want.Item1) > 1.5 || System.Math.Abs(got.Y - want.Item2) > 1.5)
                    Fail($"type {type} modaless {modaless}: the cursor movie's mouse is {got}, the window point {p} maps to {want}");
                step = 4; return;
            }
        }
    }

    int pointIndex;

    void NextKind()
    {
        if (modaless) { modaless = false; type++; } else if (type >= 0) modaless = true; else { type = 0; }
        if (type > 0x24) { Finish(); return; }
        pointIndex = 0;
        var d = MessageBoxQueue.Create(type, "check", null, "mouse_cursor_check");
        d.Modaless = modaless;
        MessageBoxQueue.Show(d);
        step = 4;
    }

    void Finish()
    {
        GD.Print($"mouse cursor modal: {kinds} box kinds, {checks} points checked, {failures} failed");
        GetTree().Quit(failures == 0 && checks > 0 ? 0 : 1);
    }
}
