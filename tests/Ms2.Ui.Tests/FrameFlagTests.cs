using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The collider change flag (<see cref="FrameFlag"/>): every PC that moves in a frame reads the same value, as
/// FUN_141bc9a80 gives every PCBase of one world update; the old read-and-clear gave it to the first reader only.</summary>
public class FrameFlagTests
{
    [Test]
    public void EveryReaderOfAFrameSeesTheChangeAndTheNextFrameDoesNot()
    {
        var f = new FrameFlag();
        f.Set();
        Assert.That(f.Read(10), Is.True, "the own player");
        Assert.That(f.Read(10), Is.True, "a remote PC after it");
        Assert.That(f.Read(10), Is.True, "another remote PC");
        Assert.That(f.Read(11), Is.False, "the next frame");
    }

    [Test]
    public void ASetAfterTheFramesFirstReadCountsForTheNextFrame()
    {
        var f = new FrameFlag();
        Assert.That(f.Read(1), Is.False);
        f.Set();
        Assert.That(f.Read(1), Is.False, "this frame's moves already read it");
        Assert.That(f.Read(2), Is.True);
        Assert.That(f.Read(2), Is.True);
        Assert.That(f.Read(3), Is.False);
    }

    [Test]
    public void AFrameWithNoReaderKeepsTheChangeForTheNextRead()
    {
        var f = new FrameFlag();
        f.Set();
        Assert.That(f.Read(7), Is.True, "frames 5 and 6 had no move");
    }
}
