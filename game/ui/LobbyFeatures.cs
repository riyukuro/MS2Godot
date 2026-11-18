using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2Godot.Ui;

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

    static XDocument Xml(PackSet packs, string entry) => packs.Exists("Xml", entry)
        ? XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('﻿'))
        : null;
}
