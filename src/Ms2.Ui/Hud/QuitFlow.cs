namespace Ms2.Ui.Hud;

/// <summary>
/// Client quit and window-close rules (windowservice.cpp:772-784, uichatballoon.cpp:463-469, uimessagebox.cpp:1225-1237):
/// OnGameExit and WM_CLOSE outside the Login (0x6200009) and Logo (0x620000b) scenes open the exit confirmation box
/// (s_msg_game_over, type 0x17, tag "gameexit"); Yes triggers HandleGameexitButton(0) -> SendRequestQuitAndDisconnect + exit.
/// </summary>
public static class QuitFlow
{
    public const int LoginSceneId = 0x6200009;
    public const int LogoSceneId = 0x620000b;
    public const int GameSceneId = 0x620000c;
    public const int LobbySceneId = 0x620000d;
    public const int LoadingSceneId = 0x620003b;
    public const int CreateSceneId = 0x6200085;

    public const int ExitBoxType = 0x17;
    public const string ExitMessageKey = "s_msg_game_over";
    public const string ExitTag = "gameexit";

    /// <summary>WindowService WM_CLOSE (case 0x10, windowservice.cpp:777-782): scenes other than Login and Logo confirm before closing.</summary>
    public static bool ConfirmOnWindowClose(int sceneId) => sceneId != LoginSceneId && sceneId != LogoSceneId;

    /// <summary>FUN_140d73bc0(0x288 s_msg_game_over, 0x17, 0, "gameexit") (uichatballoon.cpp:467, windowservice.cpp:780).</summary>
    public static MessageBoxData ShowExitConfirm(Func<string, string> common) =>
        MessageBoxQueue.ShowMessageBox(common?.Invoke(ExitMessageKey) ?? "", ExitBoxType, null, ExitTag);

    /// <summary>
    /// WindowService WM_CLOSE handler (windowservice.cpp:772-784): always restores display mode; outside Login/Logo
    /// opens the "gameexit" confirmation box and cancels immediate close (returns false). On Login/Logo or when no
    /// message box layer is available, runs <paramref name="quitNow"/> and returns true.
    /// </summary>
    public static bool HandleWindowClose(int sceneId, Func<string, string> common, bool layerAvailable, Action restoreDisplay, Action quitNow)
    {
        restoreDisplay?.Invoke();
        if (ConfirmOnWindowClose(sceneId) && layerAvailable)
        {
            if (MessageBoxQueue.Current()?.Extra != ExitTag)
                ShowExitConfirm(common);
            return false;
        }
        quitNow?.Invoke();
        return true;
    }
}
