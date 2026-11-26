using Godot;
using Ms2.Formats.Options;

namespace Ms2Godot.Tests;

/// <summary>
/// Check (res://tests/screen_mode.tscn, on a display, with --options-file): applies the screen modes the options offer in turn
/// (full screen, window, full window; a size equal to, lower than and higher than the screen; from a window and from a full
/// screen, and back) through ScreenMode.ApplyRequest, as the options dialog's apply does, and prints what the window is after
/// each. With the user argument --shots dir it also takes a whole-screen shot of each step with the desktop's screenshot tool,
/// for the A/B proof (no panel, no bars). Quits with 0 when every step's window covers the screen in the full modes.
/// With --steps w1280x720,f1920x1080,w1920x1080 (window w, full screen f, full window m, of w x h) and --hold ms it runs just those
/// steps and reports each one's frame rate and frame time; a framed window that reaches past the work area, or one that runs at
/// under half the frame rate of the first window step, fails (GitHub issue #10: the window came back from full screen at 2 fps).
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

    (string Name, WindowRequest Req)[] steps = Steps;
    string shots;
    int step = -1, failures;
    ulong due, holdMs = 3000, stepStart, stepFrames;
    readonly List<(string Name, double Fps, double Ms)> rates = new();

    public override void _Ready()
    {
        var a = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < a.Length; i++)
        {
            if (a[i] == "--shots") shots = a[i + 1];
            // --steps w1280x720,f1920x1080,w1280x720: window (w), full screen (f) or full window (m) of w x h, in turn
            if (a[i] == "--steps") steps = a[i + 1].Split(',').Select(Parse).ToArray();
            if (a[i] == "--hold") holdMs = ulong.Parse(a[i + 1]);
        }
        if (shots != null) Directory.CreateDirectory(shots);
        due = Time.GetTicksMsec() + 2000;
    }

    static (string, WindowRequest) Parse(string t)
    {
        var d = t.Substring(1).Split('x');
        int w = int.Parse(d[0]), h = int.Parse(d[1]);
        return (t, new WindowRequest(t[0] == 'f', t[0] == 'm', w, h));
    }

    double slowest, windowedFps; int slow;

    public override void _Process(double delta)
    {
        if (delta > slowest) slowest = delta;
        if (delta > 0.1) slow++;
        if (Time.GetTicksMsec() < due) return;
        if (step >= 0) Report();
        if (++step >= steps.Length)
        {
            GD.Print($"screen mode: {(failures == 0 ? "ok" : "FAIL")}, {failures} failures");
            SetProcess(false);
            GetTree().Quit(failures == 0 ? 0 : 1);
            return;
        }
        ScreenMode.ApplyRequest(steps[step].Req);
        stepStart = Time.GetTicksMsec();
        stepFrames = Engine.GetProcessFrames();
        due = stepStart + holdMs;
    }

    void Report()
    {
        var (name, r) = steps[step];
        var w = GetWindow();
        double secs = (Time.GetTicksMsec() - stepStart) / 1000.0, frames = Engine.GetProcessFrames() - stepFrames;
        rates.Add((name, frames / secs, secs * 1000.0 / Math.Max(frames, 1)));
        GD.Print($"screen mode: [{name}] {frames / secs:0.0} fps, {secs * 1000.0 / Math.Max(frames, 1):0.0} ms a frame over {secs:0.0} s, slowest frame {slowest * 1000.0:0} ms, {slow} frames over 100 ms");
        slowest = 0; slow = 0;
        var screen = DisplayServer.ScreenGetSize(DisplayServer.GetPrimaryScreen());
        var real = DisplayServer.WindowGetSize();
        bool full = r.Borderless;
        GD.Print($"screen mode: [{name}] mode {w.Mode} borderless {w.Borderless} top {w.AlwaysOnTop} size {w.Size} real {real} pos {w.Position} " +
                 $"content {ScreenMode.PixelSize(w)} emulated {ScreenMode.Emulated} visible {w.GetVisibleRect()} screen {screen}");
        if (full && real != screen) { failures++; GD.Print($"screen mode: FAIL [{name}] the window is {real}, not the screen {screen}"); }
        if (!full)
        {
            // GitHub issue #10: a framed window must fit the work area with its frame (a client the size of the screen plus a
            // title bar ran at about 2 frames a second on a window manager that does not clamp it after the full-screen state)
            var work = DisplayServer.ScreenGetUsableRect(DisplayServer.GetPrimaryScreen());
            var outer = w.GetSizeWithDecorations();
            if (outer.X > work.Size.X || outer.Y > work.Size.Y)
            { failures++; GD.Print($"screen mode: FAIL [{name}] the framed window is {outer}, past the work area {work.Size}"); }
            // and it runs as fast as the first window of the run did
            double fps = rates[^1].Fps;
            if (windowedFps == 0) windowedFps = fps;
            else if (windowedFps >= 20 && fps < windowedFps * 0.5)
            { failures++; GD.Print($"screen mode: FAIL [{name}] {fps:0.0} fps, the window ran at {windowedFps:0.0} fps before"); }
        }
        if (shots != null)
        {
            var file = System.IO.Path.Combine(System.IO.Path.GetFullPath(shots), $"{step:00}.png");
            OS.Execute("spectacle", new[] { "-b", "-n", "-f", "-o", file });
        }
    }
}
