using Godot;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// UIHomeMenuDialog (0x4f7c, movie uihomemenudialog, uihomemenudialog.cpp) over <see cref="HomeMenuHost"/>. Its menu entries reach the home
/// option dialog (<see cref="ToggleHomeOption"/>, OnHomePriority @0x140c2a370). The other dialogs the menu toggles (0x4f7a, 0x4f7b) are not ported
/// and do nothing. STILL-OPEN: the callers that open this dialog (CUIService's home list, uiservice.cpp:767-785; UIGameMenuDialog,
/// uigamemenudialog.cpp:2475-2480) are not ported, so only <see cref="ToggleHomeMenu"/> reaches it.
/// </summary>
public partial class HudScene
{
    HomeMenuDialogController homeMenu;
    MovieDialog homeMenuMovie;

    bool HomeMenuOpen => homeMenuMovie != null && homeMenu?.IsClosed == false;

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
            id => { if (id == HomeMenuDialogController.HomeOptionId) ToggleHomeOption(); }, SystemNoticeText);
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
