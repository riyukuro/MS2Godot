using Godot;
using Ms2.Formats.Options;
using Ms2.Formats.Pack;
using Ms2Godot.Ui;

namespace Ms2Godot.Tests;

/// <summary>
/// Display check (res://tests/fullwindow_stage.tscn, needs a display at least as wide as the content; args after `--`: --size WxH,
/// 1024x768 by default, and --shot PNG for the screen at the end): in a full window at
/// 1024 x 768 (on Linux a window covering the screen whose content is rendered at 1024 x 768,
/// <see cref="ScreenMode.Emulated"/>) a movie laid out in window pixels gets the stage of 1024 x 768, as the client's
/// movies get the back buffer's (GFxMovie::vfunc_7, gfxmovie.cpp:146). The loading screen's stage followed the window's
/// OS size, the screen's, so its panels went past the drawn area and were cut off at the right; and it was drawn through
/// the inverse of the viewport stretch, shrunk into the top left of the picture. Quits with 0 when the
/// stage holds, else 1.
/// </summary>
public partial class FullWindowStageCheck : Node
{
    int frames, failures;
    LoadingMovie movie;
    System.Action endPixels;
    Vector2I size = new(1024, 768);
    string shot;

    void Fail(string what) { failures++; GD.Print("full window stage: FAIL " + what); }

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--shot") shot = args[i + 1];
            if (args[i] == "--size" && args[i + 1].Split('x') is [var w, var h]) size = new Vector2I(int.Parse(w), int.Parse(h));
        }
        var screen = DisplayServer.ScreenGetSize(DisplayServer.GetPrimaryScreen());
        if (DisplayServer.GetName() == "headless" || screen.X < size.X) { GD.Print($"full window stage: FAIL needs a display at least as wide as the content (got {DisplayServer.GetName()} {screen})"); GetTree().Quit(1); return; }
        ScreenMode.ApplyRequest(new WindowRequest(false, true, size.X, size.Y));
        endPixels = ScreenMode.UseWindowPixels(GetWindow());
    }

    public override void _Process(double delta)
    {
        var root = GetTree().Root;
        if (++frames == 3)
        {
            var layer = new CanvasLayer { Layer = 100, Name = "Loading" };
            root.AddChild(layer);
            movie = new LoadingMovie(new UiAssets(PackSet.Shared(Settings.DataDir)), (Vector2I)root.GetVisibleRect().Size);
            layer.AddChild(movie);
            movie.Start(null, null, "", true, true);   // with the guide (the keyboard controls panel)
            return;
        }
        if (frames < 40) return;
        var drawn = root.GetVisibleRect().Size;
        var want = UiScale.MovieStage(size);
        var stage = new Vector2((float)movie.Dialog.Player.StageW, (float)movie.Dialog.Player.StageH);
        GD.Print($"full window stage: window {root.Size}, drawn {drawn}, loading stage {stage}, want {want}");
        if (drawn != (Vector2)size) Fail($"the content is drawn at {drawn}, not {size}");
        if (stage != want) Fail($"the loading screen's stage is {stage}, not {want}");
        var drawScale = movie.Dialog.GetGlobalTransformWithCanvas().Scale.X / movie.Dialog.Transform.Scale.X;
        if (Mathf.Abs(drawScale - 1f) > 1e-4f) Fail($"the loading screen is drawn at {drawScale} of the content's pixels, not 1");
        if (shot != null) DisplayServer.ScreenGetImage(DisplayServer.GetPrimaryScreen()).SavePng(shot);
        endPixels?.Invoke();
        SetProcess(false);
        GD.Print($"full window stage: {(failures == 0 ? "ok" : "FAIL")}, {failures} failures");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }
}
