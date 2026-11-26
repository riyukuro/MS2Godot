using Ms2.Audio;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>
/// The FMOD event limits over the client's channels (<see cref="EffectVoiceSlots.Start"/>): each "Max playbacks behavior",
/// a free channel before a steal, and the event priority when all channels sound; then every event of MS2Sound.fev on
/// MS2_DATA (KMS2_DEV or KMS2) and MS2_GMS2_DATA (GMS2) started one time more than its max playbacks in one frame.
/// </summary>
public class EventPlaybacksTests
{
    static readonly object A = new(), B = new();

    static int Sounding(EffectVoiceSlots v, int[] tickets, double now) =>
        tickets.Count(t => v.SlotOf(t) is int s and >= 0 && v.Sounding(s, now));

    [TestCase(EffectVoiceSlots.StealOldest, new[] { false, true, true })]
    [TestCase(EffectVoiceSlots.StealNewest, new[] { true, false, true })]
    [TestCase(EffectVoiceSlots.StealQuietest, new[] { true, false, true })]
    [TestCase(EffectVoiceSlots.JustFail, new[] { true, true, false })]
    [TestCase(EffectVoiceSlots.JustFailIfQuietest, new[] { true, false, true })]
    public void TheThirdOfAnEventOfTwo(int behavior, bool[] expected)
    {
        var v = new EffectVoiceSlots(EffectVoiceSlots.ClientChannels);
        // the second is the quietest; the third is louder than it
        var t = new[]
        {
            v.Start(A, 2, behavior, 2, 0.8f, 0, 5, out _),
            v.Start(A, 2, behavior, 2, 0.3f, 0, 5, out _),
            v.Start(A, 2, behavior, 2, 0.5f, 0, 5, out bool stolen),
        };
        Assert.That(t.Select(x => v.SlotOf(x) >= 0), Is.EqualTo(expected));
        Assert.That(stolen, Is.EqualTo(behavior != EffectVoiceSlots.JustFail));
        Assert.That(Sounding(v, t, 1), Is.EqualTo(2));
        Assert.That(v.SoundingOf(A, 1), Is.EqualTo(2));
        Assert.That(v.SoundingOf(B, 1), Is.Zero);
    }

    [Test]
    public void JustFailIfQuietestFailsAQuieterOne()
    {
        var v = new EffectVoiceSlots(8);
        int a = v.Start(A, 1, EffectVoiceSlots.JustFailIfQuietest, 2, 0.5f, 0, 5, out _);
        Assert.That(v.Start(A, 1, EffectVoiceSlots.JustFailIfQuietest, 2, 0.4f, 0, 5, out _), Is.Zero);
        Assert.That(v.SlotOf(a), Is.Not.EqualTo(-1));
    }

    [Test]
    public void AnEndedOrStoppedInstanceIsFree()
    {
        var v = new EffectVoiceSlots(8);
        int a = v.Start(A, 1, EffectVoiceSlots.JustFail, 2, 1, 0, 1, out _);
        Assert.That(v.Start(A, 1, EffectVoiceSlots.JustFail, 2, 1, 0.5, 1.5, out _), Is.Zero, "still sounding");
        int b = v.Start(A, 1, EffectVoiceSlots.JustFail, 2, 1, 1.0, 2, out bool stolen);
        Assert.That(b, Is.Not.Zero, "ended at 1.0");
        Assert.That(stolen, Is.False);
        Assert.That(v.Stop(b, false, 1.2), Is.GreaterThanOrEqualTo(0));
        Assert.That(v.Start(A, 1, EffectVoiceSlots.JustFail, 2, 1, 1.2, 3, out _), Is.Not.Zero, "stopped");
        Assert.That(v.SlotOf(a), Is.EqualTo(-1).Or.Not.EqualTo(v.SlotOf(b)));
    }

    /// <summary>Under its limit a sound takes a free channel, so a busy one is never cut off while one is free: the old
    /// ring of 16 took the 17th sound's voice from the first even with the first still playing.</summary>
    [Test]
    public void AFreeChannelBeforeASteal()
    {
        var v = new EffectVoiceSlots(EffectVoiceSlots.ClientChannels);
        int first = v.Start(A, 2, EffectVoiceSlots.StealOldest, 2, 1, 0, 5, out _);
        for (int i = 0; i < 98; i++)
        {
            Assert.That(v.Start(new object(), 1, EffectVoiceSlots.JustFail, 128, 1, 0, 5, out bool stolen), Is.Not.Zero);
            Assert.That(stolen, Is.False);
        }
        Assert.That(v.SlotOf(first), Is.Not.EqualTo(-1));
        Assert.That(v.SoundingCount(0), Is.EqualTo(99));
    }

    [Test]
    public void AllChannelsSoundingTheLeastImportantGoes()
    {
        var v = new EffectVoiceSlots(3);
        int hi = v.Start(A, 5, EffectVoiceSlots.StealOldest, 0, 1, 0, 5, out _);
        int lo1 = v.Start(B, 5, EffectVoiceSlots.StealOldest, 200, 1, 0, 5, out _);
        int lo2 = v.Start(B, 5, EffectVoiceSlots.StealOldest, 200, 1, 0, 5, out _);
        int n = v.Start(new object(), 1, EffectVoiceSlots.StealOldest, 100, 1, 0, 5, out bool stolen);
        Assert.That(n, Is.Not.Zero);
        Assert.That(stolen, Is.True);
        Assert.That(v.SlotOf(lo1), Is.EqualTo(-1), "the oldest of the least important");
        Assert.That(v.SlotOf(hi), Is.Not.EqualTo(-1));
        Assert.That(v.SlotOf(lo2), Is.Not.EqualTo(-1));
        Assert.That(v.Start(new object(), 1, EffectVoiceSlots.StealOldest, 250, 1, 0, 5, out _), Is.Zero, "less important than all");
    }

    static FevProject Fev(string variable)
    {
        var d = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore($"{variable} is not set");
        var f = Path.Combine(d, "Sound", "MS2Sound.fev");
        if (!File.Exists(f)) Assert.Ignore($"{variable} has no Sound/MS2Sound.fev");
        return FevProject.Load(f);
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryEventOneOverItsMaxPlaybacks(string variable)
    {
        var fev = Fev(variable);
        var v = new EffectVoiceSlots(EffectVoiceSlots.ClientChannels);
        var perBehavior = new int[6];
        var bad = new List<string>();
        var tickets = new int[129];
        double now = 0;
        int total = 0, failedExtra = 0;
        foreach (var ev in fev.AllEvents)
        {
            total++;
            if (ev.MaxPlaybacks < 1 || ev.MaxPlaybacks > 128 || ev.MaxPlaybacksBehavior < 1 || ev.MaxPlaybacksBehavior > 5
                || ev.Priority < 0 || ev.Priority > 256)
            { bad.Add($"{ev.FullPath} max {ev.MaxPlaybacks} behavior {ev.MaxPlaybacksBehavior} priority {ev.Priority}"); continue; }
            perBehavior[ev.MaxPlaybacksBehavior]++;
            now += 10;
            int n = ev.MaxPlaybacks + 1;
            for (int i = 0; i < n; i++) tickets[i] = v.Start(ev, ev.MaxPlaybacks, ev.MaxPlaybacksBehavior, ev.Priority, 1, now, now + 5, out _);
            int expect = Math.Min(ev.MaxPlaybacks, EffectVoiceSlots.ClientChannels);
            int sounding = Sounding(v, tickets[..n], now);
            if (sounding != expect) bad.Add($"{ev.FullPath}: {sounding} sounding of {n}, max {ev.MaxPlaybacks}");
            bool extraFailed = tickets[n - 1] == 0;
            if (extraFailed) failedExtra++;
            if (extraFailed != (ev.MaxPlaybacksBehavior == EffectVoiceSlots.JustFail && ev.MaxPlaybacks < EffectVoiceSlots.ClientChannels))
                bad.Add($"{ev.FullPath}: the extra one {(extraFailed ? "failed" : "played")}, behavior {ev.MaxPlaybacksBehavior}");
        }
        TestContext.Progress.WriteLine($"{variable}: {total} events; behavior 1-5: {string.Join(" ", perBehavior.Skip(1))}; " +
                                       $"the extra one failed for {failedExtra}; bad {bad.Count}");
        Assert.That(total, Is.EqualTo(fev.EventCount).And.GreaterThan(10000));
        Assert.That(bad, Is.Empty, string.Join("\n", bad.Take(20)));
        var get = fev.FindEvent("09200002", "MS2Sound/Sound/Item");
        Assert.That(get, Is.Not.Null);
        Assert.That((get.MaxPlaybacks, get.MaxPlaybacksBehavior, get.Priority), Is.EqualTo((2, EffectVoiceSlots.StealOldest, 2)), "the meso pickup sound");
    }
}
