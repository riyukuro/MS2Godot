namespace Ms2.Ui.Items;

/// <summary>
/// ASSlot's item path (ms2/client/ms2/asitemslot.cpp): the members ASSlot::FUN_1409c3860 (:1262) sets on a slot object
/// for an item, in its order, as (name, value) pairs for <see cref="UiHost.Obj"/>. Ported: 1 (the item) and 0x10 (the count),
/// which ItemInfoSlot's item slots use (ItemSlotBinder: 0x11), and the rest of the inventory's 0x1c01f (InvenSlot::vfunc_8,
/// invenslot.cpp:1601): 2, 4, 8, 0x2000, 0x4000, 0x8000, 0x10000, 0x20000, 0x40000; 0x400 (the npc shop's rows, 0x415); 0x800 (the item name: the trade slots' 0x1c81f); and 0x100 (the uid: the item preview's 0xd01). Any other flag throws until its branch is. The branches that read
/// state not ported here take it from <see cref="Sources"/>; a null source sets nothing.
/// </summary>
public static class ItemSlot
{
    /// <summary>The flags <see cref="Build"/> handles.</summary>
    /// <remarks>0x40 is not tested by FUN_1409c3860 (asitemslot.cpp:1262-1384); EquipSlot / GemSlot pass it (0x49).</remarks>
    public const int Ported = 0x1 | 0x2 | 0x4 | 0x8 | 0x10 | 0x40 | 0x100 | 0x400 | 0x800 | 0x2000 | 0x4000 | 0x8000 | 0x10000 | 0x20000 | 0x40000;
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
        /// <summary>GetItemUsableState (unit_141274d10_uihelpers.cpp:480) for the local player: 1 lock, 2 disable, 3 condition
        /// lock, else 0. Its Unk_Inventory::GetItemUseError (inventory.cpp:1578) reads the player's gender, job, level, PC-bang
        /// and VIP state, the wedding and learned emotions.</summary>
        public Func<Item, int> UsableState;
        /// <summary>CQuestManager::FUN_1416ce6a0 (questmanager_2.cpp:1280): 0 a quest can begin with the item, 1 one in
        /// progress uses it, else neither.</summary>
        public Func<Item, int> QuestMark;
        /// <summary>The flag-8 colour: for an item whose model head has the customize colour (ItemModelHead flag10,
        /// itemdatastruct.cpp:935, itemmodel/*.xml), CItemExtraData::GetCurrentRgbComponent(0); null = none.</summary>
        public Func<Item, uint?> Color;
        /// <summary>FUN_1409c34e0 (:1197): in the inventory's break / lock / extraction modes (UIInventoryDialog::GetMode
        /// 1-5) an item the mode cannot take is sealed; mode 0 sets nothing.</summary>
        public Func<Item, bool> Sealed;
        /// <summary>CPetManager::GetSummonUid: the summoned pet's item uid (0 = none).</summary>
        public Func<long> SummonUid;
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
        if ((flags & 0x400) != 0) slot.Add(("disableDrag", 1));                     // DisableDrag (:946)
        if ((flags & 0x800) != 0) slot.Add(("itemName", new ItemTooltip(model, 0).Name(item)));   // SetItemName(CItem::FUN_140527b10) (:1305)
        // (0x20 SetItemEquip: not ported, it throws above)
        if ((flags & 0x10000) != 0 && data.IsPetItem && sources?.SummonUid is { } summon && item.Uid == summon())
            slot.Add(("itemSummon", 1));                                           // SetItemSummon
        if ((flags & 2) != 0)
        {
            if (item.IsNew) slot.Add(("itemNew", 1));                              // SetItemNew
            else if (item.IsEvent) slot.Add(("itemEvent", 1));                     // SetItemEvent
        }
        if ((flags & 4) != 0)
        {
            slot.Add(("level", (uint)data.LevelLimit));                            // GetRequiredLevel (item.cpp:1251): limit.levelLimit
            // the usable state for a MyPC and an item whose type is not 0x13 (CItemData::FUN_14053c8b0, itemdata.cpp:594)
            if (hasPlayer && data.Type != 0x13 && sources?.UsableState is { } usable)
                switch (usable(item))
                {
                    case 1: slot.Add(("itemLock", 1)); break;                      // SetItemLock
                    case 2: slot.Add(("disable", 1)); break;                       // SetDisable
                    case 3: slot.Add(("itemConditionLock", 1)); break;             // SetItemConditionLock
                }
        }
        if ((flags & 8) != 0 && sources?.Color?.Invoke(item) is uint color) slot.Add(("itemColor", color));   // SetItemColor
        // SetItemCountFromItem (:873): "{0}" of the count for a stackable item, else ""
        if ((flags & 0x10) != 0) slot.Add(("itemCount", data.IsStackable ? item.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : ""));
        // (0x80 SetSlotHintString: not ported, it throws above)
        if ((flags & 0x100) != 0) slot.Add(("itemGUID", MailTexts.IdText(item.Uid)));   // SetItemGUID(FUN_141b71910(uid)) (:1361)
        if ((flags & 0x2000) != 0) slot.Add(("dropShadowDisable", 1));              // DisableDropShadow (:1372)
        if ((flags & 0x4000) != 0 && sources?.QuestMark is { } quest)              // SetItemQuestMark (:1175)
            switch (quest(item))
            {
                case 0: slot.Add(("questBeginAble", 1)); break;
                case 1: slot.Add(("questProgress", 1)); break;
            }
        if ((flags & 0x8000) != 0 && sources?.Sealed?.Invoke(item) == true) slot.Add(("itemSeal", 1));   // FUN_1409c34e0
        if ((flags & 0x20000) != 0) slot.Add(("enchant", 0));                       // ClearEnchant (:1378)
        if ((flags & 0x40000) != 0) slot.Add(("gradeBG", 0));                        // ClearGradeBG (:1380)
        return true;
    }

    /// <summary>
    /// ASSlot::SetItemById (@0x1409c4510, asitemslot.cpp:1431): the members of a slot for an item id at a rank with no item behind it
    /// (a reward, a gift): the id and grade, the pet rank (feature 0x8c) or pet flag, the cash / skin / PC bang / VIP / wedding flags,
    /// the icon path, the name (FUN_141811980(1, id)), the count (SetItemCountValue @0x1409c2190: only above 0, as text), the drag
    /// disabled, the gemstone and lapenshard level, the season and the piece flag. False (the client's 0) when the id has no data.
    /// </summary>
    public static bool BuildById(List<(string, object)> slot, int itemId, int rank, uint count, UiModel model, Sources sources = null)
    {
        var data = model.Tables.Items.Get(itemId);
        if (data == null) return false;
        slot.Add(("itemID", (uint)itemId));                                        // SetItemID
        slot.Add(("itemGrade", rank));                                             // SetItemGrade
        if (model.IsFeatureEnabled(PetRankFeature))
        {
            if (data.IsPetItem)
            {
                slot.Add(("itemPetRank", rank));                                   // SetItemPetRankAndDisableShadow (:897)
                slot.Add(("dropShadowDisable", 1));
            }
        }
        else slot.Add(("itemPet", data.IsPetItem ? 1 : 0));                        // SetItemPet
        slot.Add(("itemCash", data.IsCashItem ? 1 : 0));
        slot.Add(("itemSkin", data.HasSkinType ? 1 : 0));
        slot.Add(("itemPcBang", data.PCBang ? 1 : 0));
        slot.Add(("itemVIP", data.Vip ? 1 : 0));
        slot.Add(("itemWedding", data.Wedding ? 1 : 0));
        slot.Add(("iconPath", data.IconPath));                                     // SetIconPathString
        slot.Add(("itemName", model.Tables.ItemName(itemId)));                     // SetItemName(FUN_141811980(1, id))
        if (count > 0) slot.Add(("itemCount", count.ToString(System.Globalization.CultureInfo.InvariantCulture)));   // SetItemCountValue
        slot.Add(("disableDrag", 1));                                              // DisableDrag
        if (data.IsGemstone && sources?.GemstoneLevel?.Invoke(itemId) is int gem && (ushort)gem != 0) slot.Add(("enchant", (ushort)gem));
        if (data.IsLapenshard && sources?.LapenshardLevel?.Invoke(itemId) is int lapen && (ushort)lapen != 0) slot.Add(("enchant", (ushort)lapen));
        Season(slot, data, model.Tables.GachaBoxes);                               // SetItemSeason
        slot.Add(("itemPiece", data.IsPieceItem ? 1 : 0));                         // SetItemPiece
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
