using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2Godot.Ui;
using LobbyFeatures = Ms2.Ui.LobbyFeatures;
namespace Ms2Godot.Net;

/// <summary>
/// Which &lt;environment&gt; of an item (or any data element) the client uses: FUN_14028f8d0 (commonutil.cpp) walks the
/// environments, skips those whose locale is not the client's or whose feature is off, and folds the rest with
/// CFeatureManager::SelectFeatureElement (0x14036e160): the higher feature level wins (equal levels: the greater name),
/// and for the same feature the element of the client's locale wins over one without.
/// </summary>
public static class ItemEnvironment
{
    public static XElement Select(PackSet packs, XElement parent)
    {
        XElement best = null;
        foreach (var e in parent.Elements("environment"))
        {
            string locale = (string)e.Attribute("locale") ?? "", feature = (string)e.Attribute("feature") ?? "";
            if (locale != "" && locale != LobbyFeatures.LocaleName) continue;
            if (!LobbyFeatures.IsEnabledByName(packs, feature)) continue;
            best = best == null ? e : Fold(packs, best, e);
        }
        return best;
    }

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
