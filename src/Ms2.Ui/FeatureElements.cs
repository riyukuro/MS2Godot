using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Ui;

/// <summary>
/// How the client picks among data elements by locale and feature (commonutil.cpp): IsElementAvailable skips an element
/// whose locale is not the client's or whose feature is off, and CFeatureManager::SelectFeatureElement (0x14036e160)
/// folds two available ones: the higher feature level wins (equal levels: the greater name), and for the same feature the
/// element of the client's locale wins over one without.
/// </summary>
public static class FeatureElements
{
    /// <summary>IsElementAvailable (commonutil.cpp:1047): the client's locale (or none) and an enabled feature (or none).</summary>
    public static bool IsAvailable(PackSet packs, XElement e) => IsAvailable(packs, Locale(e), Feature(e));

    /// <summary>IsElementAvailable on an element's locale and feature attributes ("" when missing).</summary>
    public static bool IsAvailable(PackSet packs, string locale, string feature) =>
        (locale == "" || locale == LobbyFeatures.LocaleOf(packs)) && LobbyFeatures.IsEnabledByName(packs, feature);

    static string Locale(XElement e) => (string)e.Attribute("locale") ?? "";
    static string Feature(XElement e) => (string)e.Attribute("feature") ?? "";

    /// <summary>FUN_14028f8d0 (commonutil.cpp:1064): the &lt;environment&gt; of an item (or any data element) the client uses; null when none is
    /// available.</summary>
    public static XElement SelectEnvironment(PackSet packs, XElement parent)
    {
        XElement best = null;
        foreach (var e in parent.Elements("environment"))
        {
            if (!IsAvailable(packs, e)) continue;
            best = best == null ? e : Fold(packs, best, e);
        }
        return best;
    }

    /// <summary>
    /// FUN_14028faa0 (commonutil.cpp:1748): the available <paramref name="child"/> elements, one per value of
    /// <paramref name="keyAttr"/> (a later one replaces the kept one when SelectFeatureElement picks it, taking its place
    /// in the order), in order of the kept elements' positions among the available ones.
    /// </summary>
    public static List<XElement> Collect(PackSet packs, XElement parent, string child, string keyAttr) =>
        Collect(packs, parent, child, e => (string)e.Attribute(keyAttr) ?? "");

    /// <summary>FUN_14028fd90 (commonutil.cpp:2131): <see cref="Collect(PackSet, XElement, string, string)"/> keyed by two
    /// attributes as "<paramref name="keyAttr1"/>_<paramref name="keyAttr2"/>".</summary>
    public static List<XElement> Collect(PackSet packs, XElement parent, string child, string keyAttr1, string keyAttr2) =>
        Collect(packs, parent, child, e => ((string)e.Attribute(keyAttr1) ?? "") + "_" + ((string)e.Attribute(keyAttr2) ?? ""));

    static List<XElement> Collect(PackSet packs, XElement parent, string child, Func<XElement, string> keyOf) =>
        Collect(packs, parent.Elements(child), keyOf, Feature, Locale);

    /// <summary><see cref="Collect(PackSet, XElement, string, string)"/> over rows a reader took from the elements: each
    /// row's key, feature and locale attributes ("" when missing), in the elements' order.</summary>
    public static List<T> Collect<T>(PackSet packs, IEnumerable<T> rows, Func<T, string> keyOf, Func<T, string> featureOf,
        Func<T, string> localeOf) where T : class
    {
        if (rows is IReadOnlyList<T> list && list.Count <= ShortRows) return CollectShort(packs, list, keyOf, featureOf, localeOf);
        var byKey = new Dictionary<string, (int Index, T Elem)>();
        int index = 0;
        foreach (var e in rows)
        {
            if (!IsAvailable(packs, localeOf(e), featureOf(e))) continue;
            ++index;
            string key = keyOf(e);
            if (byKey.TryGetValue(key, out var kept))
            {
                if (Fold(packs, kept.Elem, e, featureOf, localeOf) == e) byKey[key] = (index, e);
            }
            else byKey[key] = (index, e);
        }
        return byKey.Values.OrderBy(v => v.Index).Select(v => v.Elem).ToList();
    }

    /// <summary>Up to this many rows are collected by <see cref="CollectShort{T}"/>.</summary>
    internal const int ShortRows = 16;

    /// <summary><see cref="Collect{T}"/> over a few rows without the key map: the kept rows are the result list itself, in
    /// the order of their positions (a replacing row has the highest position so far, so it moves to the end).</summary>
    static List<T> CollectShort<T>(PackSet packs, IReadOnlyList<T> rows, Func<T, string> keyOf, Func<T, string> featureOf,
        Func<T, string> localeOf) where T : class
    {
        var kept = new List<T>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            var e = rows[i];
            if (!IsAvailable(packs, localeOf(e), featureOf(e))) continue;
            string key = keyOf(e);
            int at = -1;
            for (int k = 0; k < kept.Count; k++)
                if (keyOf(kept[k]) == key) { at = k; break; }
            if (at < 0) kept.Add(e);
            else if (Fold(packs, kept[at], e, featureOf, localeOf) == e) { kept.RemoveAt(at); kept.Add(e); }
        }
        return kept;
    }

    /// <summary>Common::Constants with the client's row pick: each key's &lt;v&gt; by <see cref="Collect"/> (FUN_14028faa0).</summary>
    public static Ms2.Formats.World.ClientConstants Constants(PackSet packs) =>
        new(packs, root => Collect(packs, root, "v", "key"));

    /// <summary>CFeatureManager::SelectFeatureElement: <paramref name="a"/> or <paramref name="b"/>.</summary>
    static XElement Fold(PackSet packs, XElement a, XElement b) => Fold(packs, a, b, Feature, Locale);

    static T Fold<T>(PackSet packs, T a, T b, Func<T, string> featureOf, Func<T, string> localeOf)
    {
        string fa = featureOf(a), fb = featureOf(b);
        if (fa != fb)
        {
            int la = LobbyFeatures.LevelOf(packs, fa), lb = LobbyFeatures.LevelOf(packs, fb);
            bool aWins = la > lb || (la == lb && string.CompareOrdinal(fa, fb) > 0);   // SelectHigherLevelFeatureName
            return aWins ? a : b;
        }
        string xa = localeOf(a), xb = localeOf(b), mine = LobbyFeatures.LocaleOf(packs);
        if (xa != xb)
        {
            // FUN_14036dfa0: the client's locale, else the empty one, else A
            if (xa == mine) return a;
            if (xb == mine) return b;
            if (xa == "") return a;
            if (xb == "") return b;
        }
        return a;
    }
}
