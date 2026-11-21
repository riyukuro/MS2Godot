using Godot;
using Ms2.Ui.Shop;

namespace Ms2Godot.Ui;

/// <summary>
/// The system shop tabs: the HUD side of ToggleShopTab (@0x1413499b0, unit_141347b10.cpp:468) for the dialogs that call it
/// (the game menu's shop entry, FUN_14193d5c0 uishopdialog tab 9, uigamemenudialog.cpp:2209). The open request goes out
/// as Outbound 0x65; the answer (Inbound 0xA1) opens the shop through <see cref="WatchShop"/>.
/// </summary>
public partial class HudScene
{
    /// <summary>ToggleShopTab(tab): same tab open closes it, otherwise closes the shop and the inventory and asks for the tab.</summary>
    void ToggleShopTab(int tab)
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live == null) return;
        liveShop ??= CreateShopController(live);
        SystemShopFlow.ToggleShopTab(tab, liveShop, ShopOpen, () => shop.CloseShop(),
            () => { if (inventory?.IsClosed == false) inventory.Close(); },
            live.SendShop, key => FieldAudio.Instance?.PlayUi(key));
    }
}
