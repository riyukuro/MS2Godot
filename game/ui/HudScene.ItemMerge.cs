using Godot;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;

namespace Ms2Godot.Ui;

/// <summary>
/// The item merge window: UIItemMergeOptionDialog (0x4e68) as <see cref="ItemMergeOptionDialogController"/>, fed by the ItemMerge
/// packets (0x74). Modes 0, 1, 2 and 4 go to the dialog; mode 5 reads the item body into the inventory item of the uid and shows
/// the result (or the error), as CSceneStateGame::OnItemFusion does. Live only. STILL-OPEN: the NPC dialog that opens it (0x4e40,
/// no caller in this HUD), the revert dialog 0x4f3d (mode 6 only refreshes the item), the equipment refresh after a result.
/// </summary>
public partial class HudScene
{
    ItemMergeOptionDialogController itemMerge;   // from open until the close
    MovieDialog itemMergeMovie;                  // the movie, from open until the unload

    bool ItemMergeOpen => itemMerge != null && !itemMerge.IsClosed;

    /// <summary>CUIService::ToggleDialog(0x4e68).</summary>
    void ToggleItemMerge()
    {
        if (ItemMergeOpen) { itemMerge.Close(); return; }
        if (Ms2Godot.Net.LiveSession.Instance is not { } live) { GD.Print("item merge: needs the live session"); return; }
        if (itemMergeMovie != null) { itemMergeMovie.Host.Unload(); itemMergeMovie.QueueFree(); itemMergeMovie = null; }
        var tables = hudModel.Tables;
        EquipItem Raw(long uid) => live.Items.Get(uid) ?? live.Me.Equips?.FirstOrDefault(e => e.Uid == uid);
        Item ItemOf(EquipItem e) => e == null ? null : InventoryController.ToItem(e, tables.Items);
        var c = new ItemMergeOptionDialogController
        {
            Send = p => live.SendItemMerge(p),
            Find = uid => ItemOf(Raw(uid)),
            BagItems = () => live.Items.All.Where(e => live.Items.TabOf?.Invoke(e.Id) == 0).Select(ItemOf).Where(i => i != null),
            EquippedItems = () => (live.Me.Equips ?? Array.Empty<EquipItem>()).Select(ItemOf).Where(i => i != null),
            Meso = () => hudModel.HasLocalPlayer ? hudModel.Player.Mesos : 0,
            CountById = id => live.Items.All.Where(e => e.Id == id).Sum(e => e.Amount),
            CountByTag = tag => (int)Ms2.Ui.Shop.ShopController.CountItemsByTag(live.Items.All, tables.Items, tag),
            Notice = SystemNoticeText,
            MissingBox = text => MessageBoxQueue.Show(MessageBoxQueue.Create(0, text, null, "")),
            ChatLine = text => ChatLog.AddSystemLine(text, TalkKind.SystemNotice),
            Text = key => tables.Common(key) ?? key,
            ItemName = id => tables.ItemName(id) ?? "",
        };
        void OnPacket(ItemMergePacket p, Maple2.PacketLib.Tools.ByteReader r)
        {
            if (p.Mode == 5)
            {
                if (p.Error != 0) { c.ShowMergeError(p.Error); return; }
                if (Raw(p.Uid) is { } e) { ItemReader.ReadBodyInto(r, e); c.ShowMergeResult(ItemOf(e)); }
            }
            else if (p.Mode == 6) { if (Raw(p.Uid) is { } e) ItemReader.ReadBodyInto(r, e); }
            else c.OnPacket(p);
        }
        live.ItemMergeReceived += OnPacket;
        c.Unloaded += () =>
        {
            live.ItemMergeReceived -= OnPacket;
            itemMergeMovie?.Host.Unload(); itemMergeMovie?.QueueFree(); itemMergeMovie = null;
        };
        itemMerge = c;
        BindBoxAnswers();
        itemMergeMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"item merge: load={itemMergeMovie.Loaded}");
        uiRoot.AddChild(itemMergeMovie);
    }

    /// <summary>UIItemMergeOptionDialog::vfunc_13: Escape closes it except while it waits for the answer or merges.</summary>
    bool ItemMergeKeys(InputEventKey k)
    {
        if (!ItemMergeOpen || k.Keycode != Key.Escape) return false;
        if (itemMerge.CanClose) itemMerge.Close();
        return true;
    }
}
