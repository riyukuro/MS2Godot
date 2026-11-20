using Godot;

namespace Ms2Godot.Tests;

/// <summary>
/// Check (res://tests/option_window.tscn, on a display, with --options-file and --resolution 1280x720): a scene started
/// on its own takes its window into the graphic option (ScreenMode.AdoptWindow), so the options dialog shows it, but a
/// save keeps the file's window. It stores a full window at 1920 x 1080 in the file, adopts the 1280 x 720 window,
/// saves, and reads the file again. Quits with 0 when the file still holds the full window, else 1.
/// </summary>
public partial class OptionWindowCheck : Node
{
    int frames, failures;

    void Fail(string what) { failures++; GD.Print("option window: FAIL " + what); }

    public override void _Process(double delta)
    {
        if (++frames < 5) return;
        SetProcess(false);
        var g = GameOptions.Current.GraphicOption;
        g.ScreenMode = 2; g.Width = 1920; g.Height = 1080;
        GameOptions.Save();                                 // the user's stored window
        ScreenMode.AdoptWindow();
        var size = ScreenMode.PixelSize(GetWindow());
        if ((g.ScreenMode, g.Width, g.Height) != (1, size.X, size.Y)) Fail($"the option shows {g.ScreenMode} {g.Width}x{g.Height}, not the {size} window");
        GameOptions.Save();
        GameOptions.Load();
        var read = GameOptions.Current.GraphicOption;
        GD.Print($"option window: window {size}, file {read.ScreenMode} {read.Width}x{read.Height}");
        if ((read.ScreenMode, read.Width, read.Height) != (2, 1920, 1080)) Fail($"the file now holds {read.ScreenMode} {read.Width}x{read.Height}, not the full window 1920x1080");
        GD.Print($"option window: {(failures == 0 ? "ok" : "FAIL")}, {failures} failures");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }
}
