using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The global inventory's (g_143ab2a90, Unk_Inventory) auto-equip on level up, feature 232 (0xe8), inventory.cpp:1755-1882:
/// the pending item (the one asked for and not yet put on), the queue of items waiting for it, the items taken off (never
/// put on again by it) and the switch. It lives as long as the session (the client's inventory is a global); what it reads
/// of the field (the bag, the local player, the equip request) are hooks the HUD sets, and with a hook missing (no HUD, no
/// local player) nothing is asked.
/// <para>Its callers: the own level up (OnLevelUp, scenestategame.cpp:9565, <see cref="OnLevelUp"/>); a new item
/// (OnItemInventory mode 7, :2856, <see cref="OnNewItem"/>); the own put-on (OnItemPutOn, :3125, <see cref="OnOwnPutOn"/>);
/// the bind box's answer (HandleUseBindEquipItemButton, uimessagebox.cpp:1899, <see cref="OnBindEquipAnswered"/>); the
/// MyPC's frame (vfunc_53, mypc.cpp:3008, <see cref="ProcessAutoEquipQueue"/>); the own take-off of an item with a model
/// preset (MyPC::vfunc_128, mypc.cpp:4903, <see cref="ExcludeFromAutoEquip"/>); the server's internal command
/// AutoInstallEquipMent (usercommandcli.cpp:2178, <see cref="SetAutoEquipEnabled"/>); the own FieldAddUser
/// (scenestategame.cpp:11123) and Unk_Inventory::Clear (inventory.cpp:407, at the start and on the account state)
/// (<see cref="ResetAutoEquip"/>).</para>
/// </summary>
public sealed class AutoEquip
{
    /// <summary>The feature id (0xe8) every part tests.</summary>
    public const int FeatureId = 232;
    /// <summary>Client::Constants AutoInstallEquipmentMinLevel / MaxLevel's defaults (unit_1402ab440_constantsblocks.cpp:1563).</summary>
    public const int DefaultMinLevel = 5, DefaultMaxLevel = 99;
    /// <summary>The system notice of the pending item put on (scenestategame.cpp:3130), formatted with the item's name.</summary>
    public const string PutOnNoticeKey = "s_msg_item_auto_install_equipment";

    /// <summary>CFeatureManager::IsFeatureEnabled(232).</summary>
    public Func<bool> FeatureEnabled = () => false;
    /// <summary>The constants' AutoInstallEquipmentMinLevel / MaxLevel (Common::Constants::Value&lt;unsigned&gt;).</summary>
    public int MinLevel = DefaultMinLevel, MaxLevel = DefaultMaxLevel;

    /// <summary>GetItemsInCategory(0): the items of bag tab 0 in slot order; null without a bag.</summary>
    public Func<IEnumerable<Item>> EquipTabItems;
    /// <summary>FindItemByUid; null without a bag.</summary>
    public Func<long, Item> FindItemByUid;
    /// <summary>WorldService::GetLocalPlayer's level (vfunc_78); null without a local player.</summary>
    public Func<int?> PlayerLevel;
    /// <summary>The local player's GetEquipBySlotKind(slot, layer): the worn item, null for none.</summary>
    public Func<int, int, Item> WornAt;
    /// <summary>GetItemUseError (inventory.cpp:1578) for the local player.</summary>
    public Func<Item, int> ItemUseError;
    /// <summary>RequestEquipItem(item, 0, 0, 0) (<see cref="InventoryEquip.RequestEquipItem"/>): 1 when asked.</summary>
    public Func<Item, int> RequestEquipItem;
    /// <summary>CItemData::GetModelOrEmpty()->GetFirstGroupType of an item preset (CItem::FUN_140527a20): 0 without a model.</summary>
    public Func<int, int> FirstGroupType = _ => 0;

    long pendingItem;                                  // pendingItem
    bool autoEquipEnabled;                             // autoEquipEnabled
    readonly LinkedList<Item> autoEquipQueue = new();  // autoEquipQueue (CAtlList of ItemPtr)
    readonly List<long> excludedItems = new();         // excludedItems (CAtlList of uids)

    /// <summary>FUN_140a66f20: the pending item's uid, 0 for none.</summary>
    public long PendingItem => pendingItem;
    /// <summary>FUN_140a66f40: sets the pending item.</summary>
    public void SetPendingItem(long uid) => pendingItem = uid;
    /// <summary>IsAutoEquipEnabled @0x140a67000.</summary>
    public bool IsAutoEquipEnabled => autoEquipEnabled;
    /// <summary>SetAutoEquipEnabled @0x140a66fa0.</summary>
    public void SetAutoEquipEnabled(bool on) => autoEquipEnabled = on;
    /// <summary>The queued items' uids, head first.</summary>
    public IEnumerable<long> QueuedUids => autoEquipQueue.Select(i => i.Uid);
    /// <summary>The number of queued items.</summary>
    public int QueuedCount => autoEquipQueue.Count;
    /// <summary>The excluded uids, in the order added.</summary>
    public IReadOnlyList<long> ExcludedUids => excludedItems;

    /// <summary>ResetAutoEquip @0x140a66c00: no pending item, the queue and the excluded items emptied, the switch is feature
    /// 232.</summary>
    public void ResetAutoEquip()
    {
        pendingItem = 0;
        autoEquipQueue.Clear();
        excludedItems.Clear();
        autoEquipEnabled = FeatureEnabled();
    }

    /// <summary>FUN_140a66990 @0x140a66990 (inventory.cpp:1755), the own level up: with feature 232 and the switch on, each
    /// candidate of <see cref="CollectAutoEquipCandidates"/> in the map's order goes to <see cref="TryAutoEquip"/>.</summary>
    public void OnLevelUp(int level)
    {
        if (!FeatureEnabled() || !autoEquipEnabled) return;
        foreach (var item in CollectAutoEquipCandidates(level)) TryAutoEquip(item);
    }

    /// <summary>
    /// FUN_140a66ae0 @0x140a66ae0: an item that passes <see cref="CanAutoEquip"/> is asked for at once when no item is pending
    /// (RequestEquipItem(item, 0, 0, 0); an asked item becomes the pending one), else it is queued
    /// (<see cref="Enqueue"/>).
    /// </summary>
    public void TryAutoEquip(Item item)
    {
        if (!CanAutoEquip(item)) return;
        if (pendingItem == 0)
        {
            if ((RequestEquipItem?.Invoke(item) ?? 0) != 0) pendingItem = item.Uid;
        }
        else Enqueue(item);
    }

    /// <summary>
    /// CollectAutoEquipCandidates @0x140a66ca0 (inventory.cpp:1786): with a local player, each item of bag tab 0 in slot order
    /// that is not excluded, whose layer (CItem::FUN_14052a170) is 0, whose slot kind (CItem::FUN_140527a20) the player wears
    /// nothing in at layer 0, and whose level limit is the level (or any level for 0): per slot kind the one with the
    /// highest item level (CItem::FUN_140529070, m_b0; a later item only replaces on a strictly higher one). Returned in
    /// the CAtlMap's walk order (<see cref="SlotMapOrder"/>).
    /// </summary>
    public IReadOnlyList<Item> CollectAutoEquipCandidates(int level)
    {
        var result = new List<Item>();
        if (PlayerLevel?.Invoke() == null || EquipTabItems?.Invoke() is not { } items) return result;
        var bySlot = new Dictionary<int, Item>();
        var inserted = new List<int>();
        foreach (var item in items)
        {
            if (item?.Data == null) continue;
            if (IsExcluded(item.Uid)) continue;
            int slot = SlotKind(item);
            int mode = Layer(item);
            if (mode != 0) continue;
            if (WornAt?.Invoke(slot, mode) != null) continue;
            if ((ushort)level != 0 && (ushort)level != (ushort)item.Data.LevelLimit) continue;   // CItem::GetLevelLimit, u16
            if (bySlot.TryGetValue(slot, out var old))
            {
                if (ItemLevel(old) < ItemLevel(item)) bySlot[slot] = item;
            }
            else { bySlot[slot] = item; inserted.Add(slot); }
        }
        foreach (int slot in SlotMapOrder(inserted)) result.Add(bySlot[slot]);
        return result;
    }

    /// <summary>The walk order of the candidates' ATL::CAtlMap&lt;slot, ItemPtr&gt; (GetStartPosition / GetNext): its default 17
    /// bins (no rehash below 38 entries), each key in bin key % 17 (CDefaultHashTraits: the value), the bins in order and a
    /// bin's nodes newest first (NewNode puts a node at its bin's head; SetAt on a key it has keeps the node).</summary>
    public static IEnumerable<int> SlotMapOrder(IReadOnlyList<int> insertionOrder)
    {
        const int Bins = 17;
        for (int bin = 0; bin < Bins; bin++)
            for (int i = insertionOrder.Count - 1; i >= 0; i--)
                if ((uint)insertionOrder[i] % Bins == bin) yield return insertionOrder[i];
    }

    /// <summary>
    /// FUN_140a67020 @0x140a67020 (inventory.cpp:1823): an item can be asked for when feature 232 and the switch are on, its
    /// data is an equipment item (FUN_14053cbc0: type 1), the bag still has its uid, it is not excluded, there is a local
    /// player whose level is within AutoInstallEquipmentMinLevel..MaxLevel, its layer is 0, the player wears nothing in its
    /// slot kind at layer 0 and GetItemUseError is 0.
    /// </summary>
    public bool CanAutoEquip(Item item)
    {
        if (item == null) return false;
        if (!FeatureEnabled() || !autoEquipEnabled) return false;
        long id = item.Uid;
        if (item.Data?.Type != 1) return false;
        if (FindItemByUid?.Invoke(id) == null) return false;
        if (IsExcluded(id)) return false;
        if (PlayerLevel?.Invoke() is not { } raw) return false;
        int level = (ushort)raw;
        if ((uint)level < (uint)MinLevel || (uint)level > (uint)MaxLevel) return false;
        int slot = SlotKind(item);
        int mode = Layer(item);
        if (mode != 0) return false;
        if (WornAt?.Invoke(slot, mode) != null) return false;
        return (ItemUseError?.Invoke(item) ?? 11) == 0;
    }

    /// <summary>FUN_140a67390 @0x140a67390: the uid is in the excluded items.</summary>
    public bool IsExcluded(long uid) => excludedItems.Contains(uid);

    /// <summary>FUN_140a673e0 @0x140a673e0: an item that passes <see cref="CanAutoEquip"/> goes to the queue's tail.</summary>
    public void Enqueue(Item item)
    {
        if (item == null || !CanAutoEquip(item)) return;
        autoEquipQueue.AddLast(item);
    }

    /// <summary>ExcludeFromAutoEquip @0x140a674c0: with feature 232, the item's uid joins the excluded items once.</summary>
    public void ExcludeFromAutoEquip(long uid)
    {
        if (!FeatureEnabled()) return;
        if (!excludedItems.Contains(uid)) excludedItems.Add(uid);
    }

    /// <summary>ProcessAutoEquipQueue @0x140a675c0, each frame of the MyPC (vfunc_53, mypc.cpp:3008): with feature 232, a
    /// queued item and none pending, the head goes to <see cref="TryAutoEquip"/> and leaves the queue.</summary>
    public void ProcessAutoEquipQueue()
    {
        if (autoEquipQueue.Count == 0 || !FeatureEnabled()) return;
        if (pendingItem != 0) return;
        var item = autoEquipQueue.First.Value;
        if (item != null) TryAutoEquip(item);
        autoEquipQueue.RemoveFirst();
    }

    /// <summary>OnItemInventory mode 7 (scenestategame.cpp:2856-2860), after the new item's line: with feature 0xe8, an
    /// equipment item (FUN_14053cbc0) found in the bag goes to <see cref="TryAutoEquip"/>.</summary>
    public void OnNewItem(Item item)
    {
        if (item?.Data == null || !FeatureEnabled()) return;
        if (item.Data.Type == 1) TryAutoEquip(item);
    }

    /// <summary>OnItemPutOn for the own PC (scenestategame.cpp:3125-3128): with feature 0xe8 and the switch on, the put-on uid
    /// that is the pending one clears it; true then, and the caller shows <see cref="PutOnNoticeKey"/> with the item's name
    /// (ShowSystemNotice(text, 1, 1)) and closes the shown bind-on-equip box (GetCurrentBox's extra "useBindEquipItem",
    /// FUN_1419f8120).</summary>
    public bool OnOwnPutOn(long uid)
    {
        if (!FeatureEnabled() || !autoEquipEnabled || pendingItem != uid) return false;
        pendingItem = 0;
        return true;
    }

    /// <summary>HandleUseBindEquipItemButton's end (uimessagebox.cpp:1899-1900): with feature 0xe8, an answer that asked for
    /// nothing (the no, or a yes whose RequestEquipItem gave 0) for the pending uid clears it.</summary>
    public void OnBindEquipAnswered(long uid, int result)
    {
        if (FeatureEnabled() && result == 0 && pendingItem == uid) pendingItem = 0;
    }

    /// <summary>
    /// CItem::FUN_140527a20 (item.cpp:1278) of a bag item: the model's first group type (GetModelOrEmpty()->GetFirstGroupType,
    /// 0 without a model). The equip slot m_68 it returns first for a dual wield item (FUN_14053d510) is set by the put-on
    /// packet (FUN_140526c10) and not kept on the port's bag items, so a bag item reads as m_68 0.
    /// </summary>
    public int SlotKind(Item item) => FirstGroupType(item.Data.ItemPreset);

    /// <summary>CItem::FUN_14052a170 (item.cpp:1629) of a bag item (no variant, m_24 -1): 1 for a skin type, else 0 (a cash
    /// item 0; property.flag08's 3 is never loaded, ItemProperty::Load).</summary>
    public static int Layer(Item item) => item.Data.HasSkinType ? 1 : 0;

    /// <summary>CItem::FUN_140529070: m_b0, the item level total (CItem::UpdateItemLevel, item.cpp:1545).</summary>
    static int ItemLevel(Item item) => ItemLevels.Of(item)?.Total ?? 0;

    /// <summary>The notice of the pending item put on: <see cref="PutOnNoticeKey"/> (FUN_14028dfa0) with the item's name
    /// (string table 1, FUN_141811980(1, item id)).</summary>
    public static string PutOnNotice(Func<string, string> common, string itemName) =>
        Ms2.Quest.ClientFormat.Format(common(PutOnNoticeKey) ?? "", itemName ?? "");
}
