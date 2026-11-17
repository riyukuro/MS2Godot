using System.Buffers.Binary;

namespace Ms2.Video.Usm;

/// <summary>
/// A CRI USM movie container, demuxed: the CRID directory table and, per stream, its header table and its data
/// payloads joined in order (@SFV: an MPEG-1 video elementary stream; @SFA: audio, kept raw).
/// <para>
/// Every chunk is a 4-byte signature, a big-endian u32 size of the rest, then a 24-byte header: [1] payload offset
/// (from byte 8), [2..3] padding at the end, [4] channel, [7] &amp; 3 data type (0 data, 1 header table, 2 section
/// end, 3 seek table), [8..11] frame time, [12..15] frame rate base.
/// </para>
/// </summary>
public sealed class UsmFile
{
    public const uint SigCrid = 0x43524944, SigVideo = 0x40534656, SigAudio = 0x40534641;   // CRID, @SFV, @SFA

    public enum ChunkType { Data = 0, Header = 1, SectionEnd = 2, Seek = 3 }

    /// <summary>One elementary stream of one channel.</summary>
    public sealed class Stream
    {
        public uint Signature;
        public int Channel;
        /// <summary>The stream's header table (VIDEO_HDRINFO / AUDIO_HDRINFO), if it had one.</summary>
        public UtfTable Header;
        /// <summary>The data payloads joined.</summary>
        public byte[] Data = Array.Empty<byte>();
        public int Length;
        public int DataChunks;
    }

    /// <summary>CRIUSF_DIR_STREAM: row 0 is the whole file, then one row per stream (filename, filesize, stmid, chno, ...).</summary>
    public UtfTable Directory { get; private set; }
    public List<Stream> Video { get; } = new();
    public List<Stream> Audio { get; } = new();
    /// <summary>Chunks of unknown signatures (skipped).</summary>
    public int UnknownChunks { get; private set; }

    public bool HasAudio => Audio.Count > 0;

    /// <summary>Demuxes a whole .usm file.</summary>
    public static UsmFile Read(byte[] data)
    {
        var usm = new UsmFile();
        var builders = new Dictionary<(uint, int), (Stream S, MemoryStream Ms)>();
        int i = 0;
        while (i + 32 <= data.Length)
        {
            uint sig = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i));
            int size = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i + 4));
            if (size < 24 || i + 8 + size > data.Length) break;
            int payloadOffset = data[i + 9];
            int padding = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(i + 10));
            int channel = data[i + 12];
            var type = (ChunkType)(data[i + 15] & 3);
            int start = i + 8 + payloadOffset, length = size - payloadOffset - padding;
            i += 8 + size;
            if (length < 0 || start + length > data.Length) continue;

            if (sig == SigCrid)
            {
                if (type == ChunkType.Header && usm.Directory == null) usm.Directory = TryTable(data.AsSpan(start, length));
                continue;
            }
            if (sig != SigVideo && sig != SigAudio) { usm.UnknownChunks++; continue; }

            if (!builders.TryGetValue((sig, channel), out var b))
            {
                b = (new Stream { Signature = sig, Channel = channel }, new MemoryStream());
                builders[(sig, channel)] = b;
                (sig == SigVideo ? usm.Video : usm.Audio).Add(b.S);
            }
            switch (type)
            {
                case ChunkType.Header:
                    b.S.Header ??= TryTable(data.AsSpan(start, length));
                    break;
                case ChunkType.Data:
                    b.Ms.Write(data, start, length);
                    b.S.DataChunks++;
                    break;
            }
        }
        foreach (var (s, ms) in builders.Values)
        {
            s.Data = ms.GetBuffer();
            s.Length = (int)ms.Length;
        }
        return usm;
    }

    static UtfTable TryTable(ReadOnlySpan<byte> payload)
    {
        try { return UtfTable.Read(payload); }
        catch (Exception e) when (e is InvalidDataException or ArgumentOutOfRangeException) { return null; }
    }
}
