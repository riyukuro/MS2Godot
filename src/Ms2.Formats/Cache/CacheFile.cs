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
        var tmp = path + TmpMark + Environment.ProcessId;
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(head);
                fs.Write(body);
                fs.Flush(true);
            }
            File.Move(tmp, path, true);
        }
        catch
        {
            // a full disk or a file in the way: no partial file is left (a process stopped here leaves its file to
            // CacheStore's sweep of the dead process's files)
            try { File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>What a file being written has after its name, before the writing process's id.</summary>
    public const string TmpMark = ".tmp";

    /// <summary>The body of the file at <paramref name="path"/> when it is whole and built for <paramref name="key"/>; else null.
    /// The body is read once, into the array returned.</summary>
    public static byte[] Read(string path, string key)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
            if (Head(fs, key) is not { } h) return null;
            var body = new byte[h.len];
            fs.ReadExactly(body);
            return Crc32C(body) == h.crc ? body : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>The file at <paramref name="path"/> is whole (its body matches its checksum) and built for <paramref name="key"/>.</summary>
    public static bool IsWhole(string path, string key) => Read(path, key) != null;

    /// <summary>
    /// The file at <paramref name="path"/> is a cache file of <paramref name="key"/> whose length is the one its header
    /// states (its header alone is read: a launch's check; the body's checksum is checked when the body is read).
    /// </summary>
    public static bool IsCurrent(string path, string key)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1);
            return Head(fs, key) is not null;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Reads the header of a cache file of <paramref name="key"/> (the stream is left at the body): the body's length and
    /// checksum, or null when it is not one, or its length is not what is left of the file.
    /// </summary>
    static (int len, uint crc)? Head(FileStream fs, string key)
    {
        var k = Encoding.UTF8.GetBytes(key);
        var h = new byte[12 + k.Length + 12];
        if (fs.Length < h.Length || fs.ReadAtLeast(h, h.Length, throwOnEndOfStream: false) != h.Length) return null;
        if (BinaryPrimitives.ReadUInt32LittleEndian(h) != Magic || BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(4)) != Format) return null;
        if (BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(8)) != k.Length || !h.AsSpan(12, k.Length).SequenceEqual(k)) return null;
        long len = BinaryPrimitives.ReadInt64LittleEndian(h.AsSpan(12 + k.Length));
        uint crc = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(12 + k.Length + 8));
        if (len != fs.Length - h.Length || len > Array.MaxLength) return null;
        return ((int)len, crc);
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
