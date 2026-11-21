using Godot;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// UIKeySettingSelectDialog (0x4f65, movie uikeysettingselectdialog, uikeysettingselectdialog.cpp): keyboard vs mouse
/// preset selector toggled from the upper HUD input-mode button (OnInputModeClick, uiupperhuddialog.cpp:519-522).
/// </summary>
public partial class HudScene
{
    KeySettingSelectController keySettingSelect;
    MovieDialog keySettingSelectMovie;

    bool KeySettingSelectOpen => keySettingSelectMovie != null && keySettingSelect?.IsClosed == false;

    /// <summary>CUIService::ToggleDialog(0x4f65): open when closed, else close.</summary>
    void ToggleKeySettingSelect()
    {
        if (KeySettingSelectOpen) keySettingSelect.Close();
        else OpenKeySettingSelect();
    }

    void OpenKeySettingSelect()
    {
        if (GuideRefusesOpen("KeySettingSelectDialog")) return;
        if (keySettingSelectMovie != null)
        {
            keySettingSelectMovie.Host.Unload();
            keySettingSelectMovie.QueueFree();
            keySettingSelectMovie = null;
        }
        var c = keySettingSelect = new KeySettingSelectController();
        // FUN_140c58690 (uikeysettingselectdialog.cpp:101-123): store EnableMouseControl, save character settings,
        // apply CMouseController::SetEnabled (gameoptionstruct.cpp:1321-1328), update upper HUD indicator, reopen
        // active game-option tab, and refresh quick slots. STILL-OPEN: NotifyChangeGameOptionInput 9..13
        // (uikeysettingselectdialog.cpp:121, gameoptioninputdefault.cpp:851-855) has no NotifySystem port.
        c.ModeSelected += mouseControl =>
        {
            GameOptions.MouseControl.Value = mouseControl;
            GameOptions.SaveCharacterSettings();
            (field ?? FindField(this))?.SetMouseControl(mouseControl);
            upperHudSide?.SetMouseMode(mouseControl);
            if (optionHost?.IsClosed == false) optionDialog?.ReopenCurrentTab();
            playerInfo?.RebindSlots();
        };
        var m = keySettingSelectMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (keySettingSelectMovie == m) keySettingSelectMovie = null;
        };
        uiRoot.AddChild(m);
    }
}
