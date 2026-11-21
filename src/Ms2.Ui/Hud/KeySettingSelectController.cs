using Ms2.Avm2;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIKeySettingSelectDialog (ms2/client/ms2/uikeysettingselectdialog.cpp; UI id 0x4f65): keyboard vs mouse control
/// preset picker opened from the upper HUD input-mode button (OnInputModeClick, uiupperhuddialog.cpp:519-522).
/// </summary>
public sealed class KeySettingSelectController : UiController
{
    public const int UiId = 0x4f65;

    public override string Movie => "uikeysettingselectdialog";
    protected override string EntityMethod => "alignment";
    public override string TypeName => "KeySettingSelectDialog";   // GetTypeName (uikeysettingselectdialog.cpp:39)
    protected override bool TweenEnabled => true;                  // SetTweenEnabled(1) (:21)

    object keyboardButton, mouseButton;

    /// <summary>Raised after the player picks keyboard (false) or mouse (true) mode and the preset XML has been applied
    /// to <see cref="UiModel.Keys"/> (FUN_140c58690, uikeysettingselectdialog.cpp:89-123).</summary>
    public event Action<bool> ModeSelected;

    /// <summary>OnLoadComplete (uikeysettingselectdialog.cpp:54-79): binds m_button1 (keyboard) and m_button2 (mouse)
    /// and enables both iff CKeyTable::GetLocked() == 0.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        bool unlocked = !(Model.Keys?.Locked == true || Model.ServerKeys?.Locked == true);
        keyboardButton = Host.Child("m_button1");
        if (keyboardButton != null)
        {
            Listen(keyboardButton, "buttonClick", () => SelectMode(false));
            Host.Set(keyboardButton, "enabled", unlocked);
        }
        mouseButton = Host.Child("m_button2");
        if (mouseButton != null)
        {
            Listen(mouseButton, "buttonClick", () => SelectMode(true));
            Host.Set(mouseButton, "enabled", unlocked);
        }
    }

    /// <summary>FUN_140c58690 (uikeysettingselectdialog.cpp:89-123): loads DefaultKey.xml (keyboard) or
    /// DefaultMouseControlKey.xml (mouse) with send-to-server, notifies the host for mouse-control state / option
    /// refresh / quick-slot refresh, and closes.</summary>
    void SelectMode(bool mouseControl)
    {
        if (Model.Keys?.Locked == true || Model.ServerKeys?.Locked == true) return;
        Model.Keys?.LoadDefaultKeys(
            mouseControl ? KeyTable.DefaultMouseControlKeyPath : KeyTable.DefaultKeyPath,
            sendToServer: true);
        ModeSelected?.Invoke(mouseControl);
        Close();
    }

    void Listen(object target, string type, Action action)
    {
        Func<object[], object> f = _ => { action(); return null; };
        Host.Invoke(target, "addEventListener", type, f, false, 0, false);
    }
}
