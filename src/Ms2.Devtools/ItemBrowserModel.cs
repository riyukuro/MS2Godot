#if MS2_DEVTOOLS
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;

namespace Ms2.Devtools;

/// <summary>
/// The item the browser spawns, as the client's own model: the same <see cref="Item"/> and <see cref="EquipItem"/> record
/// the bag's tooltip reads, filled from <see cref="ItemBrowserSettings"/>, and the tooltip data of
/// <see cref="ItemTooltip"/> (flags 0x80, the bag's) so the hover shows the real CommonTooltip movie. The random option
/// group holds the explicit lines only; the enchant and limit break blocks hold their level and, when the same settings
/// were spawned before, the options the server made (the client has no enchant or limit-break option table of its own:
/// the client's Enchant.Options and LimitBreak.Stat/Special are read from the item stream, itemenchantinfo.cpp:194 and
/// unlimitedenchantinfo.cpp:101; the server's tables are EnchantOptionTable/UnlimitedEnchantOptionTable).
/// </summary>
public static class ItemBrowserModel
{
    /// <summary>
    /// The rarity a spawn makes: the chosen one, else the server item command's own default (Item.DefaultRarity: the
    /// constant option code when it is 1..6, else the data's rank, else 1), so a rank-0 item's hover shows the grade the
    /// bag will.
    /// </summary>
    public static int EffectiveRarity(ItemData d, int chosen) =>
        chosen > 0 ? chosen : d.ConstantId is > 0 and < 7 ? d.ConstantId : d.Rank is > 0 and < 7 ? d.Rank : 1;

    /// <summary>The item and its tooltip data; null for an unknown item or one the tooltip refuses.</summary>
    public static (Item Item, AsTooltip Tooltip)? Build(UiModel model, int itemId, ItemBrowserSettings s, EquipItem server = null)
    {
        if (model?.Tables.Items.Get(itemId) is not { } data) return null;
        var item = Item.Create(data, EffectiveRarity(data, s.Rarity));
        if (item == null) return null;
        var rec = new EquipItem { Id = itemId, Rarity = item.Rarity, Slot = EquipSlot.Unknown };
        item.Record = rec;
        item.EnchantLevel = Math.Max(0, s.Enchant);
        item.UnlimitedEnchantLevel = Math.Max(0, s.LimitBreak);

        if (server != null && (server.Enchant?.Level ?? 0) == item.EnchantLevel && item.EnchantLevel > 0)
        {
            rec.Enchant.Level = item.EnchantLevel;
            rec.Enchant.Version = server.Enchant.Version;
            rec.Enchant.Charge = server.Enchant.Charge;
            foreach (var (id, v) in server.Enchant.Options) rec.Enchant.Options[id] = v;
        }
        else if (item.EnchantLevel > 0)
        {
            rec.Enchant.Level = item.EnchantLevel;
            rec.Enchant.Version = 1;
        }
        if (server != null && (server.LimitBreak?.Level ?? 0) == item.UnlimitedEnchantLevel && item.UnlimitedEnchantLevel > 0)
        {
            rec.LimitBreak.Level = item.UnlimitedEnchantLevel;
            foreach (var (id, v) in server.LimitBreak.Stat) rec.LimitBreak.Stat[id] = v;
            foreach (var (id, v) in server.LimitBreak.Special) rec.LimitBreak.Special[id] = v;
        }
        else if (item.UnlimitedEnchantLevel > 0)
        {
            rec.LimitBreak.Level = item.UnlimitedEnchantLevel;
        }

        // The sockets: the client gives the item a maximum from its socket table; the unlocked sockets are the gems list.
        var socketMax = ItemBrowserLimits.SocketMax(model.Tables, model.IsFeatureEnabled, data, item.Rarity);
        if (socketMax > 0)
        {
            rec.Socket.MaxSlot = socketMax;
            for (int i = 0; i < s.Sockets && i < ItemBrowserLimits.MaxSockets; i++)
                rec.Socket.Gems.Add(s.Gems[i] == 0 ? null : new SocketGem { GemId = s.Gems[i] });
        }

        // The constant group is the item's own option code at its rarity, as the server makes it (or the server's own
        // group when the same settings were spawned before); the random group is the browser's lines alone. The tooltip
        // skips a kind 0 stat the item data already gives, so an extra one is harmless.
        if (server != null)
        {
            for (int kind = 0; kind <= 1; kind++)
            {
                foreach (var (id, v) in server.Options.Stat[kind]) rec.Options.Stat[kind][id] = v;
                foreach (var (id, v) in server.Options.Special[kind]) rec.Options.Special[kind][id] = v;
            }
        }
        else if (model.Tables.ItemOptions.ConstantOptions(data, item.Rarity) is { } constant)
        {
            foreach (var (id, v) in constant.Stat) rec.Options.Stat[0][id] = v;
            foreach (var (id, v) in constant.Special) rec.Options.Special[0][id] = v;
        }
        foreach (var b in s.BonusLines)
        {
            if (b.Special) rec.Options.Special[2][b.Id] = b.Value;
            else rec.Options.Stat[2][b.Id] = b.Value;
        }

        var tip = new ItemTooltip(model, 0x80) { Localize = k => k };
        if (!tip.FillCompare(null, item)) return null;
        bool feature8e = model.IsFeatureEnabled(0x8e);
        tip.SetEnablePutonBoth("", UseHint(model, item, feature8e));
        tip.SetEnablePreviewBoth(null, item);
        return (item, tip.Data);
    }

    /// <summary>
    /// InvenSlot::vfunc_0's use hint with no dialog at the head of DAT_143af2258 (the shop, trade, break, lock and merge
    /// are not the bag's): UseMode's last part (invenslot.cpp:1957) then UseHint (InventoryController.UseHint): a UGC item
    /// in look state 2 is 15, a gear item 6 (its skin line with feature 0x8e), a quick slot item 7, a gem 12.
    /// </summary>
    static string UseHint(UiModel model, Item item, bool feature8e)
    {
        var t = model.Tables;
        int mode = item.Record is { HasUgc: true, Ugc.State: 2 } ? 15 : item.Data.Type == 1 ? 6 : item.Data.IsQuickSlotUsable ? 7 : item.Data.IsGemstone ? 12 : 0;
        return mode switch
        {
            6 when feature8e && item.Data.HasSkinType => SkinHint(model),
            6 => t.Common("s_msg_rclick_puton_equip") ?? "",
            7 => t.Common("s_msg_rclick_puton_qslot") ?? "",
            _ => "",
        };
    }

    static string SkinHint(UiModel model)
    {
        var t = model.Tables;
        string text = t.Common("s_msg_rclick_puton_equip_to_coditab") ?? "";
        var keys = model.Keys;
        if (keys == null) return text;
        string key = keys.GetKeyName(keys.FindKeyByActionAndPriority(0x4940d, 0));
        return key.Length == 0 ? text : text + "\n" + Ms2.Quest.ClientFormat.Format(t.Common("s_msg_rclick_puton_equip_to_skintab_with_key") ?? "", key);
    }

    /// <summary>The bag item the browser would spawn with these settings, when the server has already made one (its
    /// enchant and limit break options are the server's own). Null for none.</summary>
    public static EquipItem Spawned(ItemBook book, UiModel model, int itemId, ItemBrowserSettings s)
    {
        if (book == null || model?.Tables.Items.Get(itemId) is not { } data) return null;
        int rarity = EffectiveRarity(data, s.Rarity);
        foreach (var e in book.All)
        {
            if (e.Id != itemId || e.Rarity != rarity) continue;
            if ((e.Enchant?.Level ?? 0) != Math.Max(0, s.Enchant)) continue;
            if ((e.LimitBreak?.Level ?? 0) != Math.Max(0, s.LimitBreak)) continue;
            if (!SameSockets(e.Socket, s)) continue;
            return e;
        }
        return null;
    }

    static bool SameSockets(ItemSocket socket, ItemBrowserSettings s)
    {
        if (s.Sockets == 0) return socket == null || socket.Gems.Count == 0;
        if (socket == null || socket.Gems.Count != s.Sockets) return false;
        for (int i = 0; i < s.Sockets; i++)
            if ((socket.Gems[i]?.GemId ?? 0) != s.Gems[i]) return false;
        return true;
    }
}
#endif
