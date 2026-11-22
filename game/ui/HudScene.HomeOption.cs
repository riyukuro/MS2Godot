using Godot;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// UIHomeOptionDialog (0x4f26, movie uihomeoptiondialog, uihomeoptiondialog.cpp) over the home's cube map service rows
/// (<see cref="HomeOptionHost"/>). STILL-OPEN: the opener, UIHomeMenuDialog::OnHomePriority (uihomemenudialog.cpp:526-535), is not
/// ported, so only <see cref="ToggleHomeOption"/> (CUIService::ToggleDialog(0x4f26)) reaches it, and nothing in the game calls that yet.
/// </summary>
public partial class HudScene
{
    HomeOptionDialogController homeOption;
    MovieDialog homeOptionMovie;

    bool HomeOptionOpen => homeOptionMovie != null && homeOption?.IsClosed == false;

    void ToggleHomeOption()
    {
        if (HomeOptionOpen) homeOption.Close();
        else OpenHomeOption();
    }

    void OpenHomeOption()
    {
        if (GuideRefusesOpen("HomeOptionDialog")) return;
        if (liveHome == null) return;
        if (commonStrings == null) SystemMenuFacts();
        EnsureOptionHost();
        if (homeOptionMovie != null)
        {
            homeOptionMovie.Host.Unload();
            homeOptionMovie.QueueFree();
            homeOptionMovie = null;
        }
        var host = new HomeOptionHost(liveHome, optionHost, id => HomeOptionHost.FieldIsHome(assets.Packs, id));
        var c = homeOption = new HomeOptionDialogController(new HomeOptionController(host))
        {
            SystemNotice = SystemNoticeText,
        };
        var m = homeOptionMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (homeOptionMovie == m) homeOptionMovie = null;
        };
        uiRoot.AddChild(m);
    }
}
