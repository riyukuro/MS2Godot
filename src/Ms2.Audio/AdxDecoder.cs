using System.Buffers.Binary;

namespace Ms2.Audio;

/// <summary>
/// Decodes CRI ADX audio, the sound of a .usm movie: the movie's @SFA stream is a whole ADX file (header, frames, end frame).
/// The client plays a movie's sound through the CRI Mana library (the VideoPlayer's MainTrack sound channel, volume set by
/// VideoProviderNetStream::SetSoundVolume @0x140025750 and UICutSceneMovie::PlayUsm @0x14190ffc0, uicutscenemovie.cpp:392-404);
/// the library's own decoder is not in the decompiled sources, so this follows the published ADX format: a 4-byte header
/// (0x80 0x00, the copyright offset), encoding 3 (standard ADX, 4-bit ADPCM), the frame size, the bit depth, the channel
/// count, the sample rate, the sample count and the high-pass cut-off, which gives the two predictor coefficients.
/// A frame holds, per channel in turn, a big-endian 16-bit scale and 32 signed 4-bit samples (the high nibble first).
/// Only unencrypted standard ADX (encoding 3, 4 bits, flags 0) is decoded; anything else gives null.
/// </summary>
public static class AdxDecoder
{
    /// <summary>The header's facts, or null if the data is not a standard unencrypted ADX file.</summary>
    public sealed record Header(int DataOffset, int FrameSize, int Channels, int Rate, int TotalSamples, int HighPass);

    public static Header ReadHeader(ReadOnlySpan<byte> d)
    {
        if (d.Length < 0x14 || d[0] != 0x80 || d[1] != 0x00) return null;
        int copyright = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(2));
        int encoding = d[4], frameSize = d[5], bits = d[6], channels = d[7];
        int rate = (int)BinaryPrimitives.ReadUInt32BigEndian(d.Slice(8));
        int total = (int)BinaryPrimitives.ReadUInt32BigEndian(d.Slice(12));
        int highPass = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(16));
        int flags = d[19];
        if (encoding != 3 || bits != 4 || frameSize != 18 || flags == 8 || channels < 1 || channels > 8 || rate <= 0 || total < 0) return null;
        int offset = copyright + 4;
        return offset > d.Length ? null : new Header(offset, frameSize, channels, rate, total, highPass);
    }

    /// <summary>The two predictor coefficients (12-bit fixed point) for a high-pass cut-off. Checked against ffmpeg's ADX decoder, which
    /// the movies decode to sample for sample (AdxDecoderTests).</summary>
    public static (int C0, int C1) Coefficients(int highPass, int rate)
    {
        double a = Math.Sqrt(2) - Math.Cos(2 * Math.PI * highPass / rate);
        double b = Math.Sqrt(2) - 1;
        double c = (a - Math.Sqrt((a + b) * (a - b))) / b;
        return ((int)(c * 8192), (int)(-(c * c) * 4096));      // truncated toward zero, as a C cast is: the second one is -3283, not -3284, at 500 Hz / 44100 Hz
    }

    /// <summary>All of the file's samples, interleaved; null if it is not a decodable ADX file. A truncated file gives the frames it has.</summary>
    public static Pcm16 Decode(ReadOnlySpan<byte> data)
    {
        var stream = AdxStream.Open(data.ToArray());
        if (stream == null) return null;
        var pcm = new short[stream.Length * stream.Channels];
        int frames = stream.Read(pcm);
        if (frames * stream.Channels != pcm.Length) Array.Resize(ref pcm, frames * stream.Channels);
        return new Pcm16 { Rate = stream.Rate, Channels = stream.Channels, Samples = pcm };
    }
}

/// <summary>
/// An ADX file decoded as it is read: a few hundred samples at a time, with the file itself (the compressed stream, 1/3.5 of the
/// PCM) as the only thing held, so a 190 s movie never has its 32 MB of PCM in memory (the client's CRI player streams the same
/// way). <see cref="AdxDecoder.Decode"/> reads one whole to the end.
/// </summary>
public sealed class AdxStream
{
    readonly byte[] data;
    readonly AdxDecoder.Header h;
    readonly int c0, c1, frames;
    readonly int[] s1, s2;
    readonly short[] block;      // one ADX frame's samples for all channels, interleaved
    int frame, blockPos, blockLen, position;
    bool ended;

    AdxStream(byte[] data, AdxDecoder.Header h)
    {
        this.data = data;
        this.h = h;
        (c0, c1) = AdxDecoder.Coefficients(h.HighPass, h.Rate);
        frames = Math.Min((data.Length - h.DataOffset) / (h.FrameSize * h.Channels), (h.TotalSamples + 31) / 32);
        s1 = new int[h.Channels];
        s2 = new int[h.Channels];
        block = new short[32 * h.Channels];
    }

    /// <summary>The stream over a standard unencrypted ADX file, or null.</summary>
    public static AdxStream Open(byte[] data)
    {
        var h = AdxDecoder.ReadHeader(data);
        return h == null ? null : new AdxStream(data, h);
    }

    public int Rate => h.Rate;
    public int Channels => h.Channels;
    /// <summary>Samples per channel: the header's count, or less if the file is cut short (an end frame can still end it earlier).</summary>
    public int Length => Math.Min(h.TotalSamples, frames * 32);
    /// <summary>Samples per channel read so far.</summary>
    public int Position => position;
    public bool Finished => ended || position >= Length;
    public double Seconds => (double)Length / h.Rate;

    /// <summary>Back to the start (a looping movie).</summary>
    public void Rewind()
    {
        frame = blockPos = blockLen = position = 0;
        ended = false;
        Array.Clear(s1);
        Array.Clear(s2);
    }

    /// <summary>Fills <paramref name="dst"/> (whole sample frames, interleaved) with the next samples; returns the sample frames written
    /// (fewer than asked at the end).</summary>
    public int Read(Span<short> dst)
    {
        int ch = h.Channels, want = dst.Length / ch, done = 0;
        while (done < want)
        {
            if (blockPos == blockLen && !NextFrame()) break;
            int n = Math.Min(want - done, blockLen - blockPos);
            block.AsSpan(blockPos * ch, n * ch).CopyTo(dst.Slice(done * ch));
            blockPos += n;
            done += n;
            position += n;
        }
        return done;
    }

    bool NextFrame()
    {
        int ch = h.Channels, step = h.FrameSize * ch;
        if (ended || frame >= frames) return false;
        int count = Math.Min(32, Length - frame * 32);
        if (count <= 0) return false;
        for (int c = 0; c < ch; c++)
        {
            var f = data.AsSpan(h.DataOffset + frame * step + c * h.FrameSize, h.FrameSize);
            int scale = BinaryPrimitives.ReadUInt16BigEndian(f);
            if (scale >= 0x8000) { ended = true; return false; }      // the end frame (0x8001)
            int a = s1[c], b = s2[c];
            for (int i = 0; i < 32; i++)
            {
                int nibble = i % 2 == 0 ? f[2 + i / 2] >> 4 : f[2 + i / 2] & 15;
                if (nibble >= 8) nibble -= 16;
                int v = Math.Clamp((nibble * scale * 4096 + c0 * a + c1 * b) >> 12, short.MinValue, short.MaxValue);
                b = a;
                a = v;
                block[i * ch + c] = (short)v;
            }
            s1[c] = a;
            s2[c] = b;
        }
        frame++;
        blockPos = 0;
        blockLen = count;
        return true;
    }
}
