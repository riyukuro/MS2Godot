using Ms2.Ui.Skills;
using Ms2.Net.Packets;

namespace Ms2.Ui.Items;

/// <summary>
/// A player's CEquipment as a record carries it: per layer, the item at its slot. The local player's one answers the
/// inventory hover's worn-item lookup (MyPC::FUN_14152e2f0) and its "equipped" test (PC::GetEquipByUid).
/// </summary>
public sealed partial class WornEquipment
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

    /// <summary>CEquipment::FUN_1411e1fc0(list, 2): the worn items of every layer but 2, each once (the item's m_bShown slot;
    /// here one entry per item at its slot).</summary>
    public IEnumerable<Item> Shown()
    {
        for (int layer = 0; layer < layers.Length; layer++)
        {
            if (layer == 2) continue;
            foreach (var it in layers[layer].Values) yield return it;
        }
    }

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
    /// CItemData::FUN_14053d340 (itemdata.cpp:860): a one-handed weapon, type 1 subtypes 0x1e-0x22, 0x28, 0x29; any type 8;
    /// a type 0x17 item whose model has exactly one of the RH (5) and LH (4) groups.
    /// </summary>
    public static bool IsOneSlotWeapon(ItemData d, ItemModelTable models) => d.Type switch
    {
        1 => d.EquipSubtype is (>= 0x1e and <= 0x22) or 0x28 or 0x29,
        8 => true,
        0x17 => models.Get(d.ItemPreset) is { } m && m.HasSlotType(5) != m.HasSlotType(4),
        _ => false,
    };

    /// <summary>CItemData::FUN_14053d2f0 (itemdata.cpp:853): a weapon, <see cref="IsOneSlotWeapon"/> or a two-handed one.</summary>
    public static bool IsWeapon(ItemData d, ItemModelTable models) => IsOneSlotWeapon(d, models) || IsTwoHanded(d, models);

    /// <summary>
    /// CItemData::FUN_14053d510 (@0x14053d510, commonfiles/itemdata.cpp:925-934): a dual-wield OH weapon, equip subtype
    /// 0x1f (31, dagger) or 0x22 (34, thrown star).
    /// </summary>
    public static bool IsDualWieldWeapon(ItemData d) => d != null && d.EquipSubtype is 0x1f or 0x22;

    /// <summary>
    /// CEquipment::FUN_1411e1860(slot, 6, 0) (equipment.cpp:816), PC::FUN_141655e10: the item at the slot in the highest layer
    /// whose item the wearer can use (PC::CanUseItem, <paramref name="canUse"/>).
    /// </summary>
    public Item TopUsable(int slot, Func<Item, bool> canUse)
    {
        for (int layer = layers.Length - 1; layer >= 0; layer--)
            if (At(slot, layer) is { } it && canUse(it)) return it;
        return null;
    }

    /// <summary>
    /// CMagic::PlayHitEffects' worn weapon material (magicdata.cpp:1709): <see cref="ItemData.AttackMaterial"/> of
    /// <see cref="TopUsable"/>(5), the RH slot; 0 with no usable RH item.
    /// </summary>
    public int WeaponMaterial(Func<Item, bool> canUse) => TopUsable(5, canUse)?.Data.AttackMaterial ?? 0;

    /// <summary>
    /// FUN_141420a70 (unit_141420a70.cpp:25): the equip slot a right-clicked item is sent for (RequestEquipItem with slot 0).
    /// FUN_141420b30 first, 0 for no weapon: a two-handed weapon RH (5). Equip subtype 0x1f / 0x22 (FUN_14053d510): a skin
    /// item by the layer-0 hands (both worn: RH while layer 1 has no RH, else LH while layer 1 has no LH, else RH; one worn:
    /// that hand; none: 0); else by the hands of <see cref="TopUsable"/>: no RH item: RH; a two-handed RH item: RH; an RH and
    /// an LH item: RH, or LH with feature 0xda when the RH item's level (CItem m_b0, <paramref name="itemLevel"/>) is above the
    /// LH one's; only RH: LH. Other weapons 0. When 0, CItem::FUN_140526c30, m_68, as CItem::FUN_140523c40 sets it for a bag
    /// item (item.cpp:2782): 5 for subtype 0x1f / 0x22, else the model's first group type (FUN_140527a20 with m_68 0).
    /// The level is <see cref="ItemLevels.Of"/>; when either is not known the RH result stays.
    /// </summary>
    public int EquipSlotFor(Item item, ItemModelTable models, Func<int, bool> feature, Func<Item, bool> canUse, Func<Item, int?> itemLevel = null)
    {
        if (item == null) return 0;
        var d = item.Data;
        int slot = 0;
        if (IsWeapon(d, models))
        {
            if (IsTwoHanded(d, models)) slot = 5;
            else if (d.EquipSubtype is 0x1f or 0x22)
            {
                if (d.HasSkinType)
                {
                    bool rh = At(5, 0) != null, lh = At(4, 0) != null;
                    if (rh && lh) slot = At(5, 1) == null ? 5 : At(4, 1) == null ? 4 : 5;
                    else if (rh) slot = 5;
                    else if (lh) slot = 4;
                }
                else
                {
                    var lh = TopUsable(4, canUse);
                    var rh = TopUsable(5, canUse);
                    if (rh == null) slot = 5;
                    else if (IsTwoHanded(rh.Data, models)) slot = 5;
                    else if (lh != null)
                    {
                        slot = 5;
                        if (feature(0xda) && itemLevel?.Invoke(rh) is int r && itemLevel(lh) is int l && r > l) slot = 4;
                    }
                    else slot = 4;
                }
            }
        }
        if (slot != 0) return slot;
        return d.EquipSubtype is 0x1f or 0x22 ? 5 : models.Get(d.ItemPreset)?.FirstSlotType ?? 0;
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
    /// <summary>
    /// The worn items' part of CEquipment::GetAbilityTotal (equipment.cpp:740), the record's m_3b0: the m_abilities sum of
    /// CEquipment::FUN_1411e76c0 (equipment.cpp:2921). Per item of layers 0 and 1 (layer 2 only counts set items there) the
    /// wearer can use (PC::CanUseItem, <paramref name="canUse"/>) at its level (CItem::CanUseAtLevel, item.cpp:653,
    /// <see cref="ItemData.IsLevelAllowed"/>), except a suit (subtype 0x16) in PA and a two-handed weapon in LH: per special
    /// ability below 0xcf, CItemStatus::GetSpecialValue(k, 3, 5) (itemsocketsystemutil.cpp:1955), the five option groups
    /// (CItemOption::SumSpecialValue, itemoptiondata_1.cpp:104) plus the limit break's special option. STILL-OPEN: the PvP
    /// field values (FUN_14062ff70), slots a buff disables (IsItemSlotDisabled), the pet item; GetAbilityTotal's buff and
    /// game rule (hot time) terms are in <see cref="AbilityTotal(int, int, AdditionalBuffList, SpecialAbilityHotTimes, bool)"/>. Null when the record has no equipment lists.
    /// </summary>
    public static Dictionary<int, int> Abilities(PcInfo record, ItemDataTable items, ItemModelTable models, Func<Item, bool> canUse)
    {
        if (record?.Equips == null) return null;
        var sum = new Dictionary<int, int>();
        void Add(int k, int v)
        {
            if (k is < 0 or >= 0xcf || v == 0) return;
            sum[k] = (sum.TryGetValue(k, out var x) ? x : 0) + v;
        }
        foreach (var e in record.Equips)
        {
            var it = Hud.InventoryController.ToItem(e, items);
            if (it == null || (int)e.Slot == 0 || !canUse(it) || !it.Data.IsLevelAllowed(record.Level)) continue;
            if ((e.Slot == EquipSlot.PA && it.Data.EquipSubtype == 0x16) || (e.Slot == EquipSlot.LH && IsTwoHanded(it.Data, models)))
                continue;
            foreach (var g in e.Options.Special) foreach (var (k, v) in g) Add(k, v);
            foreach (var (k, v) in e.LimitBreak.Special) Add(k, v);
        }
        return sum;
    }

    /// <summary>
    /// CEquipment::GetAbilityTotal @0x1411e1160 (equipment.cpp:740): the worn items' <see cref="Abilities"/> of
    /// <paramref name="type"/> plus the own buffs' <see cref="AdditionalBuffList.SpecialAbility"/>, without the game rule
    /// term (the overload over a kept worn total adds it). STILL-OPEN: the set items' term (GetSetItems()->GetSpecialAbility),
    /// 0 without the set item table.
    /// </summary>
    public static int AbilityTotal(int type, PcInfo record, ItemDataTable items, ItemModelTable models, Func<Item, bool> canUse,
                                   AdditionalBuffList buffs)
    {
        int n = Abilities(record, items, models, canUse) is { } worn && worn.TryGetValue(type, out int v) ? v : 0;
        return n + (buffs?.SpecialAbility(type) ?? 0);
    }

    /// <summary>
    /// CEquipment::GetAbilityTotal @0x1411e1160 (equipment.cpp:740-746) over a worn total kept like m_abilities
    /// (<paramref name="worn"/>, taken from <see cref="Abilities"/> when the equipment changes): plus the own buffs'
    /// <see cref="AdditionalBuffList.SpecialAbility"/> and the game rule's hot time sum (CGameRule::FUN_1404774a0, with
    /// the PC bang events when PC::FUN_141658f30, <paramref name="pcBang"/>). STILL-OPEN: the set items' term
    /// (GetSetItems()->GetSpecialAbility) is 0 here. No set row in the KMS2 or KMS2_DEV SetItemInfo carries
    /// improve_riding_speed (0xae) or improve_riding_run_speed (0xa5): 0 of 1039 / 1025 sets, 66 / 60 special values in
    /// all. So the ride speeds are right without the term; other types still need it. No allocation.
    /// </summary>
    public static int AbilityTotal(int worn, int type, AdditionalBuffList buffs, SpecialAbilityHotTimes rule, bool pcBang) =>
        worn + (buffs?.SpecialAbility(type) ?? 0) + (rule?.Total(type, pcBang) ?? 0);

    /// <summary>CEquipment::GetAbilityRate @0x1411e1110 (equipment.cpp:731): the ability total over 1000.</summary>
    public static float AbilityRate(int total) => total / 1000.0f;
}
