using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Godot;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2Godot.Ui;
using FlashPlayer = Ms2.Avm2.Flash.Player;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/press_routing.tscn, needs the game data for the views' assets): which movie a press goes to.
/// The client's dialogs are clips of one movie, so a press is the top entity's under the press itself. Two
/// <see cref="As3View"/>s (two dialogs of ours) and a game node under them take presses through the viewport's own input routing:
/// a press at a point of the lower dialog reaches it though the upper dialog's 50 ms sample of the cursor (MouseOverUi) still says
/// "over" (the cursor left it a moment ago), and a press at a point of the upper dialog reaches it though its sample says "not over"
/// (the cursor came in a moment ago). The game is not a view: like CMouseController it reads the UI movie's sampled flag
/// (<see cref="As3View.AnyMouseOverUi"/>) at the press and the release (<see cref="Ms2.Ui.MouseReleaseGate"/>), so a press at no
/// dialog is still held from it while the sample says "over", and a press taken while the sample says "not over" walks on its
/// release though a dialog took the press. Quits with 0 when all hold, else 1.
/// </summary>
public partial class PressRoutingCheck : Node
{
    sealed partial class Game : Node
    {
        public int Presses;                                // releases that run the click to move (MouseMove._Input's)
        readonly Ms2.Ui.MouseReleaseGate gate = new();
        public override void _Input(InputEvent ev)
        {
            if (ev is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mb) return;
            bool over = Ms2Godot.Movement.MouseMove.UiTakesMouse(GetViewport(), mb.Position);
            if (mb.Pressed) gate.Press(over);
            else if (gate.Release(over)) Presses++;
        }
    }

    int frames;
    As3View lower, upper;
    AsSprite lowerBox, upperBox;
    Game game;
    int lowerDown, upperDown;
    bool controlAdded;
    ColorRect controlNode;
    readonly List<string> fails = new();

    public override void _Ready()
    {
        game = new Game();
        AddChild(game);
        var assets = new UiAssets(PackSet.Shared(Settings.DataDir));
        (lower, lowerBox) = Dialog(assets, 400, 300, () => lowerDown++);
        (upper, upperBox) = Dialog(assets, 0, 0, () => upperDown++);
        AddChild(lower);
        AddChild(upper);   // the last child takes the input first
    }

    (As3View, AsSprite) Dialog(UiAssets assets, double x, double y, Action onDown)
    {
        var rt = new Runtime();
        var layout = new JsonObject { ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 }, ["symbols"] = new JsonObject(), ["root"] = new JsonObject() };
        var p = new FlashPlayer(rt, layout);
        p.Start();
        var s = (AsSprite)rt.GetClass("flash.display.Sprite").Construct();
        s.graphics.beginFill(0xff0000);
        s.graphics.drawRect(0, 0, 100, 100);
        s.x = x; s.y = y;
        p.Stage.addChild(s);
        p.Call(s, "addEventListener", "mouseDown", new AsPlainFunction(rt, new NativeMethod("down", (_, __) => { onDown(); return null; })), false, 0, false);
        return (new As3View(p, assets) { AutoPlay = false }, s);
    }

    void PressNoMotion(float x, float y)
    {
        var at = new Vector2(x, y);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left }, true);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at }, true);
    }

    void Press(float x, float y)
    {
        var at = new Vector2(x, y);
        GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at }, true);   // the hover follows the cursor, as a real click's motion
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left }, true);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at }, true);
    }

    void Is(bool ok, string what) { if (!ok) fails.Add(what); }

    public override void _Process(double delta)
    {
        frames++;
        // frame 10: the real cursor is at the viewport's (0, 0) (headless): the upper dialog, drawn there, sampled "over"
        if (frames == 15)
        {
            Is(upper.MouseOverUi, "setup: the upper dialog did not sample the cursor over it");
            // 1. a press at the lower dialog's point, the upper's sample stale (over)
            int g0 = game.Presses; lowerDown = upperDown = 0;
            Press(450, 350);
            Is(lowerDown == 1, $"a press on the lower dialog did not reach it (lower {lowerDown}, upper {upperDown}) while the upper one's sample was over");
            Is(upperDown == 0 && game.Presses == g0, "the lower dialog's press also went to the upper dialog or the game");
            // 2. the upper dialog moves away from under the cursor; its sample stays "over" until the next one
            upperBox.x = 200;
            g0 = game.Presses; lowerDown = upperDown = 0;
            Press(250, 50);
            Is(upperDown == 1, "a press on the upper dialog (moved) did not reach it");
            // 3. a press at neither dialog while the sample says "over": the client's flag (m_10) holds it from the game
            g0 = game.Presses; lowerDown = upperDown = 0;
            Press(700, 500);
            Is(lowerDown == 0 && upperDown == 0 && game.Presses == g0, $"a press at no dialog reached the game while the sample said over (lower {lowerDown}, upper {upperDown}, game +{game.Presses - g0})");
        }
        if (frames == 40 && !controlAdded)
        {
            // 4. the sample is "not over" now (the dialog stands at x 200, the cursor at 0); the dialog moves under the cursor and a press follows at once:
            //    the dialog gets it (its hit test) and the game takes it too (the flag read at the press is clear: mode 3, the release runs)
            // 5. a press at no dialog with the sample "not over" walks
            Is(!upper.MouseOverUi, "setup: the upper dialog still samples the cursor over it");
            upperBox.x = 0;
            int g0 = game.Presses; lowerDown = upperDown = 0;
            Press(50, 50);
            Is(upperDown == 1 && game.Presses == g0 + 1, $"a press on the upper dialog whose sample was not over: upper {upperDown} (want 1), game +{game.Presses - g0} (want 1)");
            upperBox.x = 200;
            g0 = game.Presses; lowerDown = upperDown = 0;
            Press(700, 500);
            Is(lowerDown == 0 && upperDown == 0 && game.Presses == g0 + 1, $"a press at no dialog with the sample not over did not reach the game (game +{game.Presses - g0})");
            // 6. a Godot Control (the creative menu, a text input) over the ground takes the click: the game does not walk, as when it
            //    ran after the GUI; a press next to it walks
            var control = controlNode = new ColorRect { Position = new Vector2(600, 400), Size = new Vector2(100, 100), MouseFilter = Control.MouseFilterEnum.Stop };
            AddChild(control);
            controlAdded = true;
        }
        if (controlAdded && frames == 45)
        {
            int g0 = game.Presses; lowerDown = upperDown = 0;
            Press(650, 450);
            Is(game.Presses == g0, $"a press on a Godot control over the ground walked (game +{game.Presses - g0})");
            g0 = game.Presses;
            Press(750, 550);
            Is(game.Presses == g0 + 1, $"a press next to the control did not walk (game +{game.Presses - g0})");
            var away = new Vector2(750, 550);
            GetViewport().PushInput(new InputEventMouseMotion { Position = away, GlobalPosition = away }, true);
        }
        if (controlAdded && frames == 50)
        {
            int g0;
            // 7. a tap with no motion before it (touch): the viewport's hover still says "no control" at the old cursor point, the
            //    press point is on the control
            g0 = game.Presses;
            PressNoMotion(650, 450);
            Is(game.Presses == g0, $"a tap on a Godot control with no motion before it walked (game +{game.Presses - g0})");
            // 8. a hidden control takes nothing
            controlNode.Visible = false;
            g0 = game.Presses;
            PressNoMotion(650, 450);
            Is(game.Presses == g0 + 1, $"a tap on a hidden control did not walk (game +{game.Presses - g0})");
            foreach (var f in fails) GD.PrintErr("press routing: " + f);
            GD.Print(fails.Count == 0 ? "press routing: ok" : $"press routing: {fails.Count} failed");
            GetTree().Quit(fails.Count == 0 ? 0 : 1);
        }
    }
}
