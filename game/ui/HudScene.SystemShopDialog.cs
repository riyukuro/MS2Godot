using Godot;
using Ms2.Ui;
using Ms2.Ui.Shop;

namespace Ms2Godot.Ui;

/// <summary>
/// The auto-fishing / auto-play pane (UISystemShopDialog 0x4f07, <see cref="SystemShopController"/>). The fishing manager
/// opens it with mode 4 when a rod is cast (fishingmanager.cpp:1065) and closes it when the fishing ends (:1338); the
/// instrument manager opens it with mode 5 (instrumentmanager.cpp:997). The HUD keeps the bait id and the auto counts the
/// pane reads and writes (CFishingManager::GetBaitId/SetBaitId/GetAutoCount/SetAutoCount). STILL-OPEN: nothing in this
/// HUD casts a rod or plays an instrument yet, so no game path calls <see cref="OpenSystemShopDialog"/>; the bait item
/// lookup, the auto packages and the buff checks have no source here and keep the controller's defaults.
/// </summary>
public partial class HudScene
{
    SystemShopController systemShop;
    MovieDialog systemShopMovie;
    int fishingBaitId;
    int fishingAutoCount;
    uint instrumentAutoCount;

    bool SystemShopDialogOpen => systemShop != null && !systemShop.IsClosed;

    /// <summary>Opens the pane in mode 4 (fishing) or 5 (instrument); an open pane only changes its mode and fills again.</summary>
    public void OpenSystemShopDialog(int mode)
    {
        if (SystemShopDialogOpen)
        {
            systemShop.Mode = mode;
            if (mode == 4) systemShop.RefreshFishing(); else systemShop.RefreshInstrument();
            return;
        }
        if (systemShopMovie != null) { systemShopMovie.Host.Unload(); systemShopMovie.QueueFree(); systemShopMovie = null; }
        var c = new SystemShopController
        {
            Mode = mode,
            GetBaitId = () => fishingBaitId,
            SetBaitId = id => fishingBaitId = id,
            GetAutoFishingCount = () => fishingAutoCount,
            SetAutoFishingCount = n => { fishingAutoCount = n; if (SystemShopDialogOpen) systemShop.RefreshFishing(); },
            GetAutoInstrumentCount = () => instrumentAutoCount,
            SetAutoInstrumentCount = n => { instrumentAutoCount = n; if (SystemShopDialogOpen) systemShop.RefreshInstrument(); },
        };
        c.Unloaded += () =>
        {
            systemShopMovie?.Host.Unload(); systemShopMovie?.QueueFree(); systemShopMovie = null;
        };
        systemShop = c;
        BindBoxAnswers();
        systemShopMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        uiRoot.AddChild(systemShopMovie);
    }

    /// <summary>Fishing ended (@0x140cdd030): the auto count is zeroed and an open pane closes.</summary>
    public void CloseSystemShopDialog()
    {
        fishingAutoCount = 0;
        if (SystemShopDialogOpen) systemShop.Close();
    }
}
