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
    static int? serverLevel;
    static Dictionary<string, int> levels;

    /// <summary>The locale the client runs as (CFeatureManager::GetLocaleName).</summary>
    public const string LocaleName = Locale;

    /// <summary>The level of a feature name (m_nameLevels); 0 for an unknown name, as the client's failed Lookup leaves it.</summary>
    public static int LevelOf(PackSet packs, string feature)
    {
        Load(packs);
        return feature != null && levels.TryGetValue(feature, out int level) ? level : 0;
    }

    /// <summary>IsFeatureEnabledByName: an empty name is on, an unknown name is off.</summary>
    public static bool IsEnabledByName(PackSet packs, string feature) => string.IsNullOrEmpty(feature) || IsEnabled(packs, feature);

    public static bool IsEnabled(PackSet packs, string feature)
    {
        Load(packs);
        return levels.TryGetValue(feature, out int level) && level <= serverLevel;
    }

    static void Load(PackSet packs)
    {
        if (levels != null) return;
        levels = new Dictionary<string, int>();
        serverLevel = 0;
        foreach (var s in Xml(packs, "table/feature_setting.xml")?.Root.Elements("setting") ?? Enumerable.Empty<XElement>())
            if ((string)s.Attribute("type") == ServerType && int.TryParse((string)s.Attribute(Locale), out int l)) serverLevel = l;
        foreach (var f in Xml(packs, "table/feature.xml")?.Root.Elements("feature") ?? Enumerable.Empty<XElement>())
            // a level of 0 (or no KR attribute) is skipped, so the feature stays off (CFeatureManager::LoadFeature, featuremanager.cpp:804-806)
            if ((string)f.Attribute("name") is { } name && int.TryParse((string)f.Attribute(Locale), out int l) && l != 0) levels[name] = l;
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
