using Ms2.Audio;
using NLayer;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>
/// Every music segment of MS2Sound.fev under MS2_DATA (KMS2_DEV or KMS2) and MS2_GMS2_DATA (GMS2), sorted the way the
/// field music's segment decode sorts it: a bank payload (<see cref="MpegFrameRun"/>) longer than the 4 s head plays from
/// its head while the rest decodes; a run no longer than the head is decoded whole; anything else falls back to the whole
/// decode of <see cref="MpegDecoder.Decode(byte[])"/>. Each fallback is given its reason, and the reason is checked
/// against <see cref="MpegFrameRun.Open(byte[])"/>, so the sweep cannot drift from the code it describes.
/// </summary>
[NonParallelizable]
public class MusicSegmentHeadSweepTests
{
    const double HeadSeconds = 4.0;

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EverySegmentTakesTheHeadPathOrAKnownFallback(string variable)
    {
        var data = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(data)) Assert.Ignore($"{variable} is not set");
        var dir = Path.Combine(data, "Sound");
        if (!File.Exists(Path.Combine(dir, "MS2Sound.fev"))) Assert.Ignore($"no Sound/MS2Sound.fev under {variable}");
        var lib = SoundLibrary.Shared(dir);

        var reasons = new SortedDictionary<string, List<int>>(StringComparer.Ordinal);
        var problems = new List<string>();
        int segments = 0, head = 0;
        double headSeconds = 0, longest = 0;
        long headPcm = 0;
        byte[] longestMp3 = null;
        int longestId = 0;
        foreach (var seg in lib.Fev.MusicSegments.OrderBy(s => s.Id))
        {
            segments++;
            var clip = lib.ResolveSegment(seg);
            string reason = clip == null ? $"no clip (bank {seg.Bank}, index {seg.Index})"
                : clip.Mp3 == null || clip.Mp3.Length == 0 ? "empty payload"
                : Fallback(clip.Mp3);
            if (clip == null || clip.Mp3 == null || clip.Mp3.Length == 0) { Add(reasons, reason, seg.Id); continue; }

            var run = MpegFrameRun.Open(clip.Mp3);
            if ((run == null) != (reason != null)) problems.Add($"segment {seg.Id}: Open gave {(run == null ? "null" : "a run")}, the sweep {reason ?? "no reason"}");
            if (run == null) { Add(reasons, reason ?? "Open null, no reason found", seg.Id); continue; }
            if (run.Seconds <= HeadSeconds) { Add(reasons, $"whole: {HeadSeconds:0.0} s or shorter", seg.Id); continue; }
            head++;
            headSeconds += run.Seconds;
            headPcm += (long)run.SampleCount * 2;
            if (run.Seconds > longest) { longest = run.Seconds; longestMp3 = clip.Mp3; longestId = seg.Id; }
        }

        TestContext.Progress.WriteLine($"{variable}: {segments} music segments; {head} on the head path ({headSeconds / 60:0.0} min, "
            + $"{headPcm / 1048576.0:0.0} MB of PCM, longest {longest:0.0} s); {segments - head} not:");
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
        Assert.That(segments, Is.GreaterThan(0));
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
        Assert.That(reasons.Keys.Where(r => r.StartsWith("Open null", StringComparison.Ordinal)), Is.Empty);
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
