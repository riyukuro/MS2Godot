using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui;

namespace Ms2Godot.Ui;

/// <summary>
/// The client's software cursor, GFxMovieMouseCursor (movie 6, CommonMouseCursor.gfx, gfxmanager.cpp:91, 521), which
/// GFxManager loads once for the run after the tooltip movie (Initialize :137, LoadMouseCursorMovie :1151). Its cursor clip
/// follows the stage's mouse over everything else. Here it is a node under the tree's root that outlives scene changes, as
/// <see cref="TooltipLayer"/>, drawn above every other layer. It takes no keys or clicks (the clip is
/// InteractiveObjectEx.setHitTestDisable), and the mouse position reaches it (vfunc_14 WM_MOUSEMOVE).
/// ASMouseCursor::SetCursorMode (asmousecursor.cpp:53-79): while the window is active (vfunc_19, mode 1) the system cursor
/// is hidden and the clip draws it; when inactive (vfunc_20, mode 2) the system cursor shows and the clip hides. The system
/// cursor is hidden only once the clip is known, so a movie that fails to load never leaves the window without a cursor.
/// </summary>
public partial class MouseCursorLayer : CanvasLayer
{
    static MouseCursorLayer instance;

    MouseCursorController controller;
    MovieDialog movie;
    As3View view;
    Viewport sizeSource;
    bool active = true;

    public static MouseCursorLayer Instance => instance;
    public MouseCursorController Controller => controller;

    /// <summary>The cursor movie's controller once its cursor clip is known, else null.</summary>
    public static MouseCursorController Cursor =>
        instance != null && IsInstanceValid(instance) && instance.controller is { Ready: true } c ? c : null;

    /// <summary>The layer, made under the tree's root the first time it is asked for (null without client data).</summary>
    public static MouseCursorLayer Ensure(Node any)
    {
        if (instance != null && IsInstanceValid(instance)) return instance;
        var dataDir = Settings.DataDir;
        if (string.IsNullOrEmpty(dataDir)) return null;
        instance = new MouseCursorLayer { Name = "MouseCursorLayer", Layer = 100, ProcessMode = ProcessModeEnum.Always };
        instance.Build(new UiAssets(PackSet.Shared(dataDir)), any.GetTree().Root);
        any.GetTree().Root.CallDeferred(Node.MethodName.AddChild, instance);
        return instance;
    }

    void Build(UiAssets assets, Window root)
    {
        UiCode.Load(Settings.DataDir);
        controller = new MouseCursorController();
        controller.CursorReady += ApplyMode;
        movie = new MovieDialog(assets, controller, new UiModel(GameTables.Shared(assets.Packs, assets.Language)), root.Size);
        view = movie.GetChildren().OfType<As3View>().FirstOrDefault();
        view?.SetProcessUnhandledInput(false);      // no keys or clicks
        AddChild(movie);
        UiHost.MouseCursor = controller;
    }

    public override void _EnterTree()
    {
        sizeSource = GetViewport();
        sizeSource.SizeChanged += Relayout;
        Relayout();
    }

    public override void _ExitTree()
    {
        if (sizeSource != null) { sizeSource.SizeChanged -= Relayout; sizeSource = null; }
        if (UiHost.MouseCursor == controller) UiHost.MouseCursor = null;
        if (instance == this) instance = null;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>GFxManager::ResizeMovies covers this movie as the tooltip's (<see cref="TooltipLayer"/>).</summary>
    void Relayout()
    {
        if (movie == null || MovieDialog.SceneResizes) return;
        MovieDialog.Restore(new[] { movie }, GetViewport().GetVisibleRect().Size);
    }

    /// <summary>vfunc_19 / vfunc_20: the window became active (mode 1) or inactive (mode 2).</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusIn) { active = true; ApplyMode(); }
        else if (what == NotificationApplicationFocusOut) { active = false; ApplyMode(); }
    }

    /// <summary>SetCursorMode: mode 1 hides the system cursor and shows the clip; mode 2 the other way round. Before the clip is
    /// known the system cursor stays.</summary>
    void ApplyMode()
    {
        bool soft = active && controller is { Ready: true };
        if (movie != null) movie.Visible = soft;
        Input.MouseMode = soft ? Input.MouseModeEnum.Hidden : Input.MouseModeEnum.Visible;
    }

    /// <summary>vfunc_14 WM_MOUSEMOVE: the mouse in stage units; the event goes on to the rest of the UI.</summary>
    public override void _Input(InputEvent e)
    {
        if (view == null || !view.IsInsideTree() || e is not InputEventMouseMotion mm) return;
        var p = view.GetGlobalTransformWithCanvas().AffineInverse() * mm.Position;
        movie.Player.MouseMove(p.X, p.Y);
    }
}
