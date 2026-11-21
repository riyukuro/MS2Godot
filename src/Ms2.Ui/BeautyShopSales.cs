using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// The game rule's beauty shop sale lists (CGameRule m_158[2], commonfiles/gamerule.cpp:SetBeautyShopSaleEvent @0x140476b40,
/// RemoveBeautyShopSaleEvent @0x140476be0) as GameEventManagerCli (gameeventmanagercli.cpp) fills them from
/// GameEventSaleBeautyShop (gameevent.cpp:787-820), and the price maths GetDiscountedPrice (@0x140476c70), GetSaleIndex
/// (@0x140476cf0) over FUN_140476d50 (@0x140476d50).
/// <para>The manager keys its events by id across every type; this holder only sees this type, so an id it shares with another
/// type's event is not skipped or replaced as in the client.</para>
/// </summary>
public sealed class BeautyShopSales
{
    /// <summary>GetSaleIndex's answer when no list holds a rate for the event.</summary>
    public const int NoSale = 2;

    readonly Dictionary<int, BeautyShopSaleEvent> events = new();
    readonly Dictionary<int, int>[] lists = { new(), new() };

    /// <summary>The list an event goes to: m_d8 == 0 is bOn = 1 (OnStart :800, vfunc_6 :811).</summary>
    static int ListOf(BeautyShopSaleEvent e) => e.Flag != 0 ? 0 : 1;

    /// <summary>GameEventManagerCli::Dispatch (:77-95) for one GameEvent message.</summary>
    public void Apply(GameEventMessage m)
    {
        switch (m.Kind)
        {
            case 0:
            case 1:
                // Add (:211-220): an id already held is not added again; else OnStart
                foreach (var e in m.BeautySaleEvents)
                {
                    if (events.ContainsKey(e.Id)) continue;
                    lists[ListOf(e)][e.SaleEventId] = e.Rate;
                    events[e.Id] = e;
                }
                break;
            case 2:
                // Remove (:223-233): vfunc_6, then RemoveKey
                foreach (int id in m.RemovedIds)
                {
                    if (!events.TryGetValue(id, out var e)) continue;
                    lists[ListOf(e)].Remove(e.SaleEventId);
                    events.Remove(id);
                }
                break;
            case 3:
                // Update (:236-242): the previous event's vfunc_6, then OnStart
                foreach (var e in m.BeautySaleEvents)
                {
                    if (events.TryGetValue(e.Id, out var prev)) lists[ListOf(prev)].Remove(prev.SaleEventId);
                    events[e.Id] = e;
                    lists[ListOf(e)][e.SaleEventId] = e.Rate;
                }
                break;
        }
    }

    /// <summary>GameEventManagerCli::Clear (:98-107): every event's vfunc_6, then RemoveAll.</summary>
    public void Clear()
    {
        foreach (var e in events.Values) lists[ListOf(e)].Remove(e.SaleEventId);
        events.Clear();
    }

    /// <summary>FUN_140476d50: the best rate of the two lists for the event and the list it came from (2 when none is above 0);
    /// list 0 does not count for level 0. A tie keeps the first list.</summary>
    public void Best(int saleEventId, int level, out uint rate, out int index)
    {
        rate = 0;
        index = NoSale;
        for (int i = 0; i < 2; i++)
        {
            if (i == 0 && level == 0) continue;
            uint r = lists[i].TryGetValue(saleEventId, out int v) ? (uint)v : 0;
            if (rate < r) { rate = r; index = i; }
        }
    }

    /// <summary>GetSaleIndex (gamerule.cpp:667): the list of the best rate, or <see cref="NoSale"/>.</summary>
    public int SaleIndex(int saleEventId, int level)
    {
        Best(saleEventId, level, out _, out int index);
        return index;
    }

    /// <summary>GetDiscountedPrice (gamerule.cpp:656): the price less price x rate / 10000, in unsigned 32-bit.</summary>
    public uint DiscountedPrice(int saleEventId, uint price, int level)
    {
        Best(saleEventId, level, out uint rate, out _);
        return unchecked(price - price * rate / 10000);
    }
}
