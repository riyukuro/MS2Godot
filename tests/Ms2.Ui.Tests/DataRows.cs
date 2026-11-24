using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Ui.Items;

namespace Ms2.Ui.Tests;

/// <summary>
/// The data set's own rows, for tests that run on either data set: KMS (locale KR, itemdata/&lt;prefix&gt;.xml) or GMS2 (locale
/// NA, one file per item). A test keeps its KR values on KR data and takes the expected value from these rows on the other.
/// </summary>
static class DataRows
{
    /// <summary>The KR data (the KMS client's own locale).</summary>
    public static bool IsKr(PackSet packs) => LobbyFeatures.LocaleOf(packs) == "KR";

    /// <summary>The item's raw row (KMS &lt;item&gt;, GMS2 the file's available environment); null when the data has none.</summary>
    public static XElement Item(PackSet packs, int id) => new ItemFiles(packs).Item(id);

    /// <summary>The first &lt;<paramref name="name"/>&gt; under the item's row.</summary>
    public static XElement ItemPart(PackSet packs, int id, string name) => Item(packs, id)?.Descendants(name).FirstOrDefault();

    /// <summary>An integer attribute as the readers take it: absent or empty is <paramref name="fallback"/>.</summary>
    public static int Int(XElement e, string name, int fallback = 0) =>
        int.TryParse((string)e?.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    /// <summary>The smallest positive id the item table has no row for.</summary>
    public static int MissingItemId(ItemDataTable items)
    {
        int id = 1;
        while (items.Get(id) != null) id++;
        return id;
    }
}
