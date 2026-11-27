using System.Diagnostics;
using Ms2.Audio;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>
/// A music segment decoded in two steps, a short head that plays first and then the rest into the same buffer, against
/// the one-step decode the field music used before: the same bytes, the head in a small part of the time, and one PCM
/// buffer allocated instead of two.
/// </summary>
[NonParallelizable]
public class MpegFrameRunTests
{
    const double HeadSeconds = 4.0;

    static SoundLibrary Library()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA is not set");
        var dir = Path.Combine(data, "Sound");
        if (!File.Exists(Path.Combine(dir, "MS2Sound.fev"))) Assert.Ignore("no Sound/MS2Sound.fev under MS2_DATA");
        return SoundLibrary.Shared(dir);
    }

    static byte[] Mp3(SoundLibrary lib, int segment)
    {
        var seg = lib.Fev.GetMusicSegment(segment);
        Assert.That(seg, Is.Not.Null, $"segment {segment}");
        var clip = lib.ResolveSegment(seg);
        Assert.That(clip, Is.Not.Null, $"segment {segment} clip");
        return clip.Mp3;
    }

    /// <summary>Segments 102 (cue 43, 143 s), 289 (cue 168, 140.6 s), 329 (cue 196, 3 s, shorter than the head) and 352
    /// (cue 212, 99.4 s): the cues of the live burst.</summary>
    [TestCase(102)]
    [TestCase(289)]
    [TestCase(329)]
    [TestCase(352)]
    public void HeadThenRestIsTheOneStepDecode(int segment)
    {
        var mp3 = Mp3(Library(), segment);
        var one = MpegDecoder.Decode(mp3);
        var expected = one.ToBytes();

        var run = MpegFrameRun.Open(mp3);
        Assert.That(run, Is.Not.Null, "a bank payload opens as a frame run");
        Assert.That((run.Rate, run.Channels), Is.EqualTo((one.Rate, one.Channels)));
        Assert.That(run.SampleCount, Is.EqualTo(one.Samples.Length), "the length from the headers is the decoded length");
        int headFrames = (int)Math.Ceiling(HeadSeconds * run.Rate);
        var head = run.Head(headFrames);
        int headExpected = Math.Min(expected.Length, (headFrames + run.FrameSamples - 1) / run.FrameSamples * run.FrameSamples * run.Channels * 2);
        Assert.That(head.Length, Is.EqualTo(headExpected), "the head ends at the end of its MPEG frame");
        Assert.That(head.AsSpan().SequenceEqual(expected.AsSpan(0, head.Length)), Is.True, "the head is the start of the PCM");
        var whole = run.Whole();
        Assert.That(run.Done, Is.True);
        Assert.That(whole.AsSpan().SequenceEqual(expected), Is.True, "head and rest are the one-step decode, byte for byte");
    }

    /// <summary>
    /// The cost a cue's first segment pays before it can play, and the PCM it allocates: before, the whole segment was
    /// decoded into a short[] and copied into a byte[] for the stream (two buffers of the whole PCM); now the head is
    /// decoded (its copy is the head's bytes) and the rest goes into the one byte[] the stream takes.
    /// </summary>
    [TestCase(102)]
    [TestCase(289)]
    public void HeadIsASmallPartOfTheDecodeAndThePcmIsAllocatedOnce(int segment)
    {
        var mp3 = Mp3(Library(), segment);
        long pcmBytes = MpegFrameRun.Open(mp3).SampleCount * 2L;

        long before = AllocatedBytes.LeastOf(3, () => MpegDecoder.Decode(mp3).ToBytes());
        long now = AllocatedBytes.LeastOf(3, () => { var r = MpegFrameRun.Open(mp3); r.Head((int)(HeadSeconds * r.Rate)); r.Whole(); });
        long headBytes = (long)Math.Ceiling(HeadSeconds * 44100 / 1152 + 1) * 1152 * 2 * 2;

        double full = double.MaxValue, headMs = double.MaxValue, restMs = double.MaxValue;
        for (int i = 0; i < 3; i++)
        {
            var sw = Stopwatch.StartNew();
            MpegDecoder.Decode(mp3).ToBytes();
            full = Math.Min(full, sw.Elapsed.TotalMilliseconds);
            sw.Restart();
            var r = MpegFrameRun.Open(mp3);
            r.Head((int)(HeadSeconds * r.Rate));
            headMs = Math.Min(headMs, sw.Elapsed.TotalMilliseconds);
            sw.Restart();
            r.Whole();
            restMs = Math.Min(restMs, sw.Elapsed.TotalMilliseconds);
        }
        TestContext.Out.WriteLine($"segment {segment}: PCM {pcmBytes / 1e6:F1} MB; allocated before {before / 1e6:F1} MB, now {now / 1e6:F1} MB; " +
            $"decode before {full:F0} ms to the first sample, now head {headMs:F1} ms then rest {restMs:F0} ms");

        Assert.That(before, Is.GreaterThanOrEqualTo(2 * pcmBytes), "before: the short[] and its byte[] copy");
        Assert.That(now, Is.LessThan(pcmBytes + headBytes + 256 * 1024), "now: one PCM buffer and the head's copy");
        Assert.That(headMs, Is.LessThan(full / 5), "the head is a small part of the decode");
    }
}
