using Ms2.Audio;
using NUnit.Framework;
using static Ms2.Audio.MusicSegmentHead;

namespace Ms2.Audio.Tests;

/// <summary>
/// The field music's rules for a segment that plays from its head while its rest decodes (FieldAudio.DecodeSegment,
/// OnSlotFinished, CompleteSegment): a head's end is not the segment's end, the rest's arrival restarts a voice whose head
/// ran out or ends the voices of a rest that failed, and a head the main thread rejects stops its rest's decode.
/// </summary>
[NonParallelizable]
public class MusicSegmentHeadTests
{
    [TestCase(true, false, true, SlotEnd.WaitForRest, TestName = "the cue's voice on a head waits for the rest")]
    [TestCase(true, false, false, SlotEnd.WaitForRest, TestName = "an outgoing voice on a head waits for the rest")]
    [TestCase(true, true, true, SlotEnd.AdvanceCue, TestName = "the cue's voice at the segment's end advances the cue")]
    [TestCase(true, true, false, SlotEnd.ChainOutgoing, TestName = "an outgoing voice at the segment's end chains")]
    [TestCase(false, false, true, SlotEnd.AdvanceCue, TestName = "a voice with no segment, the cue's, advances")]
    [TestCase(false, false, false, SlotEnd.ChainOutgoing, TestName = "a voice with no segment, outgoing, chains")]
    public void SlotFinished(bool hasSegment, bool complete, bool currentCue, SlotEnd expected) =>
        Assert.That(OnSlotFinished(hasSegment, complete, currentCue), Is.EqualTo(expected));

    [TestCase(true, true, true, RestArrival.Nothing, TestName = "the rest in while the head plays: nothing to do")]
    [TestCase(true, false, true, RestArrival.Nothing, TestName = "the rest in while an outgoing head plays: nothing to do")]
    [TestCase(true, true, false, RestArrival.ResumeFromHead, TestName = "the head ran out: the cue's voice goes on from its end")]
    [TestCase(true, false, false, RestArrival.ResumeFromHead, TestName = "the head ran out: an outgoing voice goes on from its end")]
    [TestCase(false, true, true, RestArrival.EndActiveVoice, TestName = "the rest failed on the active voice: it ends")]
    [TestCase(false, true, false, RestArrival.EndActiveVoice, TestName = "the rest failed on the stopped active voice: it ends")]
    [TestCase(false, false, true, RestArrival.ReleaseSlot, TestName = "the rest failed on another voice: its slot is freed")]
    public void RestArrived(bool decoded, bool activeVoiceOfCue, bool playing, RestArrival expected) =>
        Assert.That(OnRestArrived(decoded, activeVoiceOfCue, playing), Is.EqualTo(expected));

    [Test]
    public void ARestIsWantedUntilItsHeadIsRejected()
    {
        var pending = new SegmentRest();
        Assert.That(pending.KeepDecoding, Is.True, "the pool thread starts on the rest before the main thread sees the head");
        var taken = new SegmentRest();
        taken.Resolve(true);
        Assert.That(taken.KeepDecoding, Is.True);
        var rejected = new SegmentRest();
        rejected.Resolve(false);
        Assert.That((rejected.KeepDecoding, rejected.Rejected), Is.EqualTo((false, true)));
    }

    static byte[] Mp3(int segment)
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA is not set");
        var dir = Path.Combine(data, "Sound");
        if (!File.Exists(Path.Combine(dir, "MS2Sound.fev"))) Assert.Ignore("no Sound/MS2Sound.fev under MS2_DATA");
        var lib = SoundLibrary.Shared(dir);
        var clip = lib.ResolveSegment(lib.Fev.GetMusicSegment(segment));
        Assert.That(clip, Is.Not.Null, $"segment {segment}");
        return clip.Mp3;
    }

    /// <summary>
    /// Segment 289 (cue 168, 140.6 s), the cue a newer one supersedes in the live burst: its head is decoded and the main
    /// thread rejects it two steps into the rest. The rest stops there: nothing is returned and the run decoded the head
    /// and two seconds of the 140, where the whole rest (about 25 MB of PCM, 0.3 s) was decoded before.
    /// </summary>
    [Test]
    public void ARejectedHeadStopsItsRest()
    {
        var run = MpegFrameRun.Open(Mp3(289));
        run.Head((int)Math.Ceiling(4.0 * run.Rate));
        int headFrames = run.Written / run.Channels;
        var rest = new SegmentRest();
        int steps = 0;
        var all = run.Rest(run.Rate, () =>
        {
            if (++steps == 3) rest.Resolve(false);
            return rest.KeepDecoding;
        });
        Assert.That(all, Is.Null, "a rejected head's rest is never set");
        Assert.That(run.Done, Is.False);
        int decoded = run.Written / run.Channels;
        Assert.That(decoded, Is.LessThanOrEqualTo(headFrames + 2 * (run.Rate + run.FrameSamples)), "two steps of a second each (to their MPEG frame's end) after the head");
        Assert.That(decoded, Is.LessThan(run.SampleCount / run.Channels / 10));
        TestContext.Out.WriteLine($"segment 289: rest stopped at {(double)decoded / run.Rate:F1} of {run.Seconds:F1} s, "
            + $"{(run.SampleCount - run.Written) * 2 / 1048576.0:F1} MB of PCM not decoded");
    }

    /// <summary>A head that is taken (or still pending) lets the rest go to the end: the stepped rest is the one-step decode.</summary>
    [Test]
    public void ATakenHeadsRestIsTheOneStepDecode()
    {
        var mp3 = Mp3(289);
        var expected = MpegDecoder.Decode(mp3).ToBytes();
        var run = MpegFrameRun.Open(mp3);
        var head = run.Head((int)Math.Ceiling(4.0 * run.Rate));
        var rest = new SegmentRest();
        int steps = 0;
        var all = run.Rest(run.Rate, () =>
        {
            if (++steps == 2) rest.Resolve(true);
            return rest.KeepDecoding;
        });
        Assert.That(all, Is.Not.Null);
        Assert.That(steps, Is.GreaterThan(100), "a second a step over 136 s");
        Assert.That(all.AsSpan().SequenceEqual(expected), Is.True);
        Assert.That(head.AsSpan().SequenceEqual(expected.AsSpan(0, head.Length)), Is.True);
    }
}
