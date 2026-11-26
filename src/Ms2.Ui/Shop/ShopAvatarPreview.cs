using Ms2.Net.Packets;
using Ms2.Ui.Items;

namespace Ms2.Ui.Shop;

/// <summary>
/// The gacha shop's own ItemPreview (UIShopDialog's m_spPreview, <c>new ItemPreview(pMyPC, this, 1)</c>, uishopdialog.cpp:888): the
/// client's ItemPreview in mode 1, a list of items worn on a copy of the player's own avatar (itempreview.cpp). The class holds the
/// item list m_items and answers what the copy wears: the player's own equipment, with the slots the preview items cover taken off
/// (UIAvatarPC::UnequipSlot, uiavatarbase.cpp) and the preview items on top (UIAvatarPC::EquipItem). The avatar's render target is the
/// host's (AvatarView); <see cref="Changed"/> tells it to dress again.
/// <list type="bullet">
/// <item><see cref="InitWithItems"/> (@0x140d3c3c0, :80): the list again from the main items, each through FUN_141b48950 (not expired, not
/// DoNotPreview), CreatePreviewItem, IsGenderAllowed and a generated uid.</item>
/// <item><see cref="TryOnItem"/> (@0x140d3f400): an item whose id is already worn (IsItemWorn @0x140d3fda0) is refused; else
/// ApplyItemToAvatar and AddWornItem (@0x140d3fea0: the item of the same slot is replaced).</item>
/// <item><see cref="ResetAvatarOutfit"/> (@0x140d3f9a0) and <see cref="ShowMyEquip"/> (@0x140d3fa30): the list is cleared, the player's own
/// equipment shows again.</item>
/// <item><see cref="Unequip"/> (@0x140d3fac0): an equipment item is taken off the avatar and the list (the player's own item of the slot
/// shows again); any other kind rebuilds from the list (InitWithItems).</item>
/// </list>
/// <para>STILL-OPEN: ApplyItemToAvatar's action, skill, emotion, ride, gem and package branches (FUN_1418bced0, FUN_1418bce80, FUN_1418bcfc0,
/// FUN_1418bd050, FUN_1418bc110, FUN_1418bb2a0) are not ported: such an item stays in the list and has its icon, the avatar shows nothing
/// for it. CreatePreviewItem's ItemChangeBeauty package and the gem's CreateGem are not ported (no preview item is made). InitWithItem's
/// cube and pet avatars (mode 0) are not ported. The exact UIAvatarPC::ApplyItem result when a preview item is taken off (FUN_1418bc380)
/// is taken as the own item showing again. The hand slot of a dual-wield item (UIEquipIconContainer::GetHandSlot, :133) follows the icons
/// the list would hold.</para>
/// </summary>
public sealed class ShopAvatarPreview
{
    readonly ItemModelTable models;
    readonly List<Item> items = new();
    long nextUid = 1L << 40;

    /// <summary>The player's gender (the PC the avatar is a copy of).</summary>
    public Func<int> Gender = () => 1;
    /// <summary>The server time (CItem::IsExpired in FUN_141b48950).</summary>
    public Func<long> Now = () => 0;

    public ShopAvatarPreview(ItemModelTable models) { this.models = models; }

    /// <summary>m_items: the items on the avatar, in the order they were put on.</summary>
    public IReadOnlyList<Item> Items => items;

    /// <summary>Raised when what the avatar wears changed.</summary>
    public event Action Changed;

    /// <summary>
    /// ItemPreview::InitWithItems (@0x140d3c3c0): the list again from <paramref name="source"/>. The player's avatar is made new, so the
    /// list starts empty; an item that fails FUN_141b48950 (expired, DoNotPreview), CreatePreviewItem or IsGenderAllowed is left out.
    /// </summary>
    public void InitWithItems(IReadOnlyList<Item> source)
    {
        items.Clear();
        if (source != null)
            foreach (var item in source)
            {
                if (item?.Data == null || !IsPreviewable(item)) continue;
                var preview = CreatePreviewItem(item);
                if (preview == null || !preview.Data.IsGenderAllowed(Gender())) continue;
                preview.Uid = nextUid++;
                items.Add(preview);
            }
        Changed?.Invoke();
    }

    /// <summary>FUN_141b48950 (uiugcitemeditdialog.cpp:2337): not expired and DoNotPreview == 0 (:2325).</summary>
    public bool IsPreviewable(Item item) =>
        item?.Data != null && item.Data.DoNotPreview == 0 && !(item.ExpiryTime > 0 && item.ExpiryTime <= Now());

    /// <summary>
    /// ItemPreview::CreatePreviewItem (@0x140d3c640): a copy of the item (CItem::Clone) with its slot (CItem::FUN_140523c40: 5 for a
    /// dual-wield item, else the model's first group type). The ItemChangeBeauty package and the gem's CreateGem are not ported: null.
    /// </summary>
    public Item CreatePreviewItem(Item item)
    {
        if (item?.Data == null || item.Data.FunctionName == "ItemChangeBeauty" || item.Data.IsGemItem) return null;
        var copy = item.Clone();
        copy.Slot = SlotOf(item.Data);
        return copy;
    }

    /// <summary>The slot CItem::FUN_140523c40 sets (item.cpp:2783): 5 for FUN_14053d510 (dual-wield OH, equip subtype 0x1f / 0x22),
    /// else FUN_140527a20, the model's first group type (0 without a model).</summary>
    public int SlotOf(ItemData d) =>
        d.EquipSubtype is 0x1f or 0x22 ? 5 : models.ForItem(d)?.FirstSlotType ?? 0;

    /// <summary>The kinds ApplyItemToAvatar's last branch dresses: the ones with no special path (STILL-OPEN above) and a model.</summary>
    public bool IsDressed(ItemData d) =>
        d != null && d.Type is not (0x18 or 0x17 or 0x19) && !d.IsSkillItem && !d.IsRideItem && !d.IsGemItem
        && models.ForItem(d) is { SlotTypes.Count: > 0 };

    /// <summary>ItemPreview::IsItemWorn (@0x140d3fda0): an item of this id is in the list.</summary>
    public bool IsItemWorn(Item item)
    {
        if (item?.Data == null) return false;
        foreach (var worn in items)
            if (worn.ItemId == item.ItemId) return true;
        return false;
    }

    /// <summary>
    /// ItemPreview::TryOnItem (@0x140d3f400): false for an item already worn (same id) or one no preview item can be made of; else the
    /// item is put on and the list replaces the item of its slot (AddWornItem).
    /// </summary>
    public bool TryOnItem(Item item)
    {
        if (item?.Data == null || IsItemWorn(item)) return false;
        var preview = CreatePreviewItem(item);
        if (preview == null) return false;
        preview.Uid = nextUid++;
        AddWornItem(preview);
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// ItemPreview::InitWithItem (@0x140d3bc90) for an item the PC avatar shows: the list is that one item (mode 0 and the shop's mode 1
    /// alike; the cube and pet avatars are STILL-OPEN above).
    /// </summary>
    public bool InitWithItem(Item item)
    {
        items.Clear();
        var preview = CreatePreviewItem(item);
        if (preview != null) { preview.Uid = nextUid++; AddWornItem(preview); }
        Changed?.Invoke();
        return preview != null;
    }

    /// <summary>ItemPreview::AddWornItem (@0x140d3fea0): the item of the same slot (FUN_140526c30) leaves the list, then the item is added.</summary>
    void AddWornItem(Item item)
    {
        int at = items.FindIndex(i => i.Slot == item.Slot);
        if (at >= 0) items.RemoveAt(at);
        items.Add(item);
    }

    /// <summary>ItemPreview::ResetAvatarOutfit (@0x140d3f9a0, onClickUnequipAll) and FUN_140d3fa30 (onClickShowMyEquip): the preview is
    /// cleared, the player's own equipment shows again, the camera resets.</summary>
    public void ResetAvatarOutfit()
    {
        items.Clear();
        Changed?.Invoke();
    }

    /// <summary>ItemPreview::FUN_140d3fa30 (@0x140d3fa30, onClickShowMyEquip): RestoreAll, the pose again, the camera reset, m_items cleared.</summary>
    public void ShowMyEquip() => ResetAvatarOutfit();

    /// <summary>
    /// ItemPreview::FUN_140d3fac0 (@0x140d3fac0, UIShopDialog::Unequip, uishopdialog.cpp:3507): the item of this uid leaves the list and
    /// the avatar (FUN_1418bc380); false when none has it. For a kind that is not an equipment item the client rebuilds the avatar from
    /// the list, which here is the same list without it.
    /// </summary>
    public bool Unequip(long uid)
    {
        int at = items.FindIndex(i => i.Uid == uid);
        if (at < 0) return false;
        items.RemoveAt(at);
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// UIEquipIconContainer::GetHandSlot (@0x140e2a7e0, uiequipiconcontainer.cpp:133) for the icons of slots 4 and 5 taken or free:
    /// 0 for an item that is not dual-wield (FUN_14053d510); 5 with both free; 4 when only 4 is free; 5 when only 5 is free; with both
    /// taken the one the container did not use last (<paramref name="lastHand"/>, m_70).
    /// </summary>
    public static int HandSlot(ItemData d, bool slot4Taken, bool slot5Taken, int lastHand)
    {
        if (d == null || d.EquipSubtype is not (0x1f or 0x22)) return 0;
        if (!slot4Taken && !slot5Taken) return 5;
        if (!slot4Taken) return 4;
        if (!slot5Taken) return 5;
        return lastHand == 4 ? 5 : 4;
    }

    /// <summary>
    /// The slots the item takes off the avatar's own equipment (UnequipSlot per model group, ApplyItemToAvatar :624-638): each group's
    /// type, an OH group (0x13) as slot 5. Empty for an item that is not dressed.
    /// </summary>
    public IEnumerable<int> CoveredSlots(Item item)
    {
        if (!IsDressed(item.Data)) yield break;
        foreach (int t in models.ForItem(item.Data).SlotTypes) yield return t == 0x13 ? 5 : t;
    }

    /// <summary>
    /// What the avatar wears: <paramref name="own"/> without the items of the slots the preview items cover, then the preview items in
    /// the order they were put on, each as an equip record of its slot, uid and rarity with the colour <paramref name="colour"/> gives.
    /// </summary>
    public List<EquipItem> Dress(IReadOnlyList<EquipItem> own, Func<Item, EquipColor> colour)
    {
        var covered = new HashSet<int>();
        foreach (var item in items)
            foreach (int s in CoveredSlots(item)) covered.Add(s);
        var worn = new List<EquipItem>(own?.Count ?? 0);
        if (own != null)
            foreach (var e in own)
                if (!covered.Contains((int)e.Slot)) worn.Add(e);
        foreach (var item in items)
            if (IsDressed(item.Data))
                worn.Add(new EquipItem { Id = item.ItemId, Uid = item.Uid, Slot = (EquipSlot)item.Slot, Rarity = item.Rarity, Color = colour?.Invoke(item) ?? default });
        return worn;
    }
}
