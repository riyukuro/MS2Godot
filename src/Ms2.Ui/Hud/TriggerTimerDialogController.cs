namespace Ms2.Ui.Hud;

/// <summary>
/// UITriggerTimerDialog (client/ms2/uitriggertimerdialog.cpp, "TriggerTimerDialog", UI id 0x4e95, movie
/// uitriggertimerdialog on movie layer 0x13): the field timer a trigger's set_timer with its display flag shows
/// (Trigger type 0xf kind 1, CTriggerService::FUN_141862110). Each frame it shows the <see cref="WorldTimer"/>'s minutes
/// and seconds ("%02d%02d", setTime), starts the common resource's "timeLimit" effect once under 11 seconds, and closes
/// when the timer has run out with its auto-remove flag. The ctor (@0x141b330c0) sets SetFlag(0, 1), an always-on dialog,
/// align 1 and offset 0.
/// </summary>
public sealed class TriggerTimerDialogController : UiController
{
    /// <summary>The UI id (vfunc_48 @0x141b33730).</summary>
    public const int UiId = 0x4e95;
    /// <summary>SetAlign's value for the "TR" packet text (triggerservice.cpp:546); any other text gives <see cref="AlignCenter"/>.</summary>
    public const int AlignTopRight = 6, AlignCenter = 1;
    /// <summary>The common resource key of the low-time effect (CommonResource +0x2f0, commonresource.h:121).</summary>
    public const string TimeLimitEffectKey = "timeLimit";

    public override string Movie => "uitriggertimerdialog";

    /// <summary>The CommonDepth layer of the movie: UITriggerTimerDialog's ctor sets m_2c to 0x13 (uitriggertimerdialog.cpp:45).</summary>

    public override int LoadLayer => CommonDepth.Timer;
    protected override string EntityMethod => "setTime";
    public override string TypeName => "TriggerTimerDialog";      // GetTypeName (@0x141b33180)
    /// <summary>UITriggerTimerDialog's ctor (@0x141b330c0, uitriggertimerdialog.cpp:43) has no SetTweenEnabled: m_c4 stays 0, so CloseDialog
    /// unloads at once (FUN_141924330, uidialog.cpp:393) and a following OpenDialog loads it again.</summary>
    protected override bool TweenEnabled => false;
    public override bool AlwaysOn => true;                         // ctor SetFlag(0, 1)

    /// <summary>The field timer it reads (WorldService's).</summary>
    public WorldTimer Timer = new();
    /// <summary>CMainSystem::GetServerTick; 0 makes the timer read as unset.</summary>
    public Func<int> ServerTick = () => 0;
    /// <summary>CEffectService FUN_1411a49c0 (a kind 0 effect of the name): its handle, 0 when none started.</summary>
    public Func<string, int> PlayEffect;
    /// <summary>CEffectService FUN_1411a6060: the effect of the handle removed.</summary>
    public Action<int> StopEffect;

    int align = AlignCenter;   // m_nAlign
    int offsetY;               // m_nOffsetY
    bool effectRunning;        // m_nEffectRunning
    int effect;                // m_nEffect

    /// <summary>The text setTime was given last, for checks.</summary>
    public string ShownTime { get; private set; } = "";
    public bool EffectRunning => effectRunning;
    public int Align => align;
    public int OffsetY => offsetY;

    /// <summary>OnLoadInit (@0x141b333f0), ahead of the load complete: UIObject's, then the effect counts as not started.</summary>
    public override void OnLoadCompleteBeforeBase() => effectRunning = false;

    /// <summary>SetOffsetY (@0x141b334d0): the offset, then the layout again.</summary>
    public void SetOffsetY(int y)
    {
        offsetY = y;
        Relayout();
    }

    /// <summary>SetAlign (@0x141b33510): the align, then the layout again.</summary>
    public void SetAlign(int a)
    {
        align = a;
        Relayout();
    }

    void Relayout()
    {
        if (Host?.Entity == null) return;
        Layout(Host.Player.StageW, Host.Player.StageH);
    }

    /// <summary>vfunc_43 (@0x141b33550): UIDialog's, then the entity at (align, 0, offset + 0x82).</summary>
    public override void Layout(double stageW, double stageH)
    {
        base.Layout(stageW, stageH);
        if (Host.Entity == null) return;
        Host.Align(Host.Entity, align, 0, offsetY + 0x82);
    }

    /// <summary>
    /// vfunc_72 (@0x141b33200), each frame while open: the timer run out with the auto-remove flag closes; otherwise setTime
    /// of the minutes and seconds, and under 11 seconds (minutes 0) the low-time effect once.
    /// </summary>
    public void Update()
    {
        if (Host?.Entity == null || IsClosed) return;
        Timer.Get(ServerTick(), out int min, out int sec, out _, out int flag);
        if (flag != 0 && min == 0 && sec == 0)
        {
            Close();
            return;
        }
        ShownTime = $"{min:00}{sec:00}";
        Host.Invoke(Host.Entity, "setTime", ShownTime);
        if (!effectRunning && min == 0 && sec <= 10)
        {
            effect = PlayEffect?.Invoke(Model.Tables.CommonResource(TimeLimitEffectKey)) ?? 0;
            effectRunning = true;
        }
    }

    /// <summary>vfunc_67 (@0x141b33420): the effect removed, then UIDialog's close.</summary>
    public override void Close()
    {
        if (effect != 0)
        {
            StopEffect?.Invoke(effect);
            effect = 0;
        }
        base.Close();
    }
}
