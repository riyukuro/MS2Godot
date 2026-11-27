using System.Runtime.InteropServices;
using NLayer;

namespace Ms2.Audio;

/// <summary>
/// A bank payload's frames (whole frames from its first byte, then the bank's zero padding; MpegFrameSweepTests), decoded
/// in steps by one frame decoder, so a first part can play while the rest decodes. Every frame has the first frame's
/// version, layer, rate and channel mode (checked on the headers at <see cref="Open(byte[])"/>), so the PCM's length is
/// known before any frame is decoded: <see cref="SampleCount"/> samples, or fewer if a frame gives fewer than its header
/// says. The steps give the same samples as one decode of the whole payload: the frames go through the same decoder in
/// the same order.
/// </summary>
public sealed class MpegFrameRun
{
    readonly MpegDecoder.FrameReader frame;
    readonly MpegFrameDecoder decoder = new();
    readonly float[] buffer;
    readonly int frames;
    int next, at;
    byte[] bytes;

    MpegFrameRun(MpegDecoder.FrameReader frame, int frames, int channels)
    {
        this.frame = frame;
        this.frames = frames;
        Rate = frame.SampleRate;
        Channels = channels;
        FrameSamples = frame.SampleCount;
        buffer = new float[FrameSamples * channels];
    }

    public int Rate { get; }
    public int Channels { get; }
    /// <summary>PCM frames per MPEG frame (1152 for layer II).</summary>
    public int FrameSamples { get; }
    /// <summary>Interleaved samples of the whole payload, from its headers.</summary>
    public int SampleCount => frames * FrameSamples * Channels;
    /// <summary>The payload's length in seconds, from its headers.</summary>
    public double Seconds => (double)frames * FrameSamples / Rate;
    /// <summary>Interleaved samples decoded so far.</summary>
    public int Written { get; private set; }
    public bool Done => next == frames;

    /// <summary>The run of a bank payload; null for anything else (no whole frames from the first byte, bytes other than
    /// zeros after them, layer III, or a frame unlike the first), which <see cref="MpegDecoder.Decode(byte[])"/> decodes.</summary>
    public static MpegFrameRun Open(byte[] mpeg)
    {
        var (frames, zeroTail) = MpegDecoder.Frames(mpeg);
        return frames > 0 && zeroTail ? Open(mpeg, frames) : null;
    }

    internal static MpegFrameRun Open(byte[] mpeg, int frames)
    {
        var frame = new MpegDecoder.FrameReader(mpeg);
        if (frame.Layer == MpegLayer.LayerIII) return null;
        int channels = ChannelsOf(frame);
        var first = (frame.Version, frame.Layer, frame.SampleRate, channels);
        for (int i = 0, at = 0; i < frames; i++)
        {
            frame.At(at);
            if ((frame.Version, frame.Layer, frame.SampleRate, ChannelsOf(frame)) != first) return null;
            at += frame.FrameLength;
        }
        frame.At(0);
        return new MpegFrameRun(frame, frames, channels);
    }

    static int ChannelsOf(MpegDecoder.FrameReader frame) => frame.ChannelMode == MpegChannelMode.Mono ? 1 : 2;

    /// <summary>Decodes frames into <paramref name="output"/> (the whole payload's interleaved samples) until at least
    /// <paramref name="pcmFrames"/> PCM frames are in it or the payload ends.</summary>
    public void DecodeTo(Span<short> output, int pcmFrames)
    {
        long target = (long)pcmFrames * Channels;
        int written = Written;
        while (next < frames && written < target)
        {
            frame.At(at);
            int n = decoder.DecodeFrame(frame, buffer, 0);
            for (int k = 0; k < n; k++)
                output[written++] = (short)Math.Clamp((int)MathF.Round(buffer[k] * 32767f), short.MinValue, short.MaxValue);
            at += frame.FrameLength;
            next++;
        }
        Written = written;
    }

    /// <summary>
    /// The first <paramref name="pcmFrames"/> PCM frames (or more, to the end of their MPEG frame) as 16-bit little-endian
    /// bytes, Godot's AudioStreamWAV data: a copy of the start of the one buffer <see cref="Whole"/> then finishes.
    /// </summary>
    public byte[] Head(int pcmFrames)
    {
        DecodeBytes(pcmFrames);
        return bytes.AsSpan(0, Written * 2).ToArray();
    }

    /// <summary>The whole payload as 16-bit little-endian bytes: the run's buffer, decoded to the end (trimmed to the samples
    /// written if a frame gave fewer than its header says).</summary>
    public byte[] Whole()
    {
        DecodeBytes(int.MaxValue);
        return Written * 2 == bytes.Length ? bytes : bytes[..(Written * 2)];
    }

    /// <summary>
    /// <see cref="Whole"/> in steps of about <paramref name="stepFrames"/> PCM frames, asking <paramref name="keepGoing"/>
    /// before each: null once it says no, with the run left where it stopped (the frames not yet decoded never are).
    /// </summary>
    public byte[] Rest(int stepFrames, Func<bool> keepGoing)
    {
        while (!Done)
        {
            if (!keepGoing()) return null;
            DecodeBytes((int)Math.Min(int.MaxValue, (long)Written / Channels + stepFrames));
        }
        return Whole();
    }

    void DecodeBytes(int pcmFrames)
    {
        bytes ??= new byte[SampleCount * 2];
        // the samples' own bytes, as Pcm16.ToBytes copies them (every target the client runs on is little-endian)
        DecodeTo(MemoryMarshal.Cast<byte, short>(bytes.AsSpan()), pcmFrames);
    }
}
