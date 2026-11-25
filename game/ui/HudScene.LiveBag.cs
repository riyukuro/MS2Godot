using System.Linq;
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

    /// <summary>The right click of the bag slot <paramref name="slot"/> (notifySlotUse with button 1, InvenSlot::vfunc_14).</summary>
    public bool BagRightClickScripted(string slot) =>
        slot != null && inventory is { IsClosed: false } && inventory.OnCallback("notifySlotUse", new object[] { slot, 1 });

    /// <summary>The open bag's slot holding the item uid on the shown tab, or with uid 0 its first empty cell below the tab's
    /// capacity; null without one.</summary>
    public string BagSlotOfUidScripted(long uid)
    {
        if (inventory is not { IsClosed: false, View: { } view }) return null;
        int cap = view.Capacity(view.Tab);
        for (int i = 0; i < cap; i++)
        {
            var e = view.ItemAt(view.Tab, i);
            if (uid == 0 ? e == null : e?.Uid == uid) return Ms2.Ui.Hud.InventoryController.SlotName(i);
        }
        return null;
    }

    /// <summary>A drag from the slot <paramref name="src"/> ended on the slot <paramref name="dst"/> (the movie's
    /// notifySlotDragEnd with a target, NotifyDragSlot::SlotDragEnd): the destination's vfunc_18 with the source.</summary>
    public bool DragEndOnScripted(string src, string dst)
    {
        if (inventory is not { IsClosed: false } || src == null || dst == null) return false;
        Ms2.Ui.NotifyDragSlot.SlotDragEnd(inventoryMovie.Host.DragSlots, new object[] { src, dst });
        return true;
    }

    /// <summary>The character window's gem bag slot of the worn gem with the item id (by its gem type); null when none is worn
    /// or the window is shut.</summary>
    public string WornGemSlotScripted(int itemId)
    {
        if (myInfo == null || myInfoMovie == null) return null;
        var gem = myInfo.LocalGems?.Invoke()?.FirstOrDefault(g => g.Id == itemId);
        int index = gem == null ? -1 : Ms2.Ui.Hud.MyInfoController.GemSlotIndex(gem.GemType);
        return index < 0 ? null : Ms2.Ui.Hud.MyInfoController.GemBag + "." + index;
    }

    /// <summary>The character window shown on its gem tab (onChangeTab 2).</summary>
    public bool CharGemTabScripted() => myInfo != null && myInfo.OnCallback("onChangeTab", new object[] { 2 });

    /// <summary>The right click of the character window's slot <paramref name="slot"/> (notifySlotUse with button 1).</summary>
    public bool CharRightClickScripted(string slot) =>
        slot != null && myInfo != null && myInfo.OnCallback("notifySlotUse", new object[] { slot, 1 });

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

    /// <summary>The open select item box's rewards (the list's rows in order) with its picks and box from the item's function, or
    /// null when the dialog is not open.</summary>
    public (System.Collections.Generic.IReadOnlyList<Ms2.Ui.Items.ItemDropEntry> Rewards, int Picks, int Box)? SelectItemBoxScripted()
    {
        if (!SelectItemBoxOpen || selectItemBox.FindItem(selectItemBox.ItemUid)?.Data is not { } d) return null;
        return (selectItemBox.State.Rewards, d.SelectItemBoxCount, d.SelectItemBoxDropBoxId);
    }

    /// <summary>A click on the select item box's list row <paramref name="row"/>: MultiSelectTileCacheList.onListItemClick selects
    /// the row's renderer, then the dialog's itemClick handler (FUN_140f9a5f0) refreshes the picks.</summary>
    public bool SelectItemBoxPickScripted(int row)
    {
        if (!SelectItemBoxOpen || selectItemBoxMovie?.Host is not { } host) return false;
        var renderer = host.Invoke(host.Child("m_selectItemList"), "getRendererAt", row);
        if (Ms2.Avm2.Rt.IsNullish(renderer)) return false;
        host.Set(renderer, "selected", true);
        selectItemBox.Refresh();
        return true;
    }

    /// <summary>The select item box's OK button (its buttonClick handler, FUN_140f9a230).</summary>
    public bool SelectItemBoxOkScripted()
    {
        if (!SelectItemBoxOpen) return false;
        selectItemBox.OnOk();
        return true;
    }

    /// <summary>The select item box's close button (BindCloseButton's handler).</summary>
    public bool SelectItemBoxCloseScripted()
    {
        if (!SelectItemBoxOpen) return false;
        selectItemBox.Close();
        return true;
    }

    /// <summary>A common string as the boxes show it.</summary>
    public string CommonText(int id) => hudModel.Tables.Common(id) ?? "";

    public long WalletMerat => hudModel.HasLocalPlayer ? Ms2.Ui.Shop.CurrencyWallet.Merat(hudModel.Player) : 0;
}
