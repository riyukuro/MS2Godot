namespace Ms2.Ui;

/// <summary>
/// CMouseController::OnMouseMessage's press and release gate (unnamed/unit_140ad1860_mousecontroller.cpp:327-361, @0x140ad1da0).
/// Both read the UI movie's sampled hit flag (GFxManager::FUN_1422aeed0(2): a dialog had shapes under the cursor at the last
/// 50 ms sample). A left press records it (m_nIgnoreRelease); with it clear the press is taken (mode 3). A release with the flag
/// set still runs the release move when the mode is not idle (m_nMode != 1): a press was taken (mode 3), or the move of an
/// earlier release still runs (mode 2, which FUN_140ad23f0 sets and ResetToIdle ends). With the flag clear the release runs
/// unless the press was ignored or the cursor is out of the window (IsCursorInWindow). A press taken in the field is thus not
/// lost when the cursor ends over a dialog, and a press taken over a dialog does not walk when it ends on the ground.
/// </summary>
public sealed class MouseReleaseGate
{
    bool ignoreRelease;      // m_nIgnoreRelease: the flag at the last left press
    bool taken;              // m_nMode == 3 (a press the controller took, not yet released)

    /// <summary>A left press; <paramref name="overUi"/> the sampled flag at it.</summary>
    public void Press(bool overUi)
    {
        ignoreRelease = overUi;
        taken = !overUi;
    }

    /// <summary>WM_MOUSELEAVE (0x2a3, mousecontroller.cpp:331-334): ResetToIdle, m_nMode 1. A press taken is no longer one, so its
    /// release over a dialog does not walk. m_nIgnoreRelease is not touched.</summary>
    public void Reset() => taken = false;

    /// <summary>A left release; <paramref name="overUi"/> the sampled flag at it, <paramref name="moving"/> whether the move of
    /// an earlier release still runs (m_nMode == 2), <paramref name="inWindow"/> whether the release is in the window. True when
    /// the release move runs (FUN_140ad23f0).</summary>
    public bool Release(bool overUi, bool moving = false, bool inWindow = true)
    {
        bool run = overUi ? taken || moving : !ignoreRelease && inWindow;
        taken = false;
        return run;
    }
}
