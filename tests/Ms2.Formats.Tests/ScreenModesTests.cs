using Ms2.Formats.Options;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

public class ScreenModesTests
{
    static ScreenModes List() => new(new[]
    {
        new DisplayMode(800, 600, 32), new DisplayMode(1024, 768, 16), new DisplayMode(1024, 768, 32), new DisplayMode(1024, 768, 32),
        new DisplayMode(1280, 720, 32), new DisplayMode(1280, 700, 32), new DisplayMode(1920, 1080, 32),
    });

    [Test]
    public void UniqueRowsAndTheUsableFilter()
    {
        var m = List();
        Assert.That(m.Modes.Count, Is.EqualTo(6), "the duplicate (refresh rate) row merges");
        Assert.That(m.IsAvailable(1024, 768), Is.True);
        Assert.That(m.IsAvailable(800, 600), Is.False, "under 1024 wide");
        Assert.That(m.IsAvailable(1280, 700), Is.False, "under 720 high");
        Assert.That(ScreenModes.Usable(new DisplayMode(1024, 768, 16)), Is.False, "16 bpp");
        var g = new GameOption.Graphic();
        Assert.That(m.PickRow(g, 1280, 720), Is.True);
        Assert.That(g.ResolutionIndex, Is.EqualTo(3));
        Assert.That(m.PickRow(g, 800, 600), Is.False);
        Assert.That((g.ResolutionIndex, g.Width, g.Height), Is.EqualTo((3, 1280, 720)), "no usable row leaves the option");
    }

    [Test]
    public void FreshStartIsAFullWindowAtTheDesktopSize()
    {
        var g = new GameOption.Graphic { ScreenMode = 1, ResolutionIndex = 4, Width = 1024, Height = 768 };
        var r = List().Startup(g, fresh: true, 1920, 1080, out bool save);
        Assert.That(r, Is.EqualTo(new WindowRequest(true, true, 1920, 1080)));
        Assert.That(save, Is.True);
        Assert.That((g.ScreenMode, g.ResolutionIndex, g.Width, g.Height), Is.EqualTo((2, 5, 1920, 1080)));
    }

    [Test]
    public void StoredModeAndTheFallback()
    {
        var m = List();
        var g = new GameOption.Graphic { ScreenMode = 1, Width = 1280, Height = 720 };
        Assert.That(m.Startup(g, false, 1920, 1080, out bool save), Is.EqualTo(new WindowRequest(false, false, 1280, 720)));
        Assert.That(save, Is.False);
        g.Width = 1600; g.Height = 900;
        Assert.That(m.Startup(g, false, 1920, 1080, out _), Is.EqualTo(new WindowRequest(false, false, 1024, 768)), "not on the adapter");
        g.ScreenMode = 0;
        Assert.That(m.Startup(g, false, 1920, 1080, out _).Borderless, Is.True);
    }

    [Test]
    public void SwitchStoresTheRowSizeOrFallsBack()
    {
        var m = List();
        var g = new GameOption.Graphic();
        Assert.That(m.Switch(g, 2, 3, out bool save), Is.EqualTo(new WindowRequest(false, true, 1280, 720)));
        Assert.That((save, g.Width, g.Height), Is.EqualTo((true, 1280, 720)));
        Assert.That(m.Switch(g, 1, 0, out save), Is.EqualTo(new WindowRequest(false, false, 1024, 768)), "800x600 row: 1024x768");
        Assert.That((save, g.Width, g.Height), Is.EqualTo((false, 1280, 720)), "the fallback is not stored");
        Assert.That(new ScreenModes(new[] { new DisplayMode(800, 600, 32) }).Switch(g, 1, 0, out _), Is.Null);
    }

    [Test]
    public void WindowedPlacementClampsAndCentres()
    {
        Assert.That(ScreenModes.WindowedPlacement(1280, 720, 1920, 1080, 16, 39), Is.EqualTo((1280, 720, 312, 160)));
        Assert.That(ScreenModes.WindowedPlacement(1920, 1080, 1280, 720, 16, 39), Is.EqualTo((1280, 720, 0, 0)));
    }

    /// <summary>A framed window fits the work area with its frame (GitHub issue #10: a client size of the whole screen with a
    /// title bar reached past the screen's edge and ran at about 2 frames a second): the client is clamped to the work area
    /// less the frame, and a window that already fits is left alone.</summary>
    [Test]
    public void AFramedWindowFitsTheWorkAreaWithItsFrame()
    {
        Assert.That(ScreenModes.FitFramed(1920, 1080, 1920, 1080, 0, 28), Is.EqualTo((1920, 1052)));
        Assert.That(ScreenModes.FitFramed(1920, 1080, 1920, 1036, 2, 28), Is.EqualTo((1918, 1008)), "a panel takes part of the screen");
        Assert.That(ScreenModes.FitFramed(1280, 720, 1920, 1080, 0, 28), Is.EqualTo((1280, 720)));
        Assert.That(ScreenModes.FitFramed(1920, 1052, 1920, 1080, 0, 28), Is.EqualTo((1920, 1052)));
        Assert.That(ScreenModes.FitFramed(2560, 1440, 1920, 1080, 0, 28), Is.EqualTo((1920, 1052)));
        Assert.That(ScreenModes.FitFramed(800, 600, 20, 20, 40, 40), Is.EqualTo((1, 1)), "never below 1 x 1");
    }

    /// <summary>Every screen mode the options offer (full screen, window, full window) at a size equal to, lower than and higher
    /// than the screen: the full modes are the window manager's full-screen state at the screen's size (the panel hidden, no
    /// gap) with the content at the requested size; the window is framed, not topmost and clamped to the screen. A plan sets
    /// every field and depends on its request alone, so a window gets the same state from a window, from a full screen and
    /// going back.</summary>
    [Test]
    public void EveryScreenModeAndSizeMakesTheWindowTheScreenOrAFramedWindow()
    {
        const int sw = 1920, sh = 1080;
        var sizes = new[] { (1920, 1080), (1280, 720), (1024, 768), (2560, 1440), (3840, 2160) };
        var all = new List<WindowRequest>();
        foreach (var (w, h) in sizes)
            foreach (var (full, max) in new[] { (true, false), (false, false), (false, true) })
                all.Add(new WindowRequest(full, max, w, h));
        foreach (var r in all)
        {
            var p = ScreenModes.Plan(r, sw, sh);
            string what = $"mode {ScreenModes.ModeOf(r)} {r.Width}x{r.Height}";
            if (ScreenModes.ModeOf(r) == 1)
            {
                Assert.That((p.WmFullscreen, p.Borderless, p.TopMost), Is.EqualTo((false, false, false)), what);
                Assert.That((p.Width, p.Height), Is.EqualTo((Math.Min(r.Width, sw), Math.Min(r.Height, sh))), what + ": clamped to the screen");
                Assert.That((p.ContentWidth, p.ContentHeight), Is.EqualTo((0, 0)), what + ": drawn at the window's size");
            }
            else
            {
                Assert.That((p.WmFullscreen, p.Borderless), Is.EqualTo((true, true)), what + ": no panel, no frame");
                Assert.That((p.Width, p.Height), Is.EqualTo((sw, sh)), what + ": the window is the screen");
                Assert.That((p.ContentWidth, p.ContentHeight), Is.EqualTo((r.Width, r.Height)), what + ": content at the asked size");
            }
            // from any earlier request and back: the plan is the request's alone
            foreach (var before in all)
            {
                ScreenModes.Plan(before, sw, sh);
                Assert.That(ScreenModes.Plan(r, sw, sh), Is.EqualTo(p), what);
            }
        }
    }
}
