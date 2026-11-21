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
/// nothing left to change. History against fresh: a view taken through hide, change, show must leave the same drawn nodes
/// as a view built in the final state (<see cref="HistoryVsFresh"/>; the check sync defers hidden parts the same way as
/// the marked one, so it cannot see a change missed inside one). An object moved into a hidden sprite keeps its holder.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class As3ViewSyncCheck : Node
{
    Runtime rt;
    FlashPlayer player;
    As3View view;

    public override async void _Ready()
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

        await HistoryVsFresh(assets, Is);

        Is(As3View.CheckRuns > 0, "the check sync did not run");
        Is(As3View.CheckDiffs == 0, $"the check sync found {As3View.CheckDiffs} differences (see MS2_PERF_SYNCCHECK lines)");
        GD.Print($"as3view sync: {(fails.Count == 0 ? "ok" : "FAIL " + string.Join("; ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    /// <summary>
    /// Two movies with the same display list: one view syncs every step of a history (a hidden part changed in every way the
    /// sync defers: moved, added and removed children, a Graphics redrawn with as many ops, a text field's text, colour, a
    /// nested hidden sprite, an ancestor's blend mode; an object moved in from a shown sprite; then shown), the other is
    /// built once in the final state. After a few frames (redraws and text builds run deferred) their node trees must be the
    /// same where drawn. The history view runs without the check sync, which would fix what its marked sync missed.
    /// </summary>
    async System.Threading.Tasks.Task HistoryVsFresh(UiAssets assets, Action<bool, string> Is)
    {
        var (hrt, hp) = Movie();
        var hv = new As3View(hp, assets) { AutoPlay = false, Checks = false };
        AddChild(hv);
        var hs = hp.Stage;
        var a = Box("pA", hs, 0, 300, hrt);
        var x = Box("pX", a, 5, 5, hrt);
        var y = Box("pY", a, 5, 25, hrt);
        var top = Box("pTop", hs, 100, 300, hrt);
        var h = Box("pH", top, 0, 0, hrt);
        var c = Box("pC", h, 0, 0, hrt);
        var gone = Box("pGone", h, 0, 20, hrt);
        var g = Box("pG", h, 20, 0, hrt);
        var t = Text("pT", h, "before", hrt);
        var n = Box("pN", h, 40, 0, hrt);
        var nk = Box("pNk", n, 0, 10, hrt);
        Box("pU", h, 60, 0, hrt);   // untouched: only the walk on show gives it the ancestor's new blend
        var n1 = Box("pN1", h, 80, 0, hrt);
        var n2 = Box("pN2", n1, 0, 10, hrt);
        var q = Box("pQ", a, 5, 45, hrt);
        // a button: its states hang under it through MarkOwner, not Parent
        var b = (AsSimpleButton)hrt.GetClass("flash.display.SimpleButton").Construct();
        b.name = "pB"; b.x = 100;
        var bu = Box("pBu", null, 0, 0, hrt);
        b.upState = bu;
        b.overState = Box("pBo", null, 0, 5, hrt);
        h.addChild(b);
        // a masked object and its mask, both in the hidden part
        var mk = Box("pMk", h, 120, 0, hrt);
        var mm = Box("pMm", h, 120, 0, hrt);
        mk.mask = mm;
        // a scale-9 sprite (the grid reaches only symbol shapes, but its holder walk runs through the grid case)
        var nine = Box("pS", h, 140, 0, hrt);
        nine.Symbol = Nine();
        Box("pSk", nine, 1, 1, hrt);
        await Frames(hv);
        var xHolder = Holder(hv, "pX");
        var yHolder = Holder(hv, "pY");

        h.visible = false;
        await Frames(hv);
        c.x = 50;
        Box("pAdded", h, 0, 40, hrt);
        h.removeChild(gone);
        g.graphics.clear(); g.graphics.beginFill(0x00ff00); g.graphics.drawRect(1, 2, 30, 40);
        t.text = "after";
        n.alpha = 0.5;
        nk.visible = false;
        top.blendMode = "add";
        bu.x = 3;
        b.State = "over";
        mm.x = 125;
        mk.y = 4;
        nine.scaleX = 2;
        h.addChild(x);
        h.addChild(y);
        await Frames(hv);
        Is(IsInstanceValid(xHolder) && !xHolder.IsQueuedForDeletion(), "the holder of an object moved into a hidden sprite was freed");
        h.removeChild(y);
        await Frames(hv);
        Is(!IsInstanceValid(yHolder) || yHolder.IsQueuedForDeletion(), "a parked holder whose object left the stage was kept");
        // moved two levels into the hidden part, then removed there: the removal's marks stop at the hidden part, and
        // the parked holder still goes on the next sync
        var qHolder = Holder(hv, "pQ");
        n2.addChild(q);
        await Frames(hv);
        Is(IsInstanceValid(qHolder) && !qHolder.IsQueuedForDeletion(), "the holder of an object moved deep into a hidden sprite was freed");
        n2.removeChild(q);
        await Frames(hv);
        Is(!IsInstanceValid(qHolder) || qHolder.IsQueuedForDeletion(), "a parked holder whose object left a nested hidden container was kept");
        h.visible = true;
        await Frames(hv);
        Is(Holder(hv, "pX") == xHolder, "an object moved into a hidden sprite was built again when shown");
        // a hidden sprite inside a hidden part, changed, then shown after its parent
        n.visible = false;
        await Frames(hv);
        h.visible = false;
        nk.visible = true;
        nk.x = 7;
        await Frames(hv);
        h.visible = true;
        await Frames(hv);
        n.visible = true;
        await Frames(hv);

        var (frt, fp) = Movie();
        var fv = new As3View(fp, assets) { AutoPlay = false };
        AddChild(fv);
        var fs = fp.Stage;
        Box("pA", fs, 0, 300, frt);
        var ftop = Box("pTop", fs, 100, 300, frt);
        ftop.blendMode = "add";
        var fh = Box("pH", ftop, 0, 0, frt);
        Box("pC", fh, 50, 0, frt);
        var fg = Box("pG", fh, 20, 0, frt);
        fg.graphics.clear(); fg.graphics.beginFill(0x00ff00); fg.graphics.drawRect(1, 2, 30, 40);
        Text("pT", fh, "after", frt);
        var fn = Box("pN", fh, 40, 0, frt);
        fn.alpha = 0.5;
        Box("pNk", fn, 7, 10, frt);
        Box("pU", fh, 60, 0, frt);
        var fn1 = Box("pN1", fh, 80, 0, frt);
        Box("pN2", fn1, 0, 10, frt);
        var fb = (AsSimpleButton)frt.GetClass("flash.display.SimpleButton").Construct();
        fb.name = "pB"; fb.x = 100;
        fb.upState = Box("pBu", null, 3, 0, frt);
        fb.overState = Box("pBo", null, 0, 5, frt);
        fb.State = "over";
        fh.addChild(fb);
        var fmk = Box("pMk", fh, 120, 4, frt);
        fmk.mask = Box("pMm", fh, 125, 0, frt);
        var fnine = Box("pS", fh, 140, 0, frt);
        fnine.Symbol = Nine();
        fnine.scaleX = 2;
        Box("pSk", fnine, 1, 1, frt);
        Box("pAdded", fh, 0, 40, frt);   // added and moved in while hidden: after the rest
        Box("pX", fh, 5, 5, frt);
        await Frames(fv);

        var diffs = new List<string>();
        Compare(hv, fv, "", diffs);
        foreach (var d in diffs.Take(20)) GD.PrintErr($"as3view sync history vs fresh: {d}");
        Is(diffs.Count == 0, $"history vs fresh: {diffs.Count} differences");
        hv.QueueFree(); fv.QueueFree();
    }

    /// <summary>One explicit sync, then three frames (each frame's _Process syncs too; redraws and text builds run deferred).</summary>
    async System.Threading.Tasks.Task Frames(As3View v)
    {
        v.Sync();
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>
    /// The drawn state of two node trees: type, holder name, visibility, transform, colour, z, child order, and what each
    /// Graphics and text node last drew. A hidden node's children are not compared (nothing under it is drawn).
    /// </summary>
    static void Compare(Node a, Node b, string path, List<string> diffs)
    {
        string at = path + "/" + Label(a);
        if (a.GetType() != b.GetType()) { diffs.Add($"{at}: {a.GetType().Name} vs {b.GetType().Name}"); return; }
        if (Label(a) != Label(b)) diffs.Add($"{at}: name vs {Label(b)}");
        if (a is CanvasItem ca && b is CanvasItem cb)
        {
            if (ca.Visible != cb.Visible) diffs.Add($"{at}: visible {ca.Visible} vs {cb.Visible}");
            if (ca.Modulate != cb.Modulate) diffs.Add($"{at}: modulate {ca.Modulate} vs {cb.Modulate}");
            if (ca.ZIndex != cb.ZIndex) diffs.Add($"{at}: z {ca.ZIndex} vs {cb.ZIndex}");
            if (ca.Material != cb.Material) diffs.Add($"{at}: material {ca.Material} vs {cb.Material}");
            if (!ca.Visible) return;
        }
        if (a is Node2D na && b is Node2D nb && na.Transform != nb.Transform) diffs.Add($"{at}: transform {na.Transform} vs {nb.Transform}");
        if (a is GraphicsNode ga && b is GraphicsNode gb && ga.DrawnKey != gb.DrawnKey) diffs.Add($"{at}: drew [{ga.DrawnKey}] vs [{gb.DrawnKey}]");
        if (a is FieldNode fa && b is FieldNode fb && fa.DrawnKey != fb.DrawnKey) diffs.Add($"{at}: drew [{fa.DrawnKey}] vs [{fb.DrawnKey}]");
        int na2 = a.GetChildCount(), nb2 = b.GetChildCount();
        if (na2 != nb2) diffs.Add($"{at}: {na2} children vs {nb2}");
        for (int i = 0; i < Math.Min(na2, nb2); i++) Compare(a.GetChild(i), b.GetChild(i), at, diffs);
    }

    /// <summary>A node's name, or its type when Godot named it (a content node: the generated name differs per view).</summary>
    static string Label(Node n) { string s = n.Name; return s.StartsWith('@') ? n.GetType().Name : s; }

    AsTextField Text(string name, AsContainer parent, string text, Runtime r)
    {
        var tf = (AsTextField)r.GetClass("flash.text.TextField").Construct();
        tf.name = name;
        tf.text = text;
        parent.addChild(tf);
        return tf;
    }

    static JsonObject Nine() => new()
    {
        ["scale9"] = new JsonObject { ["x"] = 2.0, ["y"] = 2.0, ["w"] = 6.0, ["h"] = 6.0 },
        ["bounds"] = new JsonObject { ["x"] = 0.0, ["y"] = 0.0, ["w"] = 10.0, ["h"] = 10.0 },
    };

    static Node2D Holder(As3View v, string name) => v.FindChild(name, true, false) is Node2D n && !n.IsQueuedForDeletion() ? n : null;

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
