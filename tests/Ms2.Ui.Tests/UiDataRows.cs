using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Ui.Items;

namespace Ms2.Ui.Tests;

/// <summary>
/// Helpers that read the active data set's own XML rows for tests that run on either KMS (locale KR,
/// itemdata/&lt;prefix&gt;.xml) or GMS2 (locale NA, one file per item). Each test keeps its exact KR assertions on KR data
/// and asserts the same rule against the active data set's rows on GMS2.
/// </summary>
static class UiDataRows
{
    /// <summary>True when the pack's locale is KR (the KMS data).</summary>
    public static bool IsKr(PackSet packs) => LobbyFeatures.LocaleOf(packs) == "KR";

    /// <summary>The item's raw XML row (KMS &lt;item&gt;, GMS2 the file's selected environment); null when absent.</summary>
    public static XElement Item(PackSet packs, int id) => new ItemFiles(packs).Item(id);

    /// <summary>The first &lt;<paramref name="name"/>&gt; descendant under the item's raw XML row.</summary>
    public static XElement ItemPart(PackSet packs, int id, string name) => Item(packs, id)?.Descendants(name).FirstOrDefault();

    /// <summary>An integer attribute as the table readers parse it: absent or empty returns <paramref name="fallback"/>.</summary>
    public static int Int(XElement e, string name, int fallback = 0) =>
        int.TryParse((string)e?.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    /// <summary>The smallest positive item id the table has no row for (1 on KMS; higher on GMS2 where item 1 exists).</summary>
    public static int MissingItemId(ItemDataTable items)
    {
        int id = 1;
        while (items.Get(id) != null) id++;
        return id;
    }

    /// <summary>
    /// The item's sell price for <paramref name="rarity"/> read from its raw &lt;property&gt;&lt;sell price&gt; attribute
    /// following ItemProperty::GetSellPrice (one entry for every rarity, six entries indexed by rarity - 1, else 0).
    /// </summary>
    public static long SellPrice(PackSet packs, int id, int rarity)
    {
        var attr = (string)ItemPart(packs, id, "property")?.Element("sell")?.Attribute("price") ?? "";
        var prices = attr.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => (long)ItemData.Atoi(t)).ToList();
        return prices.Count == 1 ? prices[0] : prices.Count == 6 && rarity is >= 1 and <= 6 ? prices[rarity - 1] : 0;
    }

    /// <summary>
    /// The first gacha coin (by box id) in table/gacha_info.xml whose available &lt;randomBox&gt; shows a season on its icon
    /// in the pack's locale, is that coin's first box, and has an item row. Null when none exists.
    /// </summary>
    public static (int Coin, int Box, string Season)? SeasonCoin(PackSet packs)
    {
        if (!packs.Exists("Xml", "table/gacha_info.xml")) return null;
        var root = Ms2.Formats.World.GameStrings.Parse(packs.Read("Xml", "table/gacha_info.xml")).Root;
        var rows = FeatureElements.Collect(packs, root, "randomBox", "randomBoxID")
            .Select(e => (Box: Int(e, "randomBoxID"), Coin: Int(e, "coinItemID"),
                Season: Int(e, "randomBoxSeason" + LobbyFeatures.LocaleOf(packs)),
                OnIcon: (string)e.Attribute("seasonNumberOnIcon") is "1" or "true" or "TRUE" or "yes"))
            .OrderBy(r => r.Box).ToList();
        foreach (var r in rows)
        {
            if (r.Season <= 0 || !r.OnIcon || r.Coin == 0 || rows.First(o => o.Coin == r.Coin).Box != r.Box || Item(packs, r.Coin) == null) continue;
            return (r.Coin, r.Box, r.Season.ToString(CultureInfo.InvariantCulture));
        }
        return null;
    }
}
