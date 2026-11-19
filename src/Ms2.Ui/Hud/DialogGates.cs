namespace Ms2.Ui.Hud;

/// <summary>The CUIService gates the guide's arrows ask (the client's dialog scans, uiservice.cpp:1250).</summary>
public static class DialogGates
{
    /// <summary>
    /// CUIService::FUN_141adcbb0(FUN_141274d10) (uiservice.cpp:1252-1261, unit_141274d10_uihelpers.cpp:140-150): a
    /// registered dialog is shown that is not an always-on one — vfunc_56 is the always-on flag (the port's
    /// <see cref="UiController.AlwaysOn"/>), vfunc_55 the closed state; the list is the registry's (the head-up
    /// strips among the entries answer no dialog and never count). The client then also checks the group messenger
    /// browser (eUI_4e93) and the ad push (eUI_4f34); neither is in this HUD, so the callers' own popup and message
    /// box checks complete the gate. An index loop, no allocation: the gate runs every frame an arrow shows.
    /// </summary>
    public static bool AnyDialogShown(IReadOnlyList<UiController> dialogs)
    {
        for (int i = 0; i < dialogs.Count; i++)
        {
            var d = dialogs[i];
            if (d.IsDialogEntity && !d.AlwaysOn && !d.IsClosed) return true;
        }
        return false;
    }
}
