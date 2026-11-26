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

    /// <summary>
    /// The open-dialog lookup the guide's arrows use (GuideUIById / GuideUIByName, uiguideeventarrow.cpp:629-657; an
    /// arrow gives up on a null GetScript, :874-878, :719-723, :679-683): the registry's loaded, not closed controller
    /// whose type name matches (case blind), else null. The client's FindObjectByName also answers closed and
    /// never-opened dialogs; <see cref="Ms2.Ui.Guide.GuideDialogLookup.Resolve"/> adds those for the dialog conditions.
    /// A list read with no allocation: the conditions ask it on every guide tick for each event left queued by a dialog
    /// open, and the arrows every frame one shows; the HUD's former walk of its whole node tree with GetChildren there
    /// took the frame rate down after the first inventory open on a live field.
    /// </summary>
    public static UiController FindOpen(IReadOnlyList<UiController> dialogs, string typeName)
    {
        if (typeName == null) return null;
        for (int i = 0; i < dialogs.Count; i++)
        {
            var d = dialogs[i];
            if (d.Host?.Entity != null && !d.IsClosed && string.Equals(d.TypeName, typeName, StringComparison.OrdinalIgnoreCase))
                return d;
        }
        return null;
    }

    /// <summary>
    /// The dialogs' part of UISwfEntityManager::FUN_1422d1c20 (uiswfentitymanager.cpp:296-330) for a key: the entities are
    /// asked last-added first, each shown one (vfunc_9) through its key handler (<see cref="UiController.EscapeKey"/>,
    /// UIDialog::vfunc_13), and the first that takes the key ends the walk. The client walks its five layers in turn; the
    /// registry keeps the movies in the order they came, which is the order the HUD draws them. True = a dialog took it.
    /// </summary>
    public static bool EscapeKey(IReadOnlyList<UiController> dialogs)
    {
        for (int i = dialogs.Count - 1; i >= 0; i--)
        {
            if (i >= dialogs.Count) continue;                // a close that unloads now leaves the list shorter
            var d = dialogs[i];
            if (d.IsDialogEntity && d.EntityShown && d.EscapeKey()) return true;
        }
        return false;
    }

    /// <summary>FUN_141274df0 (unit_141274d10_uihelpers.cpp:153), the field Esc's open-dialog test (FUN_141274ff0): a
    /// registered dialog not always on (vfunc_56), not closed (vfunc_55), not minimized (vfunc_64) and closable (no flag 1).</summary>
    public static bool AnyClosable(IReadOnlyList<UiController> dialogs)
    {
        for (int i = 0; i < dialogs.Count; i++)
        {
            var d = dialogs[i];
            if (d.IsDialogEntity && !d.AlwaysOn && !d.IsClosed && !d.Minimized && !d.NotClosable) return true;
        }
        return false;
    }

    /// <summary>
    /// CUIService::CloseAllDialogs (uiservice.cpp:1018-1036): vfunc_67 on each dialog IsDialogClosable passes (not always
    /// on, not closed, no flag 1; the play-instrument dialog 0x4eec is not ported), then the group messenger windows
    /// minimized (UIGroupMessengerBrowser::MinimizeAllWindows, uigroupmessenger.cpp:1862: vfunc_69 on each one not
    /// minimized; the windows are the browser's, not the registry's). A dialog whose close already began is passed by: the
    /// HUD's own close list runs first, and the client's walk visits each dialog once. Walked from the end: a close can unload a movie now.
    /// </summary>
    public static void CloseAll(IReadOnlyList<UiController> dialogs)
    {
        for (int i = dialogs.Count - 1; i >= 0; i--)
        {
            if (i >= dialogs.Count) continue;
            var d = dialogs[i];
            if (!d.IsDialogEntity || d.AlwaysOn || d.IsClosed || d.NotClosable || d.CloseAsked) continue;
            if (d is GroupMessengerController m) { if (!m.Minimized) m.EscapeClose(); }
            else d.Close();
        }
    }
}
