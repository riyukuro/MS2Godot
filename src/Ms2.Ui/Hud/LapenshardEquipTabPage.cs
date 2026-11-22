using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// UILapenshardEquipTab's equip slots panel (LapenshardEquipSlotPanel, uilapenshardsystemdialog.cpp): the six worn slots
/// m_equipSlot1..6 of the page's movie. vfunc_3 (@0x140c5d680) binds the bag "LapenshardEquipSlotBag" and gives every slot
/// setData{bindID "<bag>.<slot-1>", slotIndex, isEquip}; SetEquipEffect (@0x140c5d9c0) lights m_equipEffect of the slots
/// whose kind is the selected one. OnUnmountEquipSlot (UILapenshardEquipTab::vfunc_6 @0x140c5e140) takes the worn lapenshard
/// off (mode 2) behind the use check FUN_140c62520. Inbound modes 0 to 2 rebuild the panel (events 2, 0, 1).
/// <para>STILL-OPEN: the item bag panel (LapenshardItemSlotBagPanel) and equip by drop or right-click; the slot tooltip's
/// title strings (0x751, 0x75a).</para>
/// </summary>
public sealed class LapenshardEquipTabPage : LapenshardTabPage
{
    public const string Bag = "LapenshardEquipSlotBag";
    public const int Slots = 6;
    /// <summary>The slot panel's slot flags and the tooltip's flags, as the other item bags use.</summary>
    public const int SlotFlags = 0x1401f, TooltipFlags = 0x20;

    public override string Name => "equip";
    public override string LabelKey => "s_lapenshard_tabname_equip";

    UiHost host;
    object root;
    /// <summary>The page's movie root (the body frame's first child).</summary>
    public object Root => root;

    /// <summary>The kind (1 red, 2 blue, 4 green) a slot (1..6) takes: InitEquipParts.</summary>
    public static int KindOfSlot(int slot) => slot is 1 or 2 ? 1 : slot is 3 or 4 ? 2 : 4;

    public static string SlotName(int index) => $"{Bag}.{index}";

    /// <summary>vfunc_3 of the item bag panel: the tile list's bag (LapenshardSlotBag in item mode, "%s.%d" slot names).</summary>
    public const string ItemBag = "LapenshardItemSlotBag";
    /// <summary>The bag's item list (FUN_140c60920 with bCheckUse, bWithBound, no worn, no table filter, kind 0).</summary>
    readonly List<Item> bagItems = new();
    public IReadOnlyList<Item> BagItems => bagItems;
    object itemBagChild;

    public static string ItemSlotName(int index) => $"{ItemBag}.{index}";

    static bool ParseItemSlot(string name, out int index)
    {
        index = -1;
        var prefix = ItemBag + ".";
        return name != null && name.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(name.AsSpan(prefix.Length), out index) && index >= 0;
    }

    /// <summary>FUN_140c60920: the lapenshards of inventory category 0xd the player can use (CanUseItem, job group, level).</summary>
    void CollectBagItems()
    {
        bagItems.Clear();
        var m = Owner.Model;
        if (m == null || !m.HasLocalPlayer) return;
        foreach (var it in Owner.InventoryItems(0xd))
        {
            if (it?.Data == null || !Owner.CanUseItem(it)) continue;
            if (!it.Data.IsJobAllowed(m.Player.JobGroup) || !it.Data.IsLevelAllowed(m.Player.Level)) continue;
            if (!it.Data.IsLapenshard) continue;
            bagItems.Add(it);
        }
    }

    /// <summary>The item bag panel's vfunc_3: the list, the tile list's bagId and its slots.</summary>
    void BuildItemBag()
    {
        itemBagChild ??= host?.Get(root, "m_itemBag");
        if (itemBagChild == null) return;
        CollectBagItems();
        host.Invoke(itemBagChild, "resetRenderer");
        host.Set(itemBagChild, "selectedIndex", -1);
        host.Set(itemBagChild, "bagId", ItemBag);
        for (int i = 0; i < bagItems.Count; i++) host.Push(ItemSlotName(i));
    }

    static bool ParseSlot(string name, out int index)
    {
        index = -1;
        var prefix = Bag + ".";
        return name != null && name.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(name.AsSpan(prefix.Length), out index) && index is >= 0 and < Slots;
    }

    public override void Show(UiHost h, object r)
    {
        host = h;
        root = r;
        Build();
    }

    public override void Leave() { }

    public override void Destroy() { root = null; host = null; itemBagChild = null; bagItems.Clear(); }

    /// <summary>LapenshardEquipSlotPanel::vfunc_3: a local player, then setData on every slot.</summary>
    void Build()
    {
        if (host == null || root == null || Owner?.Model == null || !Owner.Model.HasLocalPlayer) return;
        for (int i = 1; i <= Slots; i++)
        {
            var slot = host.Get(root, $"m_equipSlot{i}");
            if (slot == null) continue;
            host.Invoke(slot, "setData", host.Obj(("bindID", SlotName(i - 1)), ("slotIndex", i), ("isEquip", Owner.WornItemId(i) != 0)));
            host.Push(SlotName(i - 1));
        }
        BuildItemBag();
        SetEquipEffect(Owner.SelectedKind != 0);
    }

    /// <summary>SetEquipEffect (@0x140c5d9c0): m_equipEffect of the slots of the selected kind.</summary>
    void SetEquipEffect(bool on)
    {
        if (host == null || root == null) return;
        for (int i = 1; i <= Slots; i++)
        {
            if (host.Get(root, $"m_equipSlot{i}") is not { } slot) continue;
            if (host.Get(slot, "m_equipEffect") is not { } fx) continue;
            host.SetVisible(fx, on && KindOfSlot(i) == Owner.SelectedKind);
        }
    }

    public override void Notify(int ev)
    {
        switch (ev)
        {
            case 0 or 1 or 2: Build(); break;
            case 10: SetEquipEffect(true); break;
            case 11: SetEquipEffect(false); break;
        }
    }

    /// <summary>vfunc_9: worn list (mode 0) is event 2, equipped (1) is event 0, unequipped (2) is event 1.</summary>
    public override void OnPacket(LapenshardMessage m)
    {
        switch (m)
        {
            case LapenshardMessage.Worn: Notify(2); break;
            case LapenshardMessage.Equipped: Notify(0); break;
            case LapenshardMessage.Unequipped: Notify(1); break;
        }
    }

    /// <summary>OnUnmountEquipSlot: the use check, then mode 2 for a slot that has a lapenshard worn.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (name.Equals("notifySlotUse", StringComparison.OrdinalIgnoreCase) && args is { Length: > 1 } && args[0] is string sn
            && Rt.ToInt32(args[1]) == 1 && ParseItemSlot(sn, out _))
            return Owner != null && Owner.Model != null && Owner.Model.HasLocalPlayer ? RightClickSlot(sn) : true;
        if (!name.Equals("OnUnmountEquipSlot", StringComparison.OrdinalIgnoreCase)) return false;
        if (Owner == null || !Owner.CanUse()) return true;
        int slot = args is { Length: > 0 } ? (int)Rt.ToUint32(args[0]) : 0;
        if (Owner.WornItemId(slot) != 0) Owner.Send?.Invoke(new LapenshardUnequip(slot));
        return true;
    }

    public override IEnumerable<string> GetBagSlotBindings(string bag) =>
        bag == Bag ? Enumerable.Range(0, Slots).Select(SlotName)
        : bag == ItemBag ? Enumerable.Range(0, bagItems.Count).Select(ItemSlotName) : Array.Empty<string>();

    Item WornItem(int index) =>
        Owner.WornItemId(index + 1) is int id and not 0 ? Item.Create(Owner.Model.Tables.Items, id, 0) : null;

    Item ItemAtBag(int index) => index >= 0 && index < bagItems.Count ? bagItems[index] : null;

    public override object GetBinding(string name)
    {
        if (Owner?.Model == null) return null;
        Item item;
        if (ParseItemSlot(name, out int bi)) item = ItemAtBag(bi);
        else if (ParseSlot(name, out int i)) item = WornItem(i);
        else return null;
        if (item == null) return null;
        var slot = new List<(string, object)>();
        var m = Owner.Model;
        return ItemSlot.Build(slot, item, m.HasLocalPlayer, SlotFlags, m, m.ItemSlotSources) ? host?.Obj(slot.ToArray()) : null;
    }

    public override IDictionary<string, object> GetSlotTooltip(string name)
    {
        if (Owner?.Model == null) return null;
        Item item;
        if (ParseItemSlot(name, out int bi)) item = ItemAtBag(bi);
        else if (ParseSlot(name, out int i)) item = WornItem(i);
        else return null;
        var tip = new ItemTooltip(Owner.Model, TooltipFlags) { Localize = k => host?.Player.Translate?.Invoke(k) ?? k };
        return item != null && tip.Fill(item) ? tip.Data : new AsTooltip();
    }

    // ---- drag, drop and right-click (LapenshardSlot) ----------------------------------------------------------------------

    public override bool IsDragSlot(string name) => ParseSlot(name, out _) || ParseItemSlot(name, out _);

    public override void RefreshSlot(string name) { if (host != null && IsDragSlot(name)) host.Push(name); }

    /// <summary>LapenshardSlot::vfunc_20: a bag slot with a kind lights the equip slots of that kind (SelectSlot).</summary>
    public override void SlotDragStarted(string name)
    {
        if (ParseItemSlot(name, out int i) && ItemAtBag(i) is { } it && KindOf(it) is var kind and not 0) Owner.SelectSlot(kind);
    }

    /// <summary>LapenshardSlot::vfunc_21: the highlight goes off.</summary>
    public override void SlotDragDone(string name) { if (IsDragSlot(name)) Owner.ReleaseSlot(); }

    /// <summary>GetEquipMask of the item (0 for an id without a row).</summary>
    int KindOf(Item it) => (int)(Owner.Model.Tables.Lapenshards.Mask(it.ItemId) ?? 0);

    /// <summary>The same-group refusal both the drop and the right-click make before the send: a lapenshard of the group is
    /// worn and the slot's own is another group, no group, or this very item: the notice and no send.</summary>
    bool SameGroupRefused(int itemId, int currentSlot)
    {
        var t = Owner.Model.Tables.Lapenshards;
        if (!Owner.HasEquippedLapenshardOfSameGroup(itemId)) return false;
        int current = Owner.WornItemId(currentSlot);
        int curGroup = t.Group(current), grp = t.Group(itemId);
        if (curGroup != 0 && curGroup == grp && itemId != current) return false;
        Owner.ShowSystemNotice("s_msg_shortcutkey_invalid_already_same_lapenshard");
        return true;
    }

    /// <summary>LapenshardSlot::vfunc_18: a bag item dropped on an equip slot.</summary>
    public override void SlotDroppedOn(string name, string source, IDragSlotOwner sourceOwner)
    {
        try
        {
            if (Owner?.Model == null || !Owner.Model.HasLocalPlayer || sourceOwner is not LapenshardSystemController) return;
            if (!ParseSlot(name, out int di)) return;                   // a bag slot is no drop target
            if (!ParseItemSlot(source, out int si)) return;             // an equip slot as the source is refused
            if (ItemAtBag(si) is not { } item) return;
            int slot = di + 1;
            if (KindOfSlot(slot) != KindOf(item)) { Owner.ShowNoticeCode(0xd3); return; }
            if (!item.Data.IsLevelAllowed(Owner.Model.Player.Level)) return;
            if (SameGroupRefused(item.ItemId, slot)) return;
            Owner.SendLapenshard(slot, item.Uid);
        }
        finally { Owner?.ReleaseSlot(); }
    }

    /// <summary>LapenshardSlot::vfunc_14 (the bag branch): the slot of the item's kind (FindLapenSlotForItem), the same-group
    /// check, then the equip request. The movie's right click arrives as notifySlotUse(binding, 1).
    /// STILL-OPEN: an equip slot's right click uses the lapenshard's skill (FUN_14092fac0).</summary>
    bool RightClickSlot(string name)
    {
        if (!ParseItemSlot(name, out int si) || ItemAtBag(si) is not { } item) return false;
        int slot = Owner.FindLapenSlotForItem(KindOf(item));
        if (slot == 0) return true;
        if (SameGroupRefused(item.ItemId, slot)) return true;
        Owner.SendLapenshard(slot, item.Uid);
        return true;
    }
}
