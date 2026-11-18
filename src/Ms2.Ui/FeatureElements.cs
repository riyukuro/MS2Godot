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
    /// <summary>IsElementAvailable: the client's locale (or none) and an enabled feature (or none).</summary>
    public static bool IsAvailable(PackSet packs, XElement e)
    {
        string locale = (string)e.Attribute("locale") ?? "", feature = (string)e.Attribute("feature") ?? "";
        return (locale == "" || locale == LobbyFeatures.LocaleName) && LobbyFeatures.IsEnabledByName(packs, feature);
    }

    /// <summary>FUN_14028f8d0: the &lt;environment&gt; of an item (or any data element) the client uses; null when none is
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
    public static List<XElement> Collect(PackSet packs, XElement parent, string child, string keyAttr)
    {
        var byKey = new Dictionary<string, (int Index, XElement Elem)>();
        int index = 0;
        foreach (var e in parent.Elements(child))
        {
            if (!IsAvailable(packs, e)) continue;
            ++index;
            string key = (string)e.Attribute(keyAttr) ?? "";
            if (byKey.TryGetValue(key, out var kept))
            {
                if (Fold(packs, kept.Elem, e) == e) byKey[key] = (index, e);
            }
            else byKey[key] = (index, e);
        }
        return byKey.Values.OrderBy(v => v.Index).Select(v => v.Elem).ToList();
    }

    /// <summary>CFeatureManager::SelectFeatureElement: <paramref name="a"/> or <paramref name="b"/>.</summary>
    static XElement Fold(PackSet packs, XElement a, XElement b)
    {
        string fa = (string)a.Attribute("feature") ?? "", fb = (string)b.Attribute("feature") ?? "";
        if (fa != fb)
        {
            int la = LobbyFeatures.LevelOf(packs, fa), lb = LobbyFeatures.LevelOf(packs, fb);
            bool aWins = la > lb || (la == lb && string.CompareOrdinal(fa, fb) > 0);   // SelectHigherLevelFeatureName
            return aWins ? a : b;
        }
        string xa = (string)a.Attribute("locale") ?? "", xb = (string)b.Attribute("locale") ?? "";
        if (xa != xb)
        {
            // FUN_14036dfa0: the client's locale, else the empty one, else A
            if (xa == LobbyFeatures.LocaleName) return a;
            if (xb == LobbyFeatures.LocaleName) return b;
            if (xa == "") return a;
            if (xb == "") return b;
        }
        return a;
    }
}
