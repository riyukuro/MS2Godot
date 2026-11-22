namespace Ms2.Ui.Items;

/// <summary>
/// CEquipment::FUN_1411e3000 (@0x1411e3000, client/ms2/equipment.cpp:1190-1243): the worn items a list shows, and the helpers the
/// item colouring dialog's BuildItems (@0x1419ae170, uiitemcoloringdialog.cpp:595-635) filters them with.
/// </summary>
public sealed partial class WornEquipment
{
    /// <summary>CItemData::IsAvatarColorItem (@0x14053cf00, itemdata.cpp:744): the avatar colour item.</summary>
    public const int AvatarColorItemId = 0x9a1d21;

    /// <summary>
    /// CItemData::IsCustomizable (@0x14053e170, itemdata.cpp:1286): a model whose colour is on (ItemModelHead flag10) with a
    /// palette (+0x14, FUN_140547250) and not more than one default colour (flag11, FUN_140547230).
    /// </summary>
    public static bool IsCustomizable(ItemData d, ItemModelTable models) =>
        d != null && d.ItemPreset != 0 && models.Get(d.ItemPreset) is { } m && m.ColorEnabled && m.Palette != 0 && !m.MultiColor;

    /// <summary>
    /// FUN_141421690 (@0x141421690, unnamed/unit_1414210f0.cpp:80): the item's customize kind, true for the avatar colour item
    /// (FUN_14053cf00) and an item whose first group type is 3 (FUN_14053d0c0), 1 (FUN_14053cfd0), 2 (FUN_14053d020) or 0x12
    /// (FUN_14053cf40); false without data. (BeautyCustomize's IsRealItem hook is named after this function but describes an
    /// owned-item test; this body is the customize kind.)
    /// </summary>
    public static bool IsCustomizeKind(ItemData d, ItemModelTable models)
    {
        if (d == null) return false;
        if (d.Id == AvatarColorItemId) return true;
        int first = d.ItemPreset != 0 ? models.Get(d.ItemPreset)?.FirstSlotType ?? 0 : 0;   // GetModelOrEmpty()->GetFirstGroupType()
        return first is 3 or 1 or 2 or 0x12;
    }

    /// <summary>
    /// CItem::FUN_140525e50 (@0x140525e50, commonfiles/item.cpp:584) = CItemData::FUN_14053cae0 (itemdata.cpp:644: a weapon
    /// whose subtype is not the asked category, a nonzero one, fails; then IsGenderAllowed) and CItemData::FUN_14053c9e0
    /// (itemdata.cpp:623: a weapon needs a model part worn by the gender and, with a weapon id on both, the asked one, ModelPart::
    /// IsWornBy @0x140544aa0, itemdatastruct.cpp:421; any other item IsGenderAllowed).
    /// </summary>
    public static bool Fits(ItemData d, int gender, int category, ItemModelTable models)
    {
        if (IsWeapon(d, models) && category != 0 && d.EquipSubtype != category) return false;
        if (!d.IsGenderAllowed(gender)) return false;
        if (!IsWeapon(d, models)) return true;
        foreach (var a in models.GetAssets(d.ItemPreset))
            if ((a.Gender == 2 || a.Gender == gender) && (a.Weapon == 0 || category == 0 || a.Weapon == category)) return true;
        return false;
    }

    /// <summary>
    /// CEquipment::FUN_1411edbb0 (@0x1411edbb0, equipment.cpp:2625-2678): the items that carry the "MissMatch" tag
    /// (FUN_1411ee980, :2201). Skipped for a ClonePC. The layer-1 right and left hand items (slots 5 and 4) are flagged both when either
    /// does not fit the layer-0 item's subtype and the wearer's gender (CItem::FUN_140525e50) or cannot be used with its base
    /// (PC::CanUseItem); the layer-2 pair the same, only with feature 0x8e (<paramref name="layer2Feature"/>). Run here as the
    /// client runs it after a change of the worn items: the tag is computed fresh, never kept.
    /// </summary>
    public HashSet<Item> MissMatched(int gender, bool layer2Feature, Func<Item, bool> canUse, ItemModelTable models)
    {
        var tagged = new HashSet<Item>();
        bool miss = false, miss2 = false;
        Item rh = At(5, 1), rhBase = At(5, 0), lh = At(4, 1), lhBase = At(4, 0);
        Item rh2 = layer2Feature ? At(5, 2) : null, lh2 = layer2Feature ? At(4, 2) : null;
        void Check(Item baseItem, Item layer1, Item layer2)
        {
            if (baseItem == null) return;
            int kind = baseItem.Data.EquipSubtype;
            bool usable = canUse(baseItem);
            if (layer1 != null && (!Fits(layer1.Data, gender, kind, models) || !usable || !canUse(layer1))) miss = true;
            if (layer2Feature && layer2 != null && (!Fits(layer2.Data, gender, kind, models) || !usable || !canUse(layer2))) miss2 = true;
        }
        Check(rhBase, rh, rh2);
        Check(lhBase, lh, lh2);
        if (miss) { if (rh != null) tagged.Add(rh); if (lh != null) tagged.Add(lh); }
        if (layer2Feature && miss2) { if (rh2 != null) tagged.Add(rh2); if (lh2 != null) tagged.Add(lh2); }
        return tagged;
    }

    /// <summary>
    /// CEquipment::FUN_1411e3000(items, maxLayer, 0, force) (equipment.cpp:1190): per slot, the layers from the top down, the worn
    /// items the avatar shows. A layer at or above <paramref name="maxLayer"/> is skipped; layer 2 only while
    /// <paramref name="layerActive"/> (IsLayerActive, :1627, also FUN_1411ef870 @0x1411ef870); an item the wearer cannot use
    /// (<paramref name="canUse"/>, PC::CanUseItem) or without data is skipped; one tagged MissMatch is skipped unless the wearer is
    /// a ClonePC (<paramref name="notFitting"/>). A subtype 0x16 item (FUN_14053d2b0) marks its layer shown; the next slot 9 in
    /// that layer ends the slot's layers, and slot 9 itself ends after a 0x16 item. A two-handed item (FUN_14053d460) in slot 4
    /// ends the slot without a row. The item is added; the slot's layers end after it when <paramref name="force"/> or the item is
    /// not a weapon (FUN_14053d2f0). Items of property.flag08 (layer 3) are never loaded, see <see cref="Layers"/>.
    /// The slots go in ascending number and an item sits once at the slot the packet gave it, which stands for m_bShown (the
    /// client puts an item in each slot of its model groups, shown in the first).
    /// STILL-OPEN: bCheck (FUN_1411ef780's model test, CEquipItem::FUN_1411dc670) is not ported, only the call with 0; the order the
    /// client's slot list holds is not recovered.
    /// </summary>
    public List<Item> Listed(int maxLayer, bool force, bool layerActive, bool notFitting, Func<Item, bool> canUse,
        HashSet<Item> missMatch, ItemDataTable items, ItemModelTable models)
    {
        var result = new List<Item>();
        var shown = new bool[8];                                  // Unk_LayerBits, shared by the slots
        foreach (int slot in layers.SelectMany(l => l.Keys).Distinct().OrderBy(s => s))
        {
            bool done = false;
            for (int i = layers.Length - 1; i >= 0; i--)
            {
                if (i >= maxLayer) continue;
                if (i == 2 && !layerActive) continue;
                if (At(slot, i) is not { } item || item.Data == null) continue;
                if (!canUse(item)) continue;
                var d = item.Data;
                if (notFitting && missMatch?.Contains(item) == true) continue;
                if (d.EquipSubtype == 0x16)                       // FUN_14053d2b0
                {
                    if (slot == 9) break;
                    shown[i] = true;
                }
                if (shown[i] && slot == 9) { done = true; break; }
                if (IsTwoHanded(d, models) && slot == 4) break;   // FUN_14053d460
                if (force || !IsWeapon(d, models)) done = true;   // FUN_14053d2f0
                result.Add(item);
                if (done) break;
            }
        }
        return result;
    }
}
