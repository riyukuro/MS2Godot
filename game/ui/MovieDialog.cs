using Godot;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Ui;
using FlashPlayer = Ms2.Avm2.Flash.Player;

namespace Ms2Godot.Ui;

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

    public MovieDialog(UiAssets assets, UiController controller, UiModel model, Vector2 window)
    {
        var packs = assets.Packs;
        var src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        var (rt, p) = src.Open(controller.Movie, r => r.Budget = 20_000_000);
        Player = p;
        Resize(window);
        view = new As3View(p, assets);
        AddChild(view);
        Place(window);
        Host = new UiHost(p, controller, model);
        Loaded = Host.Load();
        foreach (var e in rt.Errors) GD.PushWarning($"[{controller.Movie}] {e}");
    }

    void Resize(Vector2 window)
    {
        var stage = UiScale.MovieStage(window);
        Player.StageW = stage.X;
        Player.StageH = stage.Y;
    }

    void Place(Vector2 window)
    {
        view.StageScale = UiScale.For(window);
        view.Position = UiScale.MovieOrigin;
    }

    /// <summary>The window was resized: the new stage, then the controller's layout (UIObject vfunc_43).</summary>
    public void Relayout(Vector2 window)
    {
        Resize(window);
        Place(window);
        Host.Controller.Layout(Player.StageW, Player.StageH);
    }

    /// <summary>The movie's next frames now (a shot steps the timeline instead of waiting for real time).</summary>
    public void Step(int frames)
    {
        for (int i = 0; i < frames; i++) Player.Tick();
        view.Sync();
    }
}
