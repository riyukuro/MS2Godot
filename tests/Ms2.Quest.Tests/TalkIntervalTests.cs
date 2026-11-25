using Ms2.Quest;
using NUnit.Framework;

namespace Ms2.Quest.Tests;

/// <summary>UITalkManager::IsTalkIntervalElapsed (<see cref="TalkInterval"/>): TalkCooldown ms after the last talk's end.</summary>
public class TalkIntervalTests
{
    [Test]
    public void NoTalkEndedYetIsElapsed()
    {
        var t = new TalkInterval();
        Assert.That(t.Elapsed(0, TalkInterval.DefaultCooldownMs), Is.True);
        Assert.That(t.Elapsed(500, TalkInterval.DefaultCooldownMs), Is.True);
    }

    [Test]
    public void ATalkMayStartOnlyOnceTheCooldownHasPassedSinceTheEnd()
    {
        var t = new TalkInterval();
        t.TalkEnded(10_000);
        Assert.That(t.Elapsed(10_000, 1000), Is.False);
        Assert.That(t.Elapsed(10_999, 1000), Is.False);
        // GetTickCountMs2() - m_nLastTick < cooldown gives 0, so exactly the cooldown is elapsed
        Assert.That(t.Elapsed(11_000, 1000), Is.True);
        Assert.That(t.Elapsed(50_000, 1000), Is.True);
        // a later end starts it again
        t.TalkEnded(50_000);
        Assert.That(t.Elapsed(50_400, 1000), Is.False);
        Assert.That(t.Elapsed(50_400, 0), Is.True);
    }

    [Test]
    public void TheTickCountWrapsAsTheClientsUnsignedSubtraction()
    {
        var t = new TalkInterval();
        t.TalkEnded(uint.MaxValue - 200);
        Assert.That(t.Elapsed(300, 1000), Is.False);     // 501 ms later across the wrap
        Assert.That(t.Elapsed(900, 1000), Is.True);      // 1101 ms later
    }
}
