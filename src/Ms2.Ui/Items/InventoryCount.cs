using Ms2.Net.Packets;

namespace Ms2.Ui.Items;

/// <summary>
/// Unk_Inventory's counts for the mastery recipes: FUN_140a5f180 (@0x140a5f180) takes the item's own bag category
/// (CItemData::FUN_14053e530), counts the items of that id there (the rarity too when it is nonzero) and skips every item
/// the character may not use (PC::CanUseItem, :4683). The other bag halves are unreachable for an id: every item of an
/// id is in the one category its data gives.
/// </summary>
public static class InventoryCount
{
    /// <summary>FUN_140a5f180 for a bag of items: 0 for an unknown id; else each matching item the character can use adds
    /// its count. <paramref name="rarity"/> 0 counts every rarity. The item's data picks one bag (FUN_14053fd90 ->
    /// FUN_14053e530, the category by type), so every item of the id is in the bag scanned.</summary>
    public static int ById(IEnumerable<EquipItem> items, ItemDataTable table, int itemId, int rarity, long characterId, long now)
    {
        if (itemId == 0 || items == null) return 0;
        if (table?.Get(itemId) == null) return 0;
        int count = 0;
        foreach (var e in items)
        {
            if (e == null || e.Id != itemId) continue;
            if (rarity != 0 && e.Rarity != rarity) continue;
            if (!CanUse(e, table, characterId, now)) continue;
            count += Math.Max(e.Amount, 0);
        }
        return count;
    }

    /// <summary>Unk_Inventory::FindItemByTag (@0x140a5e040, inventory.cpp:630) found an item: <paramref name="tag"/> is not 0
    /// and a bag item's data has that string tag (CItem::HasStringTag, the data's stringTag through
    /// <see cref="ItemTags.ByName"/>) and passes <paramref name="canUse"/> (PC::CanUseItem). Which of them the sort
    /// (FUN_140a5c650) puts first is not needed for whether one is found.</summary>
    public static bool HasItemWithTag(IEnumerable<EquipItem> items, ItemDataTable table, int tag, Func<EquipItem, ItemData, bool> canUse)
    {
        if (tag == 0 || items == null || table == null) return false;
        foreach (var e in items)
        {
            if (e == null || table.Get(e.Id) is not { } data) continue;
            if (ItemTags.ByName(data.StringTag) != tag) continue;
            if (canUse(e, data)) return true;
        }
        return false;
    }

    /// <summary>
    /// PC::CanUseItem (@0x14165c720): the part this port can decide — CItem::FUN_140528980 (an expired item is unusable
    /// unless it is a pet or its data HasMaidId) and an item bound to another character (CItemTransfer's binding and the
    /// socket gems' bindings, FUN_1406343b0) are unusable.
    /// STILL-OPEN: the PCBang and VIP gates, the wedding limit, the skin-layer gate, the score item's state and the
    /// CItemLockManager's lock are not ported, so those items count.
    /// </summary>
    public static bool CanUse(EquipItem e, ItemDataTable table, long characterId, long now)
    {
        if (e == null) return false;
        var data = table?.Get(e.Id);
        bool pet = data?.IsPetItem == true;
        if (!pet && data?.HasMaidId != true && e.ExpiryTime != 0 && e.ExpiryTime < now) return false;   // IsExpired
        if (e.Transfer.HasBinding && e.Transfer.BindingId != 0 && e.Transfer.BindingId != characterId)
            return false;                                                                   // FUN_1406343b0
        if (e.Socket != null)
            foreach (var g in e.Socket.Gems)
                if (g != null && g.HasBind && g.BindCharacterId != 0 && g.BindCharacterId != characterId)
                    return false;                                                           // AllGemsUsableByCharacter
        return true;
    }
}
