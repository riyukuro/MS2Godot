using System.Text;
using Ms2.Net.Packets;

namespace Ms2.Ui.Items;

public sealed partial class ItemTooltip
{
    /// <summary>
    /// ASTooltip::SetItemSocket (astooltip.cpp:1577), "itemSocket": with feature 0x2e (and 0x5e for a skin item). The compact
    /// form (flag 0x800) needs flag 0x4000 and a non-pet item: s_tooltip_itemoption_socket around an empty row per slot the
    /// table allows (CItemSocketTable::GetMaxSocketCount) for a socket without a max slot, else an empty row per open slot and a
    /// lock row per locked slot (the max slot less the open ones). The full form needs a max slot: per open slot, an empty row
    /// (none for a pet) without a gem or its item data, else the gem's row: its option text (GetGemstoneOptionText), its trade
    /// text (FUN_1409e6120 with ", ") in s_tooltip_itemoption_socket_transfer, and for a non-pet its icon name (GetGemStoneName)
    /// and lock text (FUN_14062b310); then a pet with rows gets s_tooltip_itemoption_socket_by_pet, any other item the lock rows
    /// and s_tooltip_itemoption_socket. A made item's socket is empty (CItem::FUN_140523c40 makes a new CItemSocket).
    /// STILL-OPEN: a gem's additional effect lines (SkillDataTableCli::FUN_140aafef0, IsVisibleItemOption), a compact
    /// socket whose max count comes from Lua (no socketDataId), and a gem whose rank comes from Lua.
    /// </summary>
    void SetItemSocket(Item item)
    {
        var d = item.Data;
        if (!model.IsFeatureEnabled(0x2e)) return;
        if (d.HasSkinType && !model.IsFeatureEnabled(0x5e)) return;
        var socket = item.Record?.Socket ?? new ItemSocket();
        int open = socket.Gems.Count;                                            // GetOpenSlotCount
        int locked = Math.Max(0, socket.MaxSlot - open);                         // GetLockedSlotCount
        string rowEmpty = Common("s_tooltip_itemoption_socket_row_empty");
        string rowLock = Common("s_tooltip_itemoption_socket_row_lock");
        var rows = new StringBuilder();
        if ((Flags & 0x800) != 0)
        {
            if ((Flags & 0x4000) == 0 || d.IsPetItem) return;
            if (socket.MaxSlot == 0)
            {
                if (Tables.ItemSockets.GetMaxSocketCount(d, item.Rarity, model.IsFeatureEnabled) is not { } max) return;
                for (uint i = 0; i < max; i++) rows.Append(rowEmpty);
            }
            else
            {
                for (int i = 0; i < open; i++) rows.Append(rowEmpty);
                for (int i = 0; i < locked; i++) rows.Append(rowLock);
            }
            Data["itemSocket"] = CommonF("s_tooltip_itemoption_socket", rows.ToString());
            return;
        }
        if (socket.MaxSlot == 0) return;
        bool pet = d.IsPetItem;
        long now = new DateTimeOffset(model.Now).ToUnixTimeSeconds();
        foreach (var gem in socket.Gems)
        {
            var gd = gem == null || gem.GemId == 0 ? null : Tables.Items.Get(gem.GemId);
            if (gd == null)
            {
                if (!pet) rows.Append(rowEmpty);
                continue;
            }
            string option = GemOptionText(gd);
            bool personal = !ItemTradeRules.GemLocked(gem, now) && gd.ShopSell;                         // IsGemShopSellable
            bool tradeable = !ItemTradeRules.GemBound(gem) && !ItemTradeRules.GemLocked(gem, now)
                && (ItemTradeRules.ComputeRule(gd.TransferType) & 4) != 0;                              // IsGemTradeable
            string bindName = gem.HasBind ? gem.BindName : "";                                          // GetBindName
            bool bindable = gd.TransferType is 2 or 3 or 4;                                             // FUN_14053d7c0
            string text = TradeText(item, tradeable, personal, bindable, bindName.Length != 0, ", ", 0, bindName, false);
            if (text.Length != 0) text = CommonF("s_tooltip_itemoption_socket_transfer", text);
            if (pet) rows.Append(CommonF("s_tooltip_itemoption_socket_row_stat_by_pet", option, text));
            else
            {
                string icon = Tables.GemstoneUpgrades.Find(gem.GemId)?.Icon ?? "";                      // GetGemStoneName
                rows.Append(CommonF("s_tooltip_itemoption_socket_row_stat", icon, option, text, GemLockText(gem, now)));
            }
        }
        if (pet)
        {
            if (rows.Length != 0) Data["itemSocket"] = CommonF("s_tooltip_itemoption_socket_by_pet", rows.ToString());
            return;
        }
        for (int i = 0; i < locked; i++) rows.Append(rowLock);
        Data["itemSocket"] = CommonF("s_tooltip_itemoption_socket", rows.ToString());
    }

    /// <summary>GetGemstoneOptionText (itemsocketsystemutil.cpp:1284): FUN_14062aeb0 makes the gem's option at its rank
    /// (CItemOptionTable::CreateOption, a gem's constant options) and FUN_14062b020 gives the first group 0 stat's value
    /// text (FUN_140631600), else the first special's (FUN_140631830), else nothing. The maps are CAtlMaps of small int
    /// keys, so the first is the lowest key.</summary>
    string GemOptionText(ItemData gd)
    {
        if (Tables.ItemOptions.ConstantOptions(gd, gd.Rank) is not { } c) return "";
        if (c.Stat.Count != 0) return StatValue(c.Stat.Keys.Min(), c.Stat[c.Stat.Keys.Min()]);
        if (c.Special.Count != 0) return SpecialValue(c.Special.Keys.Min(), c.Special[c.Special.Keys.Min()]) ?? "";
        return "";
    }

    /// <summary>FUN_14062b310 (itemsocketsystemutil.cpp:1293): a locked gem (TSocketGemStoneLock::IsLocked) shows
    /// s_tooltip_itemoption_socket_row_lock_gemstone in state 1, else s_tooltip_itemoption_socket_row_unlock_gemstone.</summary>
    string GemLockText(SocketGem gem, long now)
    {
        if (!ItemTradeRules.GemLocked(gem, now)) return "";
        return Common(gem.LockState == 1 ? "s_tooltip_itemoption_socket_row_lock_gemstone" : "s_tooltip_itemoption_socket_row_unlock_gemstone");
    }

    /// <summary>
    /// FUN_1409e6120 (astooltip.cpp:5398): the trade line (the remain-only count 0x525, a trade count 0x523 / 0x524, else
    /// 0x520, 0x51e or 0x51f by what is refused), then for a bindable item the bind line (0x527 with the bound name, else by
    /// the item's transfer type: s_word_item_limit_bind_get, _pet, _equip, _use or 0x526), then with feature 0xcf a
    /// move-disabled item's s_word_item_limit_moveDisable_store_in (the stored character) or s_word_item_limit_moveDisable,
    /// each after the separator. The transfer type and move flag are the item's even when the flags are a gem's.
    /// </summary>
    string TradeText(Item item, bool tradeable, bool personal, bool bindable, bool bindOnGet, string separator, int count,
        string bindName, bool remainOnly)
    {
        var d = item.Data;
        string text = "";
        if (remainOnly) text = CommonF("s_word_item_limit_trade_count_without_sell_for_itemlock", count);
        else if (tradeable && count > 0)
            text = personal ? CommonF("s_word_item_limit_trade_count", count) : CommonF("s_word_item_limit_trade_count_without_sell", count);
        else if (!personal && !tradeable) text = Common("s_word_item_limit_sell_trade");
        else if (!personal) text = Common("s_word_item_limit_sell");
        else if (!tradeable) text = Common("s_word_item_limit_trade");
        string Add(string t, string line) => t.Length == 0 ? line : t + separator + line;
        if (bindable)
        {
            if (bindOnGet) text = Add(text, CommonF("s_word_item_limit_bind_format", bindName));
            else
            {
                string bind = d.TransferType switch
                {
                    2 => Common("s_word_item_limit_bind_get"),
                    3 or 4 => Common(d.IsPetItem ? "s_word_item_limit_bind_pet" : d.IsEquipItem ? "s_word_item_limit_bind_equip" : "s_word_item_limit_bind_use"),
                    _ => Common("s_word_item_limit_bind"),
                };
                text = Add(text, bind);
            }
        }
        if (model.IsFeatureEnabled(0xcf) && d.MoveDisable)
        {
            var r = item.Record;
            text = r != null && r.BindingId != 0                                  // CItem::FUN_14052a480 / FUN_14052a4f0
                ? Add(text, CommonF("s_word_item_limit_moveDisable_store_in", r.BindingName))
                : Add(text, Common("s_word_item_limit_moveDisable"));
        }
        return text;
    }
}
