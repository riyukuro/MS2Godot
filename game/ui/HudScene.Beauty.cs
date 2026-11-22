using System;
using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;

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
    MovieDialog beautyMovie, beautyStyleMovie;
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
        void OnRequested(BeautyReq req) => beautyFlow.OpenFor(req);
        beautyStyleFlow.CreateController = () => CreateBeautyStyle(live);
        beautyStyleFlow.Show = ShowBeautyStyle;
        beautyStyleFlow.Log = m => GD.Print(m);
        beautyStyleFlow.CreateCustomize = () => CreateBeautyCustomize(live);
        void OnBeautyPacket(byte[] body) => BeautyPacketRouter.Route(body, beautyFlow, beautyStyleFlow, m => GD.Print(m));
        live.BeautyRequested += OnRequested;
        live.BeautyPacketReceived += OnBeautyPacket;
        unwatchBeauty = () =>
        {
            live.BeautyRequested -= OnRequested;
            live.BeautyPacketReceived -= OnBeautyPacket;
            beautyFlow.Shutdown();
            beautyStyleFlow.Shutdown();
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
    /// not send them; new preview items (FUN_140523c40) and the worn-pair slot of a hand item (FUN_141420b30) stay unset, so a
    /// preview that needs one does nothing.
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
        };
    }

    void ShowBeauty(BeautyShopDialogController c)
    {
        if (beautyMovie != null) { beautyMovie.Host.Unload(); beautyMovie.QueueFree(); beautyMovie = null; }
        var m = beautyMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () => { m.Host.Unload(); m.QueueFree(); if (beautyMovie == m) beautyMovie = null; };
        GD.Print($"beauty shop: load={m.Loaded}");
        uiRoot.AddChild(m);
    }

    /// <summary>The saved-styles dialog's controller with the host-set parts. STILL-OPEN: nothing calls
    /// <c>beautyStyleFlow.Open()</c> until the NPC talk command "BeautyStyleDialog" (uitalkcinematic.cpp:1190) is ported; the payment's
    /// "not enough" box and the hair apply effect are unset, so the paths that need them do nothing.</summary>
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
}
