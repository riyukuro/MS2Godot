using System.Globalization;
using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>A drag source that is an ItemLockSlot: the uid of the item in the named slot (null for another source).</summary>
public interface IItemLockSlotOwner
{
    long? LockSlotUid(string name);
}

/// <summary>
/// The item lock window, UIItemLockDialog (client/ms2/uiitemlockdialog.cpp, UI id 0x4f08, type name "ItemLockDialog", movie
/// uiitemlockdialog) with its ItemLockSlotBag of ItemLockSlots (itemlockslot.cpp), named "ItemLockDialog.%d". The state is
/// <see cref="ItemLockBook"/> (CItemLockManager); this controller shows it and runs the dialog's own paths: the mode 0 send
/// on load and on close, the lock / unlock buttons' confirm boxes, the tab change, and the drops.
/// <para>STILL-OPEN: the dialog's tab control itself (SetTab on the movie: the movie's own tab buttons are not driven from
/// here); UIInventoryDialog::UpdateButtonState / FUN_141999e00 after a load, close and tab change; RequestUnlockMissingItems
/// per frame while the bag is hidden (the bag is never hidden here); vfunc_43 / vfunc_66 placement beside the inventory.</para>
/// </summary>
public sealed class ItemLockDialogController : UiController, IDragSlotOwner, IItemLockSlotOwner
{
    public override string Movie => "uiitemlockdialog";
    protected override string EntityMethod => "alignment";
    public override string TypeName => "ItemLockDialog";

    public const int UiId = 0x4f08;
    public const string Bag = "ItemLockDialog";
    /// <summary>ItemLockSlot::vfunc_8 (@0x140d41cf0): ASSlot(2) built with flags 0x1401f; ITooltip::vfunc_0: ASTooltip flags 0x20.</summary>
    public const int SlotFlags = 0x1401f, TooltipFlags = 0x20;
    /// <summary>The confirm box's tag (s_strItemLockConfirm) and type.</summary>
    public const string ConfirmTag = "itemlock_confirm";
    public const int ConfirmBoxType = 0x21;

    public ItemLockBook Book;
    /// <summary>The box is shown (FUN_140d73c60).</summary>
    public Action<MessageBoxData> ShowData = d => MessageBoxQueue.Show(d);
    /// <summary>ShowSystemNotice / FUN_141275260: the notice text.</summary>
    public event Action<string> NoticeText;
    /// <summary>The server time, the item lock's clock.</summary>
    public Func<long> Now = () => 0;
    /// <summary>The dialog closed.</summary>
    public event Action Closed;
    /// <summary>The inventory item of a uid (Unk_Inventory::FindItemByUid) as the UI item; null when unknown.</summary>
    public Func<long, Item> FindItem = _ => null;
    /// <summary>An inventory slot needs a redraw (UIInventoryDialog::RefreshItemSlot).</summary>
    public event Action<long> InventorySlotRefresh;

    bool closing;
    object bagChild;

    public static string SlotName(int i) => Bag + "." + i.ToString(CultureInfo.InvariantCulture);

    static bool ParseSlot(object name, out int index)
    {
        index = -1;
        return name is string n && n.StartsWith(Bag + ".", StringComparison.Ordinal)
            && int.TryParse(n.AsSpan(Bag.Length + 1), NumberStyles.None, CultureInfo.InvariantCulture, out index)
            && index >= 0 && index < ItemLockBook.Slots;
    }

    // ---- load / close ------------------------------------------------------------------------------------------------

    /// <summary>OnLoadComplete (@0x140f56b00): BindItemBag, the item listener, SetTab(0), SendRequestItemLockMode0.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (Book == null) return;
        Book.DialogOpen = true;
        Book.Mode = 0;
        Book.BagChanged += BindItemBag;
        Book.ItemRefresh += OnItemRefresh;
        Book.Notice += OnNotice;
        BindItemBag();
        Book.SendReset();
        UpdateLockButtons();
    }

    /// <summary>vfunc_67 (close): the slot binds released, the listener dropped, the mode 0 send.</summary>
    public override void Close()
    {
        if (closing) return;
        closing = true;
        if (Book != null)
        {
            Book.BagChanged -= BindItemBag;
            Book.ItemRefresh -= OnItemRefresh;
            Book.Notice -= OnNotice;
            Book.DialogOpen = false;
            Book.SendReset();
        }
        base.Close();
        bagChild = null;
        Closed?.Invoke();
    }

    void OnItemRefresh(long uid) => InventorySlotRefresh?.Invoke(uid);

    void OnNotice(int id, int arg)
    {
        string text = id == ItemLockBook.SelectedNotice
            ? Model?.Tables.Common("s_itemlock_selected_item")
            : Model?.Tables.Common(id);
        if (!string.IsNullOrEmpty(text) && arg != 0) text = ClientFormat.Format(text, arg);
        if (!string.IsNullOrEmpty(text)) NoticeText?.Invoke(text);
    }

    /// <summary>BindItemBag (@0x140f56ce0): the bag's "bagId" is the type name and its slots are pushed again; then the lock buttons.</summary>
    void BindItemBag()
    {
        if (Host?.Entity == null || closing) return;
        bagChild ??= Host.Child("m_itemBag");
        if (bagChild != null) Host.Set(bagChild, "bagId", Bag);
        for (int i = 0; i < ItemLockBook.Slots; i++) Host.Push(SlotName(i));
        UpdateLockButtons();
    }

    /// <summary>UpdateLockButtons (@0x140f56e00): m_lock and m_unLock are enabled while the bag holds an item.</summary>
    void UpdateLockButtons()
    {
        if (Host?.Entity == null || Book == null) return;
        bool has = Book.HasLockedItem;
        if (Host.Child("m_lock") is { } l) Host.Set(l, "enabled", has);
        if (Host.Child("m_unLock") is { } u) Host.Set(u, "enabled", has);
    }

    // ---- callbacks ---------------------------------------------------------------------------------------------------

    /// <summary>vfunc_28 (@0x140f572e0): OnItemLock / OnItemUnLock with an item in the bag ask to confirm, OnChangeTab switches.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (Book == null) return false;
        switch (name.ToLowerInvariant())
        {
            case "onitemlock":
            case "onitemunlock":
                if (!Book.HasLockedItem) return false;
                AskConfirm();
                return true;
            case "onchangetab":
                ChangeTab(args.Length > 0 ? (int)Rt.ToUint32(args[0]) : 0);
                return true;
        }
        return false;
    }

    /// <summary>The tab 0: a box of type 0x21 (tag "itemlock_confirm") whose yes sends mode 3 (UIMessageBox::HandleItemlockConfirmButton);
    /// the tab 1: the unlock confirm (text with ItemUnLockTime / 3600 hours), type 1, whose yes sends mode 3 (OnMessage).</summary>
    void AskConfirm()
    {
        if (Book.Mode == 0)
        {
            var box = MessageBoxQueue.Create(ConfirmBoxType, "", null, ConfirmTag);
            box.Callback = (button, _) => { if (button == 0) Book.Confirm(); return true; };
            ShowData(box);
        }
        else if (Book.Mode == 1)
        {
            int hours = (int)(Model.Tables.ItemUnLockTime / 3600);
            string text = Host.Player.Translate?.Invoke("$s_itemlock_msgbox_unlock_confirm") ?? "";
            var box = MessageBoxQueue.CreateWithCallback(ClientFormat.Format(text, hours), 1,
                (button, _) => { if (button == 0) Book.Confirm(); return true; });
            ShowData(box);
        }
    }

    /// <summary>OnChangeTab: with an item in the bag the bag is dropped (mode 0 send), then SetTab(tab).</summary>
    public void ChangeTab(int tab)
    {
        if (Book.HasLockedItem) { Book.SendReset(); BindItemBag(); }
        Book.Mode = (byte)(tab == 1 ? 1 : 0);
        UpdateLockButtons();
    }

    // ---- the slots (ItemLockSlot) ------------------------------------------------------------------------------------

    public override IEnumerable<string> GetBagSlotBindings(string bag)
    {
        if (bag != Bag) return Array.Empty<string>();
        return Enumerable.Range(0, ItemLockBook.Slots).Select(SlotName);
    }

    public override object GetBinding(string name)
    {
        if (!ParseSlot(name, out int i)) return base.GetBinding(name);
        var item = ItemOf(i);
        if (item == null || Model == null) return null;
        var slot = new List<(string, object)>();
        return ItemSlot.Build(slot, item, Model.HasLocalPlayer, SlotFlags, Model, Model.ItemSlotSources) ? Host.Obj(slot.ToArray()) : null;
    }

    public override IDictionary<string, object> GetSlotTooltip(string name)
    {
        if (!ParseSlot(name, out int i)) return base.GetSlotTooltip(name);
        var item = ItemOf(i);
        var tip = new ItemTooltip(Model, TooltipFlags) { Localize = k => Host.Player.Translate?.Invoke(k) ?? k };
        return item != null && tip.Fill(item) ? tip.Data : new AsTooltip();
    }

    Item ItemOf(int slot) => Book[slot] is long uid and not 0 ? FindItem(uid) : null;

    // ---- IDragSlotOwner ----------------------------------------------------------------------------------------------

    public bool IsDragSlot(string name) => ParseSlot(name, out _);
    public void RefreshSlot(string name) { if (ParseSlot(name, out _)) Host.Push(name); }
    public void SlotDragStarted(string name) { }
    public void SlotDropUnresolved(string name) => RefreshSlot(name);

    /// <summary>ItemLockSlot::vfunc_18 (itemlockslot.cpp): an inventory item dropped on a slot is selected (FUN_140612e90); the
    /// source slot is refreshed either way. A bag slot dropped is not an item slot of the inventory and does nothing.</summary>
    public void SlotDroppedOn(string name, string source, IDragSlotOwner sourceOwner)
    {
        if (ParseSlot(name, out _) && Book != null && sourceOwner is IInvenSlotOwner inv && inv.InvenSlotUid(source) is long uid
            && inv.FindItem(uid) is { Record: { } rec })
            Book.Select(rec);
        sourceOwner?.RefreshSlot(source);
    }

    /// <summary>InvenSlot's right-click with this dialog at the head (use mode 10): the item is selected when the dialog is open.</summary>
    public void SelectFromInventory(Item item)
    {
        if (item?.Record != null && Book != null && Book.DialogOpen) Book.Select(item.Record);
    }

    public long? LockSlotUid(string name) => ParseSlot(name, out int i) && Book != null ? Book[i] : null;

    /// <summary>CItem::CanBeItemLocked (item.cpp): not already locked, not a PC bang, VIP or wedding item, and of an item type that locks.</summary>
    public static bool CanBeItemLocked(Ms2.Net.Packets.EquipItem rec, ItemData d)
    {
        if (rec == null || d == null || rec.LockState == 1 || d.PCBang || d.Vip || d.Wedding) return false;
        return d.Type is 1 or 5 or 9 or 11 or 12 or 13 or 21;
    }

    /// <summary>InvenSlot::vfunc_18's ItemLockSlot branch (invenslot.cpp:1245-1258): an item that can be locked dragged out of
    /// the bag onto an inventory slot: that slot's item replaces it when it can be locked too, else the item is removed.</summary>
    public void DroppedOnInventory(long oldUid, Item mine)
    {
        if (Book == null || FindItem(oldUid) is not { Record: { } old } || !Book.CanBeLocked(old)) return;
        if (mine?.Record != null && Book.CanBeLocked(mine.Record)) Book.Select(mine.Record);
        else Book.Remove(oldUid);
    }

    /// <summary>ItemLockSlot::vfunc_14: the slot's remove action sends mode 2 for its item.</summary>
    public void RemoveFromSlot(int slot)
    {
        if (Book != null && slot >= 0 && slot < ItemLockBook.Slots && Book[slot] != 0) Book.Remove(Book[slot]);
    }
}
