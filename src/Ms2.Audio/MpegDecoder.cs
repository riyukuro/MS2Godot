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
    public static Pcm16 Decode(byte[] mpeg)
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
