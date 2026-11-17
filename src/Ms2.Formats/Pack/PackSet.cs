using System.IO.Compression;
using System.Reflection;
using Maple2.File.IO;
using Maple2.File.IO.Crypto.Common;

namespace Ms2.Formats.Pack;

/// <summary>
/// The player's own client data: opens m2d/m2h archives under the client's Data folder on demand and reads
/// entries by archive-relative name (case-insensitive).
/// </summary>
public sealed class PackSet : IDisposable
{
    public string DataDir { get; }
    readonly Dictionary<string, Archive> open = new(StringComparer.OrdinalIgnoreCase);
    readonly object gate = new();

    sealed class Archive
    {
        public M2dReader Reader;
        public string Path;
        public Dictionary<string, PackFileEntry> ByName;
        public Dictionary<string, string> ByBaseName;     // lower-case file name -> entry name (first wins)
    }

    public PackSet(string dataDir)
    {
        if (!Directory.Exists(Path.Combine(dataDir, "Resource")))
            throw new DirectoryNotFoundException($"not a client Data folder (no Resource/ in it): {dataDir}");
        DataDir = dataDir;
    }

    /// <param name="archive">archive path relative to the Data folder without extension, e.g. "Resource/Model/Character"</param>
    Archive Get(string archive)
    {
        lock (gate)
        {
            if (open.TryGetValue(archive, out var a)) return a;
            var path = Path.Combine(DataDir, archive.Replace('\\', '/') + ".m2d");
            if (!File.Exists(path)) throw new FileNotFoundException("archive not found", path);
            var r = new M2dReader(path);
            a = new Archive
            {
                Reader = r,
                Path = path,
                ByName = new Dictionary<string, PackFileEntry>(StringComparer.OrdinalIgnoreCase),
                ByBaseName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            };
            foreach (var e in r.Files)
            {
                var name = e.Name.Replace('\\', '/');
                a.ByName[name] = e;
                a.ByBaseName.TryAdd(Path.GetFileName(name), name);
            }
            open[archive] = a;
            return a;
        }
    }

    public IEnumerable<string> List(string archive) => Get(archive).ByName.Keys;

    public bool Exists(string archive, string entry) => Get(archive).ByName.ContainsKey(entry.Replace('\\', '/'));

    public byte[] Read(string archive, string entry)
    {
        var a = Get(archive);
        if (!a.ByName.TryGetValue(entry.Replace('\\', '/'), out var e))
            throw new FileNotFoundException($"{entry} not in {archive}");
        if (((uint)e.FileHeader.BufferFlag & XorFlag) == XorFlag) return ReadXor(a, e);
        lock (gate) return a.Reader.GetBytes(e);
    }

    // Encryption.Xor (0xff000000) contains the Aes bits (0xee000000), so the reader's decrypt takes such entries for
    // AES and returns garbage (the movie archive, Resource/Movie, stores its .usm files this way). They are read here
    // with the package's XOR routine, which it does not expose.
    const uint XorFlag = 0xff000000, ZlibFlag = 9;
    static MethodInfo encryptXor;

    static byte[] ReadXor(Archive a, PackFileEntry e)
    {
        var h = e.FileHeader;
        var buf = new byte[h.EncodedFileSize];
        using (var fs = new FileStream(a.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            fs.Position = (long)h.Offset;
            fs.ReadExactly(buf);
        }
        encryptXor ??= typeof(M2dReader).Assembly.GetType("Maple2.File.IO.Crypto.CryptoManager")
            ?.GetMethod("EncryptXor", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
            ?? throw new NotSupportedException("the m2d package has no XOR routine");
        buf = (byte[])encryptXor.Invoke(null, new object[] { h.Version, buf, (uint)h.EncodedFileSize, (uint)h.CompressedFileSize });
        if (((uint)h.BufferFlag & ZlibFlag) == ZlibFlag)
        {
            using var z = new ZLibStream(new MemoryStream(buf), CompressionMode.Decompress);
            using var ms = new MemoryStream();
            z.CopyTo(ms);
            buf = ms.ToArray();
        }
        return buf;
    }

    /// <summary>Finds an entry by file name alone (texture references carry no folder). Returns null if absent.</summary>
    public string FindByFileName(string archive, string fileName)
    {
        var a = Get(archive);
        return a.ByBaseName.TryGetValue(Path.GetFileName(fileName.Replace('\\', '/')), out var n) ? n : null;
    }

    public void Dispose()
    {
        lock (gate)
        {
            foreach (var a in open.Values) a.Reader.Dispose();
            open.Clear();
        }
    }
}
