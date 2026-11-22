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
    BeautyShopDialogController beauty;
    MovieDialog beautyMovie;
    bool beautyWatched;

    bool BeautyOpen => beauty != null && !beauty.IsClosed;

    void WatchBeauty()
    {
        if (beautyWatched || Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        beautyWatched = true;
        live.BeautyRequested += _ => OpenBeauty(live);
        live.BeautyShopReceived += p =>
        {
            if (BeautyOpen) beauty.OnPacket(p);
            else GD.Print($"beauty shop: mode {p.Mode} arrived with the dialog closed");
        };
    }

    void OpenBeauty(Ms2Godot.Net.LiveSession live)
    {
        if (BeautyOpen) return;
        if (beautyMovie != null) { beautyMovie.Host.Unload(); beautyMovie.QueueFree(); beautyMovie = null; }
        var c = new BeautyShopDialogController
        {
            Sales = live.BeautySales,
            PlayerLevel = () => live.Me.User?.Level ?? 0,
            Send = live.SendBeauty,
        };
        c.NoticeText += SystemNoticeText;
        c.Unloaded += () => { beautyMovie?.Host.Unload(); beautyMovie?.QueueFree(); beautyMovie = null; };
        beauty = c;
        beautyMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"beauty shop: load={beautyMovie.Loaded}");
        uiRoot.AddChild(beautyMovie);
    }
}
