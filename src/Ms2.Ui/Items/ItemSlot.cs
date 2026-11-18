namespace Ms2.Ui.Items;

/// <summary>
/// ASSlot's item path (ms2/client/ms2/asitemslot.cpp): the members ASSlot::FUN_1409c3860 (:1262) sets on a slot object
/// for an item, in its order, as (name, value) pairs for <see cref="UiHost.Obj"/>. Flags 1 (the item) and 0x10 (the count)
/// are ported, the ones ItemInfoSlot's item slots use (ItemSlotBinder: 0x11); any other flag throws until its branch is.
/// </summary>
public static class ItemSlot
{
    /// <summary>The flags <see cref="Build"/> handles.</summary>
    public const int Ported = 0x1 | 0x10;
    /// <summary>The feature FUN_1409c3860 tests for pet ranks (CFeatureManager::IsFeatureEnabled(0x8c)).</summary>
    public const int PetRankFeature = 0x8c;

    /// <summary>The look-ups of tables not ported yet; null leaves the member out.</summary>
    public sealed class Sources
    {
        /// <summary>FUN_140626f20 (itemsocketsystemutil.cpp:205): a gemstone's level from the gemstone upgrade table.</summary>
        public Func<int, int> GemstoneLevel;
        /// <summary>CItemLapenshardTable::GetLevel (itemlapensharddata.cpp:356).</summary>
        public Func<int, int> LapenshardLevel;
        /// <summary>FUN_1409c2af0 (:1032): the item skill's (remaining, total) cooldown for the local player (JobSystem /
        /// SkillSystem), null when it shows none.</summary>
        public Func<Item, (float Remain, float Total)?> SkillCooldown;
    }

    /// <summary>
    /// FUN_1409c3860 (:1262) for <paramref name="item"/>; <paramref name="hasPlayer"/> is a local player being there
    /// (WorldService::GetLocalPlayer, which ItemInfoSlot::vfunc_8 passes). Returns false (the client's 0) without an item.
    /// </summary>
    public static bool Build(List<(string, object)> slot, Item item, bool hasPlayer, int flags, UiModel model, Sources sources = null)
    {
        if (item?.Data == null) return false;
        if ((flags & ~Ported) != 0) throw new NotImplementedException($"ASSlot::FUN_1409c3860 flags 0x{flags & ~Ported:x}");
        var data = item.Data;
        if ((flags & 1) != 0)
        {
            slot.Add(("itemID", (uint)data.Id));                                   // SetItemID: SetMemberUInt
            slot.Add(("itemGrade", item.Rarity));                                  // SetItemGrade: FUN_1409adc90 (int)
            slot.Add(("index", item.Slot));                                        // SetIndex
            if (model.IsFeatureEnabled(PetRankFeature))
            {
                if (data.IsPetItem)
                {
                    slot.Add(("itemPetRank", item.Rarity));                        // SetItemPetRankAndDisableShadow (:897)
                    slot.Add(("dropShadowDisable", 1));                            // DisableDropShadow
                }
            }
            else slot.Add(("itemPet", data.IsPetItem ? 1 : 0));
            slot.Add(("itemPetSkin", data.GemSubtype == 11 ? 1 : 0));
            slot.Add(("itemCash", data.IsCashItem ? 1 : 0));
            slot.Add(("itemSkin", data.HasSkinType ? 1 : 0));
            slot.Add(("itemPcBang", data.PCBang ? 1 : 0));                         // SetItemPcBang(bool): SetIntMember(b != false)
            slot.Add(("itemVIP", data.Vip ? 1 : 0));
            slot.Add(("itemWedding", data.Wedding ? 1 : 0));
            slot.Add(("iconPath", item.IconPath));                                 // SetIconPathString: SetWStringMember
            if (item.UgcTag != 0) slot.Add(("ugctag", item.UgcTag));               // SetUgcTag (:1000): not for 0
            if (hasPlayer) SkillCooldown(slot, item, model, sources);
            // SetItemEnchant (:1081): CItemEnchantInfo::FUN_14058c8a0 is always false (itemenchantinfo.cpp:138), so no
            // "enchantBroken"; a level above 0 is "enchant"
            if (item.EnchantLevel > 0) slot.Add(("enchant", item.EnchantLevel));
            if (item.UnlimitedEnchantLevel > 0) slot.Add(("unlimited", item.UnlimitedEnchantLevel));   // SetItemUnlimitedEnchant (:1101)
            IconLock(slot, item, model);
            slot.Add(("itemPiece", data.IsPieceItem ? 1 : 0));
            // SetGemstoneLevel (:1112) / SetLapenshardLevel (:1123): FUN_1409c48f0("enchant", ushort level), not for 0
            if (data.IsGemstone && sources?.GemstoneLevel?.Invoke(data.Id) is int gem && (ushort)gem != 0) slot.Add(("enchant", (ushort)gem));
            if (data.IsLapenshard && sources?.LapenshardLevel?.Invoke(data.Id) is int lapen && (ushort)lapen != 0) slot.Add(("enchant", (ushort)lapen));
            Season(slot, data, model.Tables.GachaBoxes);
        }
        // SetItemCountFromItem (:873): "{0}" of the count for a stackable item, else ""
        if ((flags & 0x10) != 0) slot.Add(("itemCount", data.IsStackable ? item.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : ""));
        return true;
    }

    /// <summary>FUN_1409c2af0 (:1032): only for an item with a skill (not a lapenshard) and a cooldown.</summary>
    static void SkillCooldown(List<(string, object)> slot, Item item, UiModel model, Sources sources)
    {
        if (item.Data.IsLapenshard || item.Data.SkillId == 0) return;
        if (sources?.SkillCooldown?.Invoke(item) is not { } cd) return;
        slot.Add(("remainTime", (double)cd.Remain));                              // SetRemainTime: SetMemberFloat
        slot.Add(("totalTime", (double)cd.Total));
        // SetCooldownStrings (:1072): GetStringCommon 0x5cb/0x5ca/0x5cc/0x5ce
        slot.Add(("cooldownSecStr", model.Tables.Common("s_word_sec") ?? ""));
        slot.Add(("cooldownMinStr", model.Tables.Common("s_word_min") ?? ""));
        slot.Add(("cooldownHourStr", model.Tables.Common("s_word_hour") ?? ""));
        slot.Add(("cooldownDayStr", model.Tables.Common("s_word_day") ?? ""));
    }

    /// <summary>FUN_1409c1f40 (:812): unlocked with a pending unlock time shows the unlock icon; lock state 1 the lock.</summary>
    static void IconLock(List<(string, object)> slot, Item item, UiModel model)
    {
        if (item.LockState == 0 && item.UnlockTime > new DateTimeOffset(model.Now).ToUnixTimeSeconds())
        {
            slot.Add(("itemIconLock", false));
            slot.Add(("itemIconUnLock", true));
            return;
        }
        slot.Add(("itemIconLock", item.LockState == 1));
        slot.Add(("itemIconUnLock", false));
    }

    /// <summary>SetItemSeason (:1145): an OpenGachaBox item's box, or the box a coin item buys; its season number when
    /// the box shows it on the icon.</summary>
    static void Season(List<(string, object)> slot, ItemData data, GachaBoxTable table)
    {
        int season = data.OpenGachaBoxParam0;
        if (table.IsCoinItem(data.Id)) season = table.RandomBoxIdByCoinItem(data.Id);
        if (season == 0 || !table.IsSeasonNumberOnIcon(season)) return;
        slot.Add(("itemSeason", table.SeasonText(season)));
    }
}
