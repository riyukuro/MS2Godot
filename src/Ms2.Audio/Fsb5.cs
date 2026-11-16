using System.Text;

namespace Ms2.Audio;

/// <summary>One sound of an FSB5 bank (the sample bank format of FMOD Designer 4.x / FMOD Ex).</summary>
public sealed class FsbSample
{
    public int Index;
    public string Name;
    public int Frequency;
    public int Channels;
    public long Samples;          // length in PCM samples
    public long DataOffset;       // absolute file offset of the payload
    public long DataSize;
    public int LoopStart = -1;    // in samples; -1 when the sample has no loop chunk
    public int LoopEnd = -1;
    public double Seconds => Frequency > 0 ? (double)Samples / Frequency : 0;
}

/// <summary>
/// Reader of an FSB5 file. Only the header, sample table and name table are read up front; payloads are read from the file on
/// demand. The client's banks hold MPEG (mode 11): a payload is a plain mp3 frame stream.
/// </summary>
public sealed class Fsb5
{
    public const int ModeMpeg = 11;
    static readonly int[] Rates = { 4000, 8000, 11000, 11025, 16000, 22050, 24000, 32000, 44100, 48000, 96000 };

    public string Path { get; }
    public int Mode { get; private set; }
    public IReadOnlyList<FsbSample> Samples => samples;
    FsbSample[] samples;
    Dictionary<string, int> byName;

    public Fsb5(string path)
    {
        Path = path;
        using var f = File.OpenRead(path);
        var r = new BinaryReader(f);
        if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "FSB5") throw new InvalidDataException("not an FSB5 file: " + path);
        int version = r.ReadInt32();
        int n = r.ReadInt32();
        int shdr = r.ReadInt32();
        int nameSize = r.ReadInt32();
        long dataSize = r.ReadUInt32();
        Mode = r.ReadInt32();
        int hdr = version == 0 ? 64 : 60;
        f.Position = hdr;
        var sh = r.ReadBytes(shdr);
        var nt = nameSize > 0 ? r.ReadBytes(nameSize) : Array.Empty<byte>();
        long dataStart = hdr + shdr + nameSize;
        samples = new FsbSample[n];
        int p = 0;
        for (int i = 0; i < n; i++)
        {
            ulong v = BitConverter.ToUInt64(sh, p); p += 8;
            var s = new FsbSample { Index = i };
            bool next = (v & 1) != 0;
            int fi = (int)((v >> 1) & 0xf);
            s.Frequency = fi < Rates.Length ? Rates[fi] : 44100;
            s.Channels = (int)((v >> 5) & 1) + 1;
            s.DataOffset = dataStart + (long)((v >> 6) & 0xfffffff) * 16;
            s.Samples = (long)(v >> 34);
            while (next)
            {
                uint c = BitConverter.ToUInt32(sh, p); p += 4;
                next = (c & 1) != 0;
                int size = (int)((c >> 1) & 0xffffff);
                int type = (int)(c >> 25);
                if (type == 3 && size >= 8)
                {
                    s.LoopStart = BitConverter.ToInt32(sh, p);
                    s.LoopEnd = BitConverter.ToInt32(sh, p + 4);
                }
                else if (type == 1 && size >= 1) s.Channels = sh[p];
                else if (type == 2 && size >= 4) s.Frequency = BitConverter.ToInt32(sh, p);
                p += size;
            }
            samples[i] = s;
        }
        for (int i = 0; i < n; i++)
            samples[i].DataSize = (i + 1 < n ? samples[i + 1].DataOffset : dataStart + dataSize) - samples[i].DataOffset;
        if (nameSize > 0)
        {
            byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < n; i++)
            {
                int o = BitConverter.ToInt32(nt, i * 4);
                int e = Array.IndexOf(nt, (byte)0, o);
                samples[i].Name = Encoding.UTF8.GetString(nt, o, e - o);
                byName.TryAdd(samples[i].Name, i);
            }
        }
    }

    public FsbSample Find(string name) => byName != null && byName.TryGetValue(name, out var i) ? samples[i] : null;

    /// <summary>The payload of a sample; for an MPEG bank this is an mp3 file.</summary>
    public byte[] ReadPayload(FsbSample s)
    {
        using var f = File.OpenRead(Path);
        f.Position = s.DataOffset;
        var buf = new byte[s.DataSize];
        f.ReadExactly(buf);
        return buf;
    }
}
