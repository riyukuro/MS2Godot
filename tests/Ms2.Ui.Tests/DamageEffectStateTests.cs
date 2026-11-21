using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The low-HP screen effect's state: UIDamageEffect's HP decision and the movie's AS3 DamageEffect
/// (changeHP, onShow, onHide, setAlpha).</summary>
public class DamageEffectStateTests
{
    static DamageEffectState Shown(int alphaOption)
    {
        var s = new DamageEffectState();
        s.Init(30, alphaOption);
        s.Hp(10, 100);
        s.Advance(DamageEffectState.TweenMs);
        return s;
    }

    [Test]
    public void TheShowTweenEndsAtTheAlphaOption()
    {
        var s = Shown(40);
        Assert.That(s.Visible, Is.True);
        Assert.That(s.Tweening, Is.False);
        Assert.That(s.Alpha, Is.EqualTo(0.4).Within(1e-9), "init's second argument x 0.01");
    }

    [Test]
    public void SetAlphaWhileShownAndIdleChangesTheAlphaNow()
    {
        var s = Shown(100);
        s.SetAlpha(25);
        Assert.That(s.Alpha, Is.EqualTo(0.25).Within(1e-9));
        Assert.That(s.MaxAlpha, Is.EqualTo(0.25).Within(1e-9));
    }

    [Test]
    public void SetAlphaDuringTheShowTweenKeepsItsTarget()
    {
        var s = new DamageEffectState();
        s.Init(30, 100);
        s.Hp(10, 100);
        s.Advance(50);
        s.SetAlpha(20);
        Assert.That(s.Alpha, Is.EqualTo(0.5).Within(1e-9), "the tween's alpha, not the new maximum");
        s.Advance(50);
        Assert.That(s.Alpha, Is.EqualTo(1).Within(1e-9), "the tween keeps the target it was made with");
        Assert.That(s.MaxAlpha, Is.EqualTo(0.2).Within(1e-9));
    }

    [Test]
    public void SetAlphaWhileHiddenStaysHidden()
    {
        var s = new DamageEffectState();
        s.Init(30, 100);
        s.SetAlpha(60);
        Assert.That(s.Visible, Is.False);
        s.Hp(30, 100);                          // at the threshold is low
        Assert.That(s.Visible, Is.True);
        Assert.That(s.Alpha, Is.EqualTo(0), "changeHP starts at alpha 0");
        s.Advance(DamageEffectState.TweenMs);
        Assert.That(s.Alpha, Is.EqualTo(0.6).Within(1e-9));
    }

    [Test]
    public void TheHideRunsFromTheCurrentAlphaAndEndsHidden()
    {
        var s = new DamageEffectState();
        s.Init(30, 100);
        s.Hp(10, 100);
        s.Advance(30);
        s.Hp(90, 100);
        Assert.That(s.Alpha, Is.EqualTo(0.3).Within(1e-9));
        s.Advance(50);
        Assert.That(s.Alpha, Is.EqualTo(0.15).Within(1e-9));
        Assert.That(s.Visible, Is.True, "visible until the hide ends");
        s.Hp(10, 100);                          // low again while the hide runs: visible already, so nothing
        s.Advance(50);
        Assert.That(s.Visible, Is.False);
        Assert.That(s.Alpha, Is.EqualTo(0));
    }

    [Test]
    public void AZeroMaxCountsAsFullHp()
    {
        var s = new DamageEffectState();
        s.Init(30, 100);
        s.Hp(0, 0);
        Assert.That(s.Visible, Is.False);
        s.Hp(0, 100);                           // dead: 0 percent is low
        Assert.That(s.Visible, Is.True);
    }

    [Test]
    public void NothingAllocatesPerFrame()
    {
        var s = Shown(100);
        long Round()
        {
            long x = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) { s.Hp(i % 2 == 0 ? 10 : 90, 100); s.SetAlpha(i); s.Advance(16); }
            return GC.GetAllocatedBytesForCurrentThread() - x;
        }
        Round();
        Assert.That(Round(), Is.EqualTo(0), "bytes over 100 frames");
    }
}
