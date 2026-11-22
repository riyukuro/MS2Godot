using Godot;
using Ms2.Formats.World;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// UIHomeMenuDialog (0x4f7c, movie uihomemenudialog, uihomemenudialog.cpp) over <see cref="HomeMenuHost"/>. Its menu entries reach the home
/// option dialog (<see cref="ToggleHomeOption"/>, OnHomePriority @0x140c2a370). The other dialogs the menu toggles (0x4f7a, 0x4f7b) are not ported
/// and do nothing. The field entry opens it in a home field (<see cref="OpenHomeMenuOnEntry"/>). STILL-OPEN: the other openers are not ported:
/// the game menu's home button (UIGameMenuDialog @0x14193b750, whose FUN_14193e9c0 toggles the design and blueprint modes,
/// uigamemenudialog.cpp:2475-2480) and the craft scene's Refresh(true) (uiservice.cpp:784-788).
/// </summary>
public partial class HudScene
{
    HomeMenuDialogController homeMenu;
    MovieDialog homeMenuMovie;

    bool HomeMenuOpen => homeMenuMovie != null && homeMenu?.IsClosed == false;

    /// <summary>CUIService::FUN_141ad9e50 (@0x141ad9e50, uiservice.cpp:766-769): entering the game scene (0x620001d) in a field whose
    /// WorldService map type is 3 (FUN_141bcb1a0, m_4c == 3) queues OpenDialog(0x4f7c). STILL-OPEN: the early return of the client's
    /// micro-game mode query FUN_1412755a0 is not ported (never taken in a field). STILL-OPEN: how the client closes the old field's
    /// menu at a move is not traced; here a menu left open by a home field is unloaded when the new field is not a home.</summary>
    void OpenHomeMenuOnEntry()
    {
        FieldData fd = null;
        try { fd = FieldData.FindByXBlock(assets.Packs, xblock); } catch (Exception ex) { GD.PushWarning("home menu field data: " + ex.Message); }
        if (fd?.MapType == 3) { if (!HomeMenuOpen) OpenHomeMenu(); }
        else if (homeMenuMovie != null) homeMenu.Close();
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
            id => { if (id == HomeMenuDialogController.HomeOptionId) ToggleHomeOption(); }, SystemNoticeText, SetHomeSaveSlotMode);
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
