using Godot;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Ui;
using FlashPlayer = Ms2.Avm2.Flash.Player;

namespace Ms2Godot.Ui;

/// <summary>A scene whose dialogs share one stage (<see cref="UiStage"/>): drops across dialogs and the drag callbacks.</summary>
public interface IUiStageHost { UiStage UiStage { get; } }

/// <summary>
/// A client window run from its own movie: the movie's ActionScript (the translated code when it is built, else the
/// interpreter) drawn by <see cref="As3View"/>, with the dialog's C++ side in a <see cref="UiController"/>. The movie is
/// placed as GFxMovie places every movie: the stage is the window's viewport at the UI scale
/// (<see cref="UiScale.MovieStage"/>), drawn at <see cref="UiScale.For"/> from <see cref="UiScale.MovieOrigin"/>.
/// </summary>
public partial class MovieDialog : Node2D
{
    public UiHost Host { get; }
    public FlashPlayer Player { get; }
    public bool Loaded { get; }
    readonly As3View view;
    public As3View View => view;

    /// <summary>
    /// The live movies' controllers (CUIService's dialog registry, m_mapDlg): a movie joins while the tree holds it
    /// and leaves when the tree drops it (the enter and exit below), so the always-on gate reads a list instead of
    /// walking the scene every frame. The head-up strips sit in it too and the gate's own dialog test keeps them
    /// out, as the client's registry holds only dialogs.
    /// </summary>
    public static readonly System.Collections.Generic.List<UiController> Live = new();

    /// <summary>
    /// The movie is a GFxMovie of its own, not a UISwfEntity in main.gfx (movie 2): the tooltip (CommonTooltip) and the mouse
    /// cursor movies (gfxmanager.cpp:1133, 1151). CUIService::IsTextFieldFocused asks movie 2 only (GFxManager::IsTextFieldFocused(2),
    /// gfxmanager.cpp:531), so the focus of such a movie is not one the keys wait for and it stays out of
    /// <see cref="OffStageTextFocus"/>. Set before the dialog enters the tree. STILL-OPEN: the movie ids of the loading movie, the
    /// cut-scene swf and the NPC balloon movies are not read; they count as movie 2 and none has an input field today.
    /// </summary>
    public bool OwnMovie { get; init; }

    public MovieDialog(UiAssets assets, UiController controller, UiModel model, Vector2 window, ICharacterFolder folder = null)
    {
        var perf = PerfLog.On ? new PerfLog.Steps("open " + controller.Movie) : null;
        // the movies are parsed, and their code and layouts read, once per pack set and shared by every open (the client's
        // loader keeps a movie's definition once loaded); the runtime, player and display list are still made on each open
        var src = assets.Movies;
        if (perf != null)
        {
            // the same reads and parses Open does, first, so each is timed on its own
            void Prefetch(string key, HashSet<string> seen)
            {
                if (!seen.Add(key) || src.Movie(key) is not { } m) return;
                foreach (var g in m.Imports) Prefetch(System.IO.Path.GetFileNameWithoutExtension(g.Url), seen);
            }
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Prefetch(controller.Movie, keys);
            foreach (var l in controller.Libraries ?? Array.Empty<string>()) Prefetch(l, keys);
            perf.Step("swf-parse+read");
            foreach (var k in keys) src.Abc(k);
            perf.Step("abc-parse");
        }
        var (rt, p) = src.Open(controller.Movie, r => r.Budget = 20_000_000, controller.Libraries);
        perf?.Step("layout+abc-load");
        Player = p;
        Resize(window);
        view = new As3View(p, assets);
        AddChild(view);
        Place(window);
        perf?.Step("view");
        Host = new UiHost(p, controller, model) { Folder = folder ?? CharacterFolder.ForChosen(), Mark = perf == null ? null : perf.Step };
        SetClientStage(window);
        Loaded = Host.Load();
        foreach (var e in rt.Errors) GD.PushWarning($"[{controller.Movie}] {e}");
        if (perf != null) { openPerf = perf; openedAt = System.Diagnostics.Stopwatch.GetTimestamp(); }
        if (PerfLog.On) lock (Opened) Opened.Add((controller.Movie, new WeakReference<FlashPlayer>(p)));
    }

    /// <summary>MS2_PERF: every movie player made, weakly held, to count the ones still alive after their windows closed.</summary>
    public static readonly List<(string Movie, WeakReference<FlashPlayer> Player)> Opened = new();

    PerfLog.Steps openPerf;
    long openedAt;

    // processed only while an open is being timed (MS2_PERF): no per-frame call otherwise
    public override void _Ready() => SetProcess(openPerf != null);

    public override void _Process(double delta)
    {
        if (resizePending) DeliverResize();         // the parent processes before its view, which advances the movie
        SetProcess(false);
        if (openPerf == null) return;
        // the first frame after the open: the time until this node is processed; the view (a child, processed after it)
        // then ticks and syncs its display list ("first-sync"), and the frame is drawn ("to-drawn": from the end of that
        // sync to the frame's post-draw, the whole frame's draw, not this dialog's alone)
        var perf = openPerf;
        openPerf = null;
        perf.Extra("to-first-frame", System.Diagnostics.Stopwatch.GetElapsedTime(openedAt).TotalMilliseconds);
        view.NextProcessMs = ms =>
        {
            perf.Extra("first-sync", ms);
            long synced = System.Diagnostics.Stopwatch.GetTimestamp();
            Action drawn = null;
            drawn = () =>
            {
                RenderingServer.FramePostDraw -= drawn;
                perf.Extra("to-drawn", System.Diagnostics.Stopwatch.GetElapsedTime(synced).TotalMilliseconds);
                perf.Done();
            };
            RenderingServer.FramePostDraw += drawn;
        };
    }

    void Resize(Vector2 window)
    {
        var stage = UiScale.MovieStage(window);
        Player.StageW = stage.X;
        Player.StageH = stage.Y;
    }

    Vector2 window;

    /// <summary>
    /// The stage origin goes at <see cref="UiScale.MovieOrigin"/> in the viewport, through whatever canvas layer the dialog
    /// is added to (a host layer may already be offset by it), so every hosted movie is placed once.
    /// </summary>
    void Place(Vector2 window)
    {
        this.window = window;
        view.StageScale = UiScale.For(window);
        if (IsInsideTree()) view.GlobalPosition = GetCanvasTransform().AffineInverse() * UiScale.MovieOrigin;
    }

    public override void _EnterTree()
    {
        Place(window);
        for (var n = GetParent(); n != null; n = n.GetParent())
            if (n is IUiStageHost h && h.UiStage != null) { stage = h.UiStage; break; }
        if (stage != null) stage.Add(Host, view.StageMouse, DrawOrder, view.IsVisibleInTree, ToLayerFront);
        else if (!OwnMovie) OffStageTextFocus.Add(Host.Player, view.IsVisibleInTree);   // a layer outside the scene's stage (the message box)
        if (Host?.Controller is { } c) Live.Add(c);        // CUIService's registry: in while the tree holds it
    }

    public override void _ExitTree()
    {
        Player.CancelDrag();             // closed mid-drag: no capture left behind
        stage?.Remove(Host);
        stage = null;
        OffStageTextFocus.Remove(Host.Player);
        if (Host?.Controller is { } c) Live.Remove(c);
    }

    UiStage stage;

    /// <summary>
    /// The stage moved this dialog to the end of its CommonDepth layer (<see cref="UiStage.TopmostLayer"/>): it goes to the
    /// end of its layer's run among its parent's dialogs (<see cref="DialogLayerOrder.ToLayerEnd"/>), so it is drawn and hit in
    /// front of the others of its layer and stays under every dialog of a higher layer.
    /// </summary>
    void ToLayerFront()
    {
        if (GetParent() is { } parent) DialogLayerOrder.ToLayerEnd(parent, this, LayerOf);
    }

    /// <summary>A dialog's CommonDepth layer (its movie's host layer); -1 for every other node and the HUD movies.</summary>
    internal static int LayerOf(Node n) => n is MovieDialog d ? d.Player?.HostLayer ?? -1 : -1;

    public override void _Notification(int what)
    {
        // added to a parent: it joins the end of its layer's run (msLoader.loadSwf adds the loader at its layer's end)
        if (what == NotificationParented && GetParent() is { } parent) DialogLayerOrder.Added(parent, this, LayerOf);
    }

    /// <summary>
    /// Where this dialog is drawn among the others: its canvas layer, then its place among the dialogs in that layer in tree
    /// order (Godot draws a layer's children in tree order).
    /// </summary>
    int DrawOrder()
    {
        Node n = this;
        while (n != null && n is not CanvasLayer) n = n.GetParent();
        if (n is not CanvasLayer layer) return 0;
        int rank = 0, found = -1;
        void Walk(Node x)
        {
            foreach (var c in x.GetChildren())
            {
                if (found >= 0) return;
                if (c == this) { found = rank; return; }
                if (c is MovieDialog) { rank++; continue; }
                Walk(c);
            }
        }
        Walk(layer);
        return layer.Layer * 100_000 + Math.Max(found, 0);
    }

    /// <summary>The client size times the UI scale (DAT_143af62d4 times FUN_141276490), a dialog's saved rates' base.</summary>
    void SetClientStage(Vector2 window)
    {
        var s = UiScale.Stage(window);
        Host.ClientStageW = s.X;
        Host.ClientStageH = s.Y;
    }

    /// <summary>
    /// The window was resized: the new stage, then the controller's vfunc_18 with the stage before and after (UIDialog keeps
    /// its position's ratio, then the layout).
    /// </summary>
    public void Relayout(Vector2 window)
    {
        double oldW = Player.StageW, oldH = Player.StageH;
        Resize(window);
        Place(window);
        SetClientStage(window);
        Host.Controller.Resize(oldW, oldH, Player.StageW, Player.StageH);
    }

    /// <summary>
    /// GFxManager::ResizeMovies (gfxmanager.cpp:158) for this movie, the device-restore step every movie takes first: the
    /// stage size before the resize is kept (UpdateStageSize, :164, read back by FUN_1422b0840), then GFxMovie::vfunc_7
    /// (gfxmovie.cpp:146) gives the movie the new viewport, (-1, -1, w + 2, h + 2) at its scale. MovieImpl::SetViewport
    /// (sfr_gfx_01i.cpp:1240-1256) calls the AS3 root's NotifyOnResize when the viewport changed (the movies run with
    /// SM_NoScale, gfxmovie.cpp:129), which queues the stage's Event.RESIZE; it is dispatched when the movie next advances
    /// (Event_Resize, sfr_as3_17d.cpp:1167), so after the dialogs' vfunc_17 and layout. Here it is queued to this node's
    /// next process, which runs before the movie's view ticks (<see cref="DeliverResize"/>). Returns the stage before the
    /// resize for the dialog's <see cref="RestoreDevice"/>.
    /// STILL-OPEN: the AS3 root's NotifyOnResize body is not in the restored source; the queueing is Scaleform's.
    /// </summary>
    public (double W, double H) ResizeViewport(Vector2 window)
    {
        var before = (Player.StageW, Player.StageH);
        bool changed = window != this.window;      // SetViewport notifies only a changed viewport
        Resize(window);
        Place(window);
        SetClientStage(window);
        if (changed) { resizePending = true; SetProcess(true); }
        return before;
    }

    bool resizePending;

    /// <summary>The queued stage Event.RESIZE (<see cref="ResizeViewport"/>), dispatched before the movie's next advance.</summary>
    void DeliverResize()
    {
        resizePending = false;
        if (Player.Stage is not { } st) return;
        var rt = Player.RT;
        rt.Guard("resize", () => st.Dispatch((AsEvent)rt.GetClass("flash.events.Event").Construct(AsEvent.RESIZE)));
    }

    /// <summary>
    /// The scene that resizes every movie itself while it runs (the HUD, <see cref="Restore"/> over its movies and the
    /// message box and tooltip layers'); the layers resize their own movie only while there is none.
    /// </summary>
    public static Node SceneResizer;

    /// <summary>True while <see cref="SceneResizer"/> is a live scene in the tree.</summary>
    public static bool SceneResizes => SceneResizer != null && GodotObject.IsInstanceValid(SceneResizer) && SceneResizer.IsInsideTree();

    /// <summary>
    /// The device restore over a set of movies, in the client's order (GFxService::vfunc_17, gfxservice.cpp:101): every
    /// movie's new viewport (GFxManager::ResizeMovies, gfxmanager.cpp:158, <see cref="ResizeViewport"/>), then every
    /// dialog's vfunc_17 (UISwfEntityManager::vfunc_5, uiswfentitymanager.cpp:483), then every entity's layout (vfunc_1,
    /// :413).
    /// </summary>
    public static void Restore(IReadOnlyList<MovieDialog> movies, Vector2 window)
    {
        var before = new (double W, double H)[movies.Count];
        for (int i = 0; i < movies.Count; i++) before[i] = movies[i].ResizeViewport(window);
        for (int i = 0; i < movies.Count; i++) movies[i].RestoreDevice(before[i]);
        for (int i = 0; i < movies.Count; i++) movies[i].LayoutLoaded();
    }

    /// <summary>UISwfEntityManager::vfunc_5 (uiswfentitymanager.cpp:483): the dialog's vfunc_17 with the stage cached
    /// before the resize (<see cref="UiController.RestoreDevice"/>).</summary>
    public void RestoreDevice((double W, double H) before) =>
        Host.Controller.RestoreDevice(before.W, before.H, Player.StageW, Player.StageH);

    /// <summary>UISwfEntityManager::vfunc_1 (uiswfentitymanager.cpp:413), after every dialog's vfunc_17: each loaded entity's
    /// layout, FUN_1422dc450.</summary>
    public void LayoutLoaded() => Host.Controller.LayoutAndClear(Player.StageW, Player.StageH);

    /// <summary>The movie's next frames now (a shot steps the timeline instead of waiting for real time).</summary>
    public void Step(int frames)
    {
        for (int i = 0; i < frames; i++) Player.Tick();
        view.Sync();
    }
}
