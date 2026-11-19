using System.Xml;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.Guide;

/// <summary>One guide xml file: its pack path and its &lt;event&gt; elements in the order the manager loads them.</summary>
public sealed record GuideTableFile(string Path, IReadOnlyList<XElement> Events);

/// <summary>
/// The guide tables (CGuideManager::LoadFile, client/ms2/guideentity_1b.cpp @0x14138b4d0): table/guide.xml and, depth
/// first, every &lt;import&gt; the client's locale and features make available (IsElementAvailable), each file's events
/// as FUN_14028faa0 collects them (one per id, the feature filter's pick). A file that is missing or does not parse is
/// skipped, as LoadXmlFile failing; one without an &lt;ms2&gt; root is logged (<see cref="BadFiles"/>) and skipped.
/// </summary>
public sealed class GuideTable
{
    public const string RootFile = "guide.xml";

    public List<GuideTableFile> Files { get; } = new();
    /// <summary>The files read without an &lt;ms2&gt; root.</summary>
    public List<string> BadFiles { get; } = new();

    /// <summary>Every event in load order.</summary>
    public IEnumerable<XElement> Events => Files.SelectMany(f => f.Events);

    /// <summary>GetXmlPath("Table", name).</summary>
    public static string PathOf(string name) => "table/" + name;

    const string CachePiece = "guide";

    /// <summary>The cache configuration: the feature levels the events and imports are picked by.</summary>
    static string CacheConfig(PackSet packs) => LobbyFeatures.Stamp(packs);

    /// <summary>The code the cached table's form depends on: the guide's, the string tables' and the XML readers'.</summary>
    static string CacheBuild => Ms2.Formats.Cache.CacheStore.Build(typeof(GuideTable).Assembly, typeof(GameStrings).Assembly, typeof(Ms2.Quest.QuestTable).Assembly);

    /// <summary>
    /// The tables of <paramref name="packs"/>: from this install's cache store when it holds them, else parsed (and the
    /// file written for the next session). MS2_UICACHE=off parses; =check reads the cache too and reports the first
    /// difference. The setup step builds the file (<see cref="BuildCache"/>).
    /// </summary>
    public static GuideTable Load(PackSet packs)
    {
        var store = Ms2.Formats.Cache.CacheStore.Of?.Invoke(packs);
        if (store == null) return Parse(packs);
        var t = store.Get(CachePiece, CacheConfig(packs), CacheBuild, () => Parse(packs),
            GuideCache.WriteBody, GuideCache.ReadBody, GuideCache.FirstDifference, out var outcome);
        Ms2.Formats.Cache.CacheStore.Logged?.Invoke(outcome);
        return t;
    }

    /// <summary>Writes the cache file of the tables when it is missing (the setup step); true when it was written.</summary>
    public static bool BuildCache(PackSet packs, Ms2.Formats.Cache.CacheStore cache) =>
        cache.Ensure(CachePiece, CacheConfig(packs), CacheBuild, () => Parse(packs), GuideCache.WriteBody);

    /// <summary>The tables parsed from the XML (the cache's reference).</summary>
    public static GuideTable Parse(PackSet packs)
    {
        var table = new GuideTable();
        if (packs != null) table.LoadFile(packs, PathOf(RootFile));
        return table;
    }

    void LoadFile(PackSet packs, string path)
    {
        if (!packs.Exists("Xml", path)) return;
        XDocument doc;
        try { doc = GameStrings.Parse(packs.Read("Xml", path)); } catch (XmlException) { return; }
        var root = doc.Root;
        if (root == null || root.Name != "ms2")
        {
            BadFiles.Add(path);
            return;
        }
        Files.Add(new GuideTableFile(path, FeatureElements.Collect(packs, root, "event", "id")));
        foreach (var import in root.Elements("import"))
        {
            if (!FeatureElements.IsAvailable(packs, import)) continue;
            string name = "";
            GuideXml.String(import, "name", ref name);
            LoadFile(packs, PathOf(name));
        }
    }
}
