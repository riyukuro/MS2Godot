using System.Text;
using System.Text.RegularExpressions;

namespace Ms2.Formats.Pack;

/// <summary>
/// The asset-web catalog (Resource/asset-web-metadata.m2d, N-Triples): resolves the <c>urn:llid:&lt;id&gt;</c> asset ids
/// used by flat models (NifAsset, ...) to an archive and entry, e.g. "/Model/Map/Tria/Cube/x.nif" ->
/// ("Resource/Model/Map", "tria/cube/x.nif").
/// </summary>
public sealed class AssetIndex
{
    public const string Archive = "Resource/asset-web-metadata";

    static readonly Regex Triple = new(@"^<urn:uuid:([0-9a-fA-F-]+)>\s+<[^>]*/(llid|relpath)>\s+""([^""]*)""", RegexOptions.Compiled);

    readonly Dictionary<string, string> llidToUuid = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> uuidToRelpath = new(StringComparer.OrdinalIgnoreCase);

    public AssetIndex(PackSet packs)
    {
        Load(packs.Read(Archive, "llid.nt"), (u, v) => llidToUuid.TryAdd(v, u));
        Load(packs.Read(Archive, "relpath.nt"), (u, v) => uuidToRelpath.TryAdd(u, v));
    }

    static void Load(byte[] data, Action<string, string> add)
    {
        using var r = new StringReader(Encoding.UTF8.GetString(data));
        string line;
        while ((line = r.ReadLine()) != null)
        {
            var m = Triple.Match(line);
            if (m.Success) add(m.Groups[1].Value, m.Groups[3].Value);
        }
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
