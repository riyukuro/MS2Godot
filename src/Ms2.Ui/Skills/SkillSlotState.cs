using Ms2.Net.Packets;
using Ms2.Ui.Items;

namespace Ms2.Ui.Skills;

/// <summary>
/// The local player state a quick slot and the skill system's cast check read. Each part is what the client asks the PC, its
/// buffs (Unk_SsBuffs), the skill system (CSkillSystem) or the field for. A part Godot does not model yet keeps the value the
/// client has with nothing going on: no buff, no cooldown, not riding, not dead.
/// </summary>
public sealed class SlotWorld
{
    public SkillTables Tables = SkillTables.Empty;
    public Func<int, ItemData> ItemData = _ => null;
    public JobSystem Jobs;
    /// <summary>WorldService::GetLocalPlayer is set.</summary>
    public bool HasPc = true;
    /// <summary>vfunc_78: the level.</summary>
    public int Level;
    /// <summary>FUN_141670a00(4 / 7 / 10): the current hp, sp, ep.</summary>
    public long Hp, Sp, Ep;
    /// <summary>DAT_143ab2898: the meso.</summary>
    public long Meso;
    public bool IsDead, IsRiding, IsBattleRiding;

    /// <summary>Unk_Inventory (g_143ab2a90): the bag items.</summary>
    public IEnumerable<EquipItem> Inventory = Array.Empty<EquipItem>();
    /// <summary>CItemData::FUN_14053fd90: the bag tab of an item id (outside 0-14 = no tab).</summary>
    public Func<int, int> TabOf = _ => -1;
    /// <summary>PC::CanUseItem (pc.cpp:4683). Default: <see cref="CanUseItemDefault"/>.</summary>
    public Func<EquipItem, bool> CanUseItem;
    public bool InPcBang, IsVip, HasWedding;
    /// <summary>DAT_143ae9750: while set, a skill slot finds no item and counts none.</summary>
    public bool SkillItemsOff;

    /// <summary>CSkillSystem::GetSkillSet is set.</summary>
    public bool SkillSetActive;
    /// <summary>CSkillSystem::FUN_141765fe0(id, mode): the cooldown left in seconds.</summary>
    public Func<uint, int, float> CooldownRemain = (_, _) => 0f;
    /// <summary>CSkillSystem::FUN_141766230(id, ..., 1): the total and whether it comes from an item.</summary>
    public Func<uint, (float Total, bool ByItem)> CooldownTotal = _ => (0f, false);
    /// <summary>CSkillSystem::GetRechargeRemainCount.</summary>
    public Func<SkillData, int> RechargeRemain = _ => 0;
    /// <summary>CSkillSystem::FUN_141764be0: the active cast's output skill for the id (no cast: the id).</summary>
    public Func<uint, uint> OutputSkill = id => id;
    /// <summary>CSkillSystem::FUN_14176ba90: the level of an emotion the player has, null without it.</summary>
    public Func<uint, ushort?> EmotionLevel = _ => null;
    /// <summary>FUN_14176b960 with no sp rate: the sp cost of a skill.</summary>
    public Func<uint, long, long> SpCost = (_, sp) => sp;

    /// <summary>Unk_SsBuffs::FUN_140970020(id, level, overlap, 1): an effect at that level and overlap is on.</summary>
    public Func<uint, ushort, uint, bool> HasEffect = (_, _, _) => false;
    /// <summary>HasEnabledWithId / HasEnabledInGroup.</summary>
    public Func<uint, bool> HasEnabledEffect = _ => false;
    public Func<int, bool> HasEnabledEffectGroup = _ => false;
    /// <summary>Unk_SsBuffs::FUN_140970bf0: the buffs' skill use disable mask (by main type).</summary>
    public uint BuffSkillMask;
    public Func<uint, bool> SkillUseDisabled = _ => false;
    /// <summary>Unk_SsBuffs::IsItemSlotDisabled(5 rh / 4 lh).</summary>
    public Func<int, bool> ItemSlotDisabled = _ => false;

    /// <summary>The current field (WorldService::GetFieldData); null when there is none.</summary>
    public SlotField Field;
    /// <summary>WorldService::FUN_141bdb660(n): CubeMapInformationService::FUN_1410c3cd0(n).</summary>
    public Func<int, bool> CubeState = _ => false;
    /// <summary>WorldService::FUN_141bcb210: riding is allowed in the field.</summary>
    public bool RideAllowed;
    /// <summary>CFieldData::FUN_1403744a0: the field of the id has map type 9.</summary>
    public Func<int, bool> IsMapType9 = _ => false;
    /// <summary>PC::GetWeaponAction (pc.cpp:2573): the hand kinds of the action <see cref="WeaponActionTable.HeldAction"/> picks
    /// (PC::UpdateWeaponAction); null when the table has no row for the hands.</summary>
    public (int Right, int Left)? Weapon;
    /// <summary>MyPC::GetLapenshardSkillLevel.</summary>
    public Func<uint, ushort> LapenshardLevel = _ => 0;
    /// <summary>WorldService::FindPetByKind finds the player's pet near them.</summary>
    public bool PetNearby;
    /// <summary>FUN_14165f150 and the held item's FUN_14053dc90: the skill of the held item, 0 without one.</summary>
    public int HeldItemSkill;
    /// <summary>DAT_143ab287c.</summary>
    public int GameMode;

    /// <summary>
    /// PC::CanUseItem (pc.cpp:4683) over what an inventory record carries: an expired item that is not a pet (FUN_140528980 :1717),
    /// a PC bang item outside a PC bang, a VIP item without VIP, a wedding item without a wedding. The client skips the last
    /// three for an item with flag 1 (CItem::HasFlags(1), item.cpp:1003), which only MarkPreviewItem (customize.cpp:210) sets on
    /// a preview copy, so a bag or equipped item never has it.
    /// STILL-OPEN: nothing feeds <see cref="InPcBang"/>, <see cref="IsVip"/> or <see cref="HasWedding"/> yet (all false), and
    /// the maid id of an expired item, the music score checks, the owner bind (FUN_1406343b0) and the codi layer are not modelled.
    /// </summary>
    public bool CanUseItemDefault(EquipItem item, DateTimeOffset now)
    {
        if (item == null) return false;
        var d = ItemData(item.Id);
        if (d == null) return false;
        if (item.ExpiryTime > 0 && item.ExpiryTime < now.ToUnixTimeSeconds() && !d.IsPetItem) return false;
        if (d.PCBang && !InPcBang) return false;
        if (d.Vip && !IsVip) return false;
        if (d.Wedding && !HasWedding) return false;
        return true;
    }

    public bool CanUse(EquipItem item) => CanUseItem?.Invoke(item) ?? CanUseItemDefault(item, DateTimeOffset.UtcNow);

    /// <summary>Unk_Inventory::FUN_140a5f180 (inventory.cpp:782) with rarity 0: the amounts of the usable items of the id in
    /// its tab; 0 for id 0, no player, no item data or a tab outside 0-14.</summary>
    public int CountById(int itemId)
    {
        if (itemId == 0 || !HasPc || ItemData(itemId) == null) return 0;
        int tab = TabOf(itemId);
        if (tab is < 0 or >= ItemBook.Tabs) return 0;
        return Inventory.Where(i => TabOf(i.Id) == tab && i.Id == itemId && CanUse(i)).Sum(i => i.Amount);
    }
}

/// <summary>The CFieldData / CFieldDataCli values the cast check reads, with the ctor defaults (fielddata.cpp:548).</summary>
public sealed class SlotField
{
    public int Id;
    public bool UseEPSkill = true, UsePet = true, CheckFly, CheckClimb = true;
    public int MapType, ShadowContinent;
    public uint SkillUseDisableMask;
}

/// <summary>
/// The skill system's cast check as the quick slot runs it: CSkillSystem::FUN_141765a70 (skillslot.cpp:2183) with errors off
/// and casting allowed (SkillSlot::IsDisabled sets both around the call). Returns the client's reason code, 0 = castable.
/// </summary>
public static class CastGate
{
    /// <summary>FUN_141765a70: the level data (FUN_141764c80), then FUN_14176a380 for a request of that skill with no item.</summary>
    public static bool CanCast(SlotWorld w, uint skillId) => LevelData(w, skillId) is { } lv && Why(w, lv, castAllowed: true) == 0;

    /// <summary>FUN_141764c80 (:2068): an emotion at the level the player has it, else JobSystemCli::vfunc_1.</summary>
    public static SkillLevelData LevelData(SlotWorld w, uint skillId)
    {
        var d = w.Tables.GetSkillData(skillId);
        if (d == null) return null;
        if (d.IsEmotion) return w.EmotionLevel(skillId) is { } lv ? d.GetLevelData(lv) : null;
        return w.Jobs?.LevelData(skillId);
    }

    /// <summary>FUN_14176a380 (:3090): the cast's output skill's level when it differs, then the conditions and the cost.</summary>
    public static int Why(SlotWorld w, SkillLevelData level, bool castAllowed)
    {
        uint id = level.Skill.Id;
        uint bas = w.OutputSkill(id);
        if (bas != 0 && bas != id && w.Jobs?.LevelData(bas) is { } other) level = other;
        int why = Condition(w, level.Skill, id, itemUid: 0);
        return why != 0 ? why : Cost(w, level, castAllowed);
    }

    /// <summary>FUN_14176a530 (:3108): ep in a field without ep skills, meso, sp, ep, then the cooldown / recharge.</summary>
    public static int Cost(SlotWorld w, SkillLevelData lv, bool castAllowed)
    {
        var skill = lv.Skill;
        if (lv.Ep != 0 && w.Field is { UseEPSkill: false }) return 3;
        if (lv.Meso > w.Meso) return 4;
        if (lv.Sp > 0 && w.Sp < w.SpCost(skill.Id, lv.Sp)) return 6;
        if (lv.Ep > 0 && w.Ep < lv.Ep) return 7;
        float cool = w.CooldownRemain(skill.Id, 0);
        bool recharge = skill.RechargeMaxCount > 0;
        if ((!recharge && cool > 0f) || (recharge && w.RechargeRemain(skill) == 0))
        {
            if (skill.Condition.UseCoolDownShowUI) return w.CooldownRemain(skill.Id, 1) > 0f ? 2 : 0x22;
            if (!castAllowed) return 0x23;
        }
        return 0;
    }

    /// <summary>
    /// FUN_14176a8c0 (:3150): the skill's conditions. <paramref name="requestSkill"/> is the request's skill (m_nSkillId), whose
    /// learnt rank is checked; <paramref name="itemUid"/> its item (m_n40), 0 from a quick slot.
    /// </summary>
    public static int Condition(SlotWorld w, SkillData d, uint requestSkill, long itemUid)
    {
        if (w.Field is not { } map) return 0xd;
        var c = d.Condition;
        if (d.ActionKind == 0x1e)
            // The glide id comes from the request's item (m_n40); a quick slot request has none, so GetGlideData(0) finds no
            // row. The glide table (SkillGlideData) is not ported, so a request with an item gives the same.
            return 0xd;
        if (d.ActionKind == 0x1d)
        {
            if (!map.CheckClimb) return 0x10;
            if (w.CubeState(1)) return 0x11;
        }
        if (JobSystem.IsBookSkillKind(d) && w.CubeState(2)) return 0x13;
        foreach (var e in c.RequireEffect)
            if (!w.HasEnabledEffect(e)) return 0x14;
        if (c.OnlyShadowWorld && map.ShadowContinent == 0) return 0x15;
        if (c.OnlyFlyableMap && !map.CheckFly) return 0x16;
        if (c.RequireMapCodes.Count != 0 && !c.RequireMapCodes.Any(code => code != 0 && map.Id == code)) return 0x17;
        foreach (var g in c.HasNotAdditionalEffectGroup)
            if (w.HasEnabledEffectGroup(g)) return 0x18;
        if (w.IsBattleRiding && !c.AllowBattleRidingState) return 0x19;
        if (!w.IsBattleRiding && c.OnlyBattleRidingState) return 0x1a;
        if (w.Hp < 1) return 5;
        long sp = w.SpCost(d.Id, c.Sp);
        if (sp > 0 && sp > w.Sp) return 0xb;
        if (w.LapenshardLevel(d.Id) > 0 && w.IsMapType9(map.Id)) return 0x1b;
        if (d.Weapons.Count != 0)
        {
            bool match = false;
            foreach (var wp in d.Weapons)
            {
                if (w.Weapon is not { } held
                    || !(held.Right == wp.RightHand || wp.RightHand == 0)
                    || !(held.Left == wp.LeftHand || wp.LeftHand == 0)) continue;
                if (wp.RightHand != 0 && w.ItemSlotDisabled(5)) continue;
                if (wp.LeftHand != 0 && w.ItemSlotDisabled(4)) continue;
                match = true;
                break;
            }
            if (!match) return 0x1c;
        }
        if (d.MainType == 0x11 && w.PetNearby) return 0x1d;
        // PCBase::GetSkillUseDisableMask (pcbase.cpp:3461): the buffs' mask or the field's
        uint mask = w.BuffSkillMask | map.SkillUseDisableMask;
        if (mask != 0 && (mask & (1u << d.MainType)) != 0) return 0x1e;
        if (w.SkillUseDisabled(d.Id)) return 0x1f;
        if (w.Jobs?.FindSkill(requestSkill) is { } learnt && learnt.JobLevel == 2 && w.GameMode == 7) return 0x20;
        if (d.MainType == 4 && w.HeldItemSkill != (int)d.Id) return 0x21;
        if (w.IsDead) return 0xc;
        return 0;
    }
}

/// <summary>
/// SkillSlot (skillslot.cpp): a quick slot's skill id, item id and item uid (the key table's Unk_SsQuickEntry), read the way
/// SkillSlot::vfunc_8 (:793) builds the slot object.
/// </summary>
public sealed record SkillSlotState(SlotWorld W, uint SkillId, int ItemId, long ItemUid)
{
    SkillData Skill(uint id) => W.Tables.GetSkillData(id);

    /// <summary>
    /// SkillSlot::FUN_141761530 (skillslot.cpp:1382): while riding the skill's level 1, else its JobSystemCli level; the first changeSkill
    /// check whose effect is on gives the replacement, unless it is the skill itself.
    /// </summary>
    public SkillData Replacement()
    {
        if (!W.HasPc || SkillId == 0) return null;
        var lv = W.IsRiding ? W.Tables.Skills.GetSkillLevelData(SkillId, 1) : W.Jobs?.LevelData(SkillId);
        if (lv == null) return null;
        var ch = lv.Skill.Change;
        uint replace = 0;
        for (int i = 0; i < ch.CheckEffectId.Count; i++)
        {
            ushort level = i < ch.CheckEffectLevel.Count ? ch.CheckEffectLevel[i] : (ushort)0;
            uint overlap = i < ch.CheckEffectOverlapCount.Count ? ch.CheckEffectOverlapCount[i] : 0;
            if (!W.HasEffect(ch.CheckEffectId[i], level, overlap)) continue;
            replace = i < ch.ChangeSkillId.Count ? ch.ChangeSkillId[i] : 0;
            break;
        }
        if (replace == 0 || replace == SkillId) return null;
        return Skill(replace);
    }

    /// <summary>GetEffectiveSkillId @0x141761d90.</summary>
    public uint EffectiveSkillId => Replacement()?.Id ?? SkillId;

    /// <summary>
    /// vfunc_25 @0x14175fbf0: an item slot's item by uid, else the first usable one of the id (Unk_Inventory::FUN_140a5e2e0);
    /// a skill slot's usable item of the skill (FindItemBySkillId), by the slot's uid instead when it is a ucc or type 0x16 item.
    /// </summary>
    public EquipItem Item()
    {
        uint skill = EffectiveSkillId;
        if (ItemId != 0)
            return (ItemUid != 0 ? FindByUid(ItemUid) : null) ?? FindById(ItemId);
        if (skill == 0) return null;
        var it = W.SkillItemsOff ? null : FindBySkill(skill);
        if (it != null && W.ItemData(it.Id) is { } d && (d.HasCubeTextureMesh || d.IsType16)) it = FindByUid(ItemUid);
        return it;
    }

    /// <summary>Unk_Inventory::FindItemByUid (inventory.cpp:561): 0 finds nothing.</summary>
    EquipItem FindByUid(long uid) => uid == 0 ? null : W.Inventory.FirstOrDefault(i => i.Uid == uid);

    /// <summary>The items of a tab in slot order (slots[category]).</summary>
    IEnumerable<EquipItem> Tab(int tab) => W.Inventory.Where(i => W.TabOf(i.Id) == tab).OrderBy(i => i.InventorySlot);

    /// <summary>FUN_140a5e2e0 (inventory.cpp:650): the first item of the id in its tab the player can use.</summary>
    EquipItem FindById(int itemId)
    {
        if (itemId == 0 || W.ItemData(itemId) == null || !W.HasPc) return null;
        int tab = W.TabOf(itemId);
        if (tab is < 0 or >= ItemBook.Tabs) return null;
        return Tab(tab).FirstOrDefault(i => i.Id == itemId && W.CanUse(i));
    }

    /// <summary>
    /// FindItemBySkillId (inventory.cpp:570, ignoreUsability 0): an item of the skill that is not a lapenshard and can be used.
    /// The client walks its uid map (CAtlMap order); here the tabs in slot order.
    /// </summary>
    EquipItem FindBySkill(uint skill)
    {
        if (skill == 0 || !W.HasPc) return null;
        return AllInBagOrder().FirstOrDefault(i => W.ItemData(i.Id) is { } d && d.SkillId != 0 && d.SkillId == (int)skill && !d.IsLapenshard && W.CanUse(i));
    }

    IEnumerable<EquipItem> AllInBagOrder() => Enumerable.Range(0, ItemBook.Tabs).SelectMany(Tab);

    /// <summary>GetItemCountBySkillId (inventory.cpp:875): the counts of the usable items of the skill; without equipment unless
    /// <paramref name="withEquipment"/> (no equip, pet or ride item); never a lapenshard.</summary>
    int CountBySkill(uint skill, bool withEquipment)
    {
        if (skill == 0 || !W.HasPc) return 0;
        int n = 0;
        foreach (var i in AllInBagOrder())
        {
            if (W.ItemData(i.Id) is not { } d || d.SkillId == 0 || d.SkillId != (int)skill) continue;
            if (!withEquipment && (d.IsEquip || d.IsPetItem || d.IsRideItem)) continue;
            if (d.IsLapenshard || !W.CanUse(i)) continue;
            n += i.Amount;
        }
        return n;
    }

    int CountById(int itemId) => W.CountById(itemId);

    /// <summary>GetItemCount @0x141760220: a skill slot (not an item count kind) counts the skill's items; an item slot counts
    /// a stackable item, a consumable one by its skill unless the skill is an item count kind.</summary>
    public int ItemCount(bool withEquipment)
    {
        bool countKind = SkillId != 0 && Skill(SkillId) is { } s && s.IsItemCountKind;
        if (ItemId == 0 && SkillId != 0 && !countKind)
            return W.SkillItemsOff ? 0 : CountBySkill(SkillId, withEquipment);
        var d = W.ItemData(ItemId);
        if (d == null || !d.IsStackable) return 0;
        if (d.IsConsumable && !countKind) return CountBySkill(SkillId, withEquipment);
        return CountById(ItemId);
    }

    /// <summary>GetIconName @0x141760690: the item's icon, else the skill's (GetSkillIconName :1214, of the effective skill), else
    /// the item data's.</summary>
    public string IconName()
    {
        if (Item() is { } it) return W.ItemData(it.Id)?.IconPath ?? "";
        if (ItemId == 0) return Skill(EffectiveSkillId)?.Icon ?? "";
        return W.ItemData(ItemId)?.IconPath ?? "";
    }

    /// <summary>
    /// IsDisabled @0x141760c00 (gray): an item slot by its item; a skill slot by its data, level, cooldown with no charge left,
    /// a motion skill without a key while no skill set is on, the ride / pet / item count of an item kind, else the learnt flag
    /// and the cast check; then the buffs' main type mask and the skill's use block.
    /// </summary>
    public bool IsDisabled(int hotkey)
    {
        if (!W.HasPc) return true;
        if (ItemId != 0)
        {
            var it = Item();
            if (it == null) return !(W.ItemData(ItemId) is { IsUgcMapOX: true });
            var d = W.ItemData(it.Id);
            if (!W.CanUse(it)) return true;
            if (d == null || !d.IsLevelAllowed(W.Level)) return true;
            return !d.IsQuickSlotUsable;
        }
        uint id = EffectiveSkillId;
        if (id == 0) return true;
        var skill = Skill(id);
        if (skill == null) return true;
        var lv = W.Jobs?.LevelData(id);
        if (lv == null) return true;
        if (CooldownRemain() > 0f && skill.Condition.UseCoolDownShowUI && W.RechargeRemain(skill) == 0) return true;
        if (hotkey == 0 && !W.SkillSetActive && lv.HasSequenceName) return true;
        if (skill.IsItemKind)
        {
            var it = Item();
            if (it != null)
            {
                var d = W.ItemData(it.Id);
                if (d is { IsRideItem: true })
                {
                    if (!W.RideAllowed || ItemCount(true) <= 0) return true;
                }
                else if (d is { IsPetItem: true })
                {
                    if (W.Field is not { UsePet: true } || ItemCount(true) <= 0) return true;
                }
            }
            else if (ItemCount(false) <= 0) return true;
        }
        else
        {
            if (W.Jobs == null || !W.Jobs.IsLearned(SkillId)) return true;
            return !CastGate.CanCast(W, id);
        }
        if (W.BuffSkillMask != 0 && (W.BuffSkillMask & (1u << skill.MainType)) != 0) return true;
        return W.SkillUseDisabled(id);
    }

    /// <summary>GetCooldownDuration @0x141760a30: an item cooldown's total, else the level's cooldown.</summary>
    public float CooldownDuration()
    {
        if (!W.HasPc) return 0;
        uint id = EffectiveSkillId;
        if (id == 0 || W.Jobs?.LevelData(id) is not { } lv) return 0;
        var (total, byItem) = W.CooldownTotal(id);
        return byItem ? total : lv.Cooldown;
    }

    /// <summary>FUN_141760b90: the cooldown left.</summary>
    public float CooldownRemain()
    {
        if (!W.HasPc) return 0;
        uint id = EffectiveSkillId;
        return id == 0 ? 0 : W.CooldownRemain(id, 1);
    }

    /// <summary>GetRechargeMaxCount @0x1417604f0 / GetRechargeRemainingCount @0x1417603f0.</summary>
    public int RechargeMax => Skill(EffectiveSkillId) is { } s ? (int)s.RechargeMaxCount : 0;
    public int RechargeRemaining => W.HasPc && Skill(EffectiveSkillId) is { RechargeMaxCount: > 0 } s ? W.RechargeRemain(s) : 0;

    /// <summary>IsPetItem @0x141760190: the slot item id's data.</summary>
    public bool IsPetItem => W.ItemData(ItemId) is { IsPetItem: true };
    /// <summary>IsPcBangItem @0x141760590 / FUN_141760610 (VIP): of the found item.</summary>
    public bool IsPcBangItem => Item() is { } it && W.ItemData(it.Id) is { PCBang: true };
    public bool IsVipItem => Item() is { } it && W.ItemData(it.Id) is { Vip: true };

    /// <summary>FUN_141275260(0xe6, 1, 1): the notice of a clicked item slot whose item is gone.</summary>
    public const int ItemGoneNotice = 0xe6;
    /// <summary>The ShowSystemNotice text keys of the click (GetStringCommonByKey).</summary>
    public const string NotEnoughLevelKey = "s_err_not_enough_level", InvalidShortkeyKey = "s_err_invalid_shortkey_code";

    /// <summary>
    /// SkillSlot::vfunc_13 @0x14175cdd0 (skillslot.cpp:399), the click to use, with the slot's key (GetHotkey(0)):
    /// - the slot's item (vfunc_25, <see cref="Item"/>): none while the slot holds an item id and uid: notice 0xe6, stop; an item
    ///   the PC's level cannot use (CItem::CanUseAtLevel, <see cref="ItemData.IsLevelAllowed"/>): s_err_not_enough_level, stop;
    /// - a key: WorldService::FUN_141bc9fd0(key) (worldservice_1.cpp:724) queues that key as pressed (<see cref="SlotClickKind.Press"/>);
    /// - no key: the effective skill's row and a PC are needed; without a skill set, the job's level data (JobSystemCli vfunc_1)
    ///   is needed and a level with a sequence gives s_err_invalid_shortkey_code; then FUN_14092f4e0 of the slot's own skill id
    ///   (<see cref="SlotClickKind.Cast"/>, <see cref="QuickSlotEdit.UseSkill"/>).
    /// The slot is pushed again after (vfunc_10). STILL-OPEN: FUN_14175f200's tween first.
    /// </summary>
    public SlotClick Click(int hotkey)
    {
        var item = Item();
        if (item == null)
        {
            if (ItemId != 0 && ItemUid != 0) return new(SlotClickKind.Notice, ItemGoneNotice);
        }
        else if (W.HasPc && W.ItemData(item.Id) is { } d && !d.IsLevelAllowed(W.Level))
            return new(SlotClickKind.NoticeText, 0, NotEnoughLevelKey);
        if (hotkey != 0) return new(SlotClickKind.Press, hotkey);
        uint skill = EffectiveSkillId;
        if (Skill(skill) == null || !W.HasPc) return default;
        if (!W.SkillSetActive)
        {
            if (W.Jobs?.LevelData(skill) is not { } lv) return default;
            if (lv.HasSequenceName) return new(SlotClickKind.NoticeText, 0, InvalidShortkeyKey);
        }
        return new(SlotClickKind.Cast, (int)SkillId);
    }
}

/// <summary>What a quick slot click does (<see cref="SkillSlotState.Click"/>).</summary>
public enum SlotClickKind { None, Notice, NoticeText, Press, Cast }

/// <summary>A quick slot click: a notice id, a notice text key, the key to press, or the skill to use.</summary>
public readonly record struct SlotClick(SlotClickKind Kind, int Value = 0, string TextKey = null);
