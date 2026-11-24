using Ms2.Net.Packets;

namespace Ms2.Ui.Items;

/// <summary>
/// The item checks the trade path runs (commonfiles/itemsocketsystemutil.cpp, itemsocket.cpp, item.cpp), over the parsed
/// item record. <paramref name="now"/> is the server time in seconds (GetServerTime).
/// </summary>
public static class ItemTradeRules
{
    /// <summary>CItem::FUN_140528e20 (item.cpp): lock state 1, or 0 with an unlock time still ahead of the server time.</summary>
    public static bool IsLocked(Item item, long now) => item.LockState == 1 || (item.LockState == 0 && now < item.UnlockTime);

    /// <summary>TSocketGemStoneInfo::IsLocked (itemsocket.cpp:147, TSocketGemStoneLock::IsLocked :58): a lock of state 1, or
    /// state 0 with its expiry ahead of the server time.</summary>
    public static bool GemLocked(SocketGem g, long now) => g.HasLock && (g.LockState == 1 || (g.LockState == 0 && now < g.LockExpireTime));

    /// <summary>TSocketGemStoneInfo::IsBound (itemsocket.cpp:131): a bind with a character id.</summary>
    public static bool GemBound(SocketGem g) => g.HasBind && g.BindCharacterId != 0;

    /// <summary>CItemTransfer::FUN_140635590 (itemsocketsystemutil.cpp:2938): 0 when bound or locked (FUN_140634cc0 is always
    /// 1); else the trade count rule (0x10) with a count left, else the trade rule bit 4.</summary>
    public static int TransferTradeRule(ItemTransfer t, bool locked)
    {
        if (t.HasBinding || locked) return 0;
        int result = (t.Rule & 0x10) != 0 && t.Count > 0 ? 1 : 0;
        return result == 0 ? t.Rule & 4 : result;
    }

    /// <summary>CItemTransfer::ComputeRule (itemsocketsystemutil.cpp:3097): the rule bits of a transfer type (0 4, 5 0x10,
    /// 2 and 4 8, 3 0x18, any other 0).</summary>
    public static int ComputeRule(int transferType) => transferType switch
    {
        0 => 4, 5 => 0x10, 2 => 8, 4 => 8, 3 => 0x18, _ => 0,
    };

    /// <summary>
    /// CItemSocket::AllGemsPass (itemsocket.cpp:669): no slot, true; each gem that is set (an empty slot or a gem id with
    /// no item data is skipped) must pass.
    /// </summary>
    static bool AllGemsPass(ItemSocket s, Func<int, ItemData> data, Func<SocketGem, ItemData, bool> pass)
    {
        if (s == null || s.MaxSlot == 0 || s.Gems.Count == 0) return true;
        foreach (var g in s.Gems)
        {
            if (g == null || g.GemId == 0) continue;
            var d = data(g.GemId);
            if (d == null) continue;
            if (!pass(g, d)) return false;
        }
        return true;
    }

    /// <summary>CItemSocket::FUN_140622790 (itemsocket.cpp:801), IsGemTradeable (:706) on each gem: not bound or locked, and
    /// its transfer type's rule has bit 4.</summary>
    public static bool GemsTradeable(ItemSocket s, Func<int, ItemData> data, long now) =>
        AllGemsPass(s, data, (g, d) => !GemBound(g) && !GemLocked(g, now) && (ComputeRule(d.TransferType) & 4) != 0);

    /// <summary>CItemSocket::FUN_140622750 (itemsocket.cpp:789) -> FUN_1406220f0 (:698): a set gem (TSocketGemStoneInfo not
    /// IsEmpty) fails, so only a socket with no gem passes.</summary>
    public static bool NoSetGem(ItemSocket s, Func<int, ItemData> data) => AllGemsPass(s, data, (_, _) => false);

    /// <summary>
    /// CanDestroyItem @0x140633700 (itemsocketsystemutil.cpp:2305), the drop on the ground: the transfer's CanDestroy (:2918: not
    /// bound, FUN_140634cc0 always 1, not locked (FUN_140635870), and the rule bit 4), no set gem, and the enchant's IsEnabled.
    /// An item without a record has none of the three parts, which pass.
    /// </summary>
    public static bool CanDrop(Item item, Func<int, ItemData> data, long now)
    {
        if (item == null) return false;
        var r = item.Record;
        if (r == null) return true;
        if (r.Transfer is { } t && (t.HasBinding || IsLocked(item, now) || (t.Rule & 4) == 0)) return false;
        if (!NoSetGem(r.Socket, data)) return false;
        if (r.Enchant != null && !r.Enchant.HistoryEnabled) return false;
        return true;
    }

    /// <summary>CItemSocket::CheckNoLockedGem (itemsocket.cpp): no gem locked (HasLockedGem).</summary>
    public static bool NoLockedGem(ItemSocket s, long now) => s == null || !s.Gems.Any(g => g != null && g.GemId != 0 && GemLocked(g, now));

    /// <summary>
    /// FUN_140633e40 (itemsocketsystemutil.cpp:2422), CanTradeItem: the transfer's trade rule (reason 1), the gems
    /// (reason 2), the enchant's IsEnabled (reason 1). False without a record.
    /// </summary>
    public static bool CanTrade(Item item, Func<int, ItemData> data, long now, out int reason)
    {
        reason = 0;
        var r = item?.Record;
        if (r == null) return false;
        if (TransferTradeRule(r.Transfer, IsLocked(item, now)) == 0) { reason = 1; return false; }
        if (!GemsTradeable(r.Socket, data, now)) { reason = 2; return false; }
        if (!r.Enchant.HistoryEnabled) { reason = 1; return false; }
        return true;
    }

    /// <summary>FUN_140634120 (itemsocketsystemutil.cpp:2478), CItemLockManager::CanUseItem's test: the item not locked
    /// (CItemTransfer::FUN_140635870, reason 1), no locked gem (reason 2).</summary>
    public static bool NotLocked(Item item, long now, out int reason)
    {
        reason = 0;
        if (item == null) return false;
        if (IsLocked(item, now)) { reason = 1; return false; }
        if (!NoLockedGem(item.Record?.Socket, now)) { reason = 2; return false; }
        return true;
    }

    /// <summary>CItem::FUN_140526850 (item.cpp:1419): a stack the count box may split: not equipment, a count other than 1,
    /// stackable and not locked.</summary>
    public static bool AsksCount(Item item, long now) =>
        !item.Data.IsEquip && item.Count != 1 && item.Data.IsStackable && !IsLocked(item, now);

    /// <summary>CItemSocket::CheckGemsShopSellable (itemsocket.cpp:783 -> FUN_140622020 :690 -> IsGemShopSellable :738):
    /// any socketed non-empty gem (m_nGemId != 0 with item data) returns false; otherwise true.</summary>
    public static bool GemsShopSellable(ItemSocket s, Func<int, ItemData> data, long now) =>
        AllGemsPass(s, data, (g, d) => g.GemId == 0 && !GemLocked(g, now) && d.ShopSell);

    /// <summary>FUN_140633840 (itemsocketsystemutil.cpp:2323): CItemTransfer::IsSellable (shopSell and not locked) and
    /// CheckGemsShopSellable.</summary>
    public static bool CanSellToShop(Item item, Func<int, ItemData> data, long now) =>
        item?.Data != null && item.Data.ShopSell && !IsLocked(item, now) && GemsShopSellable(item.Record?.Socket, data, now);

    /// <summary>CItem::FUN_140525c60 (item.cpp:1965 -> FUN_140528910 :308): m_80 &gt; 0 (the item has an expiry time).</summary>
    public static bool HasPeriod(Item item) => item != null && item.ExpiryTime > 0;
}
