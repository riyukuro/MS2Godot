using Ms2.Net.Packets;

namespace Ms2.Ui.Items;

/// <summary>
/// CEquipment's slots (m_slots: the ctor @0x1411dfe00, equipment.cpp:534-543, makes CEquipmentSlot 0 to 19, each with an item
/// per layer) as CEquipment::FUN_1411ec190 (ApplyEquipItem @0x1411ec190, equipment.cpp:2174) fills them: a worn item goes in
/// the slot of every group of its model (FUN_141420990, itemservice.cpp:568: the group's type, 0x13 the item's own slot m_68,
/// 0 none), at its layer (CItem::FUN_14052a170). So an item whose model takes several slots answers for each of them: a
/// top-and-bottom outfit worn at CL is also the item at PA, a two-hander worn at RH is also the item at LH. An item whose model
/// has no groups takes no slot. CEquipment::GetItemAt (@0x1411e1780, :806) and PC::GetEquipBySlotKind (pc.cpp:1370) read one.
/// <see cref="WornEquipment"/> is the PC's per-slot maps instead (an item at its packet slot only).
/// </summary>
public sealed class EquipmentSlots
{
    /// <summary>The ctor's slot count (0 to 19).</summary>
    public const int SlotCount = 20;
    /// <summary>The layers a slot keeps (the PC's m_mapEquip0 / 1 / 2; layer 3, property.flag08, is never loaded).</summary>
    public const int LayerCount = 3;
    /// <summary>FUN_141420990's group type for "the item's own slot" (OH).</summary>
    public const int OwnSlotGroup = 0x13;

    readonly Item[,] items = new Item[SlotCount, LayerCount];

    /// <summary>
    /// The record's equipment as the client applies it: each worn item (the equip list, then the variant-2 outfit list at layer
    /// 2) through <see cref="Apply"/> in list order, at its packet slot, its layer as <see cref="WornEquipment.Layers"/> gives
    /// it. Null when the record has no equipment lists.
    /// </summary>
    public static EquipmentSlots Of(PcInfo record, ItemDataTable items, ItemModelTable models)
    {
        if (record?.Equips == null) return null;
        var s = new EquipmentSlots();
        void Add(EquipItem e, bool outfit2)
        {
            var it = Hud.InventoryController.ToItem(e, items);
            if (it?.Data == null) return;
            int layer = outfit2 ? 2 : it.Data.HasSkinType ? 1 : 0;
            s.Apply(it, (int)e.Slot, layer, models.Get(it.Data.ItemPreset)?.SlotTypes);
        }
        foreach (var e in record.Equips) Add(e, false);
        if (record.Outfit2 != null) foreach (var e in record.Outfit2) Add(e, true);
        return s;
    }

    /// <summary>
    /// FUN_1411ec190's loop: for each of the model's group types, the slot FUN_141420990 gives (the type itself; 0x13 the item's
    /// own slot; 0 skipped) gets the item at the layer, a later item replacing an earlier one. STILL-OPEN: an 0x13 group of an
    /// item with no slot of its own (m_68 0) takes FUN_141420b30's slot; a worn record item always has its slot, so it is
    /// skipped here.
    /// </summary>
    public void Apply(Item item, int ownSlot, int layer, IReadOnlyList<int> groupTypes)
    {
        if (item == null || groupTypes == null || layer < 0 || layer >= LayerCount) return;
        foreach (int type in groupTypes)
        {
            int slot = type == OwnSlotGroup ? ownSlot : type;
            if (slot <= 0 || slot >= SlotCount) continue;   // FUN_141420990's 0, and m_slots.Lookup's miss
            items[slot, layer] = item;
        }
    }

    /// <summary>CEquipment::GetItemAt(slot, layer): the item in the slot at the layer, null when none.</summary>
    public Item At(int slot, int layer) =>
        slot >= 0 && slot < SlotCount && layer >= 0 && layer < LayerCount ? items[slot, layer] : null;

    /// <summary>The slots the model's groups take for an item at <paramref name="ownSlot"/> (FUN_141420990 per group), each once,
    /// in group order.</summary>
    public static List<int> SlotsOf(IReadOnlyList<int> groupTypes, int ownSlot)
    {
        var r = new List<int>();
        if (groupTypes == null) return r;
        foreach (int type in groupTypes)
        {
            int slot = type == OwnSlotGroup ? ownSlot : type;
            if (slot > 0 && slot < SlotCount && !r.Contains(slot)) r.Add(slot);
        }
        return r;
    }
}
