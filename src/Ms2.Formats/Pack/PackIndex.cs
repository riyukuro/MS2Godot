using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Maple2.File.IO;
using Maple2.File.IO.Crypto.Common;
using Maple2.File.IO.Crypto.Stream;
using Ms2.Formats.Cache;

namespace Ms2.Formats.Pack;

/// <summary>
/// The entry list of one archive (its .m2h index: names and the place, size and coding of each entry). The package's
/// reader decrypts and parses the whole index each time an archive is opened; here the list it gives is kept per install
/// as a cache file of the data folder's store (<see cref="CacheStore.Of"/>), keyed by the SHA-1 of the archive's .m2h
/// (the list is a function of that file alone) and the build of this code and of the package. A missing, stale or damaged
/// file opens the archive with the package's reader as before, and writes the file.
/// </summary>
public static class PackIndex
{
    /// <summary>One entry: its name (with '/' separators) and the header fields the reads use.</summary>
    public sealed class Entry : IPackFileHeader
    {
        public string Name { get; init; }
        /// <summary>The entry's place in the package reader's list (that reader is opened again only for a fallback read).</summary>
        public int Ordinal { get; init; }
        public PackVersion Version { get; init; }
        public Encryption BufferFlag { get; set; }
        public int FileIndex { get; set; }
        public ulong Offset { get; set; }
        public uint EncodedFileSize { get; set; }
        public ulong CompressedFileSize { get; set; }
        public ulong FileSize { get; set; }
        public void Encode(BinaryWriter pWriter) => throw new NotSupportedException("an index entry is read only");

        public override string ToString() =>
            $"{Ordinal} {Name} {Version} {(uint)BufferFlag:x8} {FileIndex} {Offset} {EncodedFileSize} {CompressedFileSize} {FileSize}";
    }

    /// <summary>The entries the package's reader lists, in its order.</summary>
    public static Entry[] FromReader(M2dReader r)
    {
        var list = new Entry[r.Files.Count];
        for (int i = 0; i < list.Length; i++)
        {
            var e = r.Files[i];
            var h = e.FileHeader;
            list[i] = new Entry
            {
                Name = e.Name.Replace('\\', '/'), Ordinal = i, Version = h.Version, BufferFlag = h.BufferFlag, FileIndex = h.FileIndex,
                Offset = h.Offset, EncodedFileSize = h.EncodedFileSize, CompressedFileSize = h.CompressedFileSize, FileSize = h.FileSize,
            };
        }
        return list;
    }

    /// <summary>The package reader's own entry of <paramref name="e"/>.</summary>
    public static PackFileEntry PackageEntry(M2dReader r, Entry e)
    {
        if (e.Ordinal < r.Files.Count && r.Files[e.Ordinal].Name.Replace('\\', '/') == e.Name) return r.Files[e.Ordinal];
        return r.Files.Last(f => f.Name.Replace('\\', '/') == e.Name);
    }

    static Entry[] Parse(string m2d)
    {
        using var r = new M2dReader(m2d);
        return FromReader(r);
    }

    /// <summary>The entries of the archive at <paramref name="m2d"/> (archive-relative name <paramref name="archive"/>).</summary>
    public static Entry[] Load(PackSet packs, string archive, string m2d)
    {
        // The package's reader sets the opening thread's culture to en-US; the cached open does the same, so the threads
        // that open archives format and parse numbers as they do with the reader.
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        var store = CacheStore.Of?.Invoke(packs);
        string m2hHash = null;
        if (store != null && CacheStore.HashOf != null)
            try { m2hHash = CacheStore.HashOf(Path.ChangeExtension(m2d, ".m2h")); }
            catch (IOException) { }
        if (m2hHash == null) return Parse(m2d);
        var t = store.Get(Piece(archive), m2hHash, Build, () => Parse(m2d), Write, Read, Diff, out var outcome, onlyConfig: true);
        CacheStore.Logged?.Invoke(outcome);
        return t;
    }

    /// <summary>Writes the index file of the archive when it is not there (the setup step); true when it was built.</summary>
    public static bool BuildCache(string archive, string m2d, CacheStore store)
    {
        var m2hHash = CacheStore.HashOf?.Invoke(Path.ChangeExtension(m2d, ".m2h"));
        if (m2hHash == null) return false;
        return store.Ensure(Piece(archive), m2hHash, Build, () => Parse(m2d), Write, onlyConfig: true);
    }

    /// <summary>The archives of a data folder: each .m2d with its .m2h, as the folder-relative name without extension.</summary>
    public static IEnumerable<string> Archives(string dataDir) =>
        Directory.EnumerateFiles(dataDir, "*.m2h", new EnumerationOptions { RecurseSubdirectories = true, MatchCasing = MatchCasing.CaseInsensitive })
            .Where(h => File.Exists(Path.ChangeExtension(h, ".m2d")))
            .Select(h => Path.GetRelativePath(dataDir, Path.ChangeExtension(h, null)).Replace('\\', '/'))
            .OrderBy(n => n, StringComparer.Ordinal);

    static string Piece(string archive) => "index-" + archive.Replace('\\', '/').Replace('/', '_').ToLowerInvariant();

    static string Build => CacheStore.Build(typeof(PackIndex).Assembly, typeof(M2dReader).Assembly);

    // The body: the entry count, then per entry the header fields and the name, stored as the length of the prefix it
    // shares with the previous name plus the rest (UTF-8): names are listed folder by folder, so most of each is shared.
    static byte[] Write(Entry[] list)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        w.Write(list.Length);
        byte[] prev = Array.Empty<byte>();
        foreach (var e in list)
        {
            w.Write((uint)e.Version);
            w.Write((uint)e.BufferFlag);
            w.Write(e.FileIndex);
            w.Write(e.Offset);
            w.Write(e.EncodedFileSize);
            w.Write(e.CompressedFileSize);
            w.Write(e.FileSize);
            var name = Encoding.UTF8.GetBytes(e.Name);
            int shared = 0, max = Math.Min(Math.Min(prev.Length, name.Length), ushort.MaxValue);
            while (shared < max && prev[shared] == name[shared]) shared++;
            // a cut inside a multi-byte character would still decode right (the bytes are joined before decoding)
            w.Write((ushort)shared);
            w.Write(name.Length - shared);
            w.Write(name, shared, name.Length - shared);
            prev = name;
        }
        w.Flush();
        return ms.ToArray();
    }

    static Entry[] Read(byte[] body)
    {
        var s = body.AsSpan();
        int o = 0;
        int count = BinaryPrimitives.ReadInt32LittleEndian(s);
        o += 4;
        if (count < 0 || count > body.Length / 46) throw new InvalidDataException("index: bad entry count");
        var list = new Entry[count];
        var name = new byte[256];
        int len = 0;
        for (int i = 0; i < count; i++)
        {
            if (o + 46 > body.Length) throw new InvalidDataException("index: short body");
            var version = (PackVersion)BinaryPrimitives.ReadUInt32LittleEndian(s[o..]);
            var flag = (Encryption)BinaryPrimitives.ReadUInt32LittleEndian(s[(o + 4)..]);
            int fileIndex = BinaryPrimitives.ReadInt32LittleEndian(s[(o + 8)..]);
            ulong offset = BinaryPrimitives.ReadUInt64LittleEndian(s[(o + 12)..]);
            uint encoded = BinaryPrimitives.ReadUInt32LittleEndian(s[(o + 20)..]);
            ulong compressed = BinaryPrimitives.ReadUInt64LittleEndian(s[(o + 24)..]);
            ulong size = BinaryPrimitives.ReadUInt64LittleEndian(s[(o + 32)..]);
            int shared = BinaryPrimitives.ReadUInt16LittleEndian(s[(o + 40)..]);
            int rest = BinaryPrimitives.ReadInt32LittleEndian(s[(o + 42)..]);
            o += 46;
            if (shared > len || rest < 0 || o + rest > body.Length) throw new InvalidDataException("index: bad name");
            if (shared + rest > name.Length) Array.Resize(ref name, Math.Max(name.Length * 2, shared + rest));
            s.Slice(o, rest).CopyTo(name.AsSpan(shared));
            o += rest;
            len = shared + rest;
            list[i] = new Entry
            {
                Name = Encoding.UTF8.GetString(name, 0, len), Ordinal = i, Version = version, BufferFlag = flag, FileIndex = fileIndex,
                Offset = offset, EncodedFileSize = encoded, CompressedFileSize = compressed, FileSize = size,
            };
        }
        if (o != body.Length) throw new InvalidDataException("index: trailing bytes");
        return list;
    }

    static string Diff(Entry[] a, Entry[] b)
    {
        if (a.Length != b.Length) return $"{a.Length} entries vs {b.Length}";
        for (int i = 0; i < a.Length; i++)
            if (a[i].ToString() != b[i].ToString()) return $"entry {i}: {a[i]} vs {b[i]}";
        return null;
    }
}
