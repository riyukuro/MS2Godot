namespace Ms2.Formats.Options;

/// <summary>One display mode of the adapter: NiDX9AdapterDesc::ModeDesc's width, height and bits per pixel
/// (NiDX9SystemDesc.h:153-162), read by the client as Unk_GoRawMode (gameoptionstruct.cpp:1842-1847).</summary>
public readonly record struct DisplayMode(int Width, int Height, int Bpp);

/// <summary>What the window service is asked to do: CWindowService::RecreateRenderer(bFullscreen, w, h) with
/// m_bMaximize set first (FUN_1412642e0, gameoptionstruct.cpp:4049). The window goes borderless at 0,0 with the display
/// switched to w x h when <see cref="Fullscreen"/> or <see cref="Maximize"/> is set, else it is a fixed window of w x h
/// (RecreateRenderer windowservice.cpp:257-288, ApplyWindowMode :291-375).</summary>
public readonly record struct WindowRequest(bool Fullscreen, bool Maximize, int Width, int Height)
{
    /// <summary>RecreateRenderer: a full window takes the full-screen branch of ApplyWindowMode (windowservice.cpp:284-286).</summary>
    public bool Borderless => Fullscreen || Maximize;
}

/// <summary>
/// The adapter's display mode list as the client uses it. The rows are the adapter's modes, unique by (format, width,
/// height) in enumeration order, refresh rates merged (NiDX9AdapterDesc ctor, NiDX9SystemDesc.cpp:666-713); the stored
/// "resolution" option is a row of this list. Only 32-bit modes of at least 1024 x 720 are used (FUN_14125e7e0,
/// gameoptionstruct.cpp:2181-2187).
/// </summary>
public sealed class ScreenModes
{
    public readonly IReadOnlyList<DisplayMode> Modes;

    public ScreenModes(IEnumerable<DisplayMode> enumerated)
    {
        var list = new List<DisplayMode>();
        foreach (var m in enumerated) if (!list.Contains(m)) list.Add(m);
        Modes = list;
    }

    /// <summary>FUN_14125e7e0 (gameoptionstruct.cpp:2181-2187).</summary>
    public static bool Usable(DisplayMode m) => m.Bpp == 0x20 && m.Width >= 0x400 && m.Height >= 0x2d0;

    /// <summary>IsResolutionAvailable (gameoptionstruct.cpp:2191-2216).</summary>
    public bool IsAvailable(int width, int height) => Modes.Any(m => Usable(m) && m.Width == width && m.Height == height);

    /// <summary>FUN_14125eb00 (gameoptionstruct.cpp:2265-2291): the last usable row of that size; when there is one the
    /// option's row and size are set, else nothing changes.</summary>
    public bool PickRow(GameOption.Graphic g, int width, int height)
    {
        int found = -1;
        for (int i = 0; i < Modes.Count; i++)
            if (Modes[i].Width == width && Modes[i].Height == height && Usable(Modes[i])) found = i;
        if (found < 0) return false;
        g.ResolutionIndex = found;
        g.Width = width;
        g.Height = height;
        return true;
    }

    /// <summary>
    /// FUN_14125e910 (gameoptionstruct.cpp:2223-2262), the graphics page's screen mode / resolution switch: a usable row
    /// switches to its size and stores it (the caller then saves, SaveGameSettings); otherwise 1024 x 768 when the adapter
    /// has it, not stored. Null when neither applies. The screen mode and row themselves are stored by the dialog
    /// (CGameOptionGraphicDefault::FUN_14124ce20, gameoption.cpp:1310-1320).
    /// </summary>
    public WindowRequest? Switch(GameOption.Graphic g, int screenMode, int row, out bool save)
    {
        save = false;
        bool fullscreen = screenMode == 0, fullWindow = screenMode == 2;
        if (row >= 0 && row < Modes.Count && Usable(Modes[row]))
        {
            g.Width = Modes[row].Width;
            g.Height = Modes[row].Height;
            save = true;
            return new WindowRequest(fullscreen, fullWindow, g.Width, g.Height);
        }
        return IsAvailable(0x400, 0x300) ? new WindowRequest(fullscreen, fullWindow, 0x400, 0x300) : null;
    }

    /// <summary>
    /// MS2RenderService::OnInit (ms2renderservice.cpp:699-743). A fresh or outdated option file (<paramref name="fresh"/>,
    /// IsOptionFileMissingOrOutdated) starts as a full window at the desktop size; otherwise the stored screen mode and size
    /// (the accessors named GetHeight / GetWidth there return the width and the height, ms2renderservice.cpp:140-141).
    /// A size the adapter does not have falls back to 1024 x 768 (:727-737). A fresh start then stores full window and the
    /// row of the size and saves (:738-742). The fresh start also sets the quality from the benchmark (:708-711), which is
    /// not ported here.
    /// </summary>
    public WindowRequest Startup(GameOption.Graphic g, bool fresh, int desktopWidth, int desktopHeight, out bool save)
    {
        bool fullscreen = !fresh && g.ScreenMode == 0;
        bool fullWindow = fresh || g.ScreenMode == 2;
        int width = fresh ? desktopWidth : g.Width, height = fresh ? desktopHeight : g.Height;
        if (fullWindow) fullscreen = true;
        if (!IsAvailable(width, height)) { width = 0x400; height = 0x300; }
        save = fresh;
        if (fresh)
        {
            g.ScreenMode = 2;
            PickRow(g, width, height);
        }
        return new WindowRequest(fullscreen, fullWindow, width, height);
    }

    /// <summary>
    /// The client area of a fixed window of w x h (ApplyWindowMode's windowed branch, windowservice.cpp:337-374): the size
    /// is clamped to the screen, and the outer rectangle (client plus the 0xca0000 frame, <paramref name="frame"/>) is
    /// centred on the screen, rounded down, not above or left of 0,0. Returns the client size and the outer position.
    /// </summary>
    public static (int Width, int Height, int OuterX, int OuterY) WindowedPlacement(int width, int height, int screenWidth, int screenHeight, int frameWidth, int frameHeight)
    {
        if (screenWidth < width) width = screenWidth;
        if (screenHeight < height) height = screenHeight;
        int outerW = width + frameWidth, outerH = height + frameHeight;
        int x = (int)Math.Max(0f, MathF.Floor((screenWidth - outerW) * 0.5f));
        int y = (int)Math.Max(0f, MathF.Floor((screenHeight - outerH) * 0.5f));
        return (width, height, x, y);
    }
}
