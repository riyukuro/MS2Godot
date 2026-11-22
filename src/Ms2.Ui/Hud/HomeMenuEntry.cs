namespace Ms2.Ui.Hud;

/// <summary>
/// The field entry's decision for the home menu (CUIService::FUN_141ad9e50 @0x141ad9e50, uiservice.cpp:758-769, scene 0x620001d): after the
/// micro-game query FUN_1412755a0 and the local player check (:759-763) it queues the dialogs, 0x4f7c among them when the WorldService
/// map type is 3 (FUN_141bcb1a0, :766-767). STILL-OPEN: the micro-game query FUN_1412755a0 (its early return at :759 is not ported);
/// the 0x4f7f dialog that follows 0x4f7c when FUN_1410c3600(2) == 1 (bTab2, :768-769).
/// The leave side is <see cref="HomeMenuDialogController"/>'s close: UISceneGame::vfunc_49 (@0x141ac74f0, uiscenegame.cpp:209-219) calls
/// FUN_141ada610(0x620001d) (@0x141ada610, uiservice.cpp:1142-1153), which runs CloseClosableDialogs (@0x141adc050, :1496): every dialog
/// without flag 2 is closed by vfunc_68 (0x4f7c sets only flag 0, uihomemenudialog.cpp:225); FUN_141adad20's list (:860) does not hold 0x4f7c.
/// </summary>
public static class HomeMenuEntry
{
    public const int HomeMapType = 3;

    /// <summary>Whether the entry opens the menu. <paramref name="mapType"/> null = no field data; <paramref name="hasLocalPlayer"/> is
    /// GetLocalPlayer() != 0 (:760-763); <paramref name="menuOpen"/> = the dialog is already open (a second queued open does nothing).</summary>
    public static bool Decide(int? mapType, bool hasLocalPlayer, bool menuOpen)
        => hasLocalPlayer && mapType == HomeMapType && !menuOpen;
}
