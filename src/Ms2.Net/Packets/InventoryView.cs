namespace Ms2.Net.Packets;

/// <summary>One bag cell (InvenSlot): an item uid, its id and amount; Uid 0 is an empty cell.</summary>
public readonly record struct BagCell(long Uid, int ItemId, int Amount) {
    public bool Empty => Uid == 0;
}

/// <summary>What UIInventoryDialog::UpdateButtonState 0x141999f60 hands the AS3 updateButtonState (uiinventorydialog.cpp).</summary>
public sealed record InventoryButtonState(bool bAutoPickup, bool bItemEchant, bool bItemBreak, string itemBreakToolTip, bool bItemLock,
    bool bRemakeOption, bool bUnlimitedEnchant, bool bGem, string slotExpandToolTip, bool bOpenBank, string openBankTooltip);

/// <summary>The AS3 side of uiinventorydialog.gfx, named after the calls the client makes. A UI implements it; the view only calls it.</summary>
public interface IInventoryBinding {
    /// <summary>OnLoadComplete 0x141997dd0: initData(tabButtonIndex, devMode).</summary>
    void initData(int tabButtonIndex, bool devMode);
    /// <summary>UpdateButtonState 0x141999f60.</summary>
    void updateButtonState(InventoryButtonState state);
    /// <summary>FUN_14199bc00: setNew(buttonIndex, bNew) per tab button.</summary>
    void setNew(int buttonIndex, bool bNew);
    /// <summary>UpdateExpandLabel 0x14199c0d0: the m_expand child text, "{used}/{total}".</summary>
    void m_expand(string text);
    /// <summary>FUN_14199ba10: the slot bag is created again (CreateSlotBag 0x14140eca0, name "InventoryDialog") for the tab with its cells.</summary>
    void bindSlotBag(int tab, IReadOnlyList<BagCell> cells);
    /// <summary>InventorySlotBag::RefreshSlotForItem 0x14140f5b0: one cell refreshed (InvenSlot::vfunc_10).</summary>
    void refreshSlot(int tab, int slot, BagCell cell);
}

/// <summary>
/// A pure model of UIInventoryDialog over an <see cref="ItemBook"/>, no UI. Per tab a cell array placed by the item's InventorySlot
/// (capacity = GetSlotCount(tab) = base slots + extra slots, Unk_Inventory::SetExtraSlots 0x140a5cb80). Like the dialog it keeps pending flags
/// (m_19c: 1 new flags, 2 expand label, 4 tab rebuild) and <see cref="Update"/> dispatches them as vfunc_15 0x141997580 does.
/// </summary>
public sealed class InventoryView {
    public const int PendingNew = 1, PendingExpand = 2, PendingRebuild = 4;   // m_19c bits

    readonly ItemBook book;
    readonly BagCell[][] cells = new BagCell[ItemBook.Tabs][];
    readonly Func<int, int> tabOf;
    IInventoryBinding binding;
    int pending;

    /// <summary>m_194: the tab the dialog shows.</summary>
    public int Tab { get; private set; }
    /// <summary>The sort request (UIInventoryDialog::SendRequestItemInventory 0x14199c230, "send the sort request" uiinventorydialog.h:87): (tab, stack).
    /// Set by the owner of the connection; fires only from <see cref="Sort"/> (OnClickSort / OnSortConfirm), never on a tab switch.</summary>
    public Action<int, bool> RequestSort { get; set; }
    /// <summary>The move request (<see cref="ItemInventoryMoveRequest"/>, InvenSlot::vfunc_18): (item uid, target slot index). Set by the owner of
    /// the connection; the item stays where it is until the server's item packets move it.</summary>
    public Action<long, short> RequestMove { get; set; }
    /// <summary>The equip request (<see cref="ItemEquipRequest"/>, Unk_Inventory::RequestEquipItem): (item uid, equip slot, layer mode), the mode
    /// already mapped as FUN_1401fb870 does. Set by the owner of the connection; nothing is worn until the server's equip packets say so.</summary>
    public Action<long, byte, byte> RequestEquip { get; set; }
    /// <summary>The unequip request (<see cref="ItemUnequipRequest"/>, Unk_Inventory::RequestUnequipItem): the worn item's uid.</summary>
    public Action<long> RequestUnequip { get; set; }
    /// <summary>The worn item swap (<see cref="ItemSwapEquipRequest"/>, EquipSlot::vfunc_18): (the dragged worn item's uid, the target
    /// equip slot).</summary>
    public Action<long, byte> RequestSwapEquip { get; set; }
    /// <summary>The gem equip (<see cref="GemEquipRequest"/>, EquipGem): (the gem item's uid, the gem slot, 0xff for none).</summary>
    public Action<long, byte> RequestEquipGem { get; set; }
    /// <summary>The use request (<see cref="ItemUseRequest"/>, Unk_Inventory::RequestUseItem): (item uid, argument).</summary>
    public Action<long, string> RequestUse { get; set; }
    public int Pending => pending;
    /// <summary>FUN_141999e00: m_19c = 7 (new marks, expand label and tab rebuild).</summary>
    public void MarkAllPending() { if (binding != null) pending = PendingNew | PendingExpand | PendingRebuild; }
    /// <summary>m_1a8: the tab button index of each tab (-1 = the tab has no button and is skipped); identity until the UI tells.</summary>
    public int[] TabButtons { get; set; } = Enumerable.Range(0, ItemBook.Tabs).ToArray();

    public InventoryView(ItemBook book) {
        this.book = book;
        tabOf = id => book.TabOf?.Invoke(id) ?? 7;
        for (int t = 0; t < ItemBook.Tabs; t++) cells[t] = Build(t);
        book.Changed += OnChanged;
    }

    public void Detach() => book.Changed -= OnChanged;

    /// <summary>GetSlotCount(tab): base slots + extra slots; 0 for a tab outside 0-14.</summary>
    public int Capacity(int tab) {
        if ((uint) tab >= ItemBook.Tabs) return 0;
        return (book.BaseSlots?.Invoke(tab) ?? BagSlots.Default) + (book.ExtraSlots.TryGetValue(tab, out var e) ? e : 0);
    }

    public IReadOnlyList<BagCell> Cells(int tab) => (uint) tab < ItemBook.Tabs ? cells[tab] : Array.Empty<BagCell>();

    /// <summary>The item in a cell of a tab (Unk_Inventory::FindItemByUid of the cell's uid), null for an empty cell.</summary>
    public EquipItem ItemAt(int tab, int slot) {
        var c = Cells(tab);
        return slot < 0 || slot >= c.Count || c[slot].Empty ? null : book.Get(c[slot].Uid);
    }

    /// <summary>Unk_Inventory::FindItemByUid: the item of a uid, null when the inventory has none.</summary>
    public EquipItem FindByUid(long uid) => book.Get(uid);

    /// <summary>GetItemCount(tab) 0x140a5cf30: the non-empty cells.</summary>
    public int Used(int tab) => Cells(tab).Count(c => !c.Empty);

    /// <summary>UpdateExpandLabel 0x14199c0d0: StringFormat("{0}/{1}", GetItemCount, GetSlotCount).</summary>
    public string ExpandLabel(int tab) => $"{Used(tab)}/{Capacity(tab)}";

    /// <summary>FUN_140a61870 / FUN_140a617f0: an item's tab (CItemData::FUN_14053fd90), 7 when unknown.</summary>
    public int TabOfItem(int itemId) => tabOf(itemId);

    /// <summary>FUN_140a61430 (inventory.cpp:1101): the tab has an empty cell, looked for in pairs from both ends (the last cell,
    /// then the first, inward); with an odd count the middle cell is never looked at. False for a tab outside 0-14.</summary>
    public bool HasEmptyCell(int tab) {
        var c = Cells(tab);
        int last = c.Count - 1;
        for (int i = 0; i < c.Count / 2; i++)
            if (c[last - i].Empty || c[i].Empty) return true;
        return false;
    }

    /// <summary>FUN_14199bc00 per tab: true when any item of the tab IsNew.</summary>
    public bool HasNew(int tab) => (uint) tab < ItemBook.Tabs && book.All.Any(i => i.IsNew && tabOf(i.Id) == tab);

    BagCell[] Build(int tab) {
        var arr = new BagCell[Capacity(tab)];
        foreach (var i in book.All) {
            if (tabOf(i.Id) != tab) continue;
            if (i.InventorySlot < 0 || i.InventorySlot >= arr.Length) continue;
            arr[i.InventorySlot] = new BagCell(i.Uid, i.Id, i.Amount);
        }
        return arr;
    }

    /// <summary>OnLoadComplete 0x141997dd0: initData(GetTabButtonIndex(m_194), devMode) for the tab m_194, then the first flags are pending so
    /// the next Update fills everything (the movie's initData selects the button, whose index change is OnTabChanged: m_19c = 7).</summary>
    public void Bind(IInventoryBinding b, int tab = 0, bool devMode = false) {
        binding = b;
        Tab = tab;
        b.initData(TabButton(tab), devMode);
        pending |= PendingNew | PendingExpand | PendingRebuild;
    }

    /// <summary>vfunc_67 0x141997330's tail: the bag dropped (FUN_14140ed60) and m_19c = 0; the view stops calling the UI until bound again.</summary>
    public void Unbind() {
        binding = null;
        pending = 0;
    }

    /// <summary>GetTabButtonIndex 0x14199d920: m_1a8[tab], 0 for a tab outside the array.</summary>
    public int TabButton(int tab) => tab < 0 || tab >= TabButtons.Length ? 0 : TabButtons[tab];

    /// <summary>OnTabChanged 0x14199a4b0: m_194 = the button's tabIndex, m_19c = 7. Nothing is sent.</summary>
    public void SelectTab(int tab) {
        if ((uint) tab >= ItemBook.Tabs) return;
        Tab = tab;
        pending |= PendingNew | PendingExpand | PendingRebuild;
    }

    /// <summary>GetServerTime (CMainSystem, seconds since 1970) for CItem::IsExpired; 0 until the owner sets it.</summary>
    public Func<long> ServerTime { get; set; } = () => 0;

    /// <summary>What <see cref="ClickSort"/> did.</summary>
    public enum SortClick { Nothing, Sent, Confirm }

    /// <summary>
    /// vfunc_28 "OnClickSort" (uiinventorydialog.cpp:836-857): GetSelectedTab (m_194) out of 0-14 or with no item
    /// (GetItemCount 0x140a5cf30) does nothing; with an expired item in it (CountStackableItems 0x14199d840) the caller shows
    /// box 0x2a1 and the answer goes to <see cref="OnSortConfirm"/>; else SendRequestItemInventory(tab, false).
    /// </summary>
    public SortClick ClickSort() {
        int tab = Tab;
        if (tab < 0 || tab >= ItemBook.Tabs) return SortClick.Nothing;
        if (Used(tab) == 0) return SortClick.Nothing;
        if (CountExpired() > 0) return SortClick.Confirm;
        RequestSort?.Invoke(tab, false);
        return SortClick.Sent;
    }

    /// <summary>OnSortConfirm 0x14199b480 (:1388-1393): SendRequestItemInventory(m_194, result == 0).</summary>
    public void OnSortConfirm(int result) => RequestSort?.Invoke(Tab, result == 0);

    /// <summary>CountStackableItems 0x14199d840 (:1777-1789; the name is the decomp's): the items of m_194 (its argument is unused)
    /// for which CItem::IsExpired 0x140528930 holds, an expiry time (m_80, the item's second i64 after the amount) above 0 and
    /// not after the server time.</summary>
    public int CountExpired() {
        long now = ServerTime();
        return book.All.Count(i => tabOf(i.Id) == Tab && i.ExpiryTime > 0 && i.ExpiryTime <= now);
    }

    void OnChanged(long uid) {
        if (uid == 0) {
            // Reset, LoadTab, Load, ExpandCount, Move: the tab arrays are re-placed and the shown tab rebuilt
            for (int t = 0; t < ItemBook.Tabs; t++) cells[t] = Build(t);
            pending |= PendingNew | PendingExpand | PendingRebuild;
            return;
        }
        // one item: RefreshSlotForItem (IsItemInTab, then the slot of the item)
        var it = book.Get(uid);
        if (it == null) {
            for (int t = 0; t < ItemBook.Tabs; t++)
                for (int s = 0; s < cells[t].Length; s++)
                    if (cells[t][s].Uid == uid) { cells[t][s] = default; Refresh(t, s); }
        } else {
            int tab = tabOf(it.Id);
            if ((uint) tab < ItemBook.Tabs && it.InventorySlot >= 0 && it.InventorySlot < cells[tab].Length) {
                cells[tab][it.InventorySlot] = new BagCell(it.Uid, it.Id, it.Amount);
                Refresh(tab, it.InventorySlot);
            }
        }
        pending |= PendingNew | PendingExpand;
    }

    void Refresh(int tab, int slot) {
        if (tab == Tab) binding?.refreshSlot(tab, slot, cells[tab][slot]);
    }

    /// <summary>InvenSlot::vfunc_14 0x141406a60 / vfunc_20 0x141408cd0: the item's new flag is cleared (SetNew(0)); the new flags are re-sent.</summary>
    public void ClearNew(long uid) {
        if (book.Get(uid) is { IsNew: true } i) { i.IsNew = false; pending |= PendingNew; }
    }

    /// <summary>FUN_14199be00 (vfunc_67 calls it for all 15 tabs, uiinventorydialog.cpp:540-554): SetNew(0) on every item of the tab.</summary>
    public void ClearNewInTab(int tab) {
        foreach (var i in book.All) if (i.IsNew && tabOf(i.Id) == tab) i.IsNew = false;
        pending |= PendingNew;
    }

    /// <summary>vfunc_15 0x141997580: dispatch the pending flags, new flags (FUN_14199bc00), expand label (UpdateExpandLabel), tab rebuild (FUN_14199ba10).</summary>
    public void Update() {
        if (binding == null || pending == 0) return;
        int p = pending; pending = 0;
        if ((p & PendingNew) != 0)
            for (int t = 0; t < ItemBook.Tabs; t++) if (t < TabButtons.Length && TabButtons[t] != -1) binding.setNew(TabButtons[t], HasNew(t));   // FUN_14199bc00 line 1500
        if ((p & PendingExpand) != 0) binding.m_expand(ExpandLabel(Tab));
        if ((p & PendingRebuild) != 0) binding.bindSlotBag(Tab, cells[Tab]);
    }
}
