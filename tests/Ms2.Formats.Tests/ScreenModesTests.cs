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
}
