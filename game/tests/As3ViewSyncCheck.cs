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
/// Headless check (res://tests/as3view_sync.tscn, needs the game data for the view's assets; run with MS2_PERF_SYNCCHECK=1):
/// drives <see cref="As3View"/>'s marked sync over a small display list of Graphics sprites and checks the nodes it
/// leaves. A script mask object is not drawn, even when the object it masks is off the stage. A hidden subtree is left
/// as it was while hidden and redone in full when shown (moved, added and removed children). A masked object under a
/// hidden sprite keeps its mask undrawn. A mask out of the display list that moves is followed. Another movie's marks
/// do not walk this view's script-masked objects again; its own do. After every sync the full check sync (MS2_PERF_SYNCCHECK) must find
/// nothing left to change. Quits with 0 when all hold, else 1.
/// </summary>
public partial class As3ViewSyncCheck : Node
{
    Runtime rt;
    FlashPlayer player;
    As3View view;

    public override void _Ready()
    {
        var fails = new List<string>();
        void Is(bool ok, string what) { if (!ok) fails.Add(what); }
        if (System.Environment.GetEnvironmentVariable("MS2_PERF_SYNCCHECK") == null) { GD.PrintErr("as3view sync: set MS2_PERF_SYNCCHECK=1"); GetTree().Quit(2); return; }
        var assets = new UiAssets(PackSet.Shared(Settings.DataDir));
        (rt, player) = Movie();
        view = new As3View(player, assets) { AutoPlay = false };
        AddChild(view);
        var stage = player.Stage;

        // a script mask whose masked object is not on the stage: the mask is not drawn (DisplayObject::SetMask takes its
        // render node out of the render tree whatever the masked object's place)
        var m1 = Box("m1", stage, 0, 0);
        var loose = Box("loose", null, 0, 0);
        loose.mask = m1;
        view.Sync();
        Is(Holder("m1") is { Visible: false }, "a mask whose masked object is off the stage is drawn");
        loose.mask = null;
        view.Sync();
        Is(Holder("m1") is { Visible: true }, "a mask set back to no owner is not drawn again");

        // a hidden subtree: left as it was while hidden, redone when shown
        var h = Box("h", stage, 100, 0);
        var c = Box("c", h, 0, 0);
        var gone = Box("gone", h, 0, 20);
        view.Sync();
        Is(Holder("c") is { Visible: true } && Holder("gone") != null, "children of a shown sprite not synced");
        h.visible = false;
        view.Sync();
        Is(Holder("h") is { Visible: false }, "hidden sprite's holder drawn");
        c.x = 50;
        var added = Box("added", h, 0, 40);
        h.removeChild(gone);
        view.Sync();
        Is(Holder("c")?.Position.X == 0, "a hidden subtree's child was synced while hidden (moved)");
        Is(Holder("added") == null, "a child added under a hidden sprite was synced while hidden");
        h.visible = true;
        view.Sync();
        Is(Holder("h") is { Visible: true }, "shown sprite's holder hidden");
        Is(Holder("c")?.Position.X == 50, "a child moved while hidden not moved when shown");
        Is(Holder("added") is { Visible: true } a && ChildCount(a) > 0, "a child added while hidden not drawn when shown");
        Is(Holder("gone") == null, "a child removed while hidden still in the view when shown");

        // a hidden sprite made hidden: an empty holder, then all of it when shown
        var late = Box("late", null, 0, 60);
        late.visible = false;
        var lateKid = Box("lateKid", late, 0, 0);
        stage.addChild(late);
        view.Sync();
        Is(Holder("late") is { Visible: false } && Holder("lateKid") == null, "a sprite added hidden synced its subtree");
        late.visible = true;
        view.Sync();
        Is(Holder("lateKid") is { Visible: true }, "a sprite added hidden not drawn when shown");

        // a masked object under a hidden sprite: its mask stays undrawn; shown, it is masked again
        var m2 = Box("m2", stage, 200, 0);
        var inner = Box("inner", h, 0, 80);
        inner.mask = m2;
        view.Sync();
        Is(Holder("m2") is { Visible: false }, "the mask of a masked object is drawn");
        Is(Holder("inner")?.GetParent() is MaskShape, "a masked object not under its mask node");
        h.visible = false;
        view.Sync();
        m2.x = 210;
        view.Sync();
        Is(Holder("m2") is { Visible: false }, "the mask of a masked object under a hidden sprite is drawn");
        h.visible = true;
        view.Sync();
        Is(Holder("inner")?.GetParent() is MaskShape, "a masked object shown again not under its mask node");

        // a hidden masked object: its mask stays undrawn, and it is masked when shown
        inner.visible = false;
        view.Sync();
        m2.y = 5;
        view.Sync();
        inner.visible = true;
        view.Sync();
        Is(Holder("m2") is { Visible: false }, "the mask of a hidden masked object is drawn");

        // a mask out of the display list, under a sprite that moves: the masked object's polygons follow (the check sync
        // finds any it missed)
        var holderOff = Box("off", null, 0, 0);
        var m3 = Box("m3", holderOff, 0, 0);
        var masked = Box("masked", stage, 300, 0);
        masked.mask = m3;
        view.Sync();
        holderOff.x = 30;
        view.Sync();

        // another movie's marks do not walk this view's script-masked objects again; this movie's marks do
        var (rt2, player2) = Movie();
        var other = Box("other", player2.Stage, 0, 0, rt2);
        view.Sync();
        long walks = As3View.ScriptMaskWalks;
        other.x = 5;
        view.Sync();
        Is(As3View.ScriptMaskWalks == walks, "another movie's mark walked this view's script masks");
        holderOff.x = 40;
        view.Sync();
        Is(As3View.ScriptMaskWalks == walks + 1, "this movie's mark did not walk its script masks");

        Is(As3View.CheckRuns > 0, "the check sync did not run");
        Is(As3View.CheckDiffs == 0, $"the check sync found {As3View.CheckDiffs} differences (see MS2_PERF_SYNCCHECK lines)");
        GD.Print($"as3view sync: {(fails.Count == 0 ? "ok" : "FAIL " + string.Join("; ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    static (Runtime, FlashPlayer) Movie()
    {
        var r = new Runtime();
        var layout = new JsonObject { ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 }, ["symbols"] = new JsonObject(), ["root"] = new JsonObject() };
        var p = new FlashPlayer(r, layout);
        p.Start();
        return (r, p);
    }

    AsSprite Box(string name, AsContainer parent, double x, double y, Runtime r = null)
    {
        var s = (AsSprite)(r ?? rt).GetClass("flash.display.Sprite").Construct();
        s.name = name;
        s.graphics.beginFill(0xff0000);
        s.graphics.drawRect(0, 0, 10, 10);
        s.x = x; s.y = y;
        parent?.addChild(s);
        return s;
    }

    /// <summary>The holder node the view made for the object named <paramref name="name"/> (holders are named after the
    /// instance), or null when there is none (or it is being freed).</summary>
    Node2D Holder(string name) => view.FindChild(name, true, false) is Node2D n && !n.IsQueuedForDeletion() ? n : null;

    static int ChildCount(Node n) => n.GetChildCount();
}
