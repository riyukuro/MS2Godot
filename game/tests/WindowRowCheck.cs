using Godot;
using Ms2.Formats.Options;

namespace Ms2Godot.Tests;

/// <summary>
/// Check (res://tests/window_row.tscn, on a display smaller than 2560 x 1440; run with --options-file so no user file is
/// read): a windowed request larger than the screen is clamped by ApplyWindowMode (windowservice.cpp:337-374), and the
/// graphic option keeps the request's size, as FUN_14125e910 stores the mode's width and height
/// (gameoptionstruct.cpp:2239-2248), with the size's row when the display lists it. Quits with 0 when that holds, else 1.
/// </summary>
public partial class WindowRowCheck : Node
{
    int frames, failures;

    void Fail(string what) { failures++; GD.Print("window row: FAIL " + what); }

    public override void _Process(double delta)
    {
        if (++frames == 5)
        {
            var screen = DisplayServer.ScreenGetSize(DisplayServer.GetPrimaryScreen());
            if (screen.X >= 2560 && screen.Y >= 1440) { Fail($"the screen {screen} is not smaller than the request"); Finish(); return; }
            ScreenMode.ApplyRequest(new WindowRequest(false, false, 2560, 1440));
            return;
        }
        if (frames < 15) return;
        var g = GameOptions.Current.GraphicOption;
        var size = ScreenMode.PixelSize(GetWindow());
        GD.Print($"window row: window {size}, option {g.Width}x{g.Height} mode {g.ScreenMode} row {g.ResolutionIndex}");
        if (size.X >= 2560 || size.Y >= 1440) Fail($"the window was not clamped ({size})");
        if ((g.Width, g.Height, g.ScreenMode) != (2560, 1440, 1)) Fail($"the option is {g.Width}x{g.Height} mode {g.ScreenMode}, not the request's 2560x1440 window");
        if (ScreenMode.Modes.IsAvailable(2560, 1440))
        {
            var probe = new GameOption.Graphic();
            ScreenMode.Modes.PickRow(probe, 2560, 1440);
            if (g.ResolutionIndex != probe.ResolutionIndex) Fail($"the row is {g.ResolutionIndex}, not 2560x1440's {probe.ResolutionIndex}");
        }
        Finish();
    }

    void Finish()
    {
        SetProcess(false);
        GD.Print($"window row: {(failures == 0 ? "ok" : "FAIL")}, {failures} failures");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }
}
