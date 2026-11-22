using System;
using System.Linq;
using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;

namespace Ms2Godot.Ui;

/// <summary>
/// The beauty shop dialog UIBeautyShopDialog (0x4eee) with its C++ side as <see cref="BeautyShopDialogController"/>. Live only:
/// the beauty item's use (ItemUse mode 6, <see cref="Ms2Godot.Net.LiveSession.BeautyRequested"/>) opens it and the shop packets
/// (inbound 0x31) go to it while it is open, as the client's handler does. STILL-OPEN: the open from the NPC talk (the enter
/// request's reader side), the own-avatar Customize and the style dialog (step 3), the price text and payment check
/// (GetFeePriceText, CanPayFee), the bill dialog's beauty kind.
/// </summary>
public partial class HudScene
{
    readonly BeautyShopFlow beautyFlow = new();
    readonly BeautyStyleFlow beautyStyleFlow = new();
    readonly ItemColoringFlow itemColoringFlow = new();
    MovieDialog beautyMovie, beautyStyleMovie, itemColoringMovie;
    long beautyPreviewUid;
    BeautyTalkCommand beautyTalk;
    Action unwatchBeauty;

    bool BeautyOpen => beautyFlow.IsOpen;

    /// <summary>Subscribes once per scene and unsubscribes with it (the LiveSession outlives a field reload's HUD).</summary>
    void WatchBeauty()
    {
        if (unwatchBeauty != null || Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        beautyFlow.CreateController = () => CreateBeauty(live);
        beautyFlow.Show = ShowBeauty;
        beautyFlow.Log = m => GD.Print(m);
        beautyFlow.CreateCustomize = () => CreateBeautyCustomize(live);
        beautyFlow.SendOpenRequest = uid => live.SendBeauty(new BeautyOpenRequest(uid));   // SendBeauty (scenestategame.cpp:8714)
        beautyTalk = new BeautyTalkCommand
        {
            Shop = beautyFlow, Style = beautyStyleFlow, Coloring = itemColoringFlow, Log = m => GD.Print(m),
            TalkNpcId = () => live.TalkNpcId, Send = live.SendBeauty, EndTalk = () => live.EndTalk(),   // SendNpcTalkMode0
        };
        void OnRequested(BeautyReq req) => beautyFlow.OpenFor(req);
        beautyStyleFlow.CreateController = () => CreateBeautyStyle(live);
        beautyStyleFlow.Show = ShowBeautyStyle;
        beautyStyleFlow.Log = m => GD.Print(m);
        beautyStyleFlow.CreateCustomize = () => CreateBeautyCustomize(live);
        itemColoringFlow.CreateController = () => CreateItemColoring(live);
        itemColoringFlow.Show = ShowItemColoring;
        itemColoringFlow.Log = m => GD.Print(m);
        itemColoringFlow.CreateCustomize = () => CreateItemColoringCustomize(live);
        void OnBeautyPacket(byte[] body) => BeautyPacketRouter.Route(body, beautyFlow, beautyStyleFlow, m => GD.Print(m), itemColoringFlow);
        void OnOwnItem(int itemId) => itemColoringFlow.Current?.OnItemDyed(itemId);   // vfunc_24 of the item event listeners
        live.BeautyRequested += OnRequested;
        live.OwnItemUpdated += OnOwnItem;
        live.BeautyPacketReceived += OnBeautyPacket;
        unwatchBeauty = () =>
        {
            live.BeautyRequested -= OnRequested;
            live.OwnItemUpdated -= OnOwnItem;
            live.BeautyPacketReceived -= OnBeautyPacket;
            beautyFlow.Shutdown();
            beautyStyleFlow.Shutdown();
            itemColoringFlow.Shutdown();
            beautyTalk = null;
            unwatchBeauty = null;
        };
    }

    // STILL-OPEN: FeePriceText / CanPay / NotEnough (GetFeePriceText, CanPayFee, FUN_14134a2e0 are not recovered), OpenBill (the
    // UIBillDialog beauty kind) and SlotOfKind (FUN_140f01070) stay unset, so the paths that need them do nothing.
    BeautyShopDialogController CreateBeauty(Ms2Godot.Net.LiveSession live)
    {
        var c = new BeautyShopDialogController
        {
            Sales = live.BeautySales,
            PlayerLevel = () => live.Me.User?.Level ?? 0,
            Send = live.SendBeauty,
        };
        c.NoticeText += SystemNoticeText;
        return c;
    }

    /// <summary>
    /// The own-avatar Customize over the live own player (<see cref="BeautyOwnAvatar"/>): the preview dresses the same lists the
    /// 3D avatar copy redraws from. Null without a worn item (CloneItems::Init @0x140ccf010 fails), so the flows stay shut as for
    /// no Customize. STILL-OPEN: the packets the Customize sends (Equipment::SendColors / SendAdded / SendModified / SendRemoved)
    /// need the free-try byte, the sale context and CItem::WriteItemBody, none recovered, so they stay unset and the commit does
    /// not send them; the worn-pair slot of a hand item (FUN_141420b30) stays unset, so such an item's slot is unknown and its
    /// preview does nothing.
    /// </summary>
    BeautyCustomize CreateBeautyCustomize(Ms2Godot.Net.LiveSession live)
    {
        if (live.Me.User == null || live.Me.Equips == null || live.Me.Equips.Count + (live.Me.Outfit2?.Count ?? 0) == 0) return null;
        var tables = hudModel.Tables;
        var slots = new BeautyItemSlots(tables.Items.Get, tables.ItemModels.Get);
        return new BeautyCustomize(new BeautyOwnAvatar(live.Me, Ms2Godot.Net.LiveSession.WornLayer))
        {
            IsTopBottom = i => slots.IsTopBottom(tables.Items.Get(i.Id)),
            IsTwoHanded = i => slots.IsTwoHanded(tables.Items.Get(i.Id)),
            SlotOfId = slots.SlotOfId,
            NewItem = slots.NewItem,
            HasItemData = i => tables.Items.Get(i) != null,
            NextUid = () => --beautyPreviewUid,   // GenerateUniqueId is not recovered: a preview uid never meets a real one
        };
    }

    /// <summary>
    /// The item colour dialog's Customize (<see cref="ItemColoringOwnCustomize"/>) over the same own-avatar Customize as the shop; null
    /// without a worn item. STILL-OPEN: see <see cref="ItemColoringOwnCustomize"/>.
    /// </summary>
    IItemColoringCustomize CreateItemColoringCustomize(Ms2Godot.Net.LiveSession live)
    {
        var cust = CreateBeautyCustomize(live);
        if (cust == null) return null;
        var tables = hudModel.Tables;
        return new ItemColoringOwnCustomize(cust, tables.ColorPalettes, id =>
        {
            var d = tables.Items.Get(id);
            return d != null && d.ItemPreset != 0 && tables.ItemModels.Get(d.ItemPreset)?.FirstSlotType == 2;   // FUN_14053d020
        });
    }

    void ShowBeauty(BeautyShopDialogController c)
    {
        if (beautyMovie != null) { beautyMovie.Host.Unload(); beautyMovie.QueueFree(); beautyMovie = null; }
        var m = beautyMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () => { m.Host.Unload(); m.QueueFree(); if (beautyMovie == m) beautyMovie = null; };
        GD.Print($"beauty shop: load={m.Loaded}");
        uiRoot.AddChild(m);
    }

    /// <summary>The saved-styles dialog's controller with the host-set parts. The NPC talk's "BeautyStyleDialog"
    /// action opens it (<see cref="BeautyTalkCommand"/>, uitalkcinematic.cpp:1190). STILL-OPEN: the payment's "not enough" box and the hair apply effect are unset, so the paths that need them do nothing.</summary>
    BeautyStyleDialogController CreateBeautyStyle(Ms2Godot.Net.LiveSession live)
    {
        var c = new BeautyStyleDialogController { Send = live.SendBeauty };
        c.ApplyTables(hudModel.Tables);
        c.NoticeText += (text, _) => SystemNoticeText(text);   // STILL-OPEN: what the type argument of FUN_1412752d0 changes
        return c;
    }

    void ShowBeautyStyle(BeautyStyleDialogController c)
    {
        if (beautyStyleMovie != null) { beautyStyleMovie.Host.Unload(); beautyStyleMovie.QueueFree(); beautyStyleMovie = null; }
        var m = beautyStyleMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () => { m.Host.Unload(); m.QueueFree(); if (beautyStyleMovie == m) beautyStyleMovie = null; };
        GD.Print($"beauty style: load={m.Loaded}");
        uiRoot.AddChild(m);
    }

    /// <summary>The item colour dialog's controller with the host-set parts. STILL-OPEN: see <see cref="ItemColoringDialogController"/>.</summary>
    ItemColoringDialogController CreateItemColoring(Ms2Godot.Net.LiveSession live)
    {
        var c = new ItemColoringDialogController
        {
            Sales = live.BeautySales,
            PlayerLevel = () => live.Me.User?.Level ?? 0,
            Send = live.SendBeauty,
            ItemCount = id => live.Items.All.Where(e => e.Id == id).Sum(e => (long)e.Amount),   // same sum as the shop's BindLiveState
            GuildFund = () => live.Guilds?.Info?.Fund ?? 0,
            ItemName = id => hudModel.Tables.ItemName(id) ?? "",
            EquippedRows = () => OwnColoringRows(live),
        };
        c.NoticeText += SystemNoticeText;
        return c;
    }

    /// <summary>
    /// BuildItems' worn-item list over the live own player (<see cref="ItemColoringDialogController.WornRows"/>); the model test
    /// (CEquipItem::FUN_1411dcda0) scans the item's models for a shader-textured mesh (<see cref="Ms2Godot.Net.EquipApplier.HasModelTexture"/>).
    /// </summary>
    IReadOnlyList<ColoringRow> OwnColoringRows(Ms2Godot.Net.LiveSession live)
    {
        var t = hudModel.Tables;
        long now = live.GameSession?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var record = live.Me.ToPcInfo(now);
        return ItemColoringDialogController.WornRows(WornEquipment.Of(record, t.Items), record.Gender, record.CodiActive,
            hudModel.IsFeatureEnabled(0x8e), it => ItemLevels.Usable(it, now),
            it => ColoringModels().HasModelTexture(it.ItemId, record.Gender), id => hudModel.Tables.ItemName(id) ?? "", t.Items, t.ItemModels);
    }

    Ms2Godot.Net.EquipApplier coloringModels;

    /// <summary>The item models the colouring list scans, loaded once and kept (the applier caches each file).</summary>
    Ms2Godot.Net.EquipApplier ColoringModels() => coloringModels ??=
        new Ms2Godot.Net.EquipApplier(assets.Packs, new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), assets.Packs));

    void ShowItemColoring(ItemColoringDialogController c)
    {
        if (itemColoringMovie != null) { itemColoringMovie.Host.Unload(); itemColoringMovie.QueueFree(); itemColoringMovie = null; }
        var m = itemColoringMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () => { m.Host.Unload(); m.QueueFree(); if (itemColoringMovie == m) itemColoringMovie = null; };
        GD.Print($"item coloring: load={m.Loaded}");
        uiRoot.AddChild(m);
    }
}
