using System.Text;

namespace Ms2.Ui.Items;

public sealed partial class ItemTooltip
{
    /// <summary>
    /// ASTooltip::FUN_1409c7b10 (astooltip.cpp:267), "itemDamage": the repack line (FUN_1409d95f0), the enchant line
    /// (FUN_1409d8d90), the remake line (FUN_1409d9330), the merge line (FUN_1409d99a0) and the extraction line
    /// (FUN_1409d9aa0), joined by new lines; the member is set only when the text is not empty. All five are ported.
    /// </summary>
    public void SetItemDamage(Item item)
    {
        // skip the builder when no line can add text
        bool enchant = item.Data.IsEquipItem;
        bool remake = CanRemakeOption(item, 2);
        bool merge = item.Data.Fusionable != 0 && model.IsFeatureEnabled(0x6f);
        bool repack = item.Data.RePackingLimitCount > 0 && model.IsFeatureEnabled(0x38);
        uint? scrolls = Tables.Extraction.ScrollCount(item.Data.Id);
        if (!enchant && !merge && !repack && !remake && scrolls == null) return;
        var sb = new StringBuilder();
        if (repack) AppendRepack(item, sb);
        AppendEnchant(item, sb);
        if (remake) AppendRemake(item, sb);
        AppendMerge(item, sb);
        if (scrolls != null) AppendExtraction(item, scrolls.Value, sb);
        if (sb.Length != 0) Data["itemDamage"] = sb.ToString();
    }

    static void NewLine(StringBuilder sb) { if (sb.Length != 0) sb.Append('\n'); }

    /// <summary>
    /// FUN_1409d8d90 (astooltip.cpp:3402): equipment without a skin. An expiring, period or PC bang item, or one whose data
    /// sets exceptEnchant, shows the cannot-enchant line (0x94e). An item that CItemEnchantLimitTable::CanEnchant passes
    /// shows "Can Be Enchanted" (0x959; CItemEnchantInfo::FUN_14058c8a0, which picks 0x95a, always returns 0), the transform
    /// line and, with a fail stack, s_itemenchant_fail_stack_count; one it fails shows only the transform line.
    /// </summary>
    void AppendEnchant(Item item, StringBuilder sb)
    {
        // the exe returns on a null GetStatus()->GetEnchant(); every item has one (FUN_140523c40 makes the CItemStatus,
        // whose constructor makes the CItemEnchantInfo, itemsocketsystemutil.cpp:1603), a made item's at level 0
        var d = item.Data;
        if (!d.IsEquipItem) return;                                               // FUN_14053cbc0
        if (d.HasSkinType) return;                                                // CItem::HasSkin
        bool noEnchant = item.ExpiryTime > 0 || d.HasPeriod || d.PCBang;          // FUN_140525c60, HasPeriod, IsPCBangItem
        if (d.ExceptEnchant || noEnchant)
        {
            NewLine(sb);
            sb.Append(Tables.Common(0x94e));
            return;
        }
        if (!Tables.ItemOptions.CanEnchant(item))
        {
            AppendTransform(item, sb);
            return;
        }
        NewLine(sb);
        sb.Append(Tables.Common(0x959));
        AppendTransform(item, sb);
        int failStack = item.Record?.Enchant.FailStack ?? 0;                     // CItemEnchantInfo::GetFailStackCount
        if (failStack > 0)
        {
            NewLine(sb);
            sb.Append(CommonF("s_itemenchant_fail_stack_count", failStack));
        }
    }

    /// <summary>
    /// FUN_1409d95f0 (astooltip.cpp:3508), with feature 0x38: an item whose transfer type is 3 or 5 (FUN_14053d800), that is
    /// not bound (FUN_140634230: a transfer binding, or a socketed gem bound to a character) and whose enchant history is
    /// enabled, with a repack limit, shows s_item_repacking_disable when no repack is left (GetRemainingRePackingCount: the
    /// limit less the transfer max, at least 0), else s_item_repacking_limit_count with the count left and the item's
    /// rePackingItemConsumeCount. A pet uses s_item_repacking_pet_limit_count and needs feature 0x101; without it the exe
    /// returns after the new line. FUN_140527030 (CItemEnchantInfo::FUN_14058c8a0) is always 0.
    /// </summary>
    void AppendRepack(Item item, StringBuilder sb)
    {
        var d = item.Data;
        if (d.TransferType != 3 && d.TransferType != 5) return;
        var r = item.Record;
        if (r != null && (r.Transfer.HasBinding || r.Socket.Gems.Any(g => g != null && g.GemId != 0 && ItemTradeRules.GemBound(g))))
            return;
        if (r != null && !r.Enchant.HistoryEnabled) return;                       // CItemEnchantInfo::IsEnabled
        if (d.RePackingLimitCount <= 0) return;
        NewLine(sb);
        int left = Math.Max(0, d.RePackingLimitCount - (r?.Transfer.Max ?? 0));    // a made item's transfer max is 0
        if (left == 0)
        {
            sb.Append(Common("s_item_repacking_disable"));
            return;
        }
        string key = "s_item_repacking_limit_count";
        if (d.IsPetItem)
        {
            if (!model.IsFeatureEnabled(0x101)) return;
            key = "s_item_repacking_pet_limit_count";
        }
        sb.Append(CommonF(key, left, d.RePackingItemConsumeCount));
    }

    /// <summary>
    /// CItem::CanRemakeOption (item.cpp:1831): FUN_140526670 for a pet (rarity 3 or more), else FUN_1405264c0 (rarity 4 or
    /// more, an equip subtype FUN_14053d600 or FUN_14053d5c0 passes, and levelLimit at least CFeatureManager::FUN_14036dc40,
    /// which returns 0x32); both need remakeDisable off and options in the slot's group (CItemOption::GetGroupOptionCount:
    /// its stat and special options). A made item's option is empty, so it never passes.
    /// </summary>
    internal static bool CanRemakeOption(Item item, int slot)
    {
        var d = item.Data;
        if (d.IsPetItem)
        {
            if (item.Rarity < 3) return false;
        }
        else
        {
            if (item.Rarity < 4) return false;
            if (d.EquipSubtype is not (0xc or (>= 0x12 and <= 0x15) or (>= 0xd and <= 0x11) or 0x16)) return false;
            if ((ushort)d.LevelLimit < 0x32) return false;
        }
        if (d.RemakeDisable) return false;
        var o = item.Record?.Options;
        return o != null && slot < o.Stat.Length && o.Stat[slot].Count + o.Special[slot].Count != 0;
    }

    /// <summary>FUN_1409d9330 (astooltip.cpp:3472): with flag 0x400 or 0x1000 the remake-enabled line (pet
    /// s_tooltip_petremake_enable, else 0xbfa), otherwise the remake count (CItem m_9c; pet s_tooltip_petremake_count,
    /// else BlockFormat(0xbf9)).</summary>
    void AppendRemake(Item item, StringBuilder sb)
    {
        NewLine(sb);
        bool pet = item.Data.IsPetItem;
        int count = item.Record?.RemakeCount ?? 0;
        if ((Flags & 0x400) != 0 || (Flags & 0x1000) != 0)
            sb.Append(pet ? Common("s_tooltip_petremake_enable") : Tables.Common(0xbfa));
        else
            sb.Append(pet ? CommonF("s_tooltip_petremake_count", count) : Format(Tables.Common(0xbf9), count));
    }

    /// <summary>FUN_1409d9aa0 (astooltip.cpp:3592): an item with an itemExtraction row shows s_itemextraction_trycount (the
    /// item's extraction count, the row's ScrollCount) while the count is above 0, else s_itemextraction_invalid. The count
    /// is CItem m_d8 (GetExtractionCount): the record's i16, or for a made item whether the table has its row
    /// (FUN_140523c40, item.cpp:2790).</summary>
    void AppendExtraction(Item item, uint scrollCount, StringBuilder sb)
    {
        // GlamorForges is the record's i16 that CItem m_d8 holds (GetExtractionCount); a made item here has the row, so 1
        uint tries = item.Record != null ? item.Record.GlamorForges : 1u;
        NewLine(sb);
        sb.Append(tries > 0 ? CommonF("s_itemextraction_trycount", tries, scrollCount) : Common("s_itemextraction_invalid"));
    }

    /// <summary>FUN_1409d8d90's transform line, in both branches: with feature 0xa6, an item with transform rows
    /// (CItemEnchantTransformTable::GetLowestLimitLevel != 0) shows s_itemenchant_transform_valid_item with that level after
    /// a new line, which the exe appends even to empty text.</summary>
    void AppendTransform(Item item, StringBuilder sb)
    {
        if (!model.IsFeatureEnabled(0xa6)) return;
        uint level = Tables.EnchantTransform.LowestLimitLevel(item.Data.Id);
        if (level == 0) return;
        sb.Append('\n');
        sb.Append(CommonF("s_itemenchant_transform_valid_item", level));
    }

    /// <summary>FUN_1409d99a0 (astooltip.cpp:3577): with feature 0x6f, an item whose data sets fusion fusionable shows
    /// s_item_merge_valid_item.</summary>
    void AppendMerge(Item item, StringBuilder sb)
    {
        if (!model.IsFeatureEnabled(0x6f)) return;
        if (item.Data.Fusionable == 0) return;
        NewLine(sb);
        sb.Append(Common("s_item_merge_valid_item"));
    }

    /// <summary>
    /// FUN_1409d3010 (astooltip.cpp:2533), "customizeColorData": with feature 0xb7 and the local player, an item whose model
    /// has a palette (FUN_14053e260) shows its dye colour: { color, desc }. With flag 4 or 0x8000 a model without a default
    /// colour returns first. The colour is the item's slot 0 palette and colour SN (CItemExtraData GetSlotPaletteId /
    /// GetSlotColorId, the item's EquipColor PaletteId and Index), looked up by SN (GetPaletteColorBySN); a colour without
    /// a stringKey returns. desc is its stringColorPalette name (string table 0x48).
    /// </summary>
    public void SetCustomizeColorData(Item item)
    {
        if (!model.IsFeatureEnabled(0xb7)) return;
        if (!model.HasLocalPlayer) return;                                           // FUN_1409c6540
        var d = item.Data;
        var gm = Tables.ItemModels.ForItem(d);
        if (gm == null || gm.Palette == 0) return;
        bool fixedColor = (Flags & 4) != 0 || (Flags & 0x8000) != 0;
        if (fixedColor && gm.DefaultColor(0) < 0) return;
        if (item.Record == null) return;                                             // no CItemExtraData
        var ec = item.Record.Color;
        if (ec.PaletteId == 0 || ec.Index < 0) return;
        var c = Tables.ColorPalettes.BySN(ec.PaletteId, ec.Index);
        if (c == null || c.StringKey == 0) return;
        Data["customizeColorData"] = new Dictionary<string, object>
        {
            ["color"] = ColorObject(c, d),
            ["desc"] = Tables.ColorPaletteName(c.StringKey),
        };
    }

    /// <summary>
    /// FUN_140f33250 / FUN_140f33400 (unit_140f32290_charactercustomizepayloadwriters.cpp:267), bAlt false: c1 is ch0 and
    /// c2 is ch1 for the avatar colour item (CItemData::IsAvatarColorItem, id 10100001) else ch2. realColor c1; visibleColor
    /// the palette colour, or with show2color c1 plus realColor2 and visibleColor2 c2; ch2Color c2, colorSN and paletteID.
    /// The colours are uints (ASObj::FUN_1409c4990 takes an unsigned long), colorSN an int (SetIntMember) and paletteID a
    /// uint (SetMemberUInt).
    /// </summary>
    public static Dictionary<string, object> ColorObject(ColorPaletteTable.Color c, ItemData d)
    {
        uint c1 = c.Ch0, c2 = c.Channel(d.Id == 0x9a1d21 ? 1 : 2);
        var o = new Dictionary<string, object> { ["realColor"] = c1 };
        if (!c.Show2Color) o["visibleColor"] = c.Visible;
        else
        {
            o["visibleColor"] = c1;
            o["realColor2"] = c2;
            o["visibleColor2"] = c2;
        }
        o["ch2Color"] = c2;
        o["colorSN"] = c.ColorSN;
        o["paletteID"] = (uint)c.PaletteId;
        return o;
    }
}
