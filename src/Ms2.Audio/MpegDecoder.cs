using NLayer;

namespace Ms2.Audio;

/// <summary>Interleaved 16-bit PCM.</summary>
public sealed class Pcm16
{
    public int Rate;
    public int Channels;
    public short[] Samples;
    public int Frames => Channels == 0 ? 0 : Samples.Length / Channels;
    public double Seconds => Rate == 0 ? 0 : (double)Frames / Rate;
    public byte[] ToBytes()
    {
        var b = new byte[Samples.Length * 2];
        Buffer.BlockCopy(Samples, 0, b, 0, b.Length);
        return b;
    }
}

/// <summary>
/// Decodes the banks' MPEG payloads. The client's FSB5 banks hold MPEG-1 layer II, which Godot's own mp3 decoder
/// rejects, so the payload is decoded here (NLayer, MIT) and handed to Godot as PCM.
/// </summary>
public static class MpegDecoder
{
    /// <summary>
    /// A payload's PCM. A bank payload is whole frames from its first byte, then the bank's zero padding
    /// (MpegFrameSweepTests); FMOD reads an FSB sample's frames one by one, so those are decoded frame by frame through
    /// NLayer's frame decoder. NLayer's stream reader is kept for anything else. It is not used for bank payloads because it
    /// takes a stream only after two frames in a row (the banks' one-frame stand-ins), and it refuses layer II mono frames
    /// above 192 kbps, which ISO/IEC 11172-3 lists only for two-channel modes (five ambient samples are 224 kbps mono).
    /// FMOD plays both kinds.
    /// </summary>
    public static Pcm16 Decode(byte[] mpeg)
    {
        var (frames, zeroTail) = Frames(mpeg);
        return frames > 0 && zeroTail && DecodeFrames(mpeg, frames) is { } pcm ? pcm : DecodeStream(mpeg);
    }

    /// <summary>
    /// The payload's frames one by one; null when a frame's version, layer, rate or channel mode differs from the first
    /// frame's, or for layer III (its frames borrow bytes from earlier ones, and no bank sample uses it).
    /// </summary>
    static Pcm16 DecodeFrames(byte[] mpeg, int frames)
    {
        var frame = new FrameReader(mpeg);
        if (frame.Layer == MpegLayer.LayerIII) return null;
        int channels = frame.ChannelMode == MpegChannelMode.Mono ? 1 : 2, samples = frame.SampleCount;
        var first = (frame.Version, frame.Layer, frame.SampleRate, channels);
        var output = new short[frames * samples * channels];
        var buffer = new float[samples * channels];
        var decoder = new MpegFrameDecoder();
        int at = 0, written = 0;
        for (int i = 0; i < frames; i++)
        {
            frame.At(at);
            if ((frame.Version, frame.Layer, frame.SampleRate, frame.ChannelMode == MpegChannelMode.Mono ? 1 : 2) != first) return null;
            int n = decoder.DecodeFrame(frame, buffer, 0);
            for (int k = 0; k < n; k++)
                output[written++] = (short)Math.Clamp((int)MathF.Round(buffer[k] * 32767f), short.MinValue, short.MaxValue);
            at += frame.FrameLength;
        }
        return new Pcm16 { Rate = frame.SampleRate, Channels = channels, Samples = written == output.Length ? output : output[..written] };
    }

    /// <summary>One frame of a payload, read the way NLayer's own frame reads it: bits from after the header (and its CRC).</summary>
    sealed class FrameReader : IMpegFrame
    {
        readonly byte[] b;
        int start, end, read;
        ulong bucket;
        int bits;
        Header h;

        public FrameReader(byte[] bytes) { b = bytes; At(0); }

        public void At(int at)
        {
            start = at;
            h = Parse(b, at);
            end = Math.Min(b.Length, at + h.Length);
            Reset();
        }

        public int SampleRate => h.SampleRate;
        public int SampleRateIndex => (b[start + 2] >> 2) & 3;
        public int FrameLength => h.Length;
        public int BitRate => h.Kbps * 1000;
        public MpegVersion Version => ((b[start + 1] >> 3) & 3) switch { 3 => MpegVersion.Version1, 2 => MpegVersion.Version2, _ => MpegVersion.Version25 };
        public MpegLayer Layer => (MpegLayer)(4 - ((b[start + 1] >> 1) & 3));
        public MpegChannelMode ChannelMode => (MpegChannelMode)(b[start + 3] >> 6);
        public int ChannelModeExtension => (b[start + 3] >> 4) & 3;
        public int SampleCount => h.Samples;
        public int BitRateIndex => b[start + 2] >> 4;
        public bool IsCopyrighted => (b[start + 3] & 8) != 0;
        public bool HasCrc => (b[start + 1] & 1) == 0;
        public bool IsCorrupted => false;

        public void Reset()
        {
            read = start + (HasCrc ? 6 : 4);
            bucket = 0;
            bits = 0;
        }

        /// <summary>The next bits of the frame, high bit first; past the frame's end reads zero.</summary>
        public int ReadBits(int count)
        {
            while (bits < count)
            {
                bucket = (bucket << 8) | (read < end ? b[read] : 0u);
                read++;
                bits += 8;
            }
            bits -= count;
            return (int)((bucket >> bits) & ((1UL << count) - 1));
        }
    }

    static readonly int[] V1L1 = { 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448 };
    static readonly int[] V1L2 = { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384 };
    static readonly int[] V1L3 = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 };
    static readonly int[] V2L1 = { 0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256 };
    static readonly int[] V2L23 = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 };

    /// <summary>
    /// The MPEG audio frame header at <paramref name="at"/> (ISO/IEC 11172-3 2.4.2.3, 13818-3 for MPEG-2 and 2.5): the frame's
    /// length in bytes and its PCM samples per channel; (0, 0) when there is no valid header there (also free format).
    /// </summary>
    public static (int Length, int Samples) Frame(byte[] b, int at)
    {
        var h = Parse(b, at);
        return (h.Length, h.Samples);
    }

    readonly record struct Header(int Length, int Samples, int SampleRate, int Kbps);

    static Header Parse(byte[] b, int at)
    {
        if (at < 0 || at + 4 > b.Length || b[at] != 0xff || (b[at + 1] & 0xe0) != 0xe0) return default;
        int version = (b[at + 1] >> 3) & 3, layer = (b[at + 1] >> 1) & 3;
        int rateIndex = b[at + 2] >> 4, srIndex = (b[at + 2] >> 2) & 3, pad = (b[at + 2] >> 1) & 1;
        if (version == 1 || layer == 0 || rateIndex is 0 or 15 || srIndex == 3) return default;
        int sr = new[] { 44100, 48000, 32000 }[srIndex] >> (version == 3 ? 0 : version == 2 ? 1 : 2);
        bool v1 = version == 3;
        int kbps = (layer, v1) switch
        {
            (3, true) => V1L1[rateIndex], (2, true) => V1L2[rateIndex], (1, true) => V1L3[rateIndex],
            (3, false) => V2L1[rateIndex], _ => V2L23[rateIndex],
        };
        int bps = kbps * 1000;
        return layer switch
        {
            3 => new Header((12 * bps / sr + pad) * 4, 384, sr, kbps),
            2 => new Header(144 * bps / sr + pad, 1152, sr, kbps),
            _ => new Header((v1 ? 144 : 72) * bps / sr + pad, v1 ? 1152 : 576, sr, kbps),
        };
    }

    /// <summary>The frames from the start of a payload, and whether every byte after the last one is zero (the bank's padding).</summary>
    public static (int Frames, bool ZeroTail) Frames(byte[] b)
    {
        int at = 0, n = 0;
        for (var f = Frame(b, 0); f.Length > 0 && at + f.Length <= b.Length; f = Frame(b, at)) { at += f.Length; n++; }
        bool zero = true;
        for (int i = at; i < b.Length && zero; i++) zero = b[i] == 0;
        return (n, zero);
    }

    internal static Pcm16 DecodeStream(byte[] mpeg)
    {
        using var file = new MpegFile(new MemoryStream(mpeg));
        var pcm = new Pcm16 { Rate = file.SampleRate, Channels = file.Channels };
        var all = new List<short>((int)Math.Min(file.Length / 4 + 4096, int.MaxValue / 4));
        var buf = new float[4096 * file.Channels];
        int n;
        while ((n = file.ReadSamples(buf, 0, buf.Length)) > 0)
            for (int i = 0; i < n; i++)
                all.Add((short)Math.Clamp((int)MathF.Round(buf[i] * 32767f), short.MinValue, short.MaxValue));
        pcm.Samples = all.ToArray();
        return pcm;
    }

    public static Pcm16 Decode(SoundClip clip) => Decode(clip.Mp3);
}
