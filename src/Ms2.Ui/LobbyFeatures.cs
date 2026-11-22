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
    const string ServerType = "Live";
    /// <summary>The levels a pack set's client reads (each client folder its own: a loose feature.xml in one is not the other's).</summary>
    sealed class Levels
    {
        public int Server;
        /// <summary>The locale this data is the client's: "KR" or "NA" (see <see cref="LocaleOf"/>).</summary>
        public string Locale = "KR";
        public readonly Dictionary<string, int> Names = new();
        /// <summary>Every feature name in table/feature.xml, including the ones its level leaves off (LoadFeature skips those).</summary>
        public readonly HashSet<string> All = new(StringComparer.Ordinal);
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, Levels> sets = new();

    /// <summary>
    /// The locale the client runs as (CFeatureManager::GetLocaleName, read by IsElementAvailable commonutil.cpp:1047,
    /// SelectFeatureElement FUN_14036dfa0, LoadFeature featuremanager.cpp:804-806, the locale folder of GetLocalePath
    /// filesystem.cpp:928 and CreateClientPlatform ms2clientplatform.cpp:29). The KMS exe's is "KR". The data tells which
    /// client it belongs to: the region folder <see cref="TablePath"/> finds in the pack (table/kr/ is KR, table/na/ is NA), so
    /// one value serves the feature levels, the element locales and the table folders alike.
    /// STILL-OPEN: the GMS2 build's own GetLocaleName constant (its exe is packed); "NA" is what its data (feature_setting.xml,
    /// feature.xml, item environments, table/na/) is written for.
    /// </summary>
    public static string LocaleOf(PackSet packs) => packs == null ? "KR" : Load(packs).Locale;

    /// <summary>The service region of the data (the client's DAT_143ab0248, set by its "-locale" argument through
    /// SetRegionByCode localepath.cpp:293-301; GetRegionCode: 0 KR, 2 NA): the data's locale, so the KMS2 data is 0 and the
    /// GMS2 data 2.</summary>
    public static int RegionOf(PackSet packs) => Ms2.Formats.Options.GameOptionLocale.RegionId(LocaleOf(packs));

    /// <summary>CFeatureManager::IsLocaleInList: empty, or a comma list holding the client's locale.</summary>
    public static bool IsLocaleInList(PackSet packs, string list) => string.IsNullOrEmpty(list)
        || list.Split(',').Any(l => string.Equals(l.Trim(), LocaleOf(packs), StringComparison.OrdinalIgnoreCase));

    /// <summary>The level of a feature name (m_nameLevels); 0 for an unknown name, as the client's failed Lookup leaves it.</summary>
    public static int LevelOf(PackSet packs, string feature)
    {
        return feature != null && Load(packs).Names.TryGetValue(feature, out int level) ? level : 0;
    }

    /// <summary>IsFeatureEnabledByName: an empty name is on, an unknown name is off.</summary>
    public static bool IsEnabledByName(PackSet packs, string feature) => string.IsNullOrEmpty(feature) || IsEnabled(packs, feature);

    public static bool IsEnabled(PackSet packs, string feature)
    {
        var l = Load(packs);
        return l.Names.TryGetValue(feature, out int level) && level <= l.Server;
    }

    /// <summary>
    /// The names of table/feature.xml that are off in this client (a level above the server type's, or none at all): the set the
    /// client's IsFeatureEnabledByName answers no for. A name absent from the table is off to the client too but cannot be
    /// listed here (it is not in the data); callers with a name of their own pass it to <see cref="IsEnabled"/>.
    /// </summary>
    public static HashSet<string> Disabled(PackSet packs)
    {
        var l = Load(packs);
        var off = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in l.All)
            if (!l.Names.TryGetValue(name, out int level) || level > l.Server) off.Add(name);
        return off;
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
        var levels = new Levels { Locale = TablePath.Region(e => Loose(packs.DataDir, "Xml/" + e) != null || HasPacked(packs, e)).ToUpperInvariant() };
        string Locale = levels.Locale;
        foreach (var s in Xml(packs, "table/feature_setting.xml", Locale)?.Root.Elements("setting") ?? Enumerable.Empty<XElement>())
            if ((string)s.Attribute("type") == ServerType && int.TryParse((string)s.Attribute(Locale), out int l)) levels.Server = l;
        foreach (var f in Xml(packs, "table/feature.xml", Locale)?.Root.Elements("feature") ?? Enumerable.Empty<XElement>())
            // a level of 0 (or no KR attribute) is skipped, so the feature stays off (CFeatureManager::LoadFeature, featuremanager.cpp:804-806)
            if ((string)f.Attribute("name") is { } name)
            {
                levels.All.Add(name);
                if (int.TryParse((string)f.Attribute(Locale), out int l) && l != 0) levels.Names[name] = l;
            }
        return levels;
    }

    /// <summary>
    /// CFileSystem::LoadXmlFile (filesystem.cpp:393): a loose file under the data folder is read first (the client always
    /// reads loose files, FUN_14039bf70 returns true), then its locale copy (GetLocalePath: "kr/" before the file name,
    /// localepath.cpp:604-607), and only then the pack entry. A client folder with Data/Xml/Table/feature.xml next to
    /// Xml.m2d runs with that file's levels.
    /// STILL-OPEN: the other tables (assets.Table, GameTables) read the pack alone.
    /// </summary>
    static XDocument Xml(PackSet packs, string entry, string locale)
    {
        var text = Loose(packs.DataDir, "Xml/" + entry) ?? Loose(packs.DataDir, "Xml/" + LocalePath(entry, locale));
        if (text == null && packs.Exists("Xml", entry)) text = Ms2.Formats.Text.PackXml.Text(packs.Read("Xml", entry));
        return text == null ? null : XDocument.Parse(text.TrimStart('\ufeff'));
    }

    static bool HasPacked(PackSet packs, string entry)
    {
        try { return packs.Exists("Xml", entry); }
        catch (FileNotFoundException) { return false; }
    }

    static string LocalePath(string entry, string locale)
    {
        int slash = entry.LastIndexOf('/');
        return entry[..(slash + 1)] + locale.ToLowerInvariant() + "/" + entry[(slash + 1)..];
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
