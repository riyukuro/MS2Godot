using System.Text;
using Ms2.Quest;

namespace Ms2.Ui.Items;

public sealed partial class ItemTooltip
{
    /// <summary>The tooltip owner's gender (PC::GetGender) when the owner is a player whose equipment is
    /// <see cref="OwnerEquipment"/>; 2 (any) otherwise.</summary>
    public int OwnerGender = 2;

    /// <summary>DAT_142dbac18: the model groups (HR, FA, FD, ER) that make a set piece gender-bound.</summary>
    static readonly int[] ClassGroups = { 1, 2, 3, 18 };

    /// <summary>
    /// FUN_1409c8020 (astooltip.cpp:920), "itemStatusSet": per set that lists the item (GetOriginOrItemId) and shows a
    /// tooltip, one text: the s_tooltip_itemoption_setitem_name title (set name, worn pieces up to the set's piece count,
    /// piece count), a s_tooltip_itemoption_setitem_name_row per piece (the worn colour when the owner wears a valid item of
    /// that id, else grey), then per piece count from 0 to the set's piece count with a part, its
    /// s_tooltip_itemoption_setitem_set_title and s_tooltip_itemoption_setitem_stat_row rows (stats, specials, then the
    /// non-empty buff names), green up to the worn count; a part without rows is left out. A set whose gender-bound pieces
    /// (a class model group, or a gender limit) do not fit the gender (the item's own limit when it has no class group and
    /// one gender, else the owner's) is left out. The owner is the tooltip's player (m_40 as PC; the local player when the
    /// item has no object id): <see cref="OwnerEquipment"/> and <see cref="OwnerGender"/>, else the model's
    /// <see cref="UiModel.LocalPlayer"/> (the port keeps no item object id; <see cref="Owner"/> is only the binding owner's
    /// name, so it does not stop the fallback).
    /// An item no set lists goes to the housing set block (<see cref="SetHousingSetItem"/>). STILL-OPEN: another player's
    /// item (FindPCById of its object id).
    /// </summary>
    void SetItemStatusSet(Item item)
    {
        var sets = Tables.SetItems;
        int origin = item.Data.OriginOrItemId;
        if (!sets.HasSetItem(origin))
        {
            SetHousingSetItem(item.ItemId);
            return;
        }
        int itemGender = 2;
        if (!HasClassGroup(item.Data)) itemGender = item.Data.GenderLimit;     // CItem::HasAnyEquipSlotOf
        if (itemGender == 3) itemGender = 2;
        var equipment = OwnerEquipment;
        int ownerGender = OwnerGender;
        if (equipment == null && model.LocalPlayer?.Invoke() is { } me)   // no object id (astooltip.cpp:961-963): the local player
        {
            equipment = WornEquipment.Of(me, Tables.Items);
            ownerGender = me.Gender;
        }
        int pcGender = ownerGender == 3 ? 2 : ownerGender;
        int gender = itemGender != 2 ? itemGender : pcGender;
        var worn = equipment?.Shown().ToList();                           // CEquipment::FUN_1411e1fc0(list, 2)
        var arr = new List<object>();
        foreach (var s in sets.SetsOf(origin))
        {
            if (s.DisableTooltip) continue;
            if (!FitsGender(s, gender)) continue;
            var pieces = new StringBuilder();
            int equipped = 0;
            foreach (int id in s.ItemIds)
            {
                if (Tables.Items.Get(id) == null) continue;
                uint color = 0x505050;
                if (worn != null && worn.Any(w => PieceValid(w) && w.Data.OriginOrItemId == id))   // Unk_SetPieceEquipped
                {
                    equipped++;
                    color = 0xffffff;
                }
                pieces.Append(CommonF("s_tooltip_itemoption_setitem_name_row", color, Tables.ItemName(id)));
            }
            int count = s.ItemIds.Count;
            equipped = Math.Min(equipped, count);
            var text = new StringBuilder(CommonF("s_tooltip_itemoption_setitem_name", Tables.SetItemName(s.Id), equipped, count));
            text.Append(pieces);
            for (int n = 0; n <= count; n++)
            {
                if (!s.Parts.TryGetValue(n, out var part)) continue;
                uint color = n <= equipped ? 0xd205u : 0x505050u;
                var options = new StringBuilder();
                foreach (var (k, v) in part.Stats) options.Append(CommonF("s_tooltip_itemoption_setitem_stat_row", color, StatValue(k, v)));
                foreach (var (k, v) in part.Specials) options.Append(CommonF("s_tooltip_itemoption_setitem_stat_row", color, SpecialValue(k, v)));
                foreach (var (id, level) in part.Effects)
                {
                    if (id == 0) continue;
                    string name = Tables.AdditionalDescription((uint)id, level, "name");          // GetBuffName
                    if (!string.IsNullOrEmpty(name)) options.Append(CommonF("s_tooltip_itemoption_setitem_stat_row", color, name));
                }
                if (options.Length == 0) continue;
                text.Append(CommonF("s_tooltip_itemoption_setitem_set_title", color, n));
                text.Append(options);
            }
            if (text.Length != 0) arr.Add(text.ToString());
        }
        Data["itemStatusSet"] = arr;
    }

    /// <summary>
    /// FUN_1409c8df0 (astooltip.cpp:1071), "itemStatusSet" for an install item: the item's housing row
    /// (CItemDataTable::GetHousing) names a set (setItemID) that CUGCMapSetItemTable has, then two texts: the
    /// s_tooltip_itemoption_ugcmap_setitem_name title with the set's name (GetUGCMapSetItemName(id, "name")), and a
    /// s_tooltip_itemoption_ugcmap_setitem_row per piece in piece id order, white, with the piece's item name.
    /// </summary>
    void SetHousingSetItem(int itemId)
    {
        var row = Tables.Housing?.Get(itemId);
        if (row == null || row.SetItemId == 0) return;
        var set = Tables.UgcMapSetItems.Get(row.SetItemId);
        if (set == null) return;
        var body = new StringBuilder();
        foreach (uint id in set.Pieces.Keys)
            body.Append(CommonF("s_tooltip_itemoption_ugcmap_setitem_row", 0xffffffu, Tables.ItemName((int)id)));
        Data["itemStatusSet"] = new List<object>
        {
            CommonF("s_tooltip_itemoption_ugcmap_setitem_name", Tables.UgcMapSetItemName((int)row.SetItemId)), body.ToString(),
        };
    }

    /// <summary>CItem::FUN_140528980 (item.cpp:1717) for a worn item: not expired (CItem::IsExpired, item.cpp:1303), unless a
    /// pet or a maid item; its score-item checks never apply to a worn item.</summary>
    bool PieceValid(Item w)
    {
        bool expired = w.ExpiryTime > 0 && w.ExpiryTime <= new DateTimeOffset(model.Now).ToUnixTimeSeconds();
        return !expired || w.Data.IsPetItem || w.Data.HasMaidId;
    }

    bool HasClassGroup(ItemData d) => Tables.ItemModels.Get(d.ItemPreset) is { } m && ClassGroups.Any(m.HasSlotType);

    /// <summary>The piece check: a piece with no class group and any gender is free; any other must have the gender.</summary>
    bool FitsGender(SetItemTable.Set s, int gender)
    {
        foreach (int id in s.ItemIds)
        {
            var d = Tables.Items.Get(id);
            if (d == null) continue;
            if (!HasClassGroup(d) && d.GenderLimit == 2) continue;
            if (gender != 2 && gender != d.GenderLimit) return false;
        }
        return true;
    }
}
