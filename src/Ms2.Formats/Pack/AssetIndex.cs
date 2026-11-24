using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using Ms2.Formats.Cache;

namespace Ms2.Formats.Pack;

/// <summary>
/// The asset-web catalog (Resource/asset-web-metadata.m2d, N-Triples): resolves the <c>urn:llid:&lt;id&gt;</c> asset ids
/// used by flat models (NifAsset, ...) to an archive and entry, e.g. "/Model/Map/Tria/Cube/x.nif" ->
/// ("Resource/Model/Map", "tria/cube/x.nif").
/// The two files (llid.nt, relpath.nt: about 30 MB of text, 83k llids in the KMS data) are read and parsed only to build
/// the catalog's per-install file (<see cref="CacheStore"/>, keyed by the archive's .m2h and .m2d hashes): a session reads
/// that file, the llids it resolves sorted for a binary search, and builds no table of its own.
/// </summary>
public sealed class AssetIndex
{
    public const string Archive = "Resource/asset-web-metadata";
    const string Piece = "asset-web";

    // The catalog is the same for every field of a pack set, so it is read once per pack set. The weak key keeps the
    // cache from outliving the pack set; the index itself holds no reference to it.
    static readonly ConditionalWeakTable<PackSet, Lazy<AssetIndex>> cache = new();

    /// <summary>The catalog of a pack set, read on first use and shared afterwards (safe to call from any thread).</summary>
    public static AssetIndex For(PackSet packs) => cache.GetValue(packs, p => new Lazy<AssetIndex>(() => new AssetIndex(p))).Value;

    /// <summary>Reads the catalog in the background so a later <see cref="For"/> finds it ready.</summary>
    public static void Prefetch(PackSet packs) => System.Threading.Tasks.Task.Run(() => { try { For(packs); } catch { } });

    readonly Table table;

    public AssetIndex(PackSet packs)
    {
        var store = CacheStore.Of?.Invoke(packs);
        var config = store == null ? null : Config(packs);
        if (config == null) { table = Parse(packs); return; }
        table = store.Get(Piece, config, Build, () => Parse(packs), t => t.Body, Table.Read, Table.Diff, out var outcome, onlyConfig: true);
        CacheStore.Logged?.Invoke(outcome);
    }

    internal AssetIndex(Table t) => table = t;

    /// <summary>Writes the catalog's file when it is not there (the setup step); true when it was built.</summary>
    public static bool BuildCache(PackSet packs, CacheStore store)
    {
        var config = Config(packs);
        return config != null && store.Ensure(Piece, config, Build, () => Parse(packs), t => t.Body, onlyConfig: true);
    }

    /// <summary>The archive's .m2h and .m2d hashes, or null when either can't be hashed (the catalog is then parsed).</summary>
    static string Config(PackSet packs)
    {
        if (CacheStore.HashOf == null) return null;
        try
        {
            var m2d = Path.Combine(packs.DataDir, Archive + ".m2d");
            return CacheStore.HashOf(Path.ChangeExtension(m2d, ".m2h")) + "," + CacheStore.HashOf(m2d);
        }
        catch (IOException) { return null; }
    }

    static string Build => CacheStore.Build(typeof(AssetIndex).Assembly);

    static Table Parse(PackSet packs)
    {
        // each file fills its own dictionary, so the two parse side by side; first entry wins within a file
        var uuidToRelpath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var llidToUuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var relpath = System.Threading.Tasks.Task.Run(() => Load(packs.Read(Archive, "relpath.nt"), (u, v) => uuidToRelpath.TryAdd(u, v)));
        Load(packs.Read(Archive, "llid.nt"), (u, v) => llidToUuid.TryAdd(v, u));
        relpath.GetAwaiter().GetResult();
        return Table.From(llidToUuid, uuidToRelpath);
    }

    /// <summary>The catalog of the two files' bytes (tests).</summary>
    internal static Table Parse(byte[] llidNt, byte[] relpathNt)
    {
        var uuidToRelpath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var llidToUuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Load(relpathNt, (u, v) => uuidToRelpath.TryAdd(u, v));
        Load(llidNt, (u, v) => llidToUuid.TryAdd(v, u));
        return Table.From(llidToUuid, uuidToRelpath);
    }

    /// <summary>
    /// The llids that resolve, each with its relpath (an llid whose uuid has no relpath resolves to nothing, so it is left
    /// out). Body: the count of GUID llids, their 16-byte keys in byte order, one more offset than keys into the UTF-8 paths,
    /// the paths; then the other llids (not in the 36-character GUID form; none in the KMS or GMS2 data) as length-prefixed
    /// pairs. An llid in the GUID form is looked up by its bytes, so case does not matter, as with the parse's
    /// OrdinalIgnoreCase keys.
    /// </summary>
    internal sealed class Table
    {
        public byte[] Body { get; private init; }
        int count, keysAt, offsAt, pathsAt;
        Dictionary<string, string> others;

        public int Count => count + others.Count;

        public static Table From(Dictionary<string, string> llidToUuid, Dictionary<string, string> uuidToRelpath)
        {
            var guids = new List<(Guid key, byte[] path)>();
            var rest = new List<(string llid, string path)>();
            foreach (var (llid, uuid) in llidToUuid)
            {
                if (!uuidToRelpath.TryGetValue(uuid, out var path)) continue;
                if (GuidForm(llid, out var g)) guids.Add((g, Encoding.UTF8.GetBytes(path)));
                else rest.Add((llid, path));
            }
            var keys = new byte[guids.Count][];
            for (int i = 0; i < keys.Length; i++) keys[i] = guids[i].key.ToByteArray();
            var order = Enumerable.Range(0, keys.Length).ToArray();
            Array.Sort(order, (a, b) => keys[a].AsSpan().SequenceCompareTo(keys[b]));
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms, Encoding.UTF8);
            w.Write(order.Length);
            foreach (var i in order) w.Write(keys[i]);
            int off = 0;
            foreach (var i in order) { w.Write(off); off += guids[i].path.Length; }
            w.Write(off);
            foreach (var i in order) w.Write(guids[i].path);
            w.Write(rest.Count);
            foreach (var (llid, path) in rest.OrderBy(x => x.llid, StringComparer.Ordinal)) { w.Write(llid); w.Write(path); }
            w.Flush();
            return Read(ms.ToArray());
        }

        public static Table Read(byte[] body)
        {
            var t = new Table { Body = body };
            int n = BinaryPrimitives.ReadInt32LittleEndian(body);
            if (n < 0 || 4L + 16L * n + 4L * (n + 1) > body.Length) throw new InvalidDataException("asset-web: bad count");
            t.count = n;
            t.keysAt = 4;
            t.offsAt = t.keysAt + 16 * n;
            t.pathsAt = t.offsAt + 4 * (n + 1);
            int end = t.pathsAt + t.Off(n);
            if (end > body.Length || end < t.pathsAt) throw new InvalidDataException("asset-web: bad paths");
            using var r = new BinaryReader(new MemoryStream(body, end, body.Length - end), Encoding.UTF8);
            int m = r.ReadInt32();
            t.others = new Dictionary<string, string>(m, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < m; i++) t.others.TryAdd(r.ReadString(), r.ReadString());
            return t;
        }

        /// <summary>The 36-character GUID form exactly (Guid parsing alone would also take the id with spaces around it).</summary>
        static bool GuidForm(string llid, out Guid g)
        {
            g = default;
            return llid.Length == 36 && Guid.TryParseExact(llid, "D", out g);
        }

        int Off(int i) => BinaryPrimitives.ReadInt32LittleEndian(Body.AsSpan(offsAt + 4 * i));

        public string Find(string llid)
        {
            if (!GuidForm(llid, out var g)) return others.TryGetValue(llid, out var p) ? p : null;
            Span<byte> key = stackalloc byte[16];
            g.TryWriteBytes(key);
            int lo = 0, hi = count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >>> 1;
                int c = Body.AsSpan(keysAt + 16 * mid, 16).SequenceCompareTo(key);
                if (c == 0) { int a = Off(mid); return Encoding.UTF8.GetString(Body, pathsAt + a, Off(mid + 1) - a); }
                if (c < 0) lo = mid + 1; else hi = mid - 1;
            }
            return null;
        }

        /// <summary>The first difference of two catalogs, or null (the check mode).</summary>
        public static string Diff(Table a, Table b) =>
            a.Body.AsSpan().SequenceEqual(b.Body) ? null : $"asset-web: {a.Count} llids vs {b.Count}, bodies differ";
    }

    // One line is "<urn:uuid:ID> <.../llid|relpath> "value" ..." : the id, then a predicate ending in /llid or /relpath, then the value.
    internal static void Load(byte[] data, Action<string, string> add)
    {
        var text = Encoding.UTF8.GetString(data);
        int pos = 0;
        while (pos < text.Length)
        {
            int end = text.IndexOfAny(LineEnds, pos);
            if (end < 0) end = text.Length;
            if (ParseLine(text.AsSpan(pos, end - pos), out var id, out var value)) add(id, value);
            pos = end + 1;
        }
    }

    static readonly char[] LineEnds = { '\n', '\r' };

    static bool ParseLine(ReadOnlySpan<char> l, out string id, out string value)
    {
        id = value = null;
        const string head = "<urn:uuid:";
        if (!l.StartsWith(head)) return false;
        int i = head.Length, s = i;
        while (i < l.Length && (Uri.IsHexDigit(l[i]) || l[i] == '-')) i++;
        if (i == s || i >= l.Length || l[i] != '>') return false;
        var uuid = l[s..i];
        i++;
        int w = i;
        while (i < l.Length && char.IsWhiteSpace(l[i])) i++;
        if (i == w || i >= l.Length || l[i] != '<') return false;
        int close = l[i..].IndexOf('>');
        if (close < 0) return false;
        var pred = l.Slice(i + 1, close - 1);
        if (!pred.EndsWith("/llid") && !pred.EndsWith("/relpath")) return false;
        i += close + 1;
        w = i;
        while (i < l.Length && char.IsWhiteSpace(l[i])) i++;
        if (i == w || i >= l.Length || l[i] != '"') return false;
        int q = l[(i + 1)..].IndexOf('"');
        if (q < 0) return false;
        id = uuid.ToString();
        value = l.Slice(i + 1, q).ToString();
        return true;
    }

    /// <summary>"/Model/Map/Tria/Cube/x.nif" for "urn:llid:085d045d-..." (or the bare id), or null.</summary>
    public string RelPath(string llid)
    {
        if (string.IsNullOrEmpty(llid)) return null;
        if (llid.StartsWith("urn:llid:", StringComparison.OrdinalIgnoreCase)) llid = llid[9..];
        return table.Find(llid);
    }

    /// <summary>Splits a catalog path into (archive relative to Data, entry): the first two folders name the archive.</summary>
    public static (string archive, string entry) Split(string relPath)
    {
        var parts = relPath.TrimStart('/').Split('/', 3);
        if (parts.Length < 3) return (null, null);
        return ($"Resource/{parts[0]}/{parts[1]}", parts[2].ToLowerInvariant());
    }

    public (string archive, string entry) Resolve(string llid)
    {
        var p = RelPath(llid);
        return p == null ? (null, null) : Split(p);
    }
}
