using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>CSoundEventEffect::vfunc_5 / vfunc_9 (soundeventmgr.cpp:577-626) over the effect voices: a stop at once, a
/// doNotStop stop 10 s later, and a reused voice that no older ticket can stop.</summary>
public class EffectVoiceSlotsTests
{
    [Test]
    public void AStopEndsTheSoundAtOnce()
    {
        var v = new EffectVoiceSlots(16);
        int t = v.Take(3);
        Assert.That(t, Is.Not.Zero);
        Assert.That(v.SlotOf(t), Is.EqualTo(3));
        Assert.That(v.Stop(t, false, 5.0), Is.EqualTo(3), "stopped now");
        Assert.That(v.SlotOf(t), Is.EqualTo(-1), "a sound waiting for its start delay does not start");
        Assert.That(v.Stop(t, false, 5.0), Is.EqualTo(-1), "a second stop finds nothing");
    }

    [Test]
    public void ADoNotStopSoundEndsTenSecondsAfterTheStop()
    {
        var v = new EffectVoiceSlots(16);
        int t = v.Take(0);
        Assert.That(v.Stop(t, true, 2.0), Is.EqualTo(-1), "only noted");
        Assert.That(v.Pending, Is.EqualTo(1));
        Assert.That(v.Stop(t, true, 7.0), Is.EqualTo(-1), "a second stop keeps the first time");
        Assert.That(v.NextDue(11.9), Is.EqualTo(-1));
        Assert.That(v.SlotOf(t), Is.EqualTo(0), "it plays on");
        Assert.That(v.NextDue(12.0), Is.EqualTo(0), "due at the stop + 10 s");
        Assert.That(v.NextDue(12.0), Is.EqualTo(-1));
        Assert.That(v.Pending, Is.Zero);
        Assert.That(v.SlotOf(t), Is.EqualTo(-1));
    }

    [Test]
    public void AVoiceTakenByANewerSoundKeepsItFromOlderStops()
    {
        var v = new EffectVoiceSlots(2);
        int old = v.Take(1);
        v.Stop(old, true, 0);
        int now = v.Take(1);
        Assert.That(now, Is.Not.EqualTo(old));
        Assert.That(v.Pending, Is.Zero, "the older sound's pending stop went with it");
        Assert.That(v.Stop(old, false, 1), Is.EqualTo(-1), "the older ticket stops nothing");
        Assert.That(v.NextDue(100), Is.EqualTo(-1));
        Assert.That(v.SlotOf(now), Is.EqualTo(1));
        Assert.That(v.SlotOf(0), Is.EqualTo(-1), "0 is no sound");
    }
}
