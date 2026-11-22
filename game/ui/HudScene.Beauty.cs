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
    MovieDialog beautyMovie;
    Action unwatchBeauty;

    bool BeautyOpen => beautyFlow.IsOpen;

    /// <summary>Subscribes once per scene and unsubscribes with it (the LiveSession outlives a field reload's HUD).</summary>
    void WatchBeauty()
    {
        if (unwatchBeauty != null || Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        beautyFlow.CreateController = () => CreateBeauty(live);
        beautyFlow.Show = ShowBeauty;
        beautyFlow.Log = m => GD.Print(m);
        void OnRequested(BeautyReq _) => beautyFlow.Open();
        live.BeautyRequested += OnRequested;
        live.BeautyShopReceived += beautyFlow.OnPacket;
        unwatchBeauty = () =>
        {
            live.BeautyRequested -= OnRequested;
            live.BeautyShopReceived -= beautyFlow.OnPacket;
            beautyFlow.Current?.Close();
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

    void ShowBeauty(BeautyShopDialogController c)
    {
        if (beautyMovie != null) { beautyMovie.Host.Unload(); beautyMovie.QueueFree(); beautyMovie = null; }
        var m = beautyMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () => { m.Host.Unload(); m.QueueFree(); if (beautyMovie == m) beautyMovie = null; };
        GD.Print($"beauty shop: load={m.Loaded}");
        uiRoot.AddChild(m);
    }
}
