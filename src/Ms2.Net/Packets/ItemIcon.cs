namespace Ms2.Net.Packets;

/// <summary>
/// The icon an inventory slot shows. Chain: InventorySlotBag::RefreshSlotForItem 0x14140f5b0 -> InvenSlot::vfunc_10 (DragSlot::PushBinding 0x1422cc430)
/// -> InvenSlot::vfunc_8 0x14140ae90 (invenslot.cpp:1592) -> ASSlot::FUN_1409c3860 (asitemslot.cpp:1262, flag 1) -> ASSlot::SetIconPathString(CItem::GetIconPath()),
/// member "iconPath" -> CItem::GetIconPath 0x140528590 (item.cpp:2319) -> CItemData::GetIconPath (itemdata.cpp:2070) from item xml property@slotIcon (itemdatastruct.cpp:1131).
/// CItem::GetIconPath answers first from the item's own name field (m_strName), a ugc look, a score item and a gem look; those need the item body and are not read here.
/// </summary>
public static class ItemIcon {
    /// <summary>CItemData::GetIconPath: no slotIcon -> ./Data/Resource/Image/item/icon/{id}.png; a slotIcon not starting with '.' is put into the same pattern; else it is the path.</summary>
    public static string Path(int itemId, string slotIcon) {
        if (string.IsNullOrEmpty(slotIcon)) return $"./Data/Resource/Image/item/icon/{itemId}.png";
        if (slotIcon[0] != '.') return $"./Data/Resource/Image/item/icon/{slotIcon}.png";
        return slotIcon;
    }
}
