using Godot;
using Ms2.Ui;

namespace Ms2Godot.Ui;

/// <summary>
/// The loading screen run from its own movie (uiloadingprogress) with UILoadingProgress's C++ side
/// (<see cref="LoadingProgressController"/>), for the flow scenes. A <see cref="MovieDialog"/> sized to the window's pixel
/// size, drawn through the inverse of the root's final transform: the screen lives at the tree root across a field
/// change while the HUD scene turns the root's canvas_items stretch on and off, so the movie keeps the window's own
/// pixels either way (as GFxMovie draws to the device). A new window size gives the movie its new stage
/// (<see cref="MovieDialog.Relayout"/>).
/// </summary>
public partial class LoadingMovie : Node2D
{
    public readonly LoadingProgressController Controller = new();
    public MovieDialog Dialog { get; }
    Vector2I window;

    public LoadingMovie(UiAssets assets, Vector2I windowSize)
    {
        Name = "LoadingProgressMovie";
        if (!UiCode.Load(Settings.DataDir)) GD.PushWarning("UI code is not built for this data: the loading screen runs interpreted");
        var model = new UiModel(new GameTables(assets.Packs, assets.Language));
        window = windowSize;
        Dialog = new MovieDialog(assets, Controller, model, window);
        AddChild(Dialog);
    }

    public LoadingMovie() { }

    public override void _EnterTree() => Fit();

    public override void _Process(double delta) => Fit();

    void Fit()
    {
        var root = GetTree()?.Root;
        if (root == null || Dialog == null) return;
        Transform = root.GetFinalTransform().AffineInverse();
        if (root.Size == window) return;
        window = root.Size;
        Dialog.Relayout(window);
    }
}
