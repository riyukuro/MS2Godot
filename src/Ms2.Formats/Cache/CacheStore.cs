using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Ms2.Formats.Cache;

/// <summary>
/// The cache files of one client data folder (one Xml.m2d): one file per piece and configuration, named by a hash of its
/// key. A key is the piece, its configuration (language, locale, features) and the build of the code that makes and reads
/// it (the module version ids of its assemblies), so a changed parser or table class reads as a missing file. A missing,
/// stale or damaged file is parsed again; the parse is also the reference the check mode compares the cache with.
/// </summary>
public sealed class CacheStore
{
    public string Dir { get; }

    /// <summary>MS2_UICACHE: "off" parses every piece as without a cache; "check" reads the cache and a parse and
    /// reports their first difference; anything else (the default) reads the cache.</summary>
    public static string Mode => (Environment.GetEnvironmentVariable("MS2_UICACHE") ?? "").Trim().ToLowerInvariant();

    public CacheStore(string dir) => Dir = dir;

    /// <summary>The store of a pack set's data folder (set by the game; null, or a null store, parses every table).</summary>
    public static Func<Pack.PackSet, CacheStore> Of;

    /// <summary>Told each <see cref="Load{T}"/> outcome (the game's perf log).</summary>
    public static Action<Outcome> Logged;

    /// <summary>The SHA-1 (lower-case hex) of a client file (set by the game, which remembers it per file; null keys nothing by file).</summary>
    public static Func<string, string> HashOf;

    /// <summary>
    /// <see cref="Get{T}"/> on the store of <paramref name="packs"/> (<see cref="Of"/>), or <paramref name="parse"/> when it
    /// has none: for the tables read once per pack set inside the libraries.
    /// </summary>
    public static T Load<T>(Pack.PackSet packs, string piece, string config, string build, Func<T> parse, Action<T> wire) where T : class
    {
        var store = Of?.Invoke(packs);
        if (store == null) return parse();
        var t = store.Get(piece, config, build, parse, wire, out var outcome);
        Logged?.Invoke(outcome);
        return t;
    }

    /// <summary>The build part of a key: the module version id of each assembly (deterministic builds: it changes with the code).</summary>
    public static string Build(params Assembly[] code) =>
        string.Join(",", code.Append(typeof(CacheStore).Assembly).Distinct().Select(a => a.ManifestModule.ModuleVersionId.ToString("N")[..12]));

    public static string Key(string piece, string config, string build) => $"{piece}|{build}|{config}";

    static string Short(string s) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant()[..16];

    public string PathOf(string piece, string key) => Path.Combine(Dir, $"{piece}-{Short(key)}.bin");

    /// <summary>
    /// The folder or a file directly in it was written in the last <paramref name="within"/>: a folder another process may
    /// be building (a cleanup leaves it for a later run).
    /// </summary>
    public static bool RecentlyTouched(string dir, TimeSpan within)
    {
        var since = DateTime.UtcNow - within;
        try
        {
            if (Directory.GetLastWriteTimeUtc(dir) > since) return true;
            foreach (var f in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                if (f.LastWriteTimeUtc > since) return true;
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; }
    }

    /// <summary>The file of the key is there with the key in its header (its body is checked when read).</summary>
    public bool Has(string piece, string key) => CacheFile.KeyOf(PathOf(piece, key)) == key;

    public byte[] Read(string piece, string key) => CacheFile.Read(PathOf(piece, key), key);

    /// <summary>The file of the key is there and its body matches its checksum (read whole: the setup step's check).</summary>
    public bool IsWhole(string piece, string key) => CacheFile.IsWhole(PathOf(piece, key), key);

    /// <summary>Writes the piece's file, then removes the piece's files of other builds (and of other configurations when
    /// <paramref name="onlyConfig"/>: a piece whose configuration is the hash of its input has one current file).</summary>
    public void Write(string piece, string key, ReadOnlySpan<byte> body, bool onlyConfig = false)
    {
        var path = PathOf(piece, key);
        CacheFile.Write(path, key, body);
        var build = key.Split('|')[1];
        foreach (var f in Directory.GetFiles(Dir, piece + "-*.bin"))
        {
            // the pattern also matches other pieces' files (a name that starts with this one and a dash, or one that differs
            // in case where names match without case): those stay
            if (f == path || Path.GetFileName(f).Length != piece.Length + 1 + 16 + 4) continue;
            var k = CacheFile.KeyOf(f);
            if (k != null && k.Split('|') is { Length: >= 3 } p && p[0] != piece) continue;
            if (k == null || k.Split('|') is not { Length: >= 3 } q || q[1] != build || onlyConfig)
                try { File.Delete(f); } catch (IOException) { }
        }
    }

    /// <summary>What a piece's load did, for the perf log.</summary>
    public sealed class Outcome
    {
        public string Piece, Source, Diff;
        public double ReadMs, ParseMs, WriteMs;
        public long Bytes;
        public override string ToString() =>
            $"{Piece}: {Source}" + (Bytes > 0 ? $" {Bytes / 1048576.0:F1} MB" : "") + (ReadMs > 0 ? $" read {ReadMs:F0} ms" : "")
            + (ParseMs > 0 ? $" parse {ParseMs:F0} ms" : "") + (WriteMs > 0 ? $" write {WriteMs:F0} ms" : "") + (Diff != null ? $" DIFF {Diff}" : "");
    }

    /// <summary>
    /// The piece from its cache file, else from <paramref name="parse"/> (and the file is written for the next session).
    /// <paramref name="wire"/> sets what a snapshot does not hold (the functions over the table's data) on a read table.
    /// </summary>
    public T Get<T>(string piece, string config, string build, Func<T> parse, Action<T> wire, out Outcome outcome) where T : class =>
        Get(piece, config, build, parse, t => Snapshot.Write(t), b => { var t = Snapshot.Read<T>(b); wire(t); return t; },
            (a, b) => Snapshot.Diff(a, b), out outcome);

    /// <summary><see cref="Get{T}(string,string,string,Func{T},Action{T},out Outcome)"/> with the piece's own file format:
    /// <paramref name="write"/> makes the body, <paramref name="read"/> reads it back (throwing InvalidDataException on a
    /// body it can't read), <paramref name="diff"/> gives the first difference of two tables or null.</summary>
    public T Get<T>(string piece, string config, string build, Func<T> parse, Func<T, byte[]> write, Func<byte[], T> read,
        Func<T, T, string> diff, out Outcome outcome, bool onlyConfig = false) where T : class
    {
        outcome = new Outcome { Piece = piece };
        var mode = Mode;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (mode == "off") { outcome.Source = "off"; var p = parse(); outcome.ParseMs = sw.Elapsed.TotalMilliseconds; return p; }
        var key = Key(piece, config, build);
        T cached = null;
        var body = Read(piece, key);
        if (body != null)
        {
            try { cached = read(body); outcome.Bytes = body.Length; }
            catch (Exception e) when (e is InvalidDataException or InvalidCastException or ArgumentException or TypeLoadException)
            { cached = null; outcome.Diff = "unreadable: " + e.Message; }
        }
        outcome.ReadMs = sw.Elapsed.TotalMilliseconds;
        if (cached != null && mode != "check") { outcome.Source = "cache"; return cached; }
        sw.Restart();
        var fresh = parse();
        outcome.ParseMs = sw.Elapsed.TotalMilliseconds;
        if (cached != null)
        {
            outcome.Source = "check";
            outcome.Diff = diff(cached, fresh);
            return cached;
        }
        outcome.Source = "parse";
        sw.Restart();
        try
        {
            var bytes = write(fresh);
            Directory.CreateDirectory(Dir);
            Write(piece, key, bytes, onlyConfig);
            outcome.Bytes = bytes.Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { outcome.Diff = "not written: " + e.Message; }
        outcome.WriteMs = sw.Elapsed.TotalMilliseconds;
        return fresh;
    }

    /// <summary>Builds the piece's file when it is not there whole (the setup step: a damaged body is built again here,
    /// never at a first open); true when it was built.</summary>
    public bool Ensure<T>(string piece, string config, string build, Func<T> parse) where T : class =>
        Ensure(piece, config, build, parse, t => Snapshot.Write(t));

    /// <summary><see cref="Ensure{T}(string,string,string,Func{T})"/> with the piece's own file format.</summary>
    public bool Ensure<T>(string piece, string config, string build, Func<T> parse, Func<T, byte[]> write, bool onlyConfig = false) where T : class
    {
        var key = Key(piece, config, build);
        if (IsWhole(piece, key)) return false;
        var bytes = write(parse());
        Directory.CreateDirectory(Dir);
        Write(piece, key, bytes, onlyConfig);
        return true;
    }
}
