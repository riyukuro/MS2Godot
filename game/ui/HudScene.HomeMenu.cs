using Godot;
using Ms2.Formats.World;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// UIHomeMenuDialog (0x4f7c, movie uihomemenudialog, uihomemenudialog.cpp) over <see cref="HomeMenuHost"/>. Its menu entries reach the home
/// option dialog (<see cref="ToggleHomeOption"/>, OnHomePriority @0x140c2a370). OnHomeAttribute reaches the home inside option dialog (<see cref="ToggleHomeInsideOption"/>). The other dialog the menu toggles (0x4f7b) is not ported
/// and does nothing. The field entry opens it in a home field (<see cref="OpenHomeMenuOnEntry"/>) and the field leave closes it (<see cref="CloseHomeMenuOnLeave"/>). STILL-OPEN: the other openers are not ported:
/// the game menu's home button (UIGameMenuDialog @0x14193b750, whose FUN_14193e9c0 toggles the design and blueprint modes,
/// uigamemenudialog.cpp:2475-2480) and the craft scene's Refresh(true) (uiservice.cpp:784-788).
/// </summary>
public partial class HudScene
{
    HomeMenuDialogController homeMenu;
    MovieDialog homeMenuMovie;

    bool HomeMenuOpen => homeMenuMovie != null && homeMenu?.IsClosed == false;

    /// <summary>CUIService::FUN_141ad9e50 (@0x141ad9e50, uiservice.cpp:758-769): the entry into the game scene opens the menu by
    /// <see cref="HomeMenuEntry.Decide"/>. STILL-OPEN: FUN_1412755a0 (micro-game query) and 0x4f7f / bTab2, see there.</summary>
    void OpenHomeMenuOnEntry()
    {
        FieldData fd = null;
        try { fd = FieldData.FindByXBlock(assets.Packs, xblock); } catch (Exception ex) { GD.PushWarning("home menu field data: " + ex.Message); }
        if (HomeMenuEntry.Decide(fd?.MapType, hudModel.HasLocalPlayer, HomeMenuOpen)) OpenHomeMenu();
    }

    /// <summary>UISceneGame::vfunc_49 (@0x141ac74f0, uiscenegame.cpp:218): FUN_141ada610(0x620001d) runs CloseClosableDialogs (uiservice.cpp:1496),
    /// which closes 0x4f7c (flag 2 unset) with vfunc_68; the entry then opens a fresh menu in a home field.</summary>
    void CloseHomeMenuOnLeave()
    {
        if (HomeMenuOpen) homeMenu.Close();
    }

    void ToggleHomeMenu()
    {
        if (HomeMenuOpen) homeMenu.Close();
        else OpenHomeMenu();
    }

    void OpenHomeMenu()
    {
        if (GuideRefusesOpen("HomeMenuDialog")) return;
        if (liveHome == null) return;
        if (commonStrings == null) SystemMenuFacts();
        if (homeMenuMovie != null)
        {
            homeMenuMovie.Host.Unload();
            homeMenuMovie.QueueFree();
            homeMenuMovie = null;
        }
        var host = new HomeMenuHost(liveHome, id => HomeOptionHost.FieldIsHome(assets.Packs, id), hudModel.IsFeatureEnabled,
            id =>
            {
                if (id == HomeMenuDialogController.HomeOptionId) ToggleHomeOption();
                else if (id == HomeMenuDialogController.HomeAttributeId) ToggleHomeInsideOption();
            }, SystemNoticeText, SetHomeSaveSlotMode);
        var c = homeMenu = new HomeMenuDialogController(host) { SystemNotice = SystemNoticeText };
        var m = homeMenuMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (homeMenuMovie == m) homeMenuMovie = null;
        };
        uiRoot.AddChild(m);
    }
}
