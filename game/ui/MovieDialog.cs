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

    public MovieDialog(UiAssets assets, UiController controller, UiModel model, Vector2 window, ICharacterFolder folder = null)
    {
        // the movies are parsed once per pack set and shared by every open (the client's loader keeps a movie's definition
        // once loaded); the runtime, player and display list are still made on each open
        var src = MovieSource.FromParsed(assets.Dependency);
        var (rt, p) = src.Open(controller.Movie, r => r.Budget = 20_000_000);
        Player = p;
        Resize(window);
        view = new As3View(p, assets);
        AddChild(view);
        Place(window);
        Host = new UiHost(p, controller, model) { Folder = folder ?? CharacterFolder.ForChosen() };
        SetClientStage(window);
        Loaded = Host.Load();
        foreach (var e in rt.Errors) GD.PushWarning($"[{controller.Movie}] {e}");
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
        stage?.Add(Host, view.StageMouse, DrawOrder, view.IsVisibleInTree);
    }

    public override void _ExitTree()
    {
        Player.CancelDrag();             // closed mid-drag: no capture left behind
        stage?.Remove(Host);
        stage = null;
    }

    UiStage stage;

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

    /// <summary>The movie's next frames now (a shot steps the timeline instead of waiting for real time).</summary>
    public void Step(int frames)
    {
        for (int i = 0; i < frames; i++) Player.Tick();
        view.Sync();
    }
}
