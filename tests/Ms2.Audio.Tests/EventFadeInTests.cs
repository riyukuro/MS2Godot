using Ms2.Audio;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>
/// The events' fade-in (FMOD_EVENTPROPERTY_FADEIN, record +0x8c) as the client's FMOD plays it, baked into the start of the
/// sound (<see cref="EventFadeIn"/>). The FMOD numbers come from the probe's real-time renders of the meso pickup event
/// (Item/09200002), with the client's 1024 x 10 DSP buffer and one event system update per 16.7 ms frame.
/// </summary>
public class EventFadeInTests
{
    /// <summary>Which events fade in, and by how much, on both regions; the meso pickup's own 5 / 5. Voice 379 / 304 (the
    /// regions' voice banks differ), AMB 86, Item 16, Mob 4, System 2, Object 1 on each.</summary>
    [TestCase("MS2_DATA", 488, "5:421 10:64 500:2 1000:1")]
    [TestCase("MS2_GMS2_DATA", 413, "5:346 10:64 500:2 1000:1")]
    public void FadeInsAreTheEventsOwn(string variable, int count, string byLength)
    {
        var fev = PanLevelTests.Fev(variable);
        var fading = fev.AllEvents.Where(e => e.FadeIn > 0).ToList();
        string got = string.Join(" ", fading.GroupBy(e => e.FadeIn).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}"));
        TestContext.Out.WriteLine($"{variable}: {fading.Count} events fade in ({got}); by group "
            + string.Join(", ", fading.GroupBy(e => e.Group.Split('/').ElementAtOrDefault(2)).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}"))
            + $"; fade-outs {string.Join(" ", fev.AllEvents.Where(e => e.FadeOut > 0).GroupBy(e => e.FadeOut).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}"))}");
        var meso = fev.AllEvents.Single(e => e.FullPath == "MS2Sound/Sound/Item/09200002");
        Assert.That((meso.FadeIn, meso.FadeOut), Is.EqualTo((5, 5)));
        Assert.That((fading.Count, got), Is.EqualTo((count, byLength)));
    }

    /// <summary>
    /// The baked gain is FMOD's min(1, elapsed / fade-in). The renders hold that line's value one frame in: 0.80-0.85 with a
    /// 20 ms fade-in and 0.30-0.32 with 50 ms, at 16.7 ms elapsed (0.833 and 0.333 on the line). A 5 ms fade-in is full by
    /// 5 ms; FMOD's renders are full by 3.5 ms, or from the start when an update falls before the first mix.
    /// </summary>
    [Test]
    public void BakedFadeInIsFmodsLine()
    {
        var pcm = Constant(48000, 2, 10000, 48000);
        var b = EventFadeIn.Bytes(pcm, 5, false, out int begin, out int end);
        Assert.That((begin, end, b.Length), Is.EqualTo((0, 48000, pcm.Samples.Length * 2)));
        Assert.That(At(b, 2, 0, 0), Is.EqualTo(0));
        Assert.That(At(b, 2, 120, 1), Is.EqualTo(5000));   // 2.5 ms of 5: half
        Assert.That(At(b, 2, 240, 0), Is.EqualTo(10000));
        Assert.That(At(b, 2, 47999, 1), Is.EqualTo(10000));
        float frame = 48000 / 60f;
        Assert.That(EventFadeIn.Gain((int)frame, EventFadeIn.Frames(20, 48000)), Is.InRange(0.80f, 0.85f));
        Assert.That(EventFadeIn.Gain((int)frame, EventFadeIn.Frames(50, 48000)), Is.InRange(0.30f, 0.34f));
        // a clip at its own rate fades over the same time
        Assert.That(EventFadeIn.Frames(5, 22050), Is.EqualTo(110));
    }

    /// <summary>A looping sound fades in on its first pass only: the loop runs over the sound rotated past the fade, n extra
    /// frames, so every later pass is the plain sound in order.</summary>
    [TestCase(240)]
    [TestCase(960)]   // 20 ms: a fade-in longer than the sound
    public void LoopFadesOnlyItsFirstPass(int fadeFrames)
    {
        var pcm = new Pcm16 { Rate = 48000, Channels = 1, Samples = Enumerable.Range(0, 600).Select(i => (short)(1000 + i)).ToArray() };
        var b = EventFadeIn.Bytes(pcm, fadeFrames * 1000 / 48000, true, out int begin, out int end);
        Assert.That((begin, end, b.Length), Is.EqualTo((fadeFrames, fadeFrames + 600, (fadeFrames + 600) * 2)));
        // playback: the first pass to end, then the loop from begin, again and again
        for (int t = 0; t < 4000; t++)
        {
            int i = t < end ? t : begin + (t - end) % (end - begin);
            short plain = pcm.Samples[t % 600];
            short want = t < fadeFrames ? (short)MathF.Round(plain * (float)t / fadeFrames) : plain;
            Assert.That(At(b, 1, i, 0), Is.EqualTo(want), $"frame {t}");
        }
    }

    /// <summary>An event without a fade-in plays its plain bytes over its whole length, as before.</summary>
    [Test]
    public void NoFadeInKeepsThePlainSound()
    {
        var pcm = Constant(44100, 1, -1234, 500);
        Assert.That(EventFadeIn.Bytes(pcm, 0, true, out int begin, out int end), Is.EqualTo(pcm.ToBytes()));
        Assert.That((begin, end), Is.EqualTo((0, 500)));
    }

    /// <summary>
    /// The sounds as their voices play them. Every event with a fade-in of 500 ms or more starts from silence along the line,
    /// at about a third of the plain power over the fade. The voices played the plain sound before, so this fails there
    /// (power ratio 1). The meso pickup's 5 ms falls on the decoder's silent lead-in (its first 5 ms decode to zeros; FMOD's
    /// render has its first sample 5.3 ms after the start), so its sound is unchanged.
    /// </summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void LongFadeInsStartFromSilence(string variable)
    {
        var lib = new SoundLibrary(Path.Combine(PanLevelTests.DataDir(variable), "Sound"));
        var meso = lib.ResolveEvent("MS2Sound/Sound/Item/09200002");
        var mesoPcm = MpegDecoder.Decode(meso);
        Assert.That(EventFadeIn.Bytes(mesoPcm, meso.Event.FadeIn, meso.Loops, out _, out _), Is.EqualTo(mesoPcm.ToBytes()));
        int checkedCount = 0;
        foreach (var ev in PanLevelTests.Fev(variable).AllEvents.Where(e => e.FadeIn >= 500))
        {
            var clip = lib.ResolveEvent(ev.FullPath);
            var pcm = clip == null ? null : MpegDecoder.Decode(clip);
            if (pcm == null) { TestContext.Out.WriteLine($"{ev.FullPath}: no sound"); continue; }
            var b = EventFadeIn.Bytes(pcm, ev.FadeIn, clip.Loops, out int begin, out int end);
            var plain = pcm.ToBytes();
            int n = Math.Min(EventFadeIn.Frames(ev.FadeIn, pcm.Rate), pcm.Frames), ch = pcm.Channels;
            double ratio = Power(b, ch, n) / Power(plain, ch, n);
            TestContext.Out.WriteLine($"{ev.FullPath} = {clip.Name} {pcm.Rate} Hz x{ch} {pcm.Seconds:F2} s{(clip.Loops ? " loops" : "")}: fade {ev.FadeIn} ms, power {ratio:F3} of plain, loop {begin}..{end}");
            Assert.That(ratio, Is.InRange(0.05, 0.6), ev.FullPath);
            checkedCount++;
        }
        Assert.That(checkedCount, Is.EqualTo(3));
    }

    static double Power(byte[] d, int ch, int frames)
    {
        double s = 0;
        for (int i = 0; i < frames * ch; i++) s += Math.Pow(BitConverter.ToInt16(d, i * 2), 2);
        return s;
    }

    /// <summary>
    /// A loop that went virtual (stopped past its silent distance) comes back where FMOD's channel has run on to, as the
    /// probe renders AMB/11100071 (virtual from 1924 to 4883 ms, back in one step at its full level, its first 20 ms the
    /// uninterrupted render's 29 ms on): <see cref="EventFadeIn.LoopPosition"/> is the frame a forward loop over
    /// <see cref="EventFadeIn.Bytes"/>'s range reaches by then, stepped frame by frame, with and without a baked fade-in.
    /// The old resume (the loop's start every time) is the stepped position only by chance.
    /// </summary>
    [TestCase(0, false)]
    [TestCase(5, true)]
    [TestCase(500, true)]
    public void LoopComesBackWhereTheEventRanOnTo(int fadeInMs, bool fades)
    {
        const int rate = 22050, frames = 9000;
        EventFadeIn.Bytes(Constant(rate, 1, 1000, frames), fadeInMs, true, out int begin, out int end);
        Assert.That((begin > 0, end - begin), Is.EqualTo((fades, frames)));
        int pos = 0, off = 0, checkedAt = 0;
        for (int n = 1; n <= 6 * (begin + frames); n++)
        {
            if (++pos >= end) pos = begin;
            if (n % 97 != 0) continue;
            double got = EventFadeIn.LoopPosition((n + 0.25) / rate, begin, end, rate) * rate;
            Assert.That(got, Is.EqualTo(pos + 0.25).Within(1e-3), $"frame {n}");
            if (Math.Abs(pos - begin) > 1) off++;
            checkedAt++;
        }
        Assert.That(off, Is.GreaterThan(checkedAt * 9 / 10), "the loop start alone");
        Assert.That(EventFadeIn.LoopPosition(0, begin, end, rate), Is.EqualTo(0));
        Assert.That(EventFadeIn.LoopPosition(1, begin, end, 0), Is.EqualTo(0));
    }

    static Pcm16 Constant(int rate, int ch, short v, int frames) =>
        new() { Rate = rate, Channels = ch, Samples = Enumerable.Repeat(v, frames * ch).ToArray() };

    static short At(byte[] b, int ch, int frame, int c) => BitConverter.ToInt16(b, (frame * ch + c) * 2);
}
