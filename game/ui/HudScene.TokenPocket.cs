using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The token pocket: UITokenPocketDialog (0x4ece, movie uitokenpocketdialog) with its C++ side as
/// <see cref="TokenPocketController"/>, opened by the upper HUD's token pocket button (CUIService::ToggleDialog(0x4ece)).
/// The amounts redraw from the model's token money event.
/// </summary>
public partial class HudScene
{
    MovieDialog tokenPocketMovie;
    TokenPocketController tokenPocket;

    bool TokenPocketOpen => tokenPocketMovie != null && tokenPocket?.IsClosed == false;

    /// <summary>CUIService::ToggleDialog(0x4ece): open when closed, else close.</summary>
    void ToggleTokenPocket()
    {
        if (TokenPocketOpen) tokenPocket.Close();
        else OpenTokenPocket();
    }

    void OpenTokenPocket()
    {
        if (GuideRefusesOpen("TokenPocketDialog")) return;
        if (tokenPocketMovie != null) { tokenPocketMovie.Host.Unload(); tokenPocketMovie.QueueFree(); tokenPocketMovie = null; }
        var c = tokenPocket = new TokenPocketController();
        var m = tokenPocketMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (tokenPocketMovie == m) tokenPocketMovie = null;
        };
        uiRoot.AddChild(m);
    }
}
