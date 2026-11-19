using System.Buffers.Binary;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace Ms2.Formats.Cache;

/// <summary>
/// One cache file: a header (magic, format version, the key it was built for, body length, CRC-32C of the body) and the
/// body. It is written to a temporary file and renamed into place, so a reader sees a whole file or none. A file that is
/// missing, of another key, short, or whose body does not match its checksum reads as null: the caller parses instead.
/// </summary>
public static class CacheFile
{
    const uint Magic = 0x3243534d;   // "MSC2"
    const int Format = 1;

    public static void Write(string path, string key, ReadOnlySpan<byte> body)
    {
        var k = Encoding.UTF8.GetBytes(key);
        var head = new byte[4 + 4 + 4 + k.Length + 8 + 4];
        int o = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(o), Magic); o += 4;
        BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(o), Format); o += 4;
        BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(o), k.Length); o += 4;
        k.CopyTo(head, o); o += k.Length;
        BinaryPrimitives.WriteInt64LittleEndian(head.AsSpan(o), body.Length); o += 8;
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(o), Crc32C(body));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        var tmp = path + ".tmp" + Environment.ProcessId;
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(head);
            fs.Write(body);
            fs.Flush(true);
        }
        File.Move(tmp, path, true);
    }

    /// <summary>The body of the file at <paramref name="path"/> when it is whole and built for <paramref name="key"/>; else null.</summary>
    public static byte[] Read(string path, string key) =>
        ReadAll(path) is { } all && BodyAt(all, key) is var o and >= 0 ? all.AsSpan(o).ToArray() : null;

    /// <summary>The file at <paramref name="path"/> is whole (its body matches its checksum) and built for <paramref name="key"/>.</summary>
    public static bool IsWhole(string path, string key) => ReadAll(path) is { } all && BodyAt(all, key) >= 0;

    static byte[] ReadAll(string path)
    {
        try { return File.ReadAllBytes(path); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>Where the body of a whole file of <paramref name="key"/> starts in <paramref name="all"/>; -1 when it is not one.</summary>
    static int BodyAt(byte[] all, string key)
    {
        var k = Encoding.UTF8.GetBytes(key);
        int o = 0;
        if (all.Length < 12) return -1;
        if (BinaryPrimitives.ReadUInt32LittleEndian(all.AsSpan(o)) != Magic) return -1; o += 4;
        if (BinaryPrimitives.ReadInt32LittleEndian(all.AsSpan(o)) != Format) return -1; o += 4;
        int kl = BinaryPrimitives.ReadInt32LittleEndian(all.AsSpan(o)); o += 4;
        if (kl != k.Length || all.Length < o + kl + 12 || !all.AsSpan(o, kl).SequenceEqual(k)) return -1; o += kl;
        long len = BinaryPrimitives.ReadInt64LittleEndian(all.AsSpan(o)); o += 8;
        uint crc = BinaryPrimitives.ReadUInt32LittleEndian(all.AsSpan(o)); o += 4;
        if (len != all.Length - o) return -1;
        return Crc32C(all.AsSpan(o)) == crc ? o : -1;
    }

    /// <summary>The key a cache file was built for, or null when it is not a cache file.</summary>
    public static string KeyOf(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var h = new byte[12];
            if (fs.Read(h) != 12 || BinaryPrimitives.ReadUInt32LittleEndian(h) != Magic) return null;
            int kl = BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(8));
            if (kl < 0 || kl > 4096) return null;
            var k = new byte[kl];
            fs.ReadExactly(k);
            return Encoding.UTF8.GetString(k);
        }
        catch (IOException) { return null; }
    }

    static readonly uint[] table = MakeTable();

    static uint[] MakeTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int j = 0; j < 8; j++) c = (c & 1) != 0 ? 0x82f63b78 ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    /// <summary>CRC-32C (Castagnoli), with the SSE4.2 instruction where there is one.</summary>
    public static uint Crc32C(ReadOnlySpan<byte> data)
    {
        uint crc = ~0u;
        int i = 0;
        if (Sse42.X64.IsSupported)
        {
            ulong c = crc;
            for (; i + 8 <= data.Length; i += 8) c = Sse42.X64.Crc32(c, BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(i)));
            crc = (uint)c;
            for (; i < data.Length; i++) crc = Sse42.Crc32(crc, data[i]);
        }
        else
            for (; i < data.Length; i++) crc = table[(crc ^ data[i]) & 0xff] ^ (crc >> 8);
        return ~crc;
    }
}
