using Godot;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// UIBillDialog (0x4e7b, movie uibilldialog, uibeautyshopdialog.cpp:2282) as the beauty shop opens it (OnClickSave @0x1418c8700): OpenDialog
/// shows it, and the shop's sale event refreshes its total (OnGameEventChanged @0x1418c9780). STILL-OPEN: the item colouring (shop kind 0),
/// style (2) and wedding hall (3) openers, whose sale lookups <see cref="BillDialogController.ShopSale"/> leaves as the unchanged price.
/// </summary>
public partial class HudScene
{
    BillDialogController billDialog;
    MovieDialog billMovie;

    bool BillOpen => billMovie != null && billDialog?.IsClosed == false;

    /// <summary>The sale of the open beauty shop (FUN_1418cda70's shop kind 1: GetDiscountedPrice and GetSaleIndex by its sale event id).</summary>
    (uint Price, int Sale)? BeautyBillSale(int shopKind, uint price, int level)
    {
        if (shopKind != 1 || beautyFlow.Current is not { } shop) return null;
        return (shop.Sales.DiscountedPrice(shop.SaleEventId, price, level), shop.Sales.SaleIndex(shop.SaleEventId, level));
    }

    void ShowBill(Ms2Godot.Net.LiveSession live, BillData data)
    {
        if (billMovie != null)
        {
            billMovie.Host.Unload();
            billMovie.QueueFree();
            billMovie = null;
        }
        var c = billDialog = new BillDialogController
        {
            Data = data,
            PlayerLevel = () => live.Me.User?.Level ?? 0,
            HasPlayer = () => live.Me.User != null,
            ShopSale = BeautyBillSale,
            PriceText = (type, item, price, icon) => ColoringShop(live).PriceText(type, item, price, icon),   // FUN_141349f30
        };
        var m = billMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (billMovie == m) billMovie = null;
        };
        uiRoot.AddChild(m);
    }
}
