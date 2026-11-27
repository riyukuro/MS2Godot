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

    /// <summary>The music segments a cue reaches (the cues' and their themes' segments), by id, with their samples.</summary>
    static List<(MusicSegment Seg, SoundClip Clip)> MusicSegments(string variable)
    {
        var data = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(data) || !File.Exists(Path.Combine(data, "Sound", "MS2Sound.fev"))) Assert.Ignore($"{variable} has no Sound/MS2Sound.fev");
        var lib = SoundLibrary.Shared(Path.Combine(data, "Sound"));
        var segments = new SortedDictionary<int, MusicSegment>();
        foreach (int id in lib.Fev.CueIds)
        {
            var cue = lib.Fev.GetMusicCue(id);
            if (cue == null) continue;
            foreach (var s in cue.Segments) segments.TryAdd(s.Id, s);
            if (cue.ThemeInfo != null) foreach (var s in cue.ThemeInfo.Segments) segments.TryAdd(s.Id, s);
        }
        var list = segments.Values.Select(s => (s, lib.ResolveSegment(s))).ToList();
        Assert.That(list.Count(x => x.Item2 != null), Is.GreaterThan(0), "no music segment resolved");
        return list;
    }

    /// <summary>
    /// Every music segment on MS2_DATA (KMS2_DEV or KMS2) and MS2_GMS2_DATA (GMS2) opens as a frame run, else the field
    /// music would quietly take the whole decode before its first sample (316 and 312 segments, 306 and 302 of them
    /// longer than the head). Headers only; <see cref="EveryMusicSegmentDecodesHeadThenRestAsOneStep"/> decodes them.
    /// </summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryMusicSegmentIsAFrameRun(string variable)
    {
        var problems = new List<string>();
        foreach (var (seg, clip) in MusicSegments(variable))
        {
            if (clip == null) continue;
            var run = MpegFrameRun.Open(clip.Mp3);
            if (run == null) problems.Add($"seg {seg.Id} {clip.Name}: not a frame run");
            else if (run.Seconds + 1e-9 < clip.Seconds) problems.Add($"seg {seg.Id} {clip.Name}: {run.Seconds:F3} s from the frame headers, the bank has {clip.Seconds:F3} s");
        }
        Assert.That(problems, Is.Empty, string.Join("\n", problems.Take(60)));
    }

    /// <summary>
    /// Every music segment of both data sets decoded as the field music does, the head and then the rest, against the
    /// one-step decode: the same bytes, the length from the headers, the head ending on the MPEG frame that reaches
    /// <see cref="HeadSeconds"/>. About 2 minutes a data set; run by hand (0 problems on KMS2_DEV and GMS2 at the change).
    /// </summary>
    [Explicit, TestCase("MS2_DATA"), TestCase("MS2_GMS2_DATA")]
    public void EveryMusicSegmentDecodesHeadThenRestAsOneStep(string variable)
    {
        var problems = new List<string>();
        int headed = 0, total = 0;
        foreach (var (seg, clip) in MusicSegments(variable))
        {
            if (clip == null) continue;
            total++;
            var expected = MpegDecoder.Decode(clip.Mp3).ToBytes();
            var run = MpegFrameRun.Open(clip.Mp3);
            if (run == null) { problems.Add($"seg {seg.Id} {clip.Name}: not a frame run"); continue; }
            if (run.SampleCount * 2 != expected.Length) problems.Add($"seg {seg.Id} {clip.Name}: {run.SampleCount} samples from the headers, {expected.Length / 2} decoded");
            if (run.Seconds > HeadSeconds)
            {
                headed++;
                int headFrames = (int)Math.Ceiling(HeadSeconds * run.Rate), need = headFrames * run.Channels * 2;
                var head = run.Head(headFrames);
                int frameBytes = run.FrameSamples * run.Channels * 2;
                if (head.Length % frameBytes != 0 || head.Length < need || head.Length - frameBytes >= need)
                    problems.Add($"seg {seg.Id} {clip.Name}: a head of {head.Length} bytes is not the MPEG frames that reach {headFrames} PCM frames");
                if (!head.AsSpan().SequenceEqual(expected.AsSpan(0, Math.Min(head.Length, expected.Length))))
                    problems.Add($"seg {seg.Id} {clip.Name}: the head is not the start of the one-step decode");
            }
            if (!run.Whole().AsSpan().SequenceEqual(expected)) problems.Add($"seg {seg.Id} {clip.Name}: head and rest differ from the one-step decode");
        }
        TestContext.Progress.WriteLine($"{variable}: {total} music segments, {headed} played from a head; {problems.Count} problems");
        Assert.That(problems, Is.Empty, string.Join("\n", problems.Take(60)));
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
