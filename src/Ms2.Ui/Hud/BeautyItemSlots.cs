using System;
using Ms2.Net.Packets;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The item-data predicates the Customize asks of an item: CItemData::FUN_14053d2b0 (top-and-bottom, slots 8 and 9 go
/// together), FUN_14053d460 (two-handed, slots 4 and 5), FUN_14053d340 / FUN_14053d2f0, and FUN_141420f10 (the slot an item id
/// goes on, avatarcustomize.cpp). Item data and model come from the host (<see cref="ItemDataTable.Get"/>,
/// <see cref="ItemModelTable.Get"/>); an unknown item answers false / 0.
/// <para>STILL-OPEN: FUN_141420b30 (@0x141420b30, unit_141420a70.cpp:39) picks the slot of a hand, ring or two-handed item from
/// what the avatar wears (PC layer 0/1 items of slots 4 and 5, feature 0xda, CItem::FUN_140529070). It is the host-set
/// <see cref="WornPairSlot"/>; unset, the slot of such an item is 0 (unknown) and a path that needs it does nothing.</para>
/// </summary>
public sealed class BeautyItemSlots
{
    readonly Func<int, ItemData> data;
    readonly Func<int, ItemModelTable.Model> model;

    public BeautyItemSlots(Func<int, ItemData> data, Func<int, ItemModelTable.Model> model)
    {
        this.data = data; this.model = model;
    }

    /// <summary>FUN_141420b30: the slot (4 or 5, 0 for none) a two-hand-capable item goes on given what is worn; host-set.</summary>
    public Func<ItemData, int> WornPairSlot;

    // GetModelOrEmpty: the item's model preset, an empty model without one.
    ItemModelTable.Model ModelOf(ItemData d) =>
        (d.ItemPreset != 0 ? model(d.ItemPreset) : null) ?? new ItemModelTable.Model(false, 0, Array.Empty<int>(), Array.Empty<int>());

    /// <summary>CItemData::FUN_14053d2b0 (commonfiles/itemdata.cpp:848): the equip subtype is 0x16.</summary>
    public bool IsTopBottom(ItemData d) => d != null && d.EquipSubtype == 0x16;

    /// <summary>CItemData::FUN_14053d460 (itemdata.cpp:893): a type-1 item of the equip subtypes 0x32-0x38 and 0x50-0x53, or a
    /// type-0x17 item whose model has both a slot 5 and a slot 4 group.</summary>
    public bool IsTwoHanded(ItemData d)
    {
        if (d == null) return false;
        if (d.Type == 1) return d.EquipSubtype is (>= 0x32 and <= 0x38) or (>= 0x50 and <= 0x53);
        if (d.Type == 0x17) { var m = ModelOf(d); return m.HasSlotType(5) && m.HasSlotType(4); }
        return false;
    }

    /// <summary>CItemData::FUN_14053d340 (itemdata.cpp:860): a hand-held item: type-1 subtypes 0x1e-0x22, 0x28, 0x29, any type 8,
    /// or a type-0x17 item with exactly one of the slot 5 / slot 4 groups.</summary>
    public bool IsHandItem(ItemData d)
    {
        if (d == null) return false;
        if (d.Type == 1) return d.EquipSubtype is (>= 0x1e and <= 0x22) or 0x28 or 0x29;
        if (d.Type == 8) return true;
        if (d.Type == 0x17)
        {
            var m = ModelOf(d);
            if (m.HasSlotType(5) && m.HasSlotType(4)) return false;
            return m.HasSlotType(5) || m.HasSlotType(4);
        }
        return false;
    }

    /// <summary>CItemData::FUN_14053d2f0 (itemdata.cpp:854): a hand item or a two-handed one.</summary>
    public bool IsHandOrTwoHanded(ItemData d) => IsHandItem(d) || IsTwoHanded(d);

    /// <summary>FUN_141420f10 (unit_141420f10.cpp:21): 8 for a top-and-bottom item, else the slot FUN_141420b30 names for a
    /// hand / two-handed item when it names one, else the model's first group type. 0 for an unknown item.</summary>
    public EquipSlot SlotOfId(int itemId)
    {
        var d = data(itemId);
        if (d == null) return 0;
        if (IsTopBottom(d)) return (EquipSlot)8;
        if (IsHandOrTwoHanded(d))
        {
            // FUN_141420b30: 0 unless FUN_14053d2f0; the host answers it from the worn items. Without one nothing is known.
            int paired = WornPairSlot?.Invoke(d) ?? 0;
            if (paired != 0) return (EquipSlot)paired;
            if (WornPairSlot == null) return 0;
        }
        return (EquipSlot)ModelOf(d).FirstSlotType;
    }
}
