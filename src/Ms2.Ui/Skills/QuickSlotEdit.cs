using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Ui.Skills;

/// <summary>
/// The quick slot edits the skill slots send (unnamed/unit_14092f420_accountsessionstate.cpp): putting a skill or item on a
/// slot (FUN_14092f690) and emptying one (SendKeyTableMode5). Both check, then send KeyTable unless the skill system holds a
/// skill set (FUN_140930d70, <see cref="SlotWorld.SkillSetActive"/>), and return whether the slot takes the change.
/// STILL-OPEN: CGuideManager IsRestricted(10/11/12) and its FUN_141275260(0x888) notice (the guide restrictions are not
/// modelled, so nothing is restricted), and the PlaySystemSound of each edit.
/// </summary>
public static class QuickSlotEdit
{
    /// <summary>FUN_141275260(0x9a7, 1, 1): the notice of a passive skill put on a quick slot.</summary>
    public const int PassiveNotice = 0x9a7;
    /// <summary>CFeatureManager feature 0xd3: a lapenshard item is not put on a quick slot by a skill drop.</summary>
    public const int LapenshardFeature = 0xd3;

    /// <summary>
    /// FUN_14092f690 @0x14092f690 (:352) with (skill, item, uid, page, slot index):
    /// - a skill: its row must exist; a passive skill (SkillData::FUN_1407e9de0, commonfiles/skilloverlapcompare.cpp:1053,
    ///   main type 6) gives the 0x9a7 notice; with feature 0xd3, an item of that id in the bag (FUN_140a5e2e0) that is a
    ///   lapenshard refuses;
    /// - else an item: its data, from the bag item of that uid (FindItemByUid) or else the item id's (FUN_14053f870), must
    ///   exist and pass FUN_14053e460 (<see cref="Items.ItemData.IsQuickSlotUsable"/>);
    /// - then mode 3 (<see cref="KeyTableSetQuickSlotRequest"/>) unless a skill set is held. Returns true (1).
    /// </summary>
    public static bool Add(SlotWorld w, Func<int, bool> feature, Action<IOutPacket> send, Action<int> notice,
        int skillId, int itemId, long itemUid, int page, int slot)
    {
        if (skillId != 0)
        {
            var row = w.Tables.GetSkillData((uint)skillId);
            if (row == null) return false;
            if (row.IsPassive)
            {
                notice?.Invoke(PassiveNotice);
                return false;
            }
            if (feature(LapenshardFeature))
            {
                var bagItem = FindItem(w, i => i.Id == itemId);
                if (bagItem != null && w.ItemData(bagItem.Id) is { IsLapenshard: true }) return false;
            }
        }
        else
        {
            var bagItem = FindItem(w, i => i.Uid == itemUid);
            var data = bagItem != null ? w.ItemData(bagItem.Id) : w.ItemData(itemId);
            if (data == null || !data.IsQuickSlotUsable) return false;
        }
        if (!w.SkillSetActive) send(new KeyTableSetQuickSlotRequest((short)page, skillId, itemId, itemUid, slot));
        return true;
    }

    /// <summary>
    /// SendKeyTableMode5 @0x14092fef0 (:481) with (skill, item, uid, page, slot index): an empty slot (all three 0) is 0;
    /// else mode 5 (<see cref="KeyTableRemoveQuickSlotRequest"/>) unless a skill set is held. Returns true (1).
    /// STILL-OPEN: after the send, CKeyTable::FUN_14142f470(page, slot) (client/ms2/keytable.cpp:1976) puts the bag's
    /// category 10 item of that job slot back on the page; the job slot table (FUN_141432650) is not ported.
    /// </summary>
    public static bool Remove(SlotWorld w, Action<IOutPacket> send, int skillId, int itemId, long itemUid, int page, int slot)
    {
        if (skillId == 0 && itemUid == 0 && itemId == 0) return false;
        if (!w.SkillSetActive) send(new KeyTableRemoveQuickSlotRequest((short)page, skillId, itemUid));
        return true;
    }

    /// <summary>FUN_140d73bc0(0x2e8, 0, 0, ""): the notice box of a ride item with a UGC mesh but no UGC id dropped on a slot.</summary>
    public const int RideUgcNotice = 0x2e8;

    /// <summary>
    /// SkillSlot::vfunc_18 @0x14175d530 (client/ms2/skillslot.cpp:561-604), a bag slot (InvenSlot) dropped on a quick slot:
    /// FindItemByUid of the slot's uid must find an item whose data passes FUN_14053e460
    /// (<see cref="Items.ItemData.IsQuickSlotUsable"/>); its skill is FUN_141769320's (<see cref="ItemSkillId"/>);
    /// - a ride item with a UGC mesh (HasCubeTextureMesh) and no UGC id (HasUgcId, item.cpp:1350) is refused with the
    ///   <see cref="RideUgcNotice"/> box;
    /// - else a lapenshard goes on as its skill alone (item id and uid 0), refused for a PC that has not equipped it
    ///   (MyPC::FUN_14152f860) or whose lapenshard row's equip mask is not 1;
    /// - then <see cref="Add"/> on the slot. The taken entry, else null (the slot keeps its entry; there is no swap).
    /// </summary>
    public static QuickSlotEntry? AddFromInventory(SlotWorld w, Func<int, bool> feature, Action<IOutPacket> send, Action<int> notice,
        Action<int> noticeBox, long uid, int page, int slot)
    {
        var item = uid == 0 ? null : FindItem(w, i => i.Uid == uid);
        if (item == null || w.ItemData(item.Id) is not { IsQuickSlotUsable: true } d) return null;
        int skillId = ItemSkillId(w, d), itemId = item.Id;
        long itemUid = uid;
        bool usable = true;
        if (d.IsRideItem)
        {
            if (d.HasCubeTextureMesh && item.Ugc is not { Uid: > 0 })
            {
                usable = false;
                noticeBox?.Invoke(RideUgcNotice);
            }
        }
        else if (d.IsLapenshard)
        {
            itemId = 0;
            itemUid = 0;
            if (w.HasPc)
            {
                if (!w.LapenshardEquipped(item.Id)) usable = false;
                if (w.LapenshardEquipMask(item.Id) != 1) usable = false;
            }
        }
        if (!usable || !Add(w, feature, send, notice, skillId, itemId, itemUid, page, slot)) return null;
        return new QuickSlotEntry(skillId, itemId, itemUid);
    }

    /// <summary>FUN_141769320 @0x141769320 (skillslot.cpp:2865): a ride item's skill is SummonRideSkillID, a pet item's
    /// SummonPetSkillID, a type 5 (skill) item's GlideItemSkillID, else the item's own skill.</summary>
    public static int ItemSkillId(SlotWorld w, Items.ItemData d) =>
        d.IsRideItem ? w.SummonRideSkill : d.IsPetItem ? w.SummonPetSkill : d.Type == 5 ? w.GlideItemSkill : d.SkillId;

    /// <summary>
    /// FUN_14092f4e0 @0x14092f4e0 (unit_14092f420_accountsessionstate.cpp:314), a clicked skill slot without a key: the bag's item
    /// of the skill first (Unk_Inventory::FUN_140a62d30, inventory.cpp:1310), else a skill the job knows (JobSystemCli vfunc_2,
    /// <see cref="JobSystem.IsLearned"/>) goes to the cast request (MyPC::FUN_141525480 with the level's sequence flag). True when
    /// the skill goes to the cast request.
    /// STILL-OPEN: the item skill path (FUN_140a62d30's skill record with the item uid) and the skill's m_bBlocked refusal.
    /// </summary>
    public static bool UseSkill(SlotWorld w, uint skillId) => w.Jobs != null && w.Jobs.IsLearned(skillId);

    /// <summary>The first bag item that matches (Unk_Inventory's lookups walk the bag; the first match is kept).</summary>
    static EquipItem FindItem(SlotWorld w, Func<EquipItem, bool> match)
    {
        foreach (var i in w.Inventory) if (match(i)) return i;
        return null;
    }
}
