using Godot;
using Ms2.Avm2.Flash;

namespace Ms2Godot.Ui;

/// <summary>The scripted live run's bag hooks (<see cref="Ms2Godot.Net.LiveSession.CombatRun"/>): each goes through the handler a click,
/// a drag or an answered box reaches.</summary>
public partial class HudScene
{
    /// <summary>The bag open on the tab of <paramref name="itemId"/>, and the slot name of its first stack there; null without it.</summary>
    public string BagSlotScripted(int itemId, int tab)
    {
        if (!OpenInventoryScripted() || !inventory.ShowTab(tab)) return null;
        inventory.Process();
        return inventory.SlotNameOfItem(itemId);
    }

    /// <summary>The viewport point over the open bag (its tab bar's middle), or null when it is shut.</summary>
    public Vector2? BagScreenPoint()
    {
        if (inventory is not { IsClosed: false } || inventoryMovie?.View is not { } view) return null;
        if (inventoryMovie.Host.Child("m_tab") is not AsDisplayObject tabs) return null;
        var b = tabs.GetBounds(null);
        return view.GetGlobalTransformWithCanvas() * new Vector2((float)(b.X + b.W / 2), (float)(b.Y + b.H / 2));
    }

    /// <summary>The bag's own end of a drag that left the slot <paramref name="name"/> on no slot (the movie's notifySlotDragEnd with no
    /// target, NotifyDragSlot::SlotDragEnd): the cursor's UI hit decides between the item's return and its leaving the bag.</summary>
    public bool BagDragEndScripted(string name)
    {
        if (inventory is not { IsClosed: false }) return false;
        Ms2.Ui.NotifyDragSlot.SlotDragEnd(inventoryMovie.Host.DragSlots, new object[] { name, null });
        return true;
    }

    /// <summary>The cursor's UI hit as the drag end reads it (GFxManager::FUN_1422aeed0(2)).</summary>
    public bool CursorOverUiNow() => As3View.AnyMouseOverUi(GetViewport());

    /// <summary>A box the bag showed answered as its button is (UIMessageBox FUN_1419f9410 with the clicked tag: "drop,&lt;count&gt;",
    /// "destroy_item", "expandSlot"), so it closes as it does after a click.</summary>
    public bool BagBoxAnswerScripted(Ms2.Ui.Hud.MessageBoxData box, int button, string tag)
    {
        if (box == null || MessageBoxLayer.Instance?.Controller is not { } boxes) return false;
        boxes.OnClick(button, box.Id, tag, null);
        return true;
    }

    /// <summary>The dialog's sort button (OnClickSort).</summary>
    public bool BagSortScripted() => inventory is { IsClosed: false } && inventory.OnCallback("OnClickSort", System.Array.Empty<object>());

    /// <summary>The expand button (onClickSlotExpand) with the shown tab's name; its box is answered by <see cref="BagBoxAnswerScripted"/>.</summary>
    public bool BagExpandScripted() => inventory is { IsClosed: false } && inventory.OnCallback("onClickSlotExpand", new object[] { "tab" });

    /// <summary>The search button with <paramref name="text"/> in the input (OnItemSearch with the input's text).</summary>
    public bool BagSearchScripted(string text)
    {
        if (inventory is not { IsClosed: false } || inventoryMovie.Host.Child("m_inputSearch") is not { } input) return false;
        inventoryMovie.Host.Set(input, "text", text);
        return inventory.OnCallback("OnItemSearch", new object[] { text });
    }

    /// <summary>The name the bag shows for the item in <paramref name="slot"/> (CItem::FUN_140527b10), null for none.</summary>
    public string BagItemName(string slot)
    {
        if (slot == null || inventory?.View is not { } view || !int.TryParse(slot.AsSpan(slot.LastIndexOf('.') + 1), out int i)) return null;
        return view.ItemAt(view.Tab, i) is { } e && inventory.FindItem(e.Uid) is { } item ? new Ms2.Ui.Items.ItemTooltip(hudModel, 0).Name(item) : null;
    }

    /// <summary>The slot's updateIconSearchOn as its binding has it, and m_itemBagEx's scrollPosition (-1 without it).</summary>
    public (bool Marked, int Scroll) BagSearchState(string slot)
    {
        var host = inventoryMovie.Host;
        bool marked = Ms2.Avm2.Rt.ToBoolean(host.Get(inventory.GetBinding(slot), "updateIconSearchOn"));
        int scroll = host.Child("m_itemBagEx") is { } list ? (int)Ms2.Avm2.Rt.ToNumber(host.Get(list, "scrollPosition")) : -1;
        return (marked, scroll);
    }

    System.Action<Ms2.Ui.Hud.MessageBoxData> bagBoxWatch;
    Ms2.Ui.Hud.InventoryController bagBoxWrapped;

    /// <summary>Every box the bag (opened here) shows also given to <paramref name="watch"/> (null: no longer).</summary>
    public void WatchBagBoxes(System.Action<Ms2.Ui.Hud.MessageBoxData> watch)
    {
        bagBoxWatch = watch;
        if (watch == null || !OpenInventoryScripted() || bagBoxWrapped == inventory) return;
        var show = inventory.ShowBox;
        inventory.ShowBox = b => { bagBoxWatch?.Invoke(b); show(b); };
        bagBoxWrapped = inventory;
    }

    /// <summary>The double click of the bag slot <paramref name="slot"/> (the movie's notifySlotUse with button 0,
    /// NotifyDragSlot::SlotUse).</summary>
    public bool BagUseScripted(string slot) =>
        slot != null && inventory is { IsClosed: false } && inventory.OnCallback("notifySlotUse", new object[] { slot, 0 });

    System.Action<string> bagNoticeWatch;

    /// <summary>Every notice the bag gives (a text, or a common string key) also given to <paramref name="watch"/> (null: no
    /// longer).</summary>
    public void WatchBagNotices(System.Action<string> watch)
    {
        if (inventory == null) return;
        if (bagNoticeWatch != null) { inventory.NoticeText -= bagNoticeWatch; inventory.SystemNotice -= bagNoticeWatch; }
        bagNoticeWatch = watch;
        if (watch != null) { inventory.NoticeText += watch; inventory.SystemNotice += watch; }
    }

    /// <summary>The message box on screen, null when none.</summary>
    public Ms2.Ui.Hud.MessageBoxData ShownBoxScripted() => MessageBoxLayer.Instance?.Controller?.CurrentBox;

    /// <summary>A common string as the boxes show it.</summary>
    public string CommonText(int id) => hudModel.Tables.Common(id) ?? "";

    public long WalletMerat => hudModel.HasLocalPlayer ? Ms2.Ui.Shop.CurrencyWallet.Merat(hudModel.Player) : 0;
}
