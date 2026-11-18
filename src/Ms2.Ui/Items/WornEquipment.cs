using Ms2.Net.Packets;

namespace Ms2.Ui.Items;

/// <summary>
/// A player's CEquipment as a record carries it: per layer, the item at its slot. The local player's one answers the
/// inventory hover's worn-item lookup (MyPC::FUN_14152e2f0) and its "equipped" test (PC::GetEquipByUid).
/// </summary>
public sealed class WornEquipment
{
    readonly Dictionary<int, Item>[] layers;

    WornEquipment(Dictionary<int, Item>[] layers) { this.layers = layers; }

    /// <summary>The record's equipment; null when the record has no equipment lists.</summary>
    public static WornEquipment Of(PcInfo record, ItemDataTable items) =>
        Layers(record, items) is { } m ? new WornEquipment(m) : null;

    /// <summary>
    /// The record's m_mapEquip0 / 1 / 2 (PC::vfunc_126, pc.cpp:4171): per layer, the item at its slot (CItem m_68, the
    /// packet's equip slot; FUN_140527a20's model group fallback for slot 0 is not here, such an item is left out). The layer
    /// is CItem::FUN_14052a170 (item.cpp:1629): 2 for the variant-2 outfits, else 1 with a skin type, else 0 (cash or not;
    /// property.flag08, layer 3, is never loaded: ItemProperty::Load, itemdatastruct.cpp:1122). Null when the record has no
    /// equipment lists.
    /// </summary>
    public static Dictionary<int, Item>[] Layers(PcInfo record, ItemDataTable items)
    {
        if (record?.Equips == null) return null;
        var m = new[] { new Dictionary<int, Item>(), new Dictionary<int, Item>(), new Dictionary<int, Item>() };
        void Add(EquipItem e, bool outfit2)
        {
            var it = Hud.InventoryController.ToItem(e, items);
            if (it == null || (int)e.Slot == 0) return;
            int layer = outfit2 ? 2 : it.Data.HasSkinType ? 1 : 0;
            m[layer][(int)e.Slot] = it;
        }
        foreach (var e in record.Equips) Add(e, false);
        if (record.Outfit2 != null) foreach (var e in record.Outfit2) Add(e, true);
        return m;
    }

    /// <summary>CEquipment::GetItemAt: the item at a slot of a layer, null when none.</summary>
    public Item At(int slot, int layer) =>
        layer >= 0 && layer < layers.Length && layers[layer].TryGetValue(slot, out var it) ? it : null;

    /// <summary>PC::GetEquipByUid (CEquipment::FindItemByUid): an item of any slot and layer has the uid.</summary>
    public bool Wears(long uid) => layers.Any(l => l.Values.Any(it => it.Uid == uid));

    /// <summary>
    /// CItemData::FUN_14053d460 (itemdata.cpp:893): a two-handed weapon, type 1 subtypes 0x32-0x38 and 0x50-0x53, or a type
    /// 0x17 item whose model has both an RH and an LH group (ItemModelData::HasGroupType).
    /// </summary>
    public static bool IsTwoHanded(ItemData d, ItemModelTable models)
    {
        if (d.Type == 1) return d.EquipSubtype is (>= 0x32 and <= 0x38) or (>= 0x50 and <= 0x53);
        return d.Type == 0x17 && models.Get(d.ItemPreset) is { } m && m.HasSlotType(5) && m.HasSlotType(4);
    }

    /// <summary>
    /// MyPC::FUN_14152e2f0 (mypc.cpp:3888): the worn item an item is compared with, in its layer (1 for a skin item, else
    /// 0). A two-handed weapon: RH, else LH. Equip subtype 0x1f / 0x22 (CItemData::FUN_14053d510): LH while
    /// <paramref name="shiftHeld"/> (CUIService m_n128 via FUN_141adcb40), else RH. Subtype 0x16 (FUN_14053d2b0): CL, else PA.
    /// Else the slot of CItem::FUN_140527a20, the model's first group type (its m_68 is read only for subtype 0x1f / 0x22,
    /// handled above). Null when none.
    /// </summary>
    public Item ForCompare(Item item, ItemModelTable models, bool shiftHeld)
    {
        if (item == null) return null;
        var d = item.Data;
        int layer = d.HasSkinType ? 1 : 0;                                          // CItem::HasSkin
        if (IsTwoHanded(d, models)) return At(5, layer) ?? At(4, layer);
        if (d.EquipSubtype is 0x1f or 0x22) return At(shiftHeld ? 4 : 5, layer);
        if (d.EquipSubtype == 0x16) return At(8, layer) ?? At(9, layer);
        return At(models.Get(d.ItemPreset)?.FirstSlotType ?? 0, layer);             // GetModelOrEmpty: no model, no groups
    }
}
