using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Ui;

/// <summary>
/// CFeatureManager::IsFeatureEnabled as the KR client runs it: a feature is on when its level in table/feature.xml (the
/// KR attribute, 0 meaning off) is at most the server type's level in table/feature_setting.xml. The server type is "Live"
/// (CFeatureManager::s_strServerType, the restored client's -env:Live).
/// </summary>
public static class LobbyFeatures
{
    const string ServerType = "Live", Locale = "KR";
    /// <summary>The levels a pack set's client reads (each client folder its own: a loose feature.xml in one is not the other's).</summary>
    sealed class Levels
    {
        public int Server;
        public readonly Dictionary<string, int> Names = new();
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, Levels> sets = new();

    /// <summary>The locale the client runs as (CFeatureManager::GetLocaleName).</summary>
    public const string LocaleName = Locale;

    /// <summary>The level of a feature name (m_nameLevels); 0 for an unknown name, as the client's failed Lookup leaves it.</summary>
    public static int LevelOf(PackSet packs, string feature)
    {
        return feature != null && Load(packs).Names.TryGetValue(feature, out int level) ? level : 0;
    }

    /// <summary>IsFeatureEnabledByName: an empty name is on, an unknown name is off.</summary>
    public static bool IsEnabledByName(PackSet packs, string feature) => string.IsNullOrEmpty(feature) || IsEnabled(packs, feature);

    /// <summary>CFeatureManager::IsLocaleInList: empty, or a comma list holding the client's locale.</summary>
    public static bool IsLocaleInList(string list) => string.IsNullOrEmpty(list)
        || list.Split(',').Any(l => string.Equals(l.Trim(), Locale, StringComparison.OrdinalIgnoreCase));

    public static bool IsEnabled(PackSet packs, string feature)
    {
        var l = Load(packs);
        return l.Names.TryGetValue(feature, out int level) && level <= l.Server;
    }

    /// <summary>The levels as read (the server type's, then each feature's), hashed: part of a cache key of the tables whose
    /// rows are picked by feature (a loose feature.xml changes it).</summary>
    public static string Stamp(PackSet packs)
    {
        var l = Load(packs);
        var text = l.Server + ";" + string.Join(";", l.Names.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value));
        return Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
    }

    static Levels Load(PackSet packs) => sets.GetValue(packs, Read);

    static Levels Read(PackSet packs)
    {
        var levels = new Levels();
        foreach (var s in Xml(packs, "table/feature_setting.xml")?.Root.Elements("setting") ?? Enumerable.Empty<XElement>())
            if ((string)s.Attribute("type") == ServerType && int.TryParse((string)s.Attribute(Locale), out int l)) levels.Server = l;
        foreach (var f in Xml(packs, "table/feature.xml")?.Root.Elements("feature") ?? Enumerable.Empty<XElement>())
            // a level of 0 (or no KR attribute) is skipped, so the feature stays off (CFeatureManager::LoadFeature, featuremanager.cpp:804-806)
            if ((string)f.Attribute("name") is { } name && int.TryParse((string)f.Attribute(Locale), out int l) && l != 0) levels.Names[name] = l;
        return levels;
    }

    /// <summary>
    /// CFileSystem::LoadXmlFile (filesystem.cpp:393): a loose file under the data folder is read first (the client always
    /// reads loose files, FUN_14039bf70 returns true), then its locale copy (GetLocalePath: "kr/" before the file name,
    /// localepath.cpp:604-607), and only then the pack entry. A client folder with Data/Xml/Table/feature.xml next to
    /// Xml.m2d runs with that file's levels.
    /// STILL-OPEN: the other tables (assets.Table, GameTables) read the pack alone.
    /// </summary>
    static XDocument Xml(PackSet packs, string entry)
    {
        var text = Loose(packs.DataDir, "Xml/" + entry) ?? Loose(packs.DataDir, "Xml/" + LocalePath(entry));
        if (text == null && packs.Exists("Xml", entry)) text = System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry));
        return text == null ? null : XDocument.Parse(text.TrimStart('\ufeff'));
    }

    static string LocalePath(string entry)
    {
        int slash = entry.LastIndexOf('/');
        return entry[..(slash + 1)] + "kr/" + entry[(slash + 1)..];
    }

    /// <summary>A loose file's text by a path under <paramref name="dir"/>, its segments matched without case (Windows paths); null when absent.</summary>
    static string Loose(string dir, string path)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
        var cur = dir;
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            bool last = i == parts.Length - 1;
            var hit = (last ? Directory.EnumerateFiles(cur) : Directory.EnumerateDirectories(cur))
                .FirstOrDefault(e => string.Equals(Path.GetFileName(e), parts[i], StringComparison.OrdinalIgnoreCase));
            if (hit == null) return null;
            cur = hit;
        }
        return File.ReadAllText(cur);
    }
}
