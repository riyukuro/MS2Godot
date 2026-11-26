using Godot;
using Ms2.Ui.Shop;

namespace Ms2Godot.Ui;

/// <summary>
/// The system shop tabs: the HUD side of ToggleShopTab (@0x1413499b0, unit_141347b10.cpp:468) for the dialogs that call it
/// (the game menu's shop entry, FUN_14193d5c0 uishopdialog tab 9, uigamemenudialog.cpp:2209, and the fishing pane's shop button, tab 4, uisystemshopdialog.cpp:257). The open request goes out
/// as Outbound 0x65; the answer (Inbound 0xA1) opens the shop through <see cref="WatchShop"/>.
/// </summary>
public partial class HudScene
{
    /// <summary>ToggleShopTab(tab): same tab open closes it, otherwise closes the shop and the inventory and asks for the tab.</summary>
    /// The guard is FUN_141adc7f0 / UIDialog::vfunc_73 on dialog 0x4e3b (uidialog.cpp:666): the guide's dialog limit refuses
    /// with notice 2187 (<see cref="GuideRefusesOpen"/>); the dialog's controller is made at the line above, so the
    /// "dialog missing returns 0" case cannot occur here. STILL-OPEN: the map's dialog limit (IsDialogLimited beyond the
    /// guide), WorldService::HasTutorial and the dialog's own vfunc_75 have no state in this HUD and refuse nothing.
    int ToggleShopTab(int tab)
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live == null) return 0;
        liveShop ??= CreateShopController(live);
        return SystemShopFlow.ToggleShopTab(tab, liveShop, ShopOpen, () => shop.CloseShop(),
            () => { if (inventory?.IsClosed == false) inventory.Close(); },
            live.SendShop, key => FieldAudio.Instance?.PlayUi(key),
            () => !GuideRefusesOpen("ShopDialog"));
    }
}
