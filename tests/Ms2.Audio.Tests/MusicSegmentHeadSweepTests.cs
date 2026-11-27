using Ms2.Audio;
using NLayer;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>
/// Every music segment of MS2Sound.fev under MS2_DATA (KMS2_DEV or KMS2) and MS2_GMS2_DATA (GMS2), sorted the way the
/// field music's segment decode sorts it: a bank payload (<see cref="MpegFrameRun"/>) longer than the 4 s head plays from
/// its head while the rest decodes; a run no longer than the head is decoded whole; anything else would fall back to the
/// whole decode of <see cref="MpegDecoder.Decode(byte[])"/> before its first sample. On both data sets no segment falls
/// back, so any fallback fails the sweep with its reason (the reason is checked against <see cref="MpegFrameRun.Open"/>,
/// so the sweep cannot drift from the code it describes).
/// </summary>
[NonParallelizable]
public class MusicSegmentHeadSweepTests
{
    const double HeadSeconds = 4.0;

    /// <summary>Every music segment of the bank by id, its samples (null if they do not resolve), and whether a cue reaches
    /// it (a cue's own segments or its theme's).</summary>
    static List<(MusicSegment Seg, SoundClip Clip, bool Reached)> Segments(string variable, out SoundLibrary lib)
    {
        var data = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(data)) Assert.Ignore($"{variable} is not set");
        var dir = Path.Combine(data, "Sound");
        if (!File.Exists(Path.Combine(dir, "MS2Sound.fev"))) Assert.Ignore($"no Sound/MS2Sound.fev under {variable}");
        lib = SoundLibrary.Shared(dir);
        var reached = new HashSet<int>();
        foreach (int id in lib.Fev.CueIds)
        {
            var cue = lib.Fev.GetMusicCue(id);
            if (cue == null) continue;
            foreach (var s in cue.Segments) reached.Add(s.Id);
            if (cue.ThemeInfo != null) foreach (var s in cue.ThemeInfo.Segments) reached.Add(s.Id);
        }
        var l = lib;
        return lib.Fev.MusicSegments.OrderBy(s => s.Id).Select(s => (s, l.ResolveSegment(s), reached.Contains(s.Id))).ToList();
    }

    /// <summary>
    /// Headers only, about 1 s a data set: every segment resolves (a segment a cue reaches must), opens as a frame run with
    /// at least the bank's length, and is counted on the head path or as decoded whole; the longest segment's head and rest
    /// are timed. KMS2_DEV 317 segments (316 a cue reaches), GMS2 313 (312).
    /// </summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EverySegmentTakesTheHeadPathOrIsDecodedWhole(string variable)
    {
        var list = Segments(variable, out _);
        var reasons = new SortedDictionary<string, List<int>>(StringComparer.Ordinal);
        var problems = new List<string>();
        var unreached = new List<int>();
        int head = 0;
        double headSeconds = 0, longest = 0;
        long headPcm = 0;
        byte[] longestMp3 = null;
        int longestId = 0;
        foreach (var (seg, clip, reached) in list)
        {
            if (!reached) unreached.Add(seg.Id);
            string reason = clip == null ? $"no clip (bank {seg.Bank}, index {seg.Index})"
                : clip.Mp3 == null || clip.Mp3.Length == 0 ? "empty payload"
                : Fallback(clip.Mp3);
            if (clip == null || clip.Mp3 == null || clip.Mp3.Length == 0)
            {
                Add(reasons, reason, seg.Id);
                if (reached) problems.Add($"segment {seg.Id}: a cue reaches it, {reason}");
                continue;
            }

            var run = MpegFrameRun.Open(clip.Mp3);
            if ((run == null) != (reason != null)) problems.Add($"segment {seg.Id}: Open gave {(run == null ? "null" : "a run")}, the sweep {reason ?? "no reason"}");
            if (run == null)
            {
                Add(reasons, reason ?? "Open null, no reason found", seg.Id);
                problems.Add($"segment {seg.Id} {clip.Name}: not a frame run, {reason ?? "no reason found"}");
                continue;
            }
            if (run.Seconds + 1e-9 < clip.Seconds)
                problems.Add($"segment {seg.Id} {clip.Name}: {run.Seconds:F3} s from the frame headers, the bank has {clip.Seconds:F3} s");
            if (run.Seconds <= HeadSeconds) { Add(reasons, $"whole: {HeadSeconds:0.0} s or shorter", seg.Id); continue; }
            head++;
            headSeconds += run.Seconds;
            headPcm += (long)run.SampleCount * 2;
            if (run.Seconds > longest) { longest = run.Seconds; longestMp3 = clip.Mp3; longestId = seg.Id; }
        }

        TestContext.Progress.WriteLine($"{variable}: {list.Count} music segments ({list.Count - unreached.Count} a cue reaches; "
            + $"not reached: {(unreached.Count == 0 ? "none" : string.Join(" ", unreached))}); {head} on the head path "
            + $"({headSeconds / 60:0.0} min, {headPcm / 1048576.0:0.0} MB of PCM, longest {longest:0.0} s); {list.Count - head} not:");
        foreach (var (reason, ids) in reasons)
            TestContext.Progress.WriteLine($"  {reason}: {ids.Count} ({string.Join(" ", ids)})");
        if (longestMp3 != null)
        {
            // the rest of the longest segment has to be in before its head has played (the "head ran out" restart otherwise)
            var r = MpegFrameRun.Open(longestMp3);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            r.Head((int)(HeadSeconds * r.Rate));
            double headMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            r.Whole();
            TestContext.Progress.WriteLine($"  longest, segment {longestId} ({r.Seconds:0.0} s): head {headMs:0.0} ms, rest {watch.Elapsed.TotalMilliseconds:0} ms");
        }
        Assert.That(list.Count, Is.GreaterThan(0));
        Assert.That(head, Is.GreaterThan(0));
        Assert.That(problems, Is.Empty, string.Join("\n", problems.Take(60)));
    }

    /// <summary>
    /// Every resolved music segment of both data sets decoded as the field music does, the head and then the rest, against
    /// the one-step decode: the same bytes, the length from the headers, the head ending on the MPEG frame that reaches
    /// <see cref="HeadSeconds"/>. About 2 minutes a data set; run by hand (0 problems on KMS2_DEV, restore and GMS2).
    /// </summary>
    [Explicit, TestCase("MS2_DATA"), TestCase("MS2_GMS2_DATA")]
    public void EveryMusicSegmentDecodesHeadThenRestAsOneStep(string variable)
    {
        var problems = new List<string>();
        int headed = 0, total = 0;
        foreach (var (seg, clip, _) in Segments(variable, out _))
        {
            if (clip?.Mp3 == null || clip.Mp3.Length == 0) continue;
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
        Assert.That(total, Is.GreaterThan(0));
        Assert.That(problems, Is.Empty, string.Join("\n", problems.Take(60)));
    }

    static void Add(SortedDictionary<string, List<int>> reasons, string reason, int id)
    {
        if (!reasons.TryGetValue(reason, out var ids)) reasons[reason] = ids = new List<int>();
        ids.Add(id);
    }

    /// <summary>Why a payload is not a frame run, or null if it is one.</summary>
    static string Fallback(byte[] mpeg)
    {
        var (frames, zeroTail) = MpegDecoder.Frames(mpeg);
        if (frames == 0) return "fallback: no whole frames from the first byte";
        if (!zeroTail) return "fallback: bytes other than zeros after the frames";
        var frame = new MpegDecoder.FrameReader(mpeg);
        if (frame.Layer == MpegLayer.LayerIII) return "fallback: layer III";
        var first = (frame.Version, frame.Layer, frame.SampleRate, Mono: frame.ChannelMode == MpegChannelMode.Mono);
        for (int i = 0, at = 0; i < frames; i++)
        {
            frame.At(at);
            var f = (frame.Version, frame.Layer, frame.SampleRate, Mono: frame.ChannelMode == MpegChannelMode.Mono);
            if (f != first) return $"fallback: a frame unlike the first ({first} then {f})";
            at += frame.FrameLength;
        }
        return null;
    }
}
