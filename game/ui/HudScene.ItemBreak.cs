using System;
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
/// Live only: the inventory's break button opens the break dialog, and a UI interact object opens either
/// (<see cref="OpenInteractDialog"/>). STILL-OPEN: the break ingredient table, the skin reward calculation, the lullu box opener, the dock beside the inventory, the
/// extraction scroll dialogs' states (0x4f49, 0x4f4a) in the inventory's button state.
/// </summary>
public partial class HudScene
{
    ItemBreakDialogController itemBreak;     // from open until the close
    MovieDialog itemBreakMovie;              // the movie, from open until the unload

    bool ItemBreakOpen => itemBreak != null && !itemBreak.IsClosed;

    /// <summary>
    /// A UI interact object's dialog (CInteractUIObject::vfunc_2, interactobject.cpp:1045-1056): GetUISwfEntity finds it by
    /// its type name, trimmed, without case (uiswfentitymanager.cpp:693-727), a non-empty value is given to it (m_170) and
    /// CUIService::OpenDialog opens it (an open one stays). The swf names the interact rows use are ItemBreakDialog,
    /// ItemBreakDonationDialog, WorldmapDialog with "missionAttack", BlueMarbleGameDialog, CardReverseGameDialog and
    /// WeddingBillboardDialog. Returns its ui class id, or 0 when nothing opens.
    /// STILL-OPEN: the world map's mission attack mode (m_214 from m_170 "missionAttack", uiworldmapdialog.cpp:393-396, with
    /// CQuestManager's mission attack fields and SendQuestMode15) and the blue marble, card reverse and wedding billboard
    /// dialogs are not ported: those names open nothing.
    /// </summary>
    int OpenInteractDialog(string swf, string value)
    {
        string name = (swf ?? "").Trim();
        bool donation = name.Equals("ItemBreakDonationDialog", StringComparison.OrdinalIgnoreCase);
        if (donation || name.Equals("ItemBreakDialog", StringComparison.OrdinalIgnoreCase))
        {
            int id = donation ? ItemBreakDialogController.DonationUiId : ItemBreakDialogController.UiId;
            if (!(ItemBreakOpen && itemBreak.Id == id)) ToggleItemBreak(donation);
            return ItemBreakOpen && itemBreak.Id == id ? id : 0;
        }
        NotApplied.Print($"interact dialog '{name}' value '{value}': not ported (STILL-OPEN)");
        return 0;
    }

    bool InteractDialogClosed(int id) =>
        id is not (ItemBreakDialogController.UiId or ItemBreakDialogController.DonationUiId) || !ItemBreakOpen || itemBreak.Id != id;

    void CloseInteractDialog(int id)
    {
        if (!InteractDialogClosed(id)) itemBreak.Close();
    }

    /// <summary>CUIService::ToggleDialog(0x4e8d / 0x4e8e): one of the two at a time (IsBreakDialogOpen).</summary>
    void ToggleItemBreak(bool donation)
    {
        if (ItemBreakOpen) { bool same = itemBreak.Id == (donation ? ItemBreakDialogController.DonationUiId : ItemBreakDialogController.UiId); itemBreak.Close(); if (same) return; }
        if (Ms2Godot.Net.LiveSession.Instance is not { } live) { GD.Print("item break: needs the live session"); return; }
        if (ItemLockOpen) { GD.Print("item break: refused while the item lock dialog is open"); return; }
        if (itemBreakMovie != null) { itemBreakMovie.Host.Unload(); itemBreakMovie.QueueFree(); itemBreakMovie = null; }
        var mgr = live.BreakManager;
        mgr.Find = uid => live.Items.Get(uid) is { } e ? InventoryController.ToItem(e, hudModel.Tables.Items) : null;
        mgr.Exists = uid => live.Items.Get(uid) != null;      // RemoveMissingItems runs per frame: no Item built
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
        c.InventoryButtons += InventoryButtonsAndFlags;
        c.InventoryReset += () => { if (inventory is { IsClosed: false }) inventory.AddFlags(); };
        c.InventorySlotRefresh += uid => inventory?.RefreshItemSlot(uid);
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

    /// <summary>--uigen-open itembreak: the dialog on a local manager with one item in the bag, for the gallery (no server).</summary>
    void OpenItemBreakShot()
    {
        int id = new[] { 11300001, 12000001, 20000001 }.FirstOrDefault(i => !string.IsNullOrEmpty(hudModel.Tables.ItemName(i)));
        if (id == 0) { GD.Print("item break shot: no item"); return; }
        var item = Item.Create(hudModel.Tables.Items, id, 3);
        item.Uid = 1; item.Rarity = 3;
        item.Record = new EquipItem { Id = id, Uid = 1 };
        var mgr = new ItemBreakManager { Send = _ => { }, Find = u => u == 1 ? item : null, ShowBox = MessageBoxQueue.Show };
        mgr.Text = key => hudModel.Tables.Common(key) ?? key;
        mgr.ItemName = i => hudModel.Tables.ItemName(i.ItemId) ?? "";
        var c = new ItemBreakDialogController(false) { Mgr = mgr };
        c.Unloaded += () => { itemBreakMovie?.Host.Unload(); itemBreakMovie?.QueueFree(); itemBreakMovie = null; };
        itemBreak = c;
        itemBreakMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        uiRoot.AddChild(itemBreakMovie);
        mgr.OnPacket(new ItemDismantlePacket(1) { Uid = 1, Slots = new[] { new DismantleSlot(0, 1, 1) } });
    }

    /// <summary>The item break and lock dialogs' load and unload (uiinventorydialog.cpp:2340-2362, uiitemlockdialog.cpp:160-185):
    /// the inventory's UpdateButtonState, then FUN_141999e00 (m_19c = 7).</summary>
    void InventoryButtonsAndFlags()
    {
        inventory?.ItemDialogChanged();
    }

    /// <summary>UIItemBreakDialog's key handling, as UIDialog's: Escape closes it. STILL-OPEN: UIDialog's key handler also
    /// returns early on vfunc_56 (uidialog.cpp:846), whose condition is not recovered; it is not tested here.</summary>
    bool ItemBreakKeys(InputEventKey k)
    {
        if (!ItemBreakOpen || k.Keycode != Key.Escape) return false;
        itemBreak.Close();
        return true;
    }
}
