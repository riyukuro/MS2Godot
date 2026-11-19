using Godot;
using Ms2.Formats.Options;

namespace Ms2Godot;

/// <summary>
/// The game window's screen mode and size, from the graphic options as the client sets them: at startup
/// (MS2RenderService::OnInit, ms2renderservice.cpp:699-743, <see cref="ScreenModes.Startup"/>) and from the options
/// dialog (FUN_14125e910, <see cref="ScreenModes.Switch"/>), both ending in CWindowService::ApplyWindowMode
/// (windowservice.cpp:291-375):
/// <list type="bullet">
/// <item>window (screen mode 1): a fixed-size window with a frame (style 0xca0000), the client area w x h clamped to the
/// screen, centred; the display mode is restored first.</item>
/// <item>full window (2) and full screen (0): the display is switched to w x h (SetDisplayMode) and the window becomes a
/// frameless popup at 0,0 of w x h, topmost unless it is the full window.</item>
/// </list>
/// Windows does exactly that, through <see cref="DisplayNative"/>. Linux deviation: the desktop's display mode is never
/// changed (a crash would leave the monitor at the game's resolution, and Wayland does not allow it). The full window is a
/// borderless window covering the screen whose content is rendered at w x h and scaled to the screen with its aspect kept
/// (<see cref="Emulated"/>), which looks like the client's mode switch without touching the desktop.
/// </summary>
public static class ScreenMode
{
    static ScreenModes modes;
    // the windows in window pixels: the open scopes and the content scale each had before its first scope
    static readonly Dictionary<Window, (int Scopes, Window.ContentScaleModeEnum Mode, Vector2I Size, Window.ContentScaleAspectEnum Aspect)> pixelScopes = new();

    /// <summary>The display's mode list: the system's modes (<see cref="DisplayNative.Enumerate"/>), or only the desktop
    /// size when they cannot be read.</summary>
    public static ScreenModes Modes => modes ??= new ScreenModes(DisplayNative.Enumerate() ?? new List<DisplayMode> { Desktop() });

    /// <summary>Linux full window: the size the content is rendered at; null otherwise.</summary>
    public static Vector2I? Emulated { get; private set; }

    static bool Headless => DisplayServer.GetName() == "headless";
    static Window Root => (Engine.GetMainLoop() as SceneTree)?.Root;
    static int Screen => DisplayServer.GetPrimaryScreen();

    static DisplayMode Desktop()
    {
        var s = DisplayServer.ScreenGetSize(Screen);
        return new DisplayMode(s.X, s.Y, 32);
    }

    /// <summary>
    /// OnInit's window: the stored mode and size. The client starts a fresh option file as a full window at the desktop size
    /// and saves it (ms2renderservice.cpp:738-742); that start is held back until the options dialog can change the mode
    /// again, so with no stored GameOption.xml the window stays as the project opens it and nothing is written.
    /// </summary>
    public static void ApplyAtStartup()
    {
        if (Headless || Root == null) return;
        if (GameOptions.FromDefault)
        {
            GD.Print("display: no stored GameOption.xml, the window stays as it opens");
            return;
        }
        var g = GameOptions.Current.GraphicOption;
        var desk = Desktop();
        var req = Modes.Startup(g, GameOptions.FromDefault, desk.Width, desk.Height, out bool save);
        GD.Print($"display: {Modes.Modes.Count} modes ({Modes.Modes.Count(ScreenModes.Usable)} usable), desktop {desk.Width}x{desk.Height}, start {(req.Borderless ? (req.Maximize ? "full window" : "full screen") : "window")} {req.Width}x{req.Height}");
        Apply(req);
        if (save) GameOptions.Save();
    }

    /// <summary>The options dialog's switch (FUN_14125e910): false when neither the row nor 1024 x 768 can be used.</summary>
    public static bool Switch(int screenMode, int row)
    {
        if (Headless || Root == null) return false;
        var req = Modes.Switch(GameOptions.Current.GraphicOption, screenMode, row, out bool save);
        if (req == null) return false;
        Apply(req.Value);
        if (save) GameOptions.Save();
        return true;
    }

    /// <summary>The options dialog's window request (FUN_14124ce20 after FUN_14125e910).</summary>
    public static void ApplyRequest(WindowRequest r)
    {
        if (Headless || Root == null) return;
        Apply(r);
    }

    /// <summary>ApplyWindowMode (windowservice.cpp:291-375) for a request.</summary>
    static void Apply(WindowRequest r)
    {
        var w = Root;
        if (r.Borderless)
        {
            if (OperatingSystem.IsWindows())
            {
                // SetDisplayMode, then the popup at 0,0 of w x h, topmost unless full window (windowservice.cpp:293-334)
                DisplayNative.SetDisplayMode(r.Width, r.Height);
                Emulated = null;
                w.Mode = Window.ModeEnum.Windowed;
                w.Borderless = true;
                w.AlwaysOnTop = !r.Maximize;
                w.Size = new Vector2I(r.Width, r.Height);
                w.Position = DisplayServer.ScreenGetPosition(Screen);
            }
            else
            {
                // Linux deviation (see the class summary): the popup covers the screen at its own size, the content is rendered
                // at w x h. A plain borderless window rather than the window manager's full-screen state, which needs a window
                // manager to take effect.
                Emulated = new Vector2I(r.Width, r.Height);
                w.Mode = Window.ModeEnum.Windowed;
                w.Borderless = true;
                w.AlwaysOnTop = !r.Maximize;
                w.Size = DisplayServer.ScreenGetSize(Screen);
                w.Position = DisplayServer.ScreenGetPosition(Screen);
            }
        }
        else
        {
            // RestoreDisplayMode, then a framed window of w x h clamped to the screen and centred (windowservice.cpp:337-374)
            DisplayNative.RestoreDisplayMode();
            Emulated = null;
            w.Mode = Window.ModeEnum.Windowed;
            w.Borderless = false;
            w.AlwaysOnTop = false;
            var screen = DisplayServer.ScreenGetSize(Screen);
            var first = ScreenModes.WindowedPlacement(r.Width, r.Height, screen.X, screen.Y, 0, 0);
            w.Size = new Vector2I(first.Width, first.Height);
            var frame = w.GetSizeWithDecorations() - w.Size;
            var place = ScreenModes.WindowedPlacement(r.Width, r.Height, screen.X, screen.Y, frame.X, frame.Y);
            var inset = DisplayServer.WindowGetPosition() - DisplayServer.WindowGetPositionWithDecorations();
            w.Position = DisplayServer.ScreenGetPosition(Screen) + new Vector2I(place.OuterX, place.OuterY) + inset;
        }
        foreach (var s in pixelScopes.Keys) SetPixels(s);
        GD.Print($"display: window {w.Mode} borderless={w.Borderless} at {w.Position} size {w.Size}{(Emulated is { } e ? $", content {e.X}x{e.Y}" : "")}");
    }

    /// <summary>
    /// Scenes that lay out in window pixels (the lobby, the HUD) call this instead of switching the content scale off: off
    /// normally, or the emulated w x h on Linux in the full window. Returns the restore action for when the scene leaves
    /// (it runs once). Scopes nest: the content scale the window had before the first one comes back when the last one
    /// ends, so a scene change between two such scenes, or one under the loading screen (which holds its own scope,
    /// <see cref="Ui.LoadingMovie"/>), never turns the stretch on in between.
    /// </summary>
    public static Action UseWindowPixels(Window w)
    {
        if (pixelScopes.TryGetValue(w, out var s)) pixelScopes[w] = s with { Scopes = s.Scopes + 1 };
        else pixelScopes[w] = (1, w.ContentScaleMode, w.ContentScaleSize, w.ContentScaleAspect);
        SetPixels(w);
        bool ended = false;
        return () =>
        {
            if (ended || !pixelScopes.TryGetValue(w, out var open)) return;
            ended = true;
            if (open.Scopes > 1) { pixelScopes[w] = open with { Scopes = open.Scopes - 1 }; return; }
            pixelScopes.Remove(w);
            if (!GodotObject.IsInstanceValid(w)) return;
            w.ContentScaleSize = open.Size;
            w.ContentScaleAspect = open.Aspect;
            w.ContentScaleMode = open.Mode;
        };
    }

    static void SetPixels(Window w)
    {
        if (Emulated is { } e)
        {
            w.ContentScaleSize = e;
            w.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
            w.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
        }
        else w.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
    }
}
