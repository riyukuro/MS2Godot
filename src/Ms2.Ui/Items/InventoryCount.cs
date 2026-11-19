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
    /// <summary>CItemData::FUN_14053e530 (@0x14053e530): the bag category of an item type (0 equipment, 1 skin, 2 use,
    /// 3 etc., 4 gem, 5 quest, 6 furnishing, 7 misc, 8 pet, 9 pet food, 10 currency, 0xb badge, 0xc lapenshard, 0xd ...,
    /// 0xe ...). Category 15 and up is none.</summary>
    public static int BagCategory(ItemData d) => d?.Type switch
    {
        1 => d.HasSkinType ? 1 : 0,
        6 or 8 or 0xc or 0x17 => 4,
        5 or 9 => 2,
        0xd or 0xe => 6,
        3 => 5,
        0xf or 0x13 => 3,
        0x10 => 8,
        0xb => 9,
        2 => 10,
        0x14 => 0xb,
        7 => 0xc,
        0x15 => 0xd,
        0x1b => 0xe,
        _ => 7,
    };

    /// <summary>FUN_140a5f180 for a bag of items: 0 for an unknown id; else each matching item the character can use adds
    /// its count. <paramref name="rarity"/> 0 counts every rarity.</summary>
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

    /// <summary>
    /// PC::CanUseItem (@0x14165c720): the part this port can decide — an expired item (a pet is exempt) and an item bound
    /// to another character (CItemTransfer's binding and the socket gems' bindings, FUN_1406343b0) are unusable.
    /// STILL-OPEN: the PCBang and VIP gates, the wedding limit, the skin-layer gate, the score item's state and the
    /// CItemLockManager's lock are not ported, so those items count.
    /// </summary>
    public static bool CanUse(EquipItem e, ItemDataTable table, long characterId, long now)
    {
        if (e == null) return false;
        bool pet = table?.Get(e.Id)?.IsPetItem == true;
        if (!pet && e.ExpiryTime != 0 && e.ExpiryTime < now) return false;                  // IsExpired
        if (e.Transfer.HasBinding && e.Transfer.BindingId != 0 && e.Transfer.BindingId != characterId)
            return false;                                                                   // FUN_1406343b0
        if (e.Socket != null)
            foreach (var g in e.Socket.Gems)
                if (g != null && g.HasBind && g.BindCharacterId != 0 && g.BindCharacterId != characterId)
                    return false;                                                           // AllGemsUsableByCharacter
        return true;
    }
}
