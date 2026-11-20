using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using Ms2.Formats.Options;
using Ms2Godot.Ui;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Check (res://tests/hud_resize.tscn, on a display: the window is really resized): a resize or a screen mode change keeps
/// the HUD's movies and dialogs, as the client's device reset does (CUIService::OnDeviceReset, uiservice.cpp:2829, rebuilds
/// nothing; GFxService::vfunc_17 resizes the movies, UIDialog::vfunc_17 keeps an open dialog's position ratio, then every
/// entity is laid out). It opens the HUD on 02000001_tw_tria at 1280 x 720, opens the inventory on its fourth tab and the
/// quest log, then goes to 1920 x 1080, back to 1280 x 720, to full screen and back to the window, checking after each that
/// <list type="bullet">
/// <item>every movie of the HUD is the same instance and still in the tree, and the inventory is open on the same tab;</item>
/// <item>every movie's stage is the new window's (UiScale.MovieStage);</item>
/// <item>the open inventory ends where its layout puts it on the new stage (its ratio step, UIDialog::vfunc_17, is
/// covered by the Ms2.Ui test ADeviceRestoreKeepsAPlacedDialogsRatioToTheStage);</item>
/// <item>back at 1280 x 720 every movie's root sits where it was before the round trip;</item>
/// <item>the npcs' and other players' name tag and HP bar layers (NpcPresence, LiveNameTags, LiveHpBars) take the new
/// scale (UiScale.For), and the npcs' built name tags are drawn at it;</item>
/// <item>the message box and tooltip layers' movies, under the root, take the same restore (one stage RESIZE per changed
/// viewport, which the vfunc_18 relayout they had sent none of);</item>
/// <item>a movie's own stage Event.RESIZE listener (uicastingbar's CastingBar.onResizeStage is one) runs once per changed
/// viewport and after the dialogs' restore and layout (queued by NotifyOnResize, sfr_gfx_01i.cpp:1240-1256, until the
/// movie's next advance): a listener on the casting bar's stage sees the open inventory already where the step leaves it.</item>
/// </list>
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class HudResizeCheck : Node
{
    public override void _Ready()
    {
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, new HudResizeDriver());
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://ui/hud.tscn");
    }
}

/// <summary>The check's driver, at the tree root so it outlives the scene change to the HUD.</summary>
public partial class HudResizeDriver : Node
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly (bool Full, int W, int H)[] Steps = { (false, 1920, 1080), (false, 1280, 720), (true, 1280, 720), (false, 1280, 720) };
    HudScene hud;
    readonly List<MovieDialog> movies = new();
    readonly Dictionary<MovieDialog, (double X, double Y)> start = new();
    MovieDialog inventoryMovie;
    Ms2.Ui.Hud.InventoryController inventory;
    int step = -2, wait, failures;
    int resizes, resizesBefore, layerResizes, layerResizesBefore, layerMovies; (double X, double Y) invAtResize; Vector2 windowBefore;
    readonly ulong startMs = Time.GetTicksMsec();

    static object Get(object o, string name) => o?.GetType().GetField(name, Private)?.GetValue(o);
    static void Call(object o, string name) => o.GetType().GetMethod(name, Private, Type.EmptyTypes)?.Invoke(o, null);
    void Fail(string what) { failures++; GD.Print("hud resize: FAIL " + what); }

    void Collect(Node n, List<MovieDialog> into)
    {
        var field = Get(hud, "fieldScene");
        for (int i = 0; i < n.GetChildCount(); i++)
        {
            var c = n.GetChild(i);
            if (c == field) continue;
            if (c is MovieDialog m) into.Add(m);
            Collect(c, into);
        }
    }

    static (double X, double Y) RootAt(MovieDialog m) =>
        (Ms2.Avm2.Rt.ToNumber(m.Host.Get(m.Host.Entity, "x")), Ms2.Avm2.Rt.ToNumber(m.Host.Get(m.Host.Entity, "y")));

    int TabOf() => inventory?.View?.Tab ?? -1;

    public override void _Process(double delta)
    {
        if (Time.GetTicksMsec() - startMs > 480_000) { Fail($"timed out at step {step}"); Finish(); return; }
        if (step == -2)
        {
            if (GetTree().CurrentScene is not HudScene h || !Field.LoadComplete) return;
            if (++wait < 60) return;
            hud = h; wait = 0;
            Call(hud, "ToggleInventory");
            Call(hud, "ToggleQuestLog");
            step = -1;
            return;
        }
        if (hud == null || !IsInstanceValid(hud)) { Fail("the HUD was freed"); Finish(); return; }
        if (++wait < 40) return;
        wait = 0;
        if (step == -1)
        {
            inventoryMovie = (MovieDialog)Get(hud, "inventoryMovie");
            inventory = (Ms2.Ui.Hud.InventoryController)Get(hud, "inventory");
            if (inventoryMovie == null || inventory == null || inventory.IsClosed) { Fail("the inventory did not open"); Finish(); return; }
            inventory.View?.SelectTab(3);
            if (Get(hud, "questLog") == null) GD.Print("hud resize: no quest log in this data; the inventory is the open dialog checked");
            Collect(hud, movies);
            var bar = (MovieDialog)Get(hud, "castingBar");
            if (bar == null) { Fail("no casting bar movie"); Finish(); return; }
            Func<object[], object> onResize = _ => { resizes++; invAtResize = RootAt(inventoryMovie); return null; };
            bar.Host.Invoke(bar.Player.Stage, "addEventListener", "resize", onResize, false, 0, false);
            Func<object[], object> onLayerResize = _ => { layerResizes++; return null; };
            foreach (var m in new[] { MessageBoxLayer.LayerMovie, TooltipLayer.LayerMovie })
                if (m != null) { layerMovies++; m.Host.Invoke(m.Player.Stage, "addEventListener", "resize", onLayerResize, false, 0, false); }
            if (layerMovies != 2) Fail($"{layerMovies} of the message box and tooltip movies are made");
            foreach (var m in movies) start[m] = RootAt(m);
            GD.Print($"hud resize: {movies.Count} movies at {ScreenMode.PixelSize(GetWindow())}");
            Next();
            return;
        }
        Check();
        if (step == Steps.Length - 1) { Finish(); return; }
        Next();
    }

    void Next()
    {
        step++;
        resizesBefore = resizes; layerResizesBefore = layerResizes;
        windowBefore = ScreenMode.PixelSize(GetWindow());
        var (full, w, h) = Steps[step];
        GD.Print($"hud resize: step {step + 1}: {(full ? "full screen" : "window")} {w}x{h}");
        ScreenMode.ApplyRequest(new WindowRequest(full, false, w, h));
    }

    void Check()
    {
        int n = step + 1;
        var window = (Vector2)ScreenMode.PixelSize(GetWindow());
        var (full, w, h) = Steps[step];
        if (window != new Vector2(w, h)) Fail($"step {n}: the window is {window}, not {w}x{h}");
        var now = new List<MovieDialog>();
        Collect(hud, now);
        foreach (var m in movies)
            if (!IsInstanceValid(m) || !m.IsInsideTree() || !now.Contains(m)) { Fail($"step {n}: a movie was dropped ({(IsInstanceValid(m) ? m.Host.Controller.Movie : "freed")})"); return; }
        if (inventory.IsClosed) Fail($"step {n}: the inventory closed");
        if (TabOf() != 3) Fail($"step {n}: the inventory's tab is {TabOf()}, not 3");
        var stage = UiScale.MovieStage(window);
        foreach (var m in movies)
            if (m.Player.StageW != stage.X || m.Player.StageH != stage.Y)
                Fail($"step {n}: {m.Host.Controller.Movie} stage {m.Player.StageW}x{m.Player.StageH}, not {stage}");
        // UIDialog::vfunc_17 keeps the ratio, then the layout (UIDialog::vfunc_43): a dialog not placed from its settings
        // file is aligned again by its movie, so it ends where its layout puts it on the new stage; laying it out again
        // leaves it there
        var inv = RootAt(inventoryMovie);
        inventoryMovie.LayoutLoaded();
        if (RootAt(inventoryMovie) != inv) Fail($"step {n}: the inventory is at {inv}, not where its layout puts it ({RootAt(inventoryMovie)})");
        WorldLayers(n, window);
        if (layerResizes - layerResizesBefore != (window != windowBefore ? layerMovies : 0))
            Fail($"step {n}: the message box and tooltip stages got {layerResizes - layerResizesBefore} RESIZE events, not {(window != windowBefore ? layerMovies : 0)}");
        int expected = window != windowBefore ? 1 : 0;
        if (resizes - resizesBefore != expected) Fail($"step {n}: the casting bar's stage got {resizes - resizesBefore} RESIZE events, not {expected}");
        else if (expected == 1 && invAtResize != inv) Fail($"step {n}: the stage RESIZE ran before the layout (the inventory was at {invAtResize}, ends at {inv})");
        if (w == 1280 && !full)
            foreach (var m in movies)
            {
                var at = RootAt(m);
                if (Math.Abs(at.X - start[m].X) > 0.06 || Math.Abs(at.Y - start[m].Y) > 0.06)
                    Fail($"step {n}: {m.Host.Controller.Movie} at {at}, was {start[m]}");
            }
        GD.Print($"hud resize: step {n} checked, inventory at ({inv.X:0.##}, {inv.Y:0.##})");
    }

    /// <summary>The world UI layers built outside the HUD's movies, read by name so the check builds on any tree.</summary>
    void WorldLayers(int n, Vector2 window)
    {
        float u = UiScale.For(window);
        int layers = 0, npcTags = 0;
        void Walk(Node node)
        {
            for (int i = 0; i < node.GetChildCount(); i++)
            {
                var c = node.GetChild(i);
                var type = c.GetType().Name;
                if (type is "NpcPresence" or "LiveNameTags" or "LiveHpBars")
                {
                    layers++;
                    if (Get(c, "u") is float lu && Math.Abs(lu - u) > 1e-5f) Fail($"step {n}: {type} draws at {lu}, not {u}");
                    if (type == "NpcPresence" && Get(c, "tags") is System.Collections.IEnumerable tags)
                        foreach (var t in tags)
                        {
                            npcTags++;
                            if (t.GetType().GetField("Node")?.GetValue(t) is Node2D tag && Math.Abs(tag.Scale.X - u) > 1e-5f)
                            { Fail($"step {n}: an npc name tag is drawn at {tag.Scale.X}, not {u}"); return; }
                        }
                }
                Walk(c);
            }
        }
        Walk(GetTree().Root);
        if (layers == 0 || npcTags == 0) Fail($"step {n}: no npc name tags to check ({layers} layers)");
    }

    void Finish()
    {
        SetProcess(false);
        GD.Print($"hud resize: {(failures == 0 ? "ok" : "FAIL")}, {failures} failures");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }
}
