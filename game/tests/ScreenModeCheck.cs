using Godot;
using Ms2.Formats.Options;

namespace Ms2Godot.Tests;

/// <summary>
/// Check (res://tests/screen_mode.tscn, on a display, with --options-file): applies the screen modes the options offer in turn
/// (full screen, window, full window; a size equal to, lower than and higher than the screen; from a window and from a full
/// screen, and back) through ScreenMode.ApplyRequest, as the options dialog's apply does, and prints what the window is after
/// each. With the user argument --shots dir it also takes a whole-screen shot of each step with the desktop's screenshot tool,
/// for the A/B proof (no panel, no bars). Quits with 0 when every step's window covers the screen in the full modes.
/// </summary>
public partial class ScreenModeCheck : Node
{
    static readonly (string Name, WindowRequest Req)[] Steps =
    {
        ("window 1280x720", new WindowRequest(false, false, 1280, 720)),
        ("full screen 1920x1080 (equal)", new WindowRequest(true, false, 1920, 1080)),
        ("window 1920x1080 (equal)", new WindowRequest(false, false, 1920, 1080)),
        ("full screen 1280x720 (lower)", new WindowRequest(true, false, 1280, 720)),
        ("full window 1920x1080 (equal)", new WindowRequest(false, true, 1920, 1080)),
        ("window 1280x720 (back)", new WindowRequest(false, false, 1280, 720)),
        ("full window 1280x720 (lower)", new WindowRequest(false, true, 1280, 720)),
        ("full screen 2560x1440 (higher)", new WindowRequest(true, false, 2560, 1440)),
        ("window 2560x1440 (higher)", new WindowRequest(false, false, 2560, 1440)),
        ("full window 2560x1440 (higher)", new WindowRequest(false, true, 2560, 1440)),
        ("window 1280x720 (end)", new WindowRequest(false, false, 1280, 720)),
    };

    string shots;
    int step = -1, failures;
    ulong due;

    public override void _Ready()
    {
        var a = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "--shots") shots = a[i + 1];
        if (shots != null) Directory.CreateDirectory(shots);
        due = Time.GetTicksMsec() + 2000;
    }

    public override void _Process(double delta)
    {
        if (Time.GetTicksMsec() < due) return;
        if (step >= 0) Report();
        if (++step >= Steps.Length)
        {
            GD.Print($"screen mode: {(failures == 0 ? "ok" : "FAIL")}, {failures} failures");
            SetProcess(false);
            GetTree().Quit(failures == 0 ? 0 : 1);
            return;
        }
        ScreenMode.ApplyRequest(Steps[step].Req);
        due = Time.GetTicksMsec() + 3000;
    }

    void Report()
    {
        var (name, r) = Steps[step];
        var w = GetWindow();
        var screen = DisplayServer.ScreenGetSize(DisplayServer.GetPrimaryScreen());
        var real = DisplayServer.WindowGetSize();
        bool full = r.Borderless;
        GD.Print($"screen mode: [{name}] mode {w.Mode} borderless {w.Borderless} top {w.AlwaysOnTop} size {w.Size} real {real} pos {w.Position} " +
                 $"content {ScreenMode.PixelSize(w)} emulated {ScreenMode.Emulated} visible {w.GetVisibleRect()} screen {screen}");
        if (full && real != screen) { failures++; GD.Print($"screen mode: FAIL [{name}] the window is {real}, not the screen {screen}"); }
        if (shots != null)
        {
            var file = System.IO.Path.Combine(System.IO.Path.GetFullPath(shots), $"{step:00}.png");
            OS.Execute("spectacle", new[] { "-b", "-n", "-f", "-o", file });
        }
    }
}
