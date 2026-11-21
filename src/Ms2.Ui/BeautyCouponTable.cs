using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Ms2.Ui;

/// <summary>
/// The beauty coupon table (FUN_1406e56d0 @0x1406e56d0, Table/shop_beautyCoupon.xml): &lt;ms2&gt;/&lt;shop shopID&gt;/&lt;item id&gt;. A shop or
/// item that fails IsElementAvailable, has id 0, or (items) is not an item the client knows is skipped; a later &lt;shop&gt; of the
/// same id replaces the earlier one (SetAt). FUN_1406e5af0 looks the coupons up by shop id.
/// </summary>
public sealed class BeautyCouponTable
{
    public static readonly BeautyCouponTable Empty = new(null, _ => true, _ => true);

    readonly Dictionary<uint, uint[]> coupons = new();

    public BeautyCouponTable(XDocument doc, Func<XElement, bool> available, Func<uint, bool> itemExists)
    {
        var root = doc?.Root;
        if (root == null || root.Name != "ms2") return;
        foreach (var shop in root.Elements("shop"))
        {
            if (!available(shop)) continue;
            if (!uint.TryParse((string)shop.Attribute("shopID"), out uint shopId) || shopId == 0) continue;
            var items = new List<uint>();
            foreach (var item in shop.Elements("item"))
            {
                if (!available(item)) continue;
                if (!uint.TryParse((string)item.Attribute("id"), out uint id) || id == 0) continue;
                if (itemExists(id)) items.Add(id);
            }
            coupons[shopId] = items.ToArray();
        }
    }

    /// <summary>The coupon item ids of a shop; empty when the shop has none.</summary>
    public uint[] CouponsOf(uint shopId) => coupons.TryGetValue(shopId, out var c) ? c : Array.Empty<uint>();
}
