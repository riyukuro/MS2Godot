using Godot;
using Ms2.Formats.Pack;
using Ms2Godot.Ui;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/loading_size.tscn): the loading screen keeps one size across a scene change under it. The
/// scene runs in window pixels (LobbyLayout.UseWindowPixels, as the lobby and the HUD do); a loading screen goes up at the
/// tree root and the scene changes to a new instance of itself, as character select's Start changes to the HUD under the
/// screen. The check fails when the root's content scale leaves window pixels at any point of the change (the old
/// scene's scope ending turned the project's canvas_items stretch on until the new scene entered, and the screen drew
/// under that stretch's inverse), or when the screen's transform is not the root's inverse at the end. Quits with 0
/// when all hold, else 1.
/// </summary>
public partial class LoadingSizeCheck : Node
{
    static bool driving;

    public override void _EnterTree() => LobbyLayout.UseWindowPixels(this);

    public override void _Ready()
    {
        if (driving) return;
        driving = true;
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, new LoadingSizeDriver());
    }
}

/// <summary>The check's driver, at the tree root so it outlives the scene change.</summary>
public partial class LoadingSizeDriver : Node
{
    int frames, failures, changes;
    Window.ContentScaleModeEnum pixels;
    LoadingMovie movie;
    Node oldScene;
    System.Action onSize;

    void Fail(string what) { failures++; GD.Print("loading size: FAIL " + what); }

    void Record(string when)
    {
        var mode = GetTree().Root.ContentScaleMode;
        if (mode != pixels) Fail($"the content scale is {mode} ({when}), not {pixels}");
    }

    public override void _Process(double delta)
    {
        var root = GetTree().Root;
        frames++;
        if (frames == 3)
        {
            pixels = root.ContentScaleMode;
            if (pixels == Window.ContentScaleModeEnum.CanvasItems) Fail("the scene did not take window pixels");
            var layer = new CanvasLayer { Layer = 100, Name = "Loading" };
            root.AddChild(layer);
            movie = new LoadingMovie(new UiAssets(PackSet.Shared(Settings.DataDir)), root.Size);
            layer.AddChild(movie);
            movie.Start(null, null, "", false, true);
            root.SizeChanged += onSize = () => { changes++; Record("at a size change"); };
            oldScene = GetTree().CurrentScene;
            oldScene.TreeExited += () => Record("after the old scene left");
            GetTree().ChangeSceneToFile("res://tests/loading_size.tscn");
            return;
        }
        if (frames < 8) { if (frames > 3) Record($"frame {frames}"); return; }
        if (GetTree().CurrentScene == null || GetTree().CurrentScene == oldScene) Fail("the scene did not change");
        if (movie.Transform != root.GetFinalTransform().AffineInverse()) Fail("the screen is not drawn in window pixels");
        GD.Print($"loading size: content scale {root.ContentScaleMode}, {changes} size changes during the scene change");
        root.SizeChanged -= onSize;
        // the screen ending its scope leaves the scene's in place
        movie.GetParent().Free();
        if (root.ContentScaleMode != pixels) Fail("the screen's scope ending took the scene's window pixels away");
        SetProcess(false);
        GD.Print($"loading size: {(failures == 0 ? "ok" : "FAIL")}, {failures} failures");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }
}
