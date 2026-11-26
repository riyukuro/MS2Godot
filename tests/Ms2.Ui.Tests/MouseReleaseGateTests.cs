using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CMouseController::OnMouseMessage's m_nIgnoreRelease and mode (@0x140ad1da0, mousecontroller.cpp:327-361).</summary>
public class MouseReleaseGateTests
{
    [Test]
    public void APressInTheFieldWalksOnItsRelease()
    {
        var g = new MouseReleaseGate();
        g.Press(false);
        Assert.That(g.Release(false), Is.True);
    }

    [Test]
    public void APressOnADialogThatEndsOnTheGroundDoesNotWalk()
    {
        var g = new MouseReleaseGate();
        g.Press(true);
        Assert.That(g.Release(false), Is.False, "m_nIgnoreRelease: the press was over the UI");
        Assert.That(g.Release(true), Is.False);
    }

    [Test]
    public void APressInTheFieldThatEndsOverADialogStillWalks()
    {
        var g = new MouseReleaseGate();
        g.Press(false);
        Assert.That(g.Release(true), Is.True, "the flag set at the release, mode != 1: FUN_140ad23f0 runs");
    }

    [Test]
    public void AReleaseAfterAReleaseAndWithNoPressUnderTheFlagDoesNotWalk()
    {
        var g = new MouseReleaseGate();
        g.Press(false);
        Assert.That(g.Release(false), Is.True);
        Assert.That(g.Release(true), Is.False, "mode back to 1 with the flag set: returns");
    }

    [Test]
    public void ThePressIsRememberedUntilTheNextPress()
    {
        var g = new MouseReleaseGate();
        g.Press(true);
        g.Press(false);
        Assert.That(g.Release(false), Is.True);
    }
}
