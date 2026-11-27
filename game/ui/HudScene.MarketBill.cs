using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2.Ui.Shop;

namespace Ms2Godot.Ui;

/// <summary>
/// The merat bill dialog (UIMarketBillDialog 0x4f17, <see cref="MarketBillDialogController"/>), opened by buying a star item
/// from the shop and answered by the merat market buy packets (send 0x39, answer 0x6C). Live only. STILL-OPEN: see the
/// controller's and <see cref="MeratPurchaseQueue"/>'s notes; closing the bill while a purchase runs (OnDialogClosed) is not wired.
/// </summary>
public partial class HudScene
{
    MarketBillDialogController marketBill;
    MovieDialog marketBillMovie;

    bool MarketBillOpen => marketBill != null && !marketBill.IsClosed;

    /// <summary>The shop's purchase queue is bound to the live session once, with the texts of its boxes.</summary>
    void BindMeratQueue(Ms2Godot.Net.LiveSession live, ShopController shopController)
    {
        var q = shopController.Purchases;
        var tables = hudModel.Tables;
        q.Send = live.SendMeretMarketBuy;
        q.Notice = SystemNoticeText;
        q.Text = id => tables.Common(id) ?? "";
        q.TextKey = key => tables.Common(key) ?? key;
        q.PriceText = (kind, amount) => shopController.CurrencyPriceText(kind, amount);
        q.CloseBill = () => { if (MarketBillOpen) marketBill.Close(); };
        q.ShowResult = (caption, message, _) =>
        {
            var d = MessageBoxQueue.Create(0, message, null, "");
            d.Title = caption;
            MessageBoxQueue.Show(d);
        };
        shopController.OpenMarketBill += OpenMarketBill;
        live.GameLinkLost += q.Cancel;
    }

    void OpenMarketBill(MarketBillParam param)
    {
        if (MarketBillOpen) return;
        if (marketBillMovie != null) { marketBillMovie.Host.Unload(); marketBillMovie.QueueFree(); marketBillMovie = null; }
        var tables = hudModel.Tables;
        var c = new MarketBillDialogController
        {
            Text = key => tables.Common(key) ?? key,
            TextId = id => tables.Common(id) ?? "",
            Notice = SystemNoticeText,
            PriceText = (kind, amount) => liveShop?.CurrencyPriceText(kind, amount) ?? amount.ToString(),
        };
        c.SetParam(param);
        c.Unloaded += () =>
        {
            liveShop?.Purchases.Cancel();                                  // OnDialogClosed: a run in progress ends
            marketBillMovie?.Host.Unload(); marketBillMovie?.QueueFree(); marketBillMovie = null;
        };
        marketBill = c;
        BindBoxAnswers();
        marketBillMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"merat bill: load={marketBillMovie.Loaded}");
        uiRoot.AddChild(marketBillMovie);
    }
}
