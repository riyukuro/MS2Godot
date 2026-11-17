using System.Text;
using System.Runtime.CompilerServices;

namespace Ms2.Formats.Pack;

/// <summary>
/// The asset-web catalog (Resource/asset-web-metadata.m2d, N-Triples): resolves the <c>urn:llid:&lt;id&gt;</c> asset ids
/// used by flat models (NifAsset, ...) to an archive and entry, e.g. "/Model/Map/Tria/Cube/x.nif" ->
/// ("Resource/Model/Map", "tria/cube/x.nif").
/// </summary>
public sealed class AssetIndex
{
    public const string Archive = "Resource/asset-web-metadata";

    // The catalog is the same for every field of a pack set, so it is parsed once per pack set. The weak key keeps the
    // cache from outliving the pack set; the index itself holds no reference to it.
    static readonly ConditionalWeakTable<PackSet, Lazy<AssetIndex>> cache = new();

    /// <summary>The catalog of a pack set, parsed on first use and shared afterwards (safe to call from any thread).</summary>
    public static AssetIndex For(PackSet packs) => cache.GetValue(packs, p => new Lazy<AssetIndex>(() => new AssetIndex(p))).Value;

    /// <summary>Parses the catalog in the background so a later <see cref="For"/> finds it ready.</summary>
    public static void Prefetch(PackSet packs) => System.Threading.Tasks.Task.Run(() => { try { For(packs); } catch { } });

    readonly Dictionary<string, string> llidToUuid = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> uuidToRelpath = new(StringComparer.OrdinalIgnoreCase);

    public AssetIndex(PackSet packs)
    {
        // each file fills its own dictionary, so the two parse side by side; first entry wins within a file
        var relpath = System.Threading.Tasks.Task.Run(() => Load(packs.Read(Archive, "relpath.nt"), (u, v) => uuidToRelpath.TryAdd(u, v)));
        Load(packs.Read(Archive, "llid.nt"), (u, v) => llidToUuid.TryAdd(v, u));
        relpath.GetAwaiter().GetResult();
    }

    // One line is "<urn:uuid:ID> <.../llid|relpath> "value" ..." : the id, then a predicate ending in /llid or /relpath, then the value.
    static void Load(byte[] data, Action<string, string> add)
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
        return llidToUuid.TryGetValue(llid, out var u) && uuidToRelpath.TryGetValue(u, out var p) ? p : null;
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
