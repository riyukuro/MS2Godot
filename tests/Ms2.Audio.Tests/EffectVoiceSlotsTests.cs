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
    static int Loop(EffectVoiceSlots v, object ev, double now, out bool stolen, int maxPlaybacks = 0, int priority = 128) =>
        v.Start(ev, maxPlaybacks, EffectVoiceSlots.StealOldest, priority, 1, now, now + 1, out stolen, loops: true);

    static int Shot(EffectVoiceSlots v, double now, int priority = 128) =>
        v.Start(new object(), 0, EffectVoiceSlots.StealOldest, priority, 1, now, now + 5, out _);

    [Test]
    public void ALoopingSoundHoldsItsVoiceFromOneShots()
    {
        var v = new EffectVoiceSlots(4, 2);
        int t = Loop(v, new object(), 0, out _, priority: 255);   // the least important: a one-shot still never takes it
        int s = v.SlotOf(t);
        Assert.That(v.Loops(s), Is.True);
        Assert.That(v.Looping, Is.EqualTo(1));
        for (int n = 0; n < 12; n++)
        {
            int o = Shot(v, n * 0.1);
            if (o != 0) Assert.That(v.SlotOf(o), Is.Not.EqualTo(s), "a one-shot never takes the looping voice");
        }
        Assert.That(v.SlotOf(t), Is.EqualTo(s), "it plays on until stopped, past its end");
        Assert.That(v.Sounding(s, 100), Is.True);
        Assert.That(v.Stop(t, false, 100), Is.EqualTo(s));
        Assert.That(v.Looping, Is.Zero, "the stop frees the hold");
        Assert.That(v.Loops(s), Is.False);
    }

    [Test]
    public void LoopingSoundsPastTheCapTakeTheOldestLoopingVoice()
    {
        var v = new EffectVoiceSlots(8, 3);
        var held = new int[3];
        for (int i = 0; i < 3; i++) held[i] = Loop(v, new object(), i, out _);
        Assert.That(v.Looping, Is.EqualTo(3));
        int oldest = v.SlotOf(held[0]);
        int t = Loop(v, new object(), 4, out bool stolen);
        Assert.That((v.SlotOf(t), stolen), Is.EqualTo((oldest, true)), "the fourth looping sound takes the first one's voice, free voices or not");
        Assert.That(v.Looping, Is.EqualTo(3), "never more than the cap");
        Assert.That(v.SlotOf(held[0]), Is.EqualTo(-1), "the oldest loop is gone");
        int next = v.SlotOf(held[1]);
        Assert.That(v.SlotOf(Loop(v, new object(), 5, out _)), Is.EqualTo(next), "then the next oldest");
    }

    [Test]
    public void AOneShotInALoopingVoiceEndsTheHold()
    {
        var v = new EffectVoiceSlots(2, 1);
        int t = v.Take(0, true);
        v.Stop(t, true, 0);
        Assert.That(v.Pending, Is.EqualTo(1));
        Assert.That(v.NextDue(10), Is.EqualTo(0), "a doNotStop loop goes 10 s after its stop");
        Assert.That(v.Looping, Is.Zero);
        v.Take(1, true);
        v.Take(1);
        Assert.That(v.Looping, Is.Zero, "a sound taking the voice drops its loop");
        Assert.Throws<ArgumentOutOfRangeException>(() => new EffectVoiceSlots(4, 4), "one voice stays for one-shots");
    }

    /// <summary>The event's max playbacks come first: a looping event at its limit replaces its own oldest instance (the
    /// cap is not reached, the loop count stays), and one that just fails takes nothing even with the voices free.</summary>
    [Test]
    public void MaxPlaybacksBeforeTheLoopCap()
    {
        var v = new EffectVoiceSlots(8, 4);
        var a = new object();
        int a1 = Loop(v, a, 0, out _, maxPlaybacks: 2), a2 = Loop(v, a, 1, out _, maxPlaybacks: 2);
        int first = v.SlotOf(a1);
        int a3 = Loop(v, a, 2, out bool stolen, maxPlaybacks: 2);
        Assert.That((v.SlotOf(a3), stolen, v.Looping), Is.EqualTo((first, true, 2)), "StealOldest: the first instance's voice");
        Assert.That(v.SlotOf(a1), Is.EqualTo(-1));
        Assert.That(v.SlotOf(a2), Is.Not.EqualTo(-1));
        var b = new object();
        Assert.That(v.Start(b, 1, EffectVoiceSlots.JustFail, 128, 1, 3, 4, out _, loops: true), Is.Not.Zero);
        Assert.That(v.Start(b, 1, EffectVoiceSlots.JustFail, 128, 1, 3, 4, out _, loops: true), Is.Zero, "its one playback loops on");
        Assert.That(v.Looping, Is.EqualTo(3));
    }

    /// <summary>With every voice sounding a one-shot steals the least important one-shot, never a looping voice; with
    /// none left but looping ones it fails.</summary>
    [Test]
    public void AllVoicesSoundingAOneShotStealsOnlyAOneShot()
    {
        var v = new EffectVoiceSlots(3, 2);
        int l1 = Loop(v, new object(), 0, out _, priority: 255), l2 = Loop(v, new object(), 0, out _, priority: 255);
        int o = Shot(v, 0, 100);
        int oneShotVoice = v.SlotOf(o);
        int n = Shot(v, 1, 100);
        Assert.That(v.SlotOf(n), Is.EqualTo(oneShotVoice), "the one-shot's voice, not a looping one");
        Assert.That(v.SlotOf(o), Is.EqualTo(-1));
        Assert.That(v.SlotOf(l1) >= 0 && v.SlotOf(l2) >= 0, Is.True);
        v.Stop(n, false, 2);
        Assert.That(Loop(v, new object(), 3, out bool stolen), Is.Not.Zero);
        Assert.That(stolen, Is.True, "at the cap a looping sound takes the oldest loop even with a voice free");
        Assert.That(v.Looping, Is.EqualTo(2));
    }
}
