namespace Ms2.Ui.Hud;

/// <summary>
/// The field's Esc, FUN_1418fcb70 (uichatballoon.cpp:523-555), as far as the UI's own parts own it, in the client's
/// order: the game option dialog takes it first (UIGameOptionDialog::vfunc_13 @0x141942b10), the shown tooltips
/// close next (GFxManager::CloseTooltips), then the guide's text tip takes it (UIGuideEvent::SkipTextTip
/// @0x141959a20). The cut-scene movie (0x4e7e, <see cref="CutSceneMovie.TakesEsc"/>) comes between the tooltips and the tip:
/// it takes the key and skips nothing (the movie's own key handler asks its box, <see cref="CutSceneMovie.AskExitOnEsc"/>). The rest is the field scene's own: the focused text field keeps Esc, and the closable dialogs and the system-menu toggle
/// (FUN_141274ff0, FUN_141275080, ToggleDialog 0x4e21) run only when none of these took it.
/// </summary>
public static class FieldEsc
{
    /// <summary>Whether one of the UI's parts took Esc: the option dialog's key handler, the tooltips' close, the
    /// guide tip's skip, in the client's order.</summary>
    public static bool Run(Func<bool> optionDialogTakesKey, Func<bool> closeTooltips, Func<bool> skipGuideTextTip, Func<bool> cutSceneMoviePlays = null)
    {
        if (optionDialogTakesKey()) return true;
        if (closeTooltips()) return true;      // CloseTooltips: before the guide's tip
        if (cutSceneMoviePlays?.Invoke() == true) return true;   // UICutSceneMovie (0x4e7e) plays: return (:528-530)
        return skipGuideTextTip();
    }
}
