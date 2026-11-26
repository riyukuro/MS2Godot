using Ms2.Avm2.Flash;
using Ms2.Avm2;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UIPerformanceDialog (0x4e7d, uipartydialog.cpp:1425-1475) and CMainSystem's frame rate (mainsystem.cpp:540-561).</summary>
public partial class HudTests
{
    [Test]
    public void TheFrameRateIsTheFramesOverMoreThanASecond()
    {
        var f = new FrameRateMeter();
        f.Tick(10.0);                                    // the first tick only starts the count
        Assert.That(f.Fps, Is.EqualTo(0f));
        int frames = 0;
        for (double t = 10.0 + 1 / 50.0; frames < 50; t += 1 / 50.0, frames++) f.Tick(t);
        Assert.That(f.Fps, Is.EqualTo(0f), "a second is not more than a second");
        f.Tick(10.0 + 51 / 50.0);                        // 51 frames over 1.02 s
        Assert.That(f.Fps, Is.EqualTo(50f).Within(0.01f));
        for (int i = 1; i <= 20; i++) f.Tick(10.0 + 51 / 50.0 + i * 0.1);   // 10 fps from here: the old rate holds a second
        Assert.That(f.Fps, Is.EqualTo(10f).Within(0.01f));
    }

    /// <summary>The movie's m_text gets "{0:0.1f}({1})": the rate with one decimal and the time sync average.</summary>
    [Test]
    public void ThePerformanceDialogShowsTheRateAndTheTimeSyncRoundTrip()
    {
        var c = new PerformanceDialogController();
        var h = Load(c, Model());
        var t = h.FindNamed("m_text") as AsTextField;
        Assert.That(t, Is.Not.Null);
        Assert.That(c.IsClosed, Is.False);
        Assert.That(c.AlwaysOn, Is.True);
        TestContext.WriteLine($"placed at {Rt.ToNumber(h.Get(h.Entity, "x"))},{Rt.ToNumber(h.Get(h.Entity, "y"))}, text '{t.Text}'");
        c.Update(59.96f, 23);
        Assert.That(t.Text, Is.EqualTo("60.0(23)"));
        c.Update(12.34f, 0);
        Assert.That(t.Text, Is.EqualTo("12.3(0)"));
        Assert.That(PerformanceDialogController.Line(144.04f, 7), Is.EqualTo("144.0(7)"));
        c.Close();
        Assert.That(c.IsClosed, Is.True, "no tween: the close unloads at once");
    }

    /// <summary>The frame update writes the line only when the rate or the round trip changed: a steady frame allocates nothing.</summary>
    [Test]
    public void ASteadyFrameOfThePerformanceDialogAllocatesNothing()
    {
        var c = new PerformanceDialogController();
        Load(c, Model());
        var f = new FrameRateMeter();
        double now = 100.0;
        for (int i = 0; i < 125; i++) { f.Tick(now += 1 / 60.0); c.Update(f.Fps, 31); }   // new rates near frames 61 and 122
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++) { f.Tick(now += 1 / 60.0); c.Update(f.Fps, 31); }   // frames 125-175: the next rate comes near 183
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.WriteLine($"50 steady frames: {bytes} B");
        Assert.That(bytes, Is.EqualTo(0));
    }
}
