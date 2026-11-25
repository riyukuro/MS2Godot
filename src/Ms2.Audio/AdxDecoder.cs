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
        var h = ReadHeader(data);
        if (h == null) return null;
        var (c0, c1) = Coefficients(h.HighPass, h.Rate);
        int ch = h.Channels, step = h.FrameSize * ch;
        int frames = Math.Min((data.Length - h.DataOffset) / step, (h.TotalSamples + 31) / 32);
        int samples = Math.Min(h.TotalSamples, frames * 32);
        var pcm = new short[samples * ch];
        var s1 = new int[ch];
        var s2 = new int[ch];
        for (int f = 0; f < frames; f++)
        {
            int count = Math.Min(32, samples - f * 32);
            for (int c = 0; c < ch; c++)
            {
                var frame = data.Slice(h.DataOffset + f * step + c * h.FrameSize, h.FrameSize);
                int scale = BinaryPrimitives.ReadUInt16BigEndian(frame);
                if (scale >= 0x8000) { count = 0; break; }      // the end frame (0x8001)
                int a = s1[c], b = s2[c];
                for (int i = 0; i < 32; i++)
                {
                    int nibble = i % 2 == 0 ? frame[2 + i / 2] >> 4 : frame[2 + i / 2] & 15;
                    if (nibble >= 8) nibble -= 16;
                    int s = (nibble * scale * 4096 + c0 * a + c1 * b) >> 12;
                    s = Math.Clamp(s, short.MinValue, short.MaxValue);
                    b = a;
                    a = s;
                    if (i < count) pcm[(f * 32 + i) * ch + c] = (short)s;
                }
                s1[c] = a;
                s2[c] = b;
            }
            if (count == 0) { Array.Resize(ref pcm, f * 32 * ch); break; }
        }
        return new Pcm16 { Rate = h.Rate, Channels = ch, Samples = pcm };
    }
}
