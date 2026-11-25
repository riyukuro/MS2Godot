using System.Buffers.Binary;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>
/// The sound of a .usm movie: its @SFA stream is a whole ADX file (the client plays it through the CRI Mana library,
/// UICutSceneMovie::PlayUsm @0x14190ffc0, uicutscenemovie.cpp:392-404). These build small standard ADX files by hand;
/// the real movies are checked sample for sample against ffmpeg's decoder in Ms2.Video.Tests (UsmSoundTests).
/// </summary>
public class AdxDecoderTests
{
    /// <summary>A standard ADX file: header (copyright offset 0x1C, so the data starts at 0x20), then the frames as given.</summary>
    static byte[] Adx(int channels, int rate, int total, int highPass, byte flags, params byte[][] frames)
    {
        var d = new byte[0x20 + frames.Sum(f => f.Length)];
        d[0] = 0x80;
        BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(2), 0x1C);
        d[4] = 3; d[5] = 18; d[6] = 4; d[7] = (byte)channels;
        BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(8), (uint)rate);
        BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(12), (uint)total);
        BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(16), (ushort)highPass);
        d[18] = 4; d[19] = flags;
        int at = 0x20;
        foreach (var f in frames) { f.CopyTo(d, at); at += f.Length; }
        return d;
    }

    /// <summary>One channel's frame: the scale and the 32 nibbles (the unlisted ones are 0).</summary>
    static byte[] Frame(int scale, params int[] nibbles)
    {
        var f = new byte[18];
        BinaryPrimitives.WriteUInt16BigEndian(f, (ushort)scale);
        for (int i = 0; i < nibbles.Length; i++)
        {
            int n = nibbles[i] & 15;
            f[2 + i / 2] |= (byte)(i % 2 == 0 ? n << 4 : n);
        }
        return f;
    }

    static byte[] EndFrame(int channels) => Enumerable.Range(0, channels).SelectMany(_ => Frame(0x8001)).ToArray();

    [Test]
    public void TheHeaderGivesTheFormatsFacts()
    {
        var h = AdxDecoder.ReadHeader(Adx(2, 44100, 1234, 500, 0));
        Assert.That(h, Is.Not.Null);
        Assert.That((h.DataOffset, h.FrameSize, h.Channels, h.Rate, h.TotalSamples, h.HighPass), Is.EqualTo((0x20, 18, 2, 44100, 1234, 500)));
    }

    [Test]
    public void OnlyUnencryptedStandardAdxIsRead()
    {
        Assert.That(AdxDecoder.ReadHeader(Adx(2, 44100, 64, 500, 8)), Is.Null, "flags 8: encrypted");
        var other = Adx(2, 44100, 64, 500, 0); other[4] = 4;
        Assert.That(AdxDecoder.ReadHeader(other), Is.Null, "encoding 4: not standard ADX");
        var bits = Adx(2, 44100, 64, 500, 0); bits[6] = 8;
        Assert.That(AdxDecoder.ReadHeader(bits), Is.Null, "8 bit samples");
        var magic = Adx(2, 44100, 64, 500, 0); magic[0] = 0;
        Assert.That(AdxDecoder.ReadHeader(magic), Is.Null);
        Assert.That(AdxDecoder.ReadHeader(new byte[10]), Is.Null, "shorter than the header");
        Assert.That(AdxDecoder.Decode(other), Is.Null);
        var channels = Adx(0, 44100, 64, 500, 0);
        Assert.That(AdxDecoder.ReadHeader(channels), Is.Null, "no channels");
    }

    [Test]
    public void TheCoefficientsAreTruncatedTowardZero()
    {
        // 500 Hz at 44100 Hz (every shipped movie): -3283, not the floor -3284 (checked against ffmpeg on the real movies).
        Assert.That(AdxDecoder.Coefficients(500, 44100), Is.EqualTo((7334, -3283)));
    }

    [Test]
    public void AFrameDecodesWithTheTwoPreviousSamples()
    {
        var (c0, c1) = AdxDecoder.Coefficients(500, 44100);
        // scale 100: the nibbles are 3, 0, -1 (0xF), then zeros. The first sample has no history, the next two use it.
        var pcm = AdxDecoder.Decode(Adx(1, 44100, 32, 500, 0, Frame(100, 3, 0, -1), EndFrame(1)));
        Assert.That(pcm, Is.Not.Null);
        Assert.That((pcm.Rate, pcm.Channels, pcm.Frames), Is.EqualTo((44100, 1, 32)));
        int s0 = 3 * 100;
        int s1 = (0 + c0 * s0) >> 12;
        int s2 = (-1 * 100 * 4096 + c0 * s1 + c1 * s0) >> 12;
        Assert.That(pcm.Samples.Take(3), Is.EqualTo(new[] { s0, s1, s2 }.Select(v => (short)v)));
        Assert.That(s1, Is.EqualTo(537));
        Assert.That(s2, Is.EqualTo(621));
    }

    [Test]
    public void ChannelsAreInterleavedAndKeepTheirOwnHistory()
    {
        var pcm = AdxDecoder.Decode(Adx(2, 44100, 32, 500, 0, Frame(10, 5), Frame(20, -2), EndFrame(2)));
        Assert.That(pcm.Channels, Is.EqualTo(2));
        Assert.That(pcm.Samples[0], Is.EqualTo(50));          // left, frame 0
        Assert.That(pcm.Samples[1], Is.EqualTo(-40));         // right
        Assert.That(pcm.Samples[2], Is.EqualTo((7334 * 50) >> 12));
        Assert.That(pcm.Samples[3], Is.EqualTo((7334 * -40) >> 12));
        Assert.That(pcm.Samples.Length, Is.EqualTo(64));
    }

    [Test]
    public void TheEndFrameStopsTheDecode()
    {
        // The header promises 96 samples; the end frame after the first frame ends it there.
        var pcm = AdxDecoder.Decode(Adx(1, 44100, 96, 500, 0, Frame(100, 3), EndFrame(1), Frame(100, 7)));
        Assert.That(pcm.Frames, Is.EqualTo(32));
    }

    [Test]
    public void ATruncatedFileGivesTheFramesItHas()
    {
        var full = Adx(1, 44100, 96, 500, 0, Frame(1, 1), Frame(1, 1), Frame(1, 1));
        var pcm = AdxDecoder.Decode(full.AsSpan(0, full.Length - 5));      // the third frame is cut
        Assert.That(pcm.Frames, Is.EqualTo(64));
    }

    [Test]
    public void TheLastFrameIsCutToTheHeadersSampleCount()
    {
        var pcm = AdxDecoder.Decode(Adx(1, 44100, 40, 500, 0, Frame(1, 1), Frame(1, 1), EndFrame(1)));
        Assert.That(pcm.Frames, Is.EqualTo(40));
    }

    [Test]
    public void SamplesClampToSixteenBitsAndTheHistoryKeepsTheClampedValue()
    {
        var pcm = AdxDecoder.Decode(Adx(1, 44100, 32, 500, 0, Frame(0x7FFF, 7, 7, -8), EndFrame(1)));
        Assert.That(pcm.Samples[0], Is.EqualTo(short.MaxValue));
        Assert.That(pcm.Samples[1], Is.EqualTo(short.MaxValue));
        Assert.That(pcm.Samples[2], Is.EqualTo(short.MinValue));
    }
}
