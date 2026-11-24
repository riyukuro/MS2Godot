using Ms2.Audio;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>
/// The MPEG payloads of every FSB5 bank under Sound/ of MS2_DATA (KMS2_DEV or KMS2) and MS2_GMS2_DATA (GMS2). Each sample is
/// a run of whole frames from its first byte, then the bank's zero padding, and MpegDecoder decodes it frame by frame as FMOD
/// does. Two kinds NLayer's stream reader does not open: a one-frame sample (the banks' stand-ins for lines never recorded: one
/// silent layer II frame; the reader, like ffmpeg's probe, wants two frames in a row), and layer II mono above 192 kbps
/// (ISO/IEC 11172-3 lists those rates for two-channel modes only; the reader refuses the frames). FMOD plays both.
/// </summary>
[NonParallelizable]
public class MpegFrameSweepTests
{
    static IEnumerable<string> Banks(string variable)
    {
        var d = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(Path.Combine(d, "Sound"))) Assert.Ignore($"{variable} has no Sound folder");
        return Directory.GetFiles(Path.Combine(d, "Sound"), "*.fsb").OrderBy(f => f, StringComparer.Ordinal);
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EverySampleIsWholeFramesAndEveryOneFrameSampleDecodes(string variable)
    {
        var problems = new List<string>();
        var single = new List<string>();
        var monoHigh = new List<string>();
        int banks = 0, total = 0, sound = 0;
        foreach (var path in Banks(variable))
        {
            var bank = new Fsb5(path);
            if (bank.Mode != Fsb5.ModeMpeg) { problems.Add($"{Path.GetFileName(path)}: mode {bank.Mode}"); continue; }
            banks++;
            using var f = File.OpenRead(path);
            foreach (var s in bank.Samples)
            {
                total++;
                var b = new byte[s.DataSize];
                f.Position = s.DataOffset;
                f.ReadExactly(b);
                var (frames, zeroTail) = MpegDecoder.Frames(b);
                var first = MpegDecoder.Frame(b, 0);
                if (frames == 0 || !zeroTail) { problems.Add($"{Path.GetFileName(path)} {s.Name}: {frames} frame(s), tail {(zeroTail ? "zero" : "not zero")}"); continue; }
                if ((long)frames * first.Samples < s.Samples) problems.Add($"{Path.GetFileName(path)} {s.Name}: {frames} frames for {s.Samples} samples");
                bool high = (b[1] >> 1 & 3) == 2 && (b[1] >> 3 & 3) == 3 && b[3] >> 6 == 3 && b[2] >> 4 > 10;
                if (high)
                {
                    monoHigh.Add($"{Path.GetFileName(path)}:{s.Name} {new[] { 224, 256, 320, 384 }[(b[2] >> 4) - 11]} kbps");
                    var mono = MpegDecoder.Decode(b);
                    if (mono.Channels != 1 || mono.Frames != frames * first.Samples) problems.Add($"{Path.GetFileName(path)} {s.Name}: mono above 192 kbps decoded to {mono.Channels} channel(s), {mono.Frames} samples of {frames * first.Samples}");
                    else if (mono.Samples.All(x => x == 0)) problems.Add($"{Path.GetFileName(path)} {s.Name}: mono above 192 kbps decoded silent");
                }
                if (frames != 1) continue;
                single.Add($"{Path.GetFileName(path)}:{s.Name}");
                var pcm = MpegDecoder.Decode(b);
                if (pcm.Frames != s.Samples) problems.Add($"{Path.GetFileName(path)} {s.Name}: one frame decoded to {pcm.Frames} samples, the header has {s.Samples}");
                if (pcm.Samples.Any(x => x != 0)) sound++;
            }
        }
        var byBank = single.GroupBy(x => x[..x.IndexOf(':')]).Select(g => $"{g.Key} {g.Count()}");
        TestContext.Progress.WriteLine($"{variable}: {banks} banks, {total} samples; {single.Count} of one frame ({string.Join(", ", byBank)}), {sound} of them not silent; "
            + $"{monoHigh.Count} layer II mono above 192 kbps ({string.Join(", ", monoHigh)}); {problems.Count} problems");
        foreach (var x in single) TestContext.Progress.WriteLine("  " + x);
        Assert.That(problems, Is.Empty, string.Join("\n", problems.Take(60)));
    }

    [Test]
    public void AOneFrameStandInDecodesToItsSilentFrame()
    {
        // one MPEG-1 layer II frame, 160 kbps, 44.1 kHz, mono (the stand-ins' header), its 522 bytes zero after the header
        // (all allocations zero: silence), then the bank's padding
        var b = new byte[544];
        b[0] = 0xff; b[1] = 0xfd; b[2] = 0x90; b[3] = 0xc4;
        Assert.That(MpegDecoder.Frame(b, 0), Is.EqualTo((522, 1152)));
        Assert.That(MpegDecoder.Frames(b), Is.EqualTo((1, true)));
        var pcm = MpegDecoder.Decode(b);
        Assert.That((pcm.Rate, pcm.Channels, pcm.Frames), Is.EqualTo((44100, 1, 1152)));
        Assert.That(pcm.Samples.All(x => x == 0), Is.True);
    }

    /// <summary>
    /// Every sample of every bank through the decoder, and the same PCM as NLayer's stream reader wherever that reader opens the
    /// payload (a few minutes a data set; run by hand).
    /// </summary>
    [Explicit, TestCase("MS2_DATA"), TestCase("MS2_GMS2_DATA")]
    public void EverySampleDecodes(string variable)
    {
        var failures = new List<string>();
        int total = 0, compared = 0;
        foreach (var path in Banks(variable))
        {
            var bank = new Fsb5(path);
            using var f = File.OpenRead(path);
            foreach (var s in bank.Samples)
            {
                total++;
                var b = new byte[s.DataSize];
                f.Position = s.DataOffset;
                f.ReadExactly(b);
                try
                {
                    var pcm = MpegDecoder.Decode(b);
                    if (pcm.Frames < s.Samples) failures.Add($"{Path.GetFileName(path)} {s.Name}: {pcm.Frames} of {s.Samples} samples");
                    Pcm16 stream = null;
                    try { stream = MpegDecoder.DecodeStream(b); } catch (Exception) { }
                    if (stream == null || stream.Frames == 0) continue;
                    compared++;
                    if (stream.Channels != pcm.Channels || !stream.Samples.AsSpan().SequenceEqual(pcm.Samples))
                        failures.Add($"{Path.GetFileName(path)} {s.Name}: {pcm.Frames} samples, the stream reader {stream.Frames}, first difference at {FirstDifference(pcm.Samples, stream.Samples)}");
                }
                catch (Exception ex) { failures.Add($"{Path.GetFileName(path)} {s.Name}: {ex.GetType().Name}: {ex.Message}"); }
            }
        }
        TestContext.Progress.WriteLine($"{variable}: {total} samples decoded, {compared} compared with the stream reader, {failures.Count} failures");
        Assert.That(failures, Is.Empty, string.Join("\n", failures.Take(60)));
    }

    static int FirstDifference(short[] a, short[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++) if (a[i] != b[i]) return i;
        return n;
    }
}
