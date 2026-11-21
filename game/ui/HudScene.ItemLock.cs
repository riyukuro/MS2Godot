using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The item lock: CItemLockManager (the session's <see cref="ItemLockBook"/>, the ItemLock packets 0xDB) and its dialog
/// UIItemLockDialog (0x4f08) with its C++ side as <see cref="ItemLockDialogController"/>. Live only: the inventory's lock
/// button opens it. STILL-OPEN: the inventory slot redraw on the lock packets, the dialog's tab control, the black
/// market's CanUseItem test (invenslot.cpp:1357/1410; no black market in this client). The shop sell and the trade add run
/// CanUseItem already (ItemTradeRules.NotLocked in ShopController.CanSellItem and InventoryController.TryAddToTrade).
/// </summary>
public partial class HudScene
{
    ItemLockDialogController itemLock;       // from open until the close
    MovieDialog itemLockMovie;               // the movie, from open until the unload

    bool ItemLockOpen => itemLock != null && !itemLock.IsClosed;

    /// <summary>CUIService::ToggleDialog(0x4f08).</summary>
    void ToggleItemLock()
    {
        if (ItemLockOpen) { itemLock.Close(); return; }
        if (Ms2Godot.Net.LiveSession.Instance is not { } live) { GD.Print("item lock: needs the live session"); return; }
        if (itemLockMovie != null) { itemLockMovie.Host.Unload(); itemLockMovie.QueueFree(); itemLockMovie = null; }
        var book = live.LockBook;
        book.Send = live.SendLock;
        book.Find = live.Items.Get;
        book.CanBeLocked = rec => ItemLockDialogController.CanBeItemLocked(rec, hudModel.Tables.Items.Get(rec.Id));
        var c = new ItemLockDialogController
        {
            Book = book,
            Now = () => live.GameSession?.ServerTime ?? 0,
            FindItem = uid => live.Items.Get(uid) is { } e ? InventoryController.ToItem(e, hudModel.Tables.Items) : null,
        };
        c.NoticeText += SystemNoticeText;
        c.InventoryButtons += InventoryButtonsAndFlags;
        c.InventorySlotRefresh += uid => inventory?.RefreshItemSlot(uid);
        c.Unloaded += () =>
        {
            itemLockMovie?.Host.Unload(); itemLockMovie?.QueueFree(); itemLockMovie = null;
        };
        itemLock = c;
        itemLockMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"item lock: load={itemLockMovie.Loaded}");
        uiRoot.AddChild(itemLockMovie);
    }

    /// <summary>UIItemLockDialog's key handling, as UIDialog's: Escape closes it.</summary>
    bool ItemLockKeys(InputEventKey k)
    {
        if (!ItemLockOpen || k.Keycode != Key.Escape) return false;
        itemLock.Close();
        return true;
    }
}
