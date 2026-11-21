using Ms2.Net.Packets;

namespace Ms2.Ui.Shop;

/// <summary>
/// CShopEventManager's running premium market sale events (shopeventmanager.cpp; a PreminmMarketSaleEventContext is the
/// rate in 1/10000, the covered ids and the currency item id), filled by GameEventManagerCli::Dispatch
/// (gameeventmanagercli.cpp:77) from the GameEventPremiumMarketSale entries (OnStart gameevent.cpp:1439 adds, vfunc_6 removes),
/// and the price maths of CDBPremiumProduct::GetSalePrice (meratmarketbasketinfo.cpp:685).
/// </summary>
public sealed class ShopSaleEvents
{
    /// <summary>The currency item id of "every currency" (0x186a2, GetSalePrice :700).</summary>
    public const int AllCurrency = 100002;

    readonly Dictionary<int, PremiumMarketSaleEvent> events = new();

    public int Count => events.Count;

    /// <summary>Dispatch for one message: kinds 0 and 1 add (an id already held is not added again), 2 removes, 3 replaces.</summary>
    public void Apply(GameEventMessage m)
    {
        switch (m.Kind)
        {
            case 0:
            case 1:
                foreach (var e in m.SaleEvents) events.TryAdd(e.Id, e);
                break;
            case 2:
                foreach (int id in m.RemovedIds) events.Remove(id);
                break;
            case 3:
                foreach (var e in m.SaleEvents) events[e.Id] = e;
                break;
        }
    }

    /// <summary>Test and scripted entry: one event added as OnStart does.</summary>
    public void Add(PremiumMarketSaleEvent e) => events[e.Id] = e;

    /// <summary>GameEventManagerCli::Clear (:98): every event's vfunc_6.</summary>
    public void Clear() => events.Clear();

    /// <summary>FUN_1407cde30 (shopeventmanager.cpp:46): the event covers the id for the currency: the currency matches, and the
    /// ids hold the id's hundred, the id itself or 0.</summary>
    static bool Covers(PremiumMarketSaleEvent e, int id, int currency)
    {
        if (currency != e.Currency) return false;
        int rest = id % 100;
        int baseId = -1;
        if (rest > 0) baseId = id - rest;
        bool found = baseId >= 0 && Has(e, baseId);
        if (!found) found = Has(e, id);
        if (!found) found = Has(e, 0);
        return found;
    }

    static bool Has(PremiumMarketSaleEvent e, int id)
    {
        for (int i = 0; i < e.Ids.Count; i++) if (e.Ids[i] == id) return true;
        return false;
    }

    /// <summary>GetDiscountedPrice (shopeventmanager.cpp:97): ceilf of price x (10000 - rate) / 10000 in float, the product an int.</summary>
    static long Discounted(PremiumMarketSaleEvent e, int price)
    {
        int product = unchecked(price * (10000 - e.Rate));
        return (int)MathF.Ceiling((float)product / 10000.0f);
    }

    /// <summary>
    /// CDBPremiumProduct::GetSalePrice (@0x14069f980, meratmarketbasketinfo.cpp:685): types 3, 6 and 7 the sale price; type 8 the
    /// sale price less the percent of FUN_1406a61c0 (0 for a price or rate of 0); else a meso or merat product (currency item
    /// id 100000 / 100001) takes the best running event: with coupon bit 1 the best of the every-currency events, then the best
    /// (highest rate, the first on a tie) of the events of its currency that cover id 0, its category, and for coupon flags above
    /// 0 the ids 10 (flags 2, 8) and 11 (flags 4, 0x10, FUN_14069ffc0); a sub product (type 2) takes its parent's flags.
    /// </summary>
    public long SalePrice(PremiumProduct p, PremiumProduct parent = null)
    {
        if (p.Type == 3 || p.Type == 6 || p.Type == 7) return p.SalePrice;
        if (p.Type == 8)
        {
            if (p.SalePrice == 0 || p.RandomSaleRate == 0) return 0;
            return p.SalePrice * (100 - Math.Min((uint)p.RandomSaleRate, 100u)) / 100;
        }
        int currency = p.Currency == 1 ? 100000 : p.Currency == 2 ? 100001 : 0;   // GetCurrencyItemId (:806)
        if (currency == 0) return p.SalePrice;
        int flags = p.CouponFlags;
        if (parent != null && p.Type == 2) flags = parent.CouponFlags;
        PremiumMarketSaleEvent best = null;
        if ((flags & 1) != 0)
            foreach (var e in events.Values)
                if (e.Currency == AllCurrency && (best == null || best.Rate < e.Rate)) best = e;
        Span<int> ids = stackalloc int[5];
        int n = 0;
        ids[n++] = 0;
        if (p.CategoryId > 0) ids[n++] = p.CategoryId;
        if (flags > 0 && flags != 1)
        {
            if ((flags & 2) != 0 || (flags & 8) != 0) ids[n++] = 10;
            if ((flags & 4) != 0 || (flags & 0x10) != 0) ids[n++] = 11;
        }
        foreach (var e in events.Values)
        {
            if (e.Currency != currency) continue;
            for (int j = 0; j < n; j++)
                if (Covers(e, ids[j], currency) && (best == null || best.Rate < e.Rate)) best = e;
        }
        return best != null ? Discounted(best, (int)p.SalePrice) : p.SalePrice;
    }
}
