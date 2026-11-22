using System.Buffers.Text;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Reflection;
using Microsoft.Win32.SafeHandles;
using Maple2.File.IO;
using Maple2.File.IO.Crypto.Common;
using Maple2.File.IO.Crypto.Keys;
using Maple2.File.IO.Crypto.Stream;

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
        public M2dReader Reader;                          // the package's reader, opened only for a read that falls back to it
        public string Path;
        public Dictionary<string, PackIndex.Entry> ByName;
        public Dictionary<string, string> ByBaseName;     // lower-case file name -> entry name (first wins)
        public SafeFileHandle Handle;                     // the archive file, opened on the first read of its own
    }

    static PackSet session;
    static readonly object sessionLock = new();

    /// <summary>The pack set of this session: the client opens its archives once and keeps them across map changes, so every field
    /// load of the same Data folder gets the same one. It holds only the archive index, the open readers and pure-data caches.</summary>
    public static PackSet Shared(string dataDir)
    {
        lock (sessionLock)
        {
            if (session == null || !string.Equals(session.DataDir, dataDir, StringComparison.Ordinal)) session = new PackSet(dataDir);
            return session;
        }
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
        // The index of one archive takes tens of milliseconds to open. It is built under a lock of its own, so other
        // archives open (and open ones are read) meanwhile; the pack set's lock is held only for the table lookups.
        object mine;
        lock (gate)
        {
            if (open.TryGetValue(archive, out var done)) return done;
            if (!opening.TryGetValue(archive, out mine)) opening[archive] = mine = new object();
        }
        lock (mine)
        {
            lock (gate) if (open.TryGetValue(archive, out var done)) return done;
            var path = Path.Combine(DataDir, archive.Replace('\\', '/') + ".m2d");
            if (!File.Exists(path)) throw new FileNotFoundException("archive not found", path);
            var entries = PackIndex.Load(this, archive, path);
            var a = new Archive
            {
                Path = path,
                ByName = new Dictionary<string, PackIndex.Entry>(entries.Length, StringComparer.OrdinalIgnoreCase),
                ByBaseName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            };
            foreach (var e in entries)
            {
                a.ByName[e.Name] = e;
                a.ByBaseName.TryAdd(Path.GetFileName(e.Name), e.Name);
            }
            lock (gate) open[archive] = a;
            return a;
        }
    }

    readonly Dictionary<string, object> opening = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Opens the named archives on background threads, side by side, so later reads find them open. A name that
    /// is missing or fails to open is left for the read that needs it to report.</summary>
    public void Prefetch(params string[] archives)
    {
        foreach (var name in archives)
            System.Threading.Tasks.Task.Run(() => { try { Get(name); } catch (Exception) { } });
    }

    public IEnumerable<string> List(string archive) => Get(archive).ByName.Keys;

    public bool Exists(string archive, string entry) => Get(archive).ByName.ContainsKey(entry.Replace('\\', '/'));

    public byte[] Read(string archive, string entry)
    {
        var a = Get(archive);
        if (!a.ByName.TryGetValue(entry.Replace('\\', '/'), out var e))
            throw new FileNotFoundException($"{entry} not in {archive}");
        if (((uint)e.BufferFlag & XorFlag) == XorFlag) return ReadXor(a, e);
        return ReadPlain(a, e);
    }

    /// <summary>The entry's bytes as <see cref="Read"/> gives them, as a stream that inflates them as it is read (no array of
    /// the whole entry). A stream whose inflated length is not the entry's size throws <see cref="InvalidDataException"/>
    /// at its end, where <see cref="Read"/> would fall back to the package's reader.</summary>
    public Stream OpenRead(string archive, string entry)
    {
        var a = Get(archive);
        if (!a.ByName.TryGetValue(entry.Replace('\\', '/'), out var e))
            throw new FileNotFoundException($"{entry} not in {archive}");
        if (((uint)e.BufferFlag & XorFlag) != XorFlag && ((uint)e.BufferFlag & ZlibFlag) == ZlibFlag && Decrypted(a, e) is { } plain)
            return new InflateStream(plain, (long)e.FileSize);
        return new MemoryStream(Read(archive, entry), false);
    }

    /// <summary>An entry's inflate, checked against its size: more bytes, or fewer at the end, throw.</summary>
    internal sealed class InflateStream : Stream
    {
        readonly ZLibStream z;
        readonly long size;
        long read;
        public InflateStream(byte[] deflated, long size) { z = new ZLibStream(new MemoryStream(deflated, false), CompressionMode.Decompress); this.size = size; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            int n = z.Read(buffer);
            read += n;
            if (read > size || (n == 0 && buffer.Length > 0 && read != size)) throw new InvalidDataException($"inflated {read} bytes of an entry of {size}");
            return n;
        }
        protected override void Dispose(bool disposing) { if (disposing) z.Dispose(); base.Dispose(disposing); }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => size;
        public override long Position { get => read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // M2dReader.GetBytes allocates about six times the entry's size on the way (copies of the encrypted block, the
    // decrypted block, a growing inflate buffer). The same bytes come from the package's own decrypt (which is kept: the
    // cipher is not reimplemented here) over exactly the entry's encoded bytes, then an inflate into a buffer of the
    // exact size. Anything unexpected falls back to GetBytes, which is the baseline.
    static MethodInfo decrypt;
    static bool noDecrypt;

    // The package's AES entries are base64 text of a CTR-style stream: keystream block i is AES-ECB(key) of the 128-bit
    // big-endian counter iv + i (CipherKeys.GetKeyAndIV gives key and iv), xored over the decoded bytes. The package makes one
    // transform call and one 16-byte array per block and runs over the base64 length (a third more blocks than bytes). Here
    // the same standard AES (System.Security.Cryptography.Aes, AES-NI) makes exactly the needed blocks in one call and the
    // xor is vectorized. Returns null for anything else (not an AES entry, any exception): the package's decrypt then runs.
    // FastAesMisses counts every entry the platform-AES route did not decrypt (a null return for any reason); Fallbacks every
    // read that ended in the package's GetBytes. Zero of both over a test run means every entry took the fast path.
    internal static long FastAesHits, FastAesMisses, Fallbacks;

    static byte[] Miss() { Interlocked.Increment(ref FastAesMisses); return null; }

    static byte[] FastAes(IPackFileHeader h, Encryption flag, byte[] src)
    {
        try
        {
            if (((uint)flag & 0xee000000) != 0xee000000) return Miss();
            CipherKeys.GetKeyAndIV(h.Version, (uint)h.CompressedFileSize, out var key, out var iv);
            if (key == null || iv == null || iv.Length != 16 || (key.Length != 16 && key.Length != 24 && key.Length != 32)) return Miss();
            var dec = new byte[Base64.GetMaxDecodedFromUtf8Length(src.Length)];
            if (Base64.DecodeFromUtf8(src, dec, out var used, out var written) != System.Buffers.OperationStatus.Done || used != src.Length) return Miss();
            if (written != dec.Length) Array.Resize(ref dec, written);
            int blocks = (written + 15) / 16;
            var ks = new byte[blocks * 16];
            for (int b = 0; b < blocks; b++)
            {
                iv.CopyTo(ks, b * 16);
                for (int k = 15; k >= 0 && ++iv[k] == 0; k--) { }
            }
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.EncryptEcb(ks, ks, PaddingMode.None);
            }
            int i = 0, vs = Vector<byte>.Count;
            for (; i + vs <= written; i += vs)
                (new Vector<byte>(dec, i) ^ new Vector<byte>(ks, i)).CopyTo(dec, i);
            for (; i < written; i++) dec[i] ^= ks[i];
            Interlocked.Increment(ref FastAesHits);
            return dec;
        }
        catch (Exception) { return Miss(); }
    }

    byte[] ReadPlain(Archive a, PackIndex.Entry e)
    {
        var h = e;
        try
        {
            var plain = Decrypted(a, e);
            if (plain != null)
            {
                if (((uint)h.BufferFlag & ZlibFlag) != ZlibFlag) return plain.Length == (int)h.FileSize ? plain : Fallback(a, e);
                var output = new byte[(int)h.FileSize];
                using var z = new ZLibStream(new MemoryStream(plain, false), CompressionMode.Decompress);
                z.ReadExactly(output);
                if (z.Read(new byte[1], 0, 1) != 0) return Fallback(a, e);
                return output;
            }
        }
        catch (Exception) { }
        return Fallback(a, e);
    }

    /// <summary>The entry's decrypted bytes (deflated when its flag has the zlib bit), or null where only the package's
    /// reader can read it.</summary>
    byte[] Decrypted(Archive a, PackIndex.Entry e)
    {
        var h = e;
        try
        {
            if (!noDecrypt)
            {
                decrypt ??= typeof(M2dReader).Assembly.GetType("Maple2.File.IO.Crypto.CryptoManager")
                    ?.GetMethod("Decrypt", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                var ps = decrypt?.GetParameters();
                if (ps == null || ps.Length != 5 || ps[0].ParameterType != typeof(PackVersion) || ps[1].ParameterType != typeof(uint)
                    || ps[2].ParameterType != typeof(uint) || ps[3].ParameterType != typeof(Encryption) || ps[4].ParameterType != typeof(byte[]))
                    noDecrypt = true;
            }
            // an empty entry goes to the package reader, whatever it does with it (none in the KMS2 archives: 0 of 210645)
            if (!noDecrypt && h.EncodedFileSize > 0 && h.FileSize > 0 && h.FileSize <= int.MaxValue)
            {
                var buf = new byte[h.EncodedFileSize];
                byte[] plain;
                lock (gate)
                {
                    a.Handle ??= File.OpenHandle(a.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    if (RandomAccess.Read(a.Handle, buf, (long)h.Offset) != buf.Length) return null;
                }
                var flag = (Encryption)((uint)h.BufferFlag & ~ZlibFlag);   // the package's decrypt, without its inflate
                plain = FastAes(h, flag, buf);
                if (plain == null)
                    lock (gate) plain = (byte[])decrypt.Invoke(null, new object[] { h.Version, (uint)h.EncodedFileSize, (uint)h.CompressedFileSize, flag, buf });
                return plain;
            }
        }
        catch (Exception) { }
        return null;
    }

    byte[] Fallback(Archive a, PackIndex.Entry e)
    {
        Interlocked.Increment(ref Fallbacks);
        lock (gate)
        {
            a.Reader ??= new M2dReader(a.Path);
            return a.Reader.GetBytes(PackIndex.PackageEntry(a.Reader, e));
        }
    }

    // Encryption.Xor (0xff000000) contains the Aes bits (0xee000000), so the reader's decrypt takes such entries for
    // AES and returns garbage (the movie archive, Resource/Movie, stores its .usm files this way). They are read here
    // with the package's XOR routine, which it does not expose.
    const uint XorFlag = 0xff000000, ZlibFlag = 9;
    static MethodInfo encryptXor;

    static byte[] ReadXor(Archive a, PackIndex.Entry e)
    {
        var h = e;
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
            foreach (var a in open.Values) { a.Reader?.Dispose(); a.Handle?.Dispose(); }
            open.Clear();
        }
    }
}
