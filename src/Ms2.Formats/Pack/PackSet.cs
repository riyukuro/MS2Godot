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
        lock (gate) return a.Reader.GetBytes(e);
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
