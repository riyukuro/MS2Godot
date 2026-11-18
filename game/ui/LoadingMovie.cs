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
        foreach (var n in Dialog.GetChildren())
            if (n is As3View v) v.ImageLookup = Lookup;
        Controller.FadeInComplete += () => Active = false;
    }

    /// <summary>Between StartProgress and the movie's OnFadeInComplete.</summary>
    public bool Active { get; private set; }

    /// <summary>
    /// StartProgress (uiloadingprogress.cpp:85-111). <paramref name="picture"/> is the table's picture (GetLoadingImagePath,
    /// an entry of LoadingImageTable.ImageArchive), or null for none: the movie then shows <paramref name="screenShot"/>.
    /// </summary>
    public void Start(string picture, Texture2D screenShot, string tip, bool showGuide, bool showFrame)
    {
        ScreenShot = picture == null ? screenShot : null;
        Active = true;
        Controller.StartProgress(new LoadingProgressController.StartInfo
        {
            FilePath = picture ?? "", Tip = tip ?? "", ShowGuide = showGuide ? 1 : 0, ShowFrame = showFrame,
        });
    }

    /// <summary>SetProgress (uiloadingprogress.cpp:140-146).</summary>
    public void Progress(int count, int total)
    {
        if (Active) Controller.SetProgress("", count, total);
    }

    /// <summary>StopProgress (uiloadingprogress.cpp:115-122): stopProgress(GetFadeOutFlag); the movie fades out (or not) and
    /// answers OnFadeInComplete.</summary>
    public void Stop(bool fade)
    {
        if (Active) Controller.StopProgress(fade ? 1 : 0);
    }

    /// <summary>
    /// The scene screenshot the movie shows when it gets no picture of its own (filePath ""): it loads
    /// "img://" + getID + ":screenShot", which UILoadingProgress::vfunc_25 (uiloadingprogress.cpp:215-232) binds to the
    /// screenshot's render texture.
    /// </summary>
    public Texture2D ScreenShot;

    Texture2D Lookup(string url) =>
        ScreenShot != null && string.Equals(url, "img://" + Dialog.Host.Id + ":screenShot", System.StringComparison.OrdinalIgnoreCase) ? ScreenShot : null;

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
