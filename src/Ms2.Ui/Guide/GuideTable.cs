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

    /// <summary>The per-install <see cref="GuideCache"/> file of a data folder (the game sets it: user://uigen/KEY/...);
    /// null = none, the tables are parsed.</summary>
    public static Func<string, string> CacheOf = _ => null;
    /// <summary>Where MS2_UICACHE=check reports a difference between the cache and the parse.</summary>
    public static Action<string> Log = _ => { };

    /// <summary>
    /// The tables of <paramref name="packs"/>: from this install's cache when it is there and passes its checks, else parsed.
    /// MS2_UICACHE=off always parses; MS2_UICACHE=check parses, reads the cache too and logs the first difference.
    /// </summary>
    public static GuideTable Load(PackSet packs)
    {
        string mode = Environment.GetEnvironmentVariable("MS2_UICACHE");
        string path = packs?.DataDir is { } dir ? CacheOf(dir) : null;
        if (path == null || mode == "off") return Parse(packs);
        if (mode == "check")
        {
            var parsed = Parse(packs);
            if (GuideCache.FirstDifference(parsed, GuideCache.Read(path)) is { } diff) Log("guide cache: " + diff);
            return parsed;
        }
        return GuideCache.Read(path) ?? Parse(packs);
    }

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
