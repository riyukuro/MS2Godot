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
/// leaves. A mask out of the display list that moves is followed. Another movie's marks do not walk this view's
/// script-masked objects again; its own do. After every sync the full check sync (MS2_PERF_SYNCCHECK) must find nothing
/// left to change. Quits with 0 when all hold, else 1.
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
