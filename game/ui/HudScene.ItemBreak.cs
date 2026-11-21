using Godot;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;

namespace Ms2Godot.Ui;

/// <summary>
/// The item dismantle: CItemBreakManager (the session's <see cref="ItemBreakManager"/>, the ItemDismantle packets 0x96) and its
/// dialogs UIItemBreakDialog (0x4e8d) and UIItemBreakDonationDialog (0x4e8e) as <see cref="ItemBreakDialogController"/>.
/// Live only: the inventory's break button opens the break dialog. STILL-OPEN: the donation dialog's opener (no caller in this
/// HUD), the break ingredient table, the skin reward calculation, the lullu box opener, the dock beside the inventory.
/// </summary>
public partial class HudScene
{
    ItemBreakDialogController itemBreak;     // from open until the close
    MovieDialog itemBreakMovie;              // the movie, from open until the unload

    bool ItemBreakOpen => itemBreak != null && !itemBreak.IsClosed;

    /// <summary>CUIService::ToggleDialog(0x4e8d / 0x4e8e): one of the two at a time (IsBreakDialogOpen).</summary>
    void ToggleItemBreak(bool donation)
    {
        if (ItemBreakOpen) { bool same = itemBreak.Id == (donation ? ItemBreakDialogController.DonationUiId : ItemBreakDialogController.UiId); itemBreak.Close(); if (same) return; }
        if (Ms2Godot.Net.LiveSession.Instance is not { } live) { GD.Print("item break: needs the live session"); return; }
        if (ItemLockOpen) { GD.Print("item break: refused while the item lock dialog is open"); return; }
        if (itemBreakMovie != null) { itemBreakMovie.Host.Unload(); itemBreakMovie.QueueFree(); itemBreakMovie = null; }
        var mgr = live.BreakManager;
        mgr.Find = uid => live.Items.Get(uid) is { } e ? InventoryController.ToItem(e, hudModel.Tables.Items) : null;
        mgr.Send = p => live.SendItemDismantle(p);
        mgr.Text = key => hudModel.Tables.Common(key) ?? key;
        mgr.Common = id => hudModel.Tables.Common(id) ?? "";
        mgr.ServerTime = () => live.GameSession?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        mgr.NoticeKey = SystemNotice;
        mgr.NoticeId = (id, arg) =>
        {
            string text = hudModel.Tables.Common(id);
            SystemNoticeText(arg == int.MinValue ? text : ClientFormat.Format(text, arg));
        };
        mgr.ShowBox = MessageBoxQueue.Show;
        mgr.InputText = d => MessageBoxLayer.Instance?.Controller?.InputText(d) ?? "";
        mgr.Sound = key => FieldAudio.Instance?.PlayUi(key);
        mgr.ItemName = item => hudModel.Tables.ItemName(item.ItemId) ?? "";
        mgr.CanUseItem = item => ItemTradeRules.NotLocked(item, mgr.ServerTime(), out _);
        mgr.SkinFeature = () => hudModel.IsFeatureEnabled(0x3a);
        var c = new ItemBreakDialogController(donation) { Mgr = mgr };
        void OnPacket(ItemDismantlePacket p) => mgr.OnPacket(p);
        live.ItemDismantleReceived += OnPacket;
        c.Sound += key => FieldAudio.Instance?.PlayUi(key);
        c.Unloaded += () =>
        {
            live.ItemDismantleReceived -= OnPacket;
            itemBreakMovie?.Host.Unload(); itemBreakMovie?.QueueFree(); itemBreakMovie = null;
        };
        itemBreak = c;
        BindBoxAnswers();
        itemBreakMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"item break: load={itemBreakMovie.Loaded}");
        uiRoot.AddChild(itemBreakMovie);
    }

    /// <summary>UIItemBreakDialog's key handling, as UIDialog's: Escape closes it.</summary>
    bool ItemBreakKeys(InputEventKey k)
    {
        if (!ItemBreakOpen || k.Keycode != Key.Escape) return false;
        itemBreak.Close();
        return true;
    }
}
