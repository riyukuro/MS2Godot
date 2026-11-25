using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui;

namespace Ms2Godot.Ui;

/// <summary>
/// The client's tooltip movie, GFxMovieTooltip (movie 5, CommonTooltip.gfx, gfxmanager.cpp:91), which GFxManager loads
/// once for the run beside the main movie: every dialog's TooltipManager sends its "Tooltip" calls there
/// (<see cref="UiHost.Tooltips"/>) and the movie draws the tooltips over all the UI. Here it is a node under the tree's
/// root that outlives scene changes, as <see cref="MessageBoxLayer"/>. It takes no keys or clicks; the mouse position
/// reaches it (GFxMovieTooltip::vfunc_14 WM_MOUSEMOVE).
/// </summary>
public partial class TooltipLayer : CanvasLayer
{
    static TooltipLayer instance;

    TooltipController controller;
    MovieDialog movie;
    As3View view;
    Viewport sizeSource;

    public static TooltipLayer Instance => instance;
    public TooltipController Controller => controller;
    public MovieDialog Movie => movie;

    /// <summary>The layer, made under the tree's root the first time it is asked for (null without client data).</summary>
    public static TooltipLayer Ensure(Node any)
    {
        if (instance != null && IsInstanceValid(instance)) return instance;
        var dataDir = Settings.DataDir;
        if (string.IsNullOrEmpty(dataDir)) return null;
        instance = new TooltipLayer { Name = "TooltipLayer", Layer = 14, ProcessMode = ProcessModeEnum.Always };
        instance.Build(new UiAssets(PackSet.Shared(dataDir)), any.GetTree().Root);
        any.GetTree().Root.CallDeferred(Node.MethodName.AddChild, instance);
        return instance;
    }

    void Build(UiAssets assets, Window root)
    {
        UiCode.Load(Settings.DataDir);
        controller = new TooltipController();
        movie = new MovieDialog(assets, controller, new UiModel(GameTables.Shared(assets.Packs, assets.Language)), root.Size);
        view = movie.GetChildren().OfType<As3View>().FirstOrDefault();
        view?.SetProcessUnhandledInput(false);      // no keys or clicks
        AddChild(movie);
        UiHost.Tooltips = controller;
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
        if (UiHost.Tooltips == controller) UiHost.Tooltips = null;
        if (instance == this) instance = null;
    }

    /// <summary>The layer's movie (CUIService's one tooltip movie), while it is made.</summary>
    public static MovieDialog LayerMovie => instance != null && IsInstanceValid(instance) ? instance.movie : null;

    /// <summary>
    /// GFxManager::ResizeMovies covers this movie as every other (gfxmanager.cpp:158): under the HUD the HUD's resize
    /// restores it with its own movies (<see cref="MovieDialog.SceneResizes"/>); in another scene the layer runs the same
    /// restore on its movie alone (<see cref="MovieDialog.Restore"/>).
    /// </summary>
    void Relayout()
    {
        if (movie == null || MovieDialog.SceneResizes) return;
        MovieDialog.Restore(new[] { movie }, GetViewport().GetVisibleRect().Size);
    }

    /// <summary>vfunc_14 WM_MOUSEMOVE: the mouse in stage units; the event goes on to the rest of the UI.</summary>
    public override void _Input(InputEvent e)
    {
        if (view == null || !view.IsInsideTree() || e is not InputEventMouseMotion mm) return;
        var p = view.GetGlobalTransformWithCanvas().AffineInverse() * mm.Position;
        movie.Player.MouseMove(p.X, p.Y);
    }
}
