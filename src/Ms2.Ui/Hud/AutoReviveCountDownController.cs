namespace Ms2.Ui.Hud;

/// <summary>
/// UIAutoReviveCountDownDialog (ms2/client/ms2/uiautorevivecountdowndialog.cpp; UI id 0x4ea2, movie
/// uiautorevivecountdowndialog), the countdown to the automatic revive the AutoRevive packet (Inbound 0xA2) starts.
/// The ctor (@0x141284ac0) sets only flag 1, not closable: Esc and CloseAllDialogs pass it by (CUIService::IsDialogClosable,
/// uiservice.cpp:1009), so the HUD's closable list leaves it out, and it is not always on, so the guide's dialog gate counts
/// it. The dtor (@0x141284b30) is empty; vfunc_67 (@0x141284d50) is UIDialog's close, <see cref="UiController.Close"/>;
/// vfunc_48 (@0x141284df0) is the UI id 0x4ea2.
/// </summary>
public sealed class AutoReviveCountDownController : UiController
{
    public override string Movie => "uiautorevivecountdowndialog";
    protected override string EntityMethod => "alignment";
    public override string TypeName => "AutoReviveCountDownDialog";         // GetTypeName @0x141284d00

    uint endTime;                       // m_nEndTime (ms tick)
    uint remainSec;                     // m_nRemainSec

    /// <summary>The seconds the movie was told last (setTime), for checks.</summary>
    public uint RemainSeconds => remainSec;

    /// <summary>FUN_141284d70 (@0x141284d70): arm the countdown with a duration in ms; the whole seconds are the duration / 1000 + 1.</summary>
    public void Arm(uint nowMs, int durationMs)
    {
        endTime = nowMs + (uint)durationMs;
        remainSec = (uint)durationMs / 1000 + 1;
    }

    /// <summary>vfunc_43 (@0x141284c50): UIDialog's, then the entity at align (0, 0, 0x118).</summary>
    public override void Layout(double stageW, double stageH)
    {
        base.Layout(stageW, stageH);                    // UIDialog::vfunc_43 (uiautorevivecountdowndialog.cpp:57)
        Host.Align(Host.Entity, 0, 0, 0x118);
    }

    /// <summary>
    /// vfunc_72's decision (@0x141284b70): the deadline past closes (-1); else the remaining whole seconds when they changed
    /// from the last told (a second counts until the deadline leaves it), 0 when unchanged.
    /// </summary>
    public long Step(uint nowMs)
    {
        if (endTime < nowMs) return -1;
        uint remain = (endTime - nowMs) / 1000 + 1;
        if (remain == remainSec) return 0;
        remainSec = remain;
        return remain;
    }

    /// <summary>vfunc_72, each frame: nothing without the movie; the deadline past closes; a changed second goes to setTime.</summary>
    public void Update(uint nowMs)
    {
        if (Host?.Entity == null || IsClosed) return;
        long step = Step(nowMs);
        if (step < 0) Close();
        else if (step > 0) Host.Invoke(Host.Entity, "setTime", (uint)step);
    }
}
