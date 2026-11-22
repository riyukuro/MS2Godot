using Godot;
using Ms2.Formats.World;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// UIHomeInsideOptionDialog (0x4f7a, movie uihomeinsideoptiondialog, uihomeinsideoptiondialog.cpp) over <see cref="HomeInsideOptionHost"/>.
/// The opener is the home menu's OnHomeAttribute (<see cref="HomeMenuDialogController"/>), which calls <see cref="ToggleHomeInsideOption"/>
/// (CUIService::ToggleDialog(0x4f7a)).
/// </summary>
public partial class HudScene
{
    HomeInsideOptionDialogController homeInsideOption;
    MovieDialog homeInsideOptionMovie;
    UgcHomeOptionData ugcHomeOptions;

    bool HomeInsideOptionOpen => homeInsideOptionMovie != null && homeInsideOption?.IsClosed == false;

    void ToggleHomeInsideOption()
    {
        if (HomeInsideOptionOpen) homeInsideOption.Close();
        else OpenHomeInsideOption();
    }

    void OpenHomeInsideOption()
    {
        if (GuideRefusesOpen("HomeInsideOptionDialog")) return;
        if (liveHome == null) return;
        if (commonStrings == null) SystemMenuFacts();
        if (homeInsideOptionMovie != null)
        {
            homeInsideOptionMovie.Host.Unload();
            homeInsideOptionMovie.QueueFree();
            homeInsideOptionMovie = null;
        }
        ugcHomeOptions ??= new UgcHomeOptionData(assets.Packs, id => hudModel.Tables.StringTableText(UgcHomeOptionData.NameTable, id));
        var c = homeInsideOption = new HomeInsideOptionDialogController(new HomeInsideOptionHost(liveHome, ugcHomeOptions));
        var m = homeInsideOptionMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (homeInsideOptionMovie == m) homeInsideOptionMovie = null;
        };
        uiRoot.AddChild(m);
    }
}
