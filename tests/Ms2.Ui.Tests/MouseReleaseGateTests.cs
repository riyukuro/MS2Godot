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
    public void AReleaseUnderTheFlagAfterTheMoveEndedDoesNotWalk()
    {
        var g = new MouseReleaseGate();
        g.Press(false);
        Assert.That(g.Release(false), Is.True);
        Assert.That(g.Release(true, moving: false), Is.False, "the move ended (ResetToIdle, mode 1) with the flag set: returns");
    }

    [Test]
    public void AClickOnADialogWhileTheMoveRunsWalksOnItsRelease()
    {
        var g = new MouseReleaseGate();
        g.Press(true);
        Assert.That(g.Release(true, moving: true), Is.True, "FUN_140ad23f0 left mode 2; the flag set, mode != 1: FUN_140ad23f0 runs");
        g.Press(true);
        Assert.That(g.Release(false, moving: true), Is.False, "the flag clear: m_nIgnoreRelease returns, whatever the mode");
    }

    [Test]
    public void TheCursorLeavingTheWindowEndsATakenPress()
    {
        var g = new MouseReleaseGate();
        g.Press(false);
        g.Reset();   // WM_MOUSELEAVE: ResetToIdle, mode 1
        Assert.That(g.Release(true), Is.False, "mode 1 with the flag set: returns");
    }

    [Test]
    public void TheCursorLeavingTheWindowKeepsTheIgnoredRelease()
    {
        var g = new MouseReleaseGate();
        g.Press(true);
        g.Reset();
        Assert.That(g.Release(false), Is.False, "m_nIgnoreRelease stays set");
    }

    [Test]
    public void AfterTheCursorLeftAReleaseOnTheGroundStillWalks()
    {
        var g = new MouseReleaseGate();
        g.Press(false);
        g.Reset();   // WM_MOUSELEAVE, then the cursor comes back in
        Assert.That(g.Release(false), Is.True, "the flag clear: only m_nIgnoreRelease and IsCursorInWindow are read, not the mode");
    }

    [Test]
    public void AReleaseOutOfTheWindowDoesNotWalk()
    {
        var g = new MouseReleaseGate();
        g.Press(false);
        Assert.That(g.Release(false, inWindow: false), Is.False, "IsCursorInWindow == 0: returns");
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
