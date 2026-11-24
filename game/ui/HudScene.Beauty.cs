using System;
using System.Linq;
using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using Ms2.Ui.Shop;

namespace Ms2Godot.Ui;

/// <summary>
/// The beauty shop dialog UIBeautyShopDialog (0x4eee) with its C++ side as <see cref="BeautyShopDialogController"/>. Live only:
/// the beauty item's use (ItemUse mode 6, <see cref="Ms2Godot.Net.LiveSession.BeautyRequested"/>) opens it and the shop packets
/// (inbound 0x31) go to it while it is open, as the client's handler does. STILL-OPEN: the open from the NPC talk (the enter
/// request's reader side), the own-avatar Customize and the style dialog (step 3). The price text is GetFeePriceText @0x14134a220;
/// CanPayFee @0x14134a1d0 is wired and the bill is modal over the shop. STILL-OPEN: no buy is made yet. The items, colour and skin pages commit through the
/// Customize's sends (SendColors, SendAdded, SendModified), and the special hair page's mode-7 buy needs the 0xb answer (the keep-old or keep-new choice);
/// none is ported, so <see cref="BeautyShopDialogController.CommitSendsPorted"/> and <see cref="BeautyShopDialogController.StyleSelectPorted"/> stay false and a bill's OK does nothing.
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
        void OnBeautyPacket(byte[] body) => BeautyPacketRouter.Route(body, beautyFlow, beautyStyleFlow, m => GD.Print(m), itemColoringFlow, live.GameProfile);
        void OnOwnItem(int itemId)   // vfunc_24 of the item event listeners
        {
            itemColoringFlow.Current?.OnItemDyed(itemId);
            beautyFlow.Current?.OnItemEvent();   // vfunc_24 (uibeautyshopdialog.cpp:896-925)
        }
        // OnItemPutOn (scenestategame.cpp:3112-3123): with the beauty shop's sub scene the hub's vfunc_20 runs for slots 1, 3 and 2 only
        void OnOwnPutOn(Ms2.Net.Packets.EquipItem item)
        {
            if (beautyFlow.Current is { } shop && (int)item.Slot is 1 or 2 or 3) shop.OnItemEvent(item.Id, ownEquip: true);
        }
        void OnOwnSkinColor() => beautyFlow.Current?.OnItemEvent();   // hub slot 0 -> vfunc_25 (uibeautyshopdialog.cpp:896-925), no item stored
        live.BeautyRequested += OnRequested;
        live.OwnItemUpdated += OnOwnItem;
        live.OwnSkinColor += OnOwnSkinColor;
        live.Me.OwnItemPutOn += OnOwnPutOn;
        live.BeautyPacketReceived += OnBeautyPacket;
        unwatchBeauty = () =>
        {
            live.BeautyRequested -= OnRequested;
            live.OwnItemUpdated -= OnOwnItem;
            live.OwnSkinColor -= OnOwnSkinColor;
            live.Me.OwnItemPutOn -= OnOwnPutOn;
            live.BeautyPacketReceived -= OnBeautyPacket;
            beautyFlow.Shutdown();
            beautyStyleFlow.Shutdown();
            itemColoringFlow.Shutdown();
            beautyTalk = null;
            unwatchBeauty = null;
        };
    }

    // SlotOfKind (FUN_140f01070) is the controller's default.
    BeautyShopDialogController CreateBeauty(Ms2Godot.Net.LiveSession live)
    {
        BeautyShopDialogController cRef = null;
        var c = cRef = new BeautyShopDialogController
        {
            Sales = live.BeautySales,
            PlayerLevel = () => live.Me.User?.Level ?? 0,
            Send = live.SendBeauty,
            FeePriceText = (fee, amount) => ColoringShop(live).PriceText(fee.Type, fee.ItemId, (uint)amount, fee.IconTag),   // GetFeePriceText @0x14134a220
            NotEnough = fee => ColoringShop(live).ShowLackCurrency(fee.Type, fee.ItemId, 0),                          // FUN_14134a2e0 @0x14134a2e0
            // CanPayFee @0x14134a1d0 (unit_141347b10.cpp:574-620): the wallet of the fee's currency against the unsigned price
            CanPay = (fee, amount) => ColoringShop(live).CanPayFee(fee.Type, fee.ItemId, amount),
            // PC::FUN_141655e10 (pc.cpp:1364, @0x141655e10) = CEquipment::FUN_1411e1860(slot, 6, 0) (equipment.cpp:820-834): layers 5 down to 0,
            // the first item the player can use (PC::CanUseItem). Inferred, not recovered: the second outfit before the gear is the higher layer
            // winning, and the last item of each list as the top one. STILL-OPEN: the CanUseItem filter. An empty slot is 0 here as the port's
            // choice (the client dereferences the null item at uibeautyshopdialog.cpp:1848); no local player = null, as the `if (pPc)` guard at :1845.
            WornItemId = slot => live.Me.User == null ? null
                : (live.Me.Outfit2?.LastOrDefault(e => (int)e.Slot == slot) ?? live.Me.Equips?.LastOrDefault(e => (int)e.Slot == slot))?.Id ?? 0,
            // WriteItemInfo @0x140f324f0 (charactercustomizepayloadwriters.cpp:128-146). STILL-OPEN: "editable" (the model head's rotation,
            // translation and scale) has no source in the port's item model table, so it is not sent; "itemmovie" is empty as with bMovie false.
            ItemInfo = id =>
            {
                var t = hudModel.Tables;
                var d = t.Items.Get(id);
                if (d == null) return null;
                int group = t.ItemModels.Get(d.ItemPreset)?.FirstSlotType ?? 0;
                return new (string, object)[]
                {
                    ("itemname", t.ItemName(id)), ("itemid", id.ToString()), ("iconpath", Ms2.Net.Packets.ItemIcon.Path(id, d.SlotIcon)),
                    ("isSkinBg", group is 2 or 3), ("isHideSlot", id == WornEquipment.AvatarColorItemId), ("itemmovie", ""),
                };
            },
            HasItemData = id => hudModel.Tables.Items.Get(id) != null,   // FUN_14053f870
            // FUN_1418c7fe0 (uibeautyshopdialog.cpp): Customize::EquipItem @0x140cd1c90 (customize.cpp:1668) checks the gender, then equips.
            // STILL-OPEN: the client equips the kept CItem with its colours; the port holds ids only, so the preview has the default colours.
            EquipPreview = id =>
            {
                var d = hudModel.Tables.Items.Get(id);
                if (d == null || live.Me.User == null || !d.IsGenderAllowed(live.Me.User.Gender)) return;
                if (cRef.Customize is not BeautyCustomize cust) return;
                var item = cust.CreateItem(id);
                if (item != null) cust.Apply(item, cRef.SlotOfKind(cRef.ShopKind));
            },
            OpenBill = data => ShowBill(live, data),
            BillIsOpen = () => BillOpen,
            CloseBill = () => { if (BillOpen) billDialog.Close(); },
            UpdateBill = price => { if (BillOpen) billDialog.UpdatePrice(price); },
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
        return new BeautyCustomize(new BeautyOwnAvatar(live.Me, Ms2Godot.Net.LiveSession.WornLayer,
            id => WornEquipment.IsCustomizeKind(tables.Items.Get(id), tables.ItemModels)))
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
        m.View.Blocked = () => BillOpen || c.InputOff;   // the bill is modal (UIModalDialog vfunc_8 @0x141a55380): no mouse or key reaches the shop; after a buy the root's input stays off until the answer (:1860)
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
            FeePriceText = (fee, amount) => ColoringShop(live).PriceText(fee.Type, fee.ItemId, (uint)amount, fee.IconTag),   // GetFeePriceText @0x14134a220
            NotEnough = fee => ColoringShop(live).ShowLackCurrency(fee.Type, fee.ItemId, 0),                          // FUN_14134a2e0 @0x14134a2e0
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

    ShopController ColoringShop(Ms2Godot.Net.LiveSession live) => liveShop ??= CreateShopController(live);

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
