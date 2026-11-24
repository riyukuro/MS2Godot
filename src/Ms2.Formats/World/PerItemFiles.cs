using System.Globalization;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

/// <summary>
/// The data that keeps one file per item (GMS2: item/&lt;a&gt;/&lt;bb&gt;/&lt;id&gt;.xml, whose &lt;ms2&gt; root holds the item's
/// &lt;environment&gt; children) where the KMS data has itemdata/ tables and table/housing.xml. STILL-OPEN: the GMS2 client's own
/// path rule (its exe is packed, so unread); a file is the item of the id in its name.
/// </summary>
public static class PerItemFiles
{
    /// <summary>id -> entry for every item/.../&lt;id&gt;.xml, empty when the names hold itemdata/ files.</summary>
    public static Dictionary<int, string> Index(IEnumerable<string> names)
    {
        var index = new Dictionary<int, string>();
        if (names.Any(n => n.StartsWith("itemdata/", StringComparison.Ordinal))) return index;
        foreach (var n in names)
        {
            if (!n.StartsWith("item/", StringComparison.Ordinal) || !n.EndsWith(".xml", StringComparison.Ordinal)) continue;
            int slash = n.LastIndexOf('/');
            if (int.TryParse(n.AsSpan(slash + 1, n.Length - slash - 5), NumberStyles.None, CultureInfo.InvariantCulture, out int i)) index.TryAdd(i, n);
        }
        return index;
    }

    /// <summary><see cref="Index(IEnumerable{string})"/> of the pack set's Xml names.</summary>
    public static Dictionary<int, string> Index(PackSet packs) => Index(packs.List("Xml"));
}
