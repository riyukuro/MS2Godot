using Godot;

namespace Ms2Godot.Ui;

/// <summary>The scripted live combat run's hooks (MS2_LIVE_COMBAT_STEP, <see cref="Ms2Godot.Net.LiveSession.CombatRun"/>): each does what
/// a key press or a button click does, through the same handler.</summary>
public partial class HudScene
{
    /// <summary>The attack key pressed: the key bound to the attack action (0x49401) goes the way <see cref="CastKeys"/> sends it.
    /// True when the press gave a cast request.</summary>
    public bool AttackScripted()
    {
        if (hudModel?.Keys is not { } keys) return false;
        for (int dik = 1; dik < 0xd4; dik++)
            if (keys.IsKeyBoundToAction(dik, 0x49401)) return CastKey(dik) == true;
        return false;
    }

    /// <summary>Whether the revival dialog or the auto-revive countdown is open (the own death's dialogs).</summary>
    public bool RevivalUiUp() => RevivalOpen || (autoRevive is { IsClosed: false });

    /// <summary>The revival dialog's "onRevival(button)" callback, as its movie raises it. False without the dialog.</summary>
    public bool ReviveScripted(int button) => RevivalOpen && revival.OnCallback("onRevival", new object[] { button });
}
