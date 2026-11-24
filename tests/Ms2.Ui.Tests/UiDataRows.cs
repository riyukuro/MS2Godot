using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Ui.Items;

namespace Ms2.Ui.Tests;

/// <summary>
/// Raw-XML helpers for tests that run on either KMS (region "kr", itemdata/&lt;prefix&gt;.xml) or GMS2 (region "na",
/// one file per item). Each test keeps its exact KR assertions on KR data and asserts the same rule against the active
/// data set's own XML rows on GMS2.
/// </summary>
static class UiDataRows
{
    /// <summary>True when the pack's region folder is "kr" (the KMS data; same TablePath.Region check as Formats.Tests). Throws on null.</summary>
    public static bool IsKr(PackSet packs)
    {
        ArgumentNullException.ThrowIfNull(packs);
        return TablePath.Region(e => packs.Exists("Xml", e)) == "kr";
    }

    /// <summary>The item's raw XML row (KMS &lt;item&gt;, GMS2 the file's selected environment); null when absent.</summary>
    public static XElement Item(PackSet packs, int id) => new ItemFiles(packs).Item(id);

    /// <summary>The first &lt;<paramref name="name"/>&gt; descendant under the item's raw XML row.</summary>
    public static XElement ItemPart(PackSet packs, int id, string name) => Item(packs, id)?.Descendants(name).FirstOrDefault();

    /// <summary>Raw integer attribute read: absent or non-integer returns <paramref name="fallback"/>.</summary>
    public static int Int(XElement e, string name, int fallback = 0) =>
        int.TryParse((string)e?.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    /// <summary>The smallest positive item id the table has no row for (1 on KMS; higher on GMS2 where item 1 exists).</summary>
    public static int MissingItemId(ItemDataTable items)
    {
        int id = 1;
        while (items.Get(id) != null) id++;
        return id;
    }

    /// <summary>Raw &lt;property&gt;&lt;sell price&gt; list from the item's XML, returning the single price or the 1-based rarity entry.</summary>
    public static long SellPrice(PackSet packs, int id, int rarity)
    {
        var attr = (string)ItemPart(packs, id, "property")?.Element("sell")?.Attribute("price") ?? "";
        var prices = attr.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => long.TryParse(t.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : 0L).ToList();
        return prices.Count == 1 ? prices[0] : prices.Count == 6 && rarity is >= 1 and <= 6 ? prices[rarity - 1] : 0;
    }

    /// <summary>
    /// Raw &lt;randomBox&gt; scan of table/gacha_info.xml: the first box (by id) with a positive locale season attribute
    /// and seasonNumberOnIcon enabled that is its coin's first box and whose coin has an item XML row.
    /// </summary>
    public static (int Coin, int Box, string Season)? SeasonCoin(PackSet packs)
    {
        ArgumentNullException.ThrowIfNull(packs);
        if (!packs.Exists("Xml", "table/gacha_info.xml")) return null;
        string locale = TablePath.Region(e => packs.Exists("Xml", e)).ToUpperInvariant();
        var root = Ms2.Formats.World.GameStrings.Parse(packs.Read("Xml", "table/gacha_info.xml")).Root;
        var rows = FeatureElements.Collect(packs, root, "randomBox", "randomBoxID")
            .Select(e => (Box: Int(e, "randomBoxID"), Coin: Int(e, "coinItemID"),
                Season: Int(e, "randomBoxSeason" + locale),
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
