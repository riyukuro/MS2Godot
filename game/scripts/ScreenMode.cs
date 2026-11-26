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
/// window in the window manager's full-screen state (no panel, no gap) whose content is rendered at w x h and scaled to the
/// screen with its aspect kept (<see cref="Emulated"/>), which looks like the client's mode switch without touching the
/// desktop (<see cref="ScreenModes.Plan"/>).
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
    /// OnInit's window: the stored mode and size. A missing or outdated option file (IsOptionFileMissingOrOutdated,
    /// ms2renderservice.cpp:699-703) starts as a full window at the desktop size (GetSystemMetrics SM_CXSCREEN/SM_CYSCREEN),
    /// stores full window and the desktop's row, and saves (:746-750), so a first run fills the screen at its own
    /// resolution and the options dialog's resolution row shows it (<see cref="ScreenModes.Startup"/>).
    /// </summary>
    public static void ApplyAtStartup()
    {
        if (Headless || Root == null) return;
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

    /// <summary>The options dialog's window request (FUN_14124ce20 after FUN_14125e910), or a scripted one; the option
    /// takes the request (<see cref="ScreenModes.Adopt"/>), as the dialog's store does after its request: the row's size,
    /// even when a window larger than the screen is clamped by ApplyWindowMode (windowservice.cpp:337-374;
    /// FUN_14125e910 stores the mode's width and height, gameoptionstruct.cpp:2239-2248).</summary>
    public static void ApplyRequest(WindowRequest r)
    {
        if (Headless || Root == null) return;
        Apply(r);
        Modes.Adopt(GameOptions.Current.GraphicOption, r);
    }

    /// <summary>The last request the window was made from (<see cref="Apply"/>); null while it is as the engine opened it.</summary>
    public static WindowRequest? Current { get; private set; }

    /// <summary>
    /// A scene started without the game's startup (no <see cref="ApplyAtStartup"/>: the window is the engine's, sized by
    /// the command line) takes that window into the graphic option, so the options dialog shows the window's mode and
    /// size (<see cref="ScreenModes.Adopt"/>). A window made from the option is left alone.
    /// </summary>
    public static void AdoptWindow()
    {
        if (Headless || Root == null || Current != null) return;
        var g = GameOptions.Current.GraphicOption;
        StoredWindow ??= (g.ScreenMode, g.ResolutionIndex, g.Width, g.Height);
        var w = Root;
        var size = PixelSize(w);
        Modes.Adopt(GameOptions.Current.GraphicOption, new WindowRequest(w.Borderless && w.AlwaysOnTop, w.Borderless && !w.AlwaysOnTop, size.X, size.Y));
    }

    /// <summary>
    /// The window part of the graphic option as it was read, kept when a scene started on its own (a test run with
    /// --resolution) took its window into the option (<see cref="AdoptWindow"/>); null in the game, whose window is made
    /// from the option at startup. <see cref="GameOptions.Save"/> writes these instead, so such a run never stores its
    /// window in the user's file. In the client the file's window is only ever one the option made (MS2RenderService::
    /// OnInit, ms2renderservice.cpp:699-743, or the options dialog), which the game keeps.
    /// </summary>
    public static (int Mode, int Row, int Width, int Height)? StoredWindow { get; private set; }

    /// <summary>ApplyWindowMode (windowservice.cpp:291-375) for a request.</summary>
    static void Apply(WindowRequest r)
    {
        var w = Root;
        Current = r;
        keepRequest++;
        if (Engine.IsEmbeddedInEditor())
        {
            // Not the client's: the editor's Game tab runs the project inside the editor's window, where the engine does not
            // support changing the window's mode, flags, size or position (Engine.IsEmbeddedInEditor); doing it splits the
            // game out into a second window and leaves the tab empty. The window stays the editor's; the option still
            // records the request, so a run outside the editor opens as the option says.
            Emulated = null;
        }
        else if (r.Borderless)
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
                // Linux deviation (see the class summary): the window manager's full-screen state at the screen's size, the content
                // rendered at w x h (ScreenModes.Plan). A plain borderless window is clamped to the work area by the window
                // manager: the panel stays and the content shows bars.
                var screen = DisplayServer.ScreenGetSize(Screen);
                var plan = ScreenModes.Plan(r, screen.X, screen.Y);
                Emulated = new Vector2I(plan.ContentWidth, plan.ContentHeight);
                w.AlwaysOnTop = plan.TopMost;
                w.Borderless = plan.Borderless;
                w.Mode = plan.WmFullscreen ? Window.ModeEnum.Fullscreen : Window.ModeEnum.Windowed;
            }
        }
        else
        {
            // RestoreDisplayMode, then a framed window of w x h clamped to the screen and centred (windowservice.cpp:337-374)
            DisplayNative.RestoreDisplayMode();
            Emulated = null;
            bool wasFullscreen = w.Mode == Window.ModeEnum.Fullscreen;
            w.Mode = Window.ModeEnum.Windowed;
            w.Borderless = false;
            w.AlwaysOnTop = false;
            PlaceWindowed(w, r);
            // Leaving the window manager's full-screen state, the window manager gives the window back its geometry from before
            // (on Wayland a moment later), over the size and position just set: set them again once it has done so.
            if (wasFullscreen) KeepWindowed(w, r);
        }
        foreach (var s in pixelScopes.Keys) SetPixels(s);
        GD.Print($"display: window {w.Mode} borderless={w.Borderless} at {w.Position} size {w.Size}{(Emulated is { } e ? $", content {e.X}x{e.Y}" : "")}");
    }

    /// <summary>The client size of the framed window of a request and the frame the window has now: the size clamped to the
    /// screen (windowservice.cpp:337-374) and, on Linux, to the work area less the frame, as the window manager makes a new
    /// window (<see cref="ScreenModes.FitFramed"/>); the frame is zero until the window manager has told the window its frame.</summary>
    static (Vector2I Size, Vector2I Frame, Rect2I Area) FramedSize(Window w, WindowRequest r)
    {
        var screen = DisplayServer.ScreenGetSize(Screen);
        var first = ScreenModes.WindowedPlacement(r.Width, r.Height, screen.X, screen.Y, 0, 0);
        var frame = w.GetSizeWithDecorations() - w.Size;
        var size = new Vector2I(first.Width, first.Height);
        var area = new Rect2I(DisplayServer.ScreenGetPosition(Screen), screen);
        if (!OperatingSystem.IsWindows())
        {
            var work = DisplayServer.ScreenGetUsableRect(Screen);
            var fit = ScreenModes.FitFramed(first.Width, first.Height, work.Size.X, work.Size.Y, frame.X, frame.Y);
            if (fit.Width != size.X || fit.Height != size.Y) { size = new Vector2I(fit.Width, fit.Height); area = work; }
        }
        return (size, frame, area);
    }

    /// <summary>The framed window of a request: its client size clamped to the screen, centred (windowservice.cpp:337-374). On
    /// Linux the framed window also has to fit the work area (<see cref="FramedSize"/>): leaving the full-screen state the
    /// window manager keeps the size set here, and a window whose frame reaches past the screen's edge runs at about 2 frames
    /// a second (GitHub issue #10). Returns the client size.</summary>
    static Vector2I PlaceWindowed(Window w, WindowRequest r)
    {
        var screen = DisplayServer.ScreenGetSize(Screen);
        var first = ScreenModes.WindowedPlacement(r.Width, r.Height, screen.X, screen.Y, 0, 0);
        w.Size = new Vector2I(first.Width, first.Height);
        var (size, frame, area) = FramedSize(w, r);
        w.Size = size;
        var place = ScreenModes.WindowedPlacement(size.X, size.Y, area.Size.X, area.Size.Y, frame.X, frame.Y);
        var inset = DisplayServer.WindowGetPosition() - DisplayServer.WindowGetPositionWithDecorations();
        w.Position = area.Position + new Vector2I(place.OuterX, place.OuterY) + inset;
        return size;
    }

    static int keepRequest;

    /// <summary>Places the framed window of <paramref name="r"/> again on the next frames until the window manager has settled
    /// on it: the window has the size placed for the frame it has now, for 10 frames in a row, and the window manager has
    /// told it its frame (the frame, so the size that fits the work area, is only known a few frames after the window left
    /// the full-screen state); a newer request ends this one.</summary>
    static void KeepWindowed(Window w, WindowRequest r)
    {
        var tree = w.GetTree();
        int mine = ++keepRequest, frames = 0, same = 0;
        Action tick = null;
        tick = () =>
        {
            bool done = mine != keepRequest || !GodotObject.IsInstanceValid(w) || ++frames > 120;
            if (!done && w.Mode == Window.ModeEnum.Windowed && !w.Borderless)
            {
                var (size, frame, _) = FramedSize(w, r);
                if (DisplayServer.WindowGetSize() != size) { PlaceWindowed(w, r); same = 0; }
                else if (frame != Vector2I.Zero && ++same >= 10) done = true;
            }
            if (done) tree.ProcessFrame -= tick;
        };
        tree.ProcessFrame += tick;
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

    /// <summary>
    /// The size a scene in window pixels lays out at (<see cref="UseWindowPixels"/>): the emulated w x h in the Linux full
    /// window, the window's own size otherwise. The client sizes every movie to the back buffer (GFxMovie::vfunc_7,
    /// gfxmovie.cpp:146), which a full window's SetDisplayMode makes w x h (windowservice.cpp:293-334); the window's OS
    /// size there is the screen's, so a movie laid out at it is wider than what is drawn and cut off at the right.
    /// </summary>
    public static Vector2I PixelSize(Window w) => w == Root && Emulated is { } e ? e : w.Size;

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
