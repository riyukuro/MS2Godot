using Godot;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;

namespace Ms2Godot.Ui;

/// <summary>
/// The merge revert window: UIItemMergeRevertDialog (0x4f3d) as <see cref="ItemMergeRevertDialogController"/>, opened by using an
/// "ItemMergeRevert" scroll and answered by ItemMerge mode 6. Live only. STILL-OPEN: the revert price (a game Lua function), the
/// dock beside the inventory.
/// </summary>
public partial class HudScene
{
    ItemMergeRevertDialogController itemRevert;  // from open until the close
    MovieDialog itemRevertMovie;                 // the movie, from open until the unload

    bool ItemRevertOpen => itemRevert != null && !itemRevert.IsClosed;

    /// <summary>ItemMergeRevert (@0x140d21480): already open returns; else the scroll is the dialog's item and it is opened.</summary>
    void OpenItemRevert(Item scroll)
    {
        if (ItemRevertOpen) return;
        if (Ms2Godot.Net.LiveSession.Instance is not { } live) { GD.Print("item merge revert: needs the live session"); return; }
        if (itemRevertMovie != null) { itemRevertMovie.Host.Unload(); itemRevertMovie.QueueFree(); itemRevertMovie = null; }
        var tables = hudModel.Tables;
        Item ItemOf(EquipItem e) => e == null ? null : InventoryController.ToItem(e, tables.Items);
        var c = new ItemMergeRevertDialogController
        {
            Send = p => live.SendItemMerge(p),
            Find = uid => ItemOf(live.Items.Get(uid)),
            Notice = SystemNoticeText,
            DoneNotice = SystemNoticeText,
            Text = key => tables.Common(key) ?? key,
            ItemName = item => tables.ItemName(item.ItemId) ?? "",
        };
        c.SetItemUid(scroll);
        c.Unloaded += () =>
        {
            itemRevertMovie?.Host.Unload(); itemRevertMovie?.QueueFree(); itemRevertMovie = null;
        };
        itemRevert = c;
        BindBoxAnswers();
        itemRevertMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"item merge revert: load={itemRevertMovie.Loaded}");
        uiRoot.AddChild(itemRevertMovie);
    }
}
