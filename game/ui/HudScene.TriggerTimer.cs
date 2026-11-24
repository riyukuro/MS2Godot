using Godot;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The trigger timer dialog (UITriggerTimerDialog, 0x4e95, uitriggertimerdialog.cpp): Trigger type 0xf kind 1
/// (CTriggerService::FUN_141862110, triggerservice.cpp:531) opens it with its offset and align when the duration is above
/// 0 and closes it otherwise; while open, its vfunc_72 runs each frame over the session's field timer.
/// STILL-OPEN: the low-time effect (the common resource's timeLimit, UI/Eff_TimeLimit.xml) is played by CEffectService
/// with no owner; that screen effect path is not ported, so it is logged and not shown.
/// </summary>
public partial class HudScene
{
    TriggerTimerDialogController triggerTimer;     // its C++ side, kept between opens as the client's dialog object
    MovieDialog triggerTimerMovie;                 // the movie, from open until OnEndClose
    Action unwatchTriggerTimer;

    void WatchTriggerTimer()
    {
        unwatchTriggerTimer?.Invoke();
        unwatchTriggerTimer = null;
        if (Net.LiveSession.Instance is not { } live) return;
        Action<Ms2.Net.Packets.TriggerDialogCommand> on = c => OnTriggerTimer(live, c);
        live.TriggerTimerReceived += on;
        unwatchTriggerTimer = () => live.TriggerTimerReceived -= on;
    }

    /// <summary>FUN_141862110 kind 1 after the timer is set: a duration above 0 opens 0x4e95 (OpenDialog), then SetOffsetY and
    /// SetAlign ("TR" 6, else 1); 0 closes it (CloseDialog).</summary>
    void OnTriggerTimer(Net.LiveSession live, Ms2.Net.Packets.TriggerDialogCommand c)
    {
        if ((uint)c.Duration > 0)
        {
            if (triggerTimer == null)
                triggerTimer = new TriggerTimerDialogController
                {
                    Timer = live.FieldTimer,
                    ServerTick = () => Net.LiveSession.Instance?.ServerTick ?? 0,
                    PlayEffect = name => { NotApplied.Print($"trigger timer: effect {name} is not played (no screen effect path)"); return 0; },
                };
            triggerTimer.Timer = live.FieldTimer;
            if (triggerTimerMovie == null)
            {
                triggerTimer.Unloaded += TriggerTimerUnloaded;
                triggerTimerMovie = new MovieDialog(assets, triggerTimer, hudModel, ScreenMode.PixelSize(GetWindow()));
                uiRoot.AddChild(triggerTimerMovie);
            }
            triggerTimer.SetOffsetY(c.OffsetY);
            triggerTimer.SetAlign(c.Align == "TR" ? TriggerTimerDialogController.AlignTopRight : TriggerTimerDialogController.AlignCenter);
        }
        else if (triggerTimer is { IsClosed: false } && triggerTimerMovie != null) triggerTimer.Close();
    }

    void TriggerTimerUnloaded()
    {
        triggerTimer.Unloaded -= TriggerTimerUnloaded;
        triggerTimerMovie?.Host.Unload();
        triggerTimerMovie?.QueueFree();
        triggerTimerMovie = null;
    }

    /// <summary>UITriggerTimerDialog::vfunc_72 while it is open.</summary>
    void TriggerTimerTick()
    {
        if (triggerTimerMovie != null && triggerTimer is { IsClosed: false }) triggerTimer.Update();
    }

    void TriggerTimerUnwatch()
    {
        unwatchTriggerTimer?.Invoke();
        unwatchTriggerTimer = null;
    }
}
