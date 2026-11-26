namespace Ms2.Ui.Hud;

/// <summary>
/// ICastingEventListener: what a caster implements. Slot 0 (OnCastingFinished) runs when the gauge completes, slot 1
/// (OnCastingCancelled) when another cast replaces it.
/// </summary>
public interface ICastingEventListener
{
    void OnCastingFinished();
    void OnCastingCancelled();
}

/// <summary>
/// Unk_CastingParam (uicastingbar.h, ctor 0x140ac2eb0): the arguments of one cast. The defaults are the ctor's: an
/// active cast without time lines, start 0, scale 1, the two mode strings "show" and "yellow".
/// </summary>
public sealed class CastingParam
{
    /// <summary>+0x00 the gauge time in ms.</summary>
    public int Duration;
    /// <summary>+0x04 show the two time lines.</summary>
    public bool ShowTime;
    /// <summary>+0x05 the gauge is drawn (without it only the timer runs).</summary>
    public bool Active = true;
    public float Start;
    public float Scale = 1f;
    public int N10;
    public string Str18 = "show";
    public string Str20 = "yellow";
    /// <summary>+0x28 the caption.</summary>
    public string Text = "";
    /// <summary>+0x30 the text shown when the cast ends.</summary>
    public string End = "";
}

/// <summary>
/// UICastingBar (uicameraeditdialog.cpp:903-1135, "CastingBar", UI id 0x4e72, a UIHeadUpDisplay), the C++ side of
/// uicastingbar: the cast gauge. CUIService registers it at init (uiservice.cpp:2343), loads it (vfunc_2) when the game
/// scene 0x620001d is entered (FUN_141ada6f0, uiservice.cpp:818) and hides it (vfunc_3) when the scene is left
/// (FUN_141adad20, uiservice.cpp:884). A caster starts a cast through it and is called back when the movie reports
/// OnCastingComplete or the timer expires (<see cref="Process"/>, vfunc_15), or when another cast replaces it.
/// </summary>
public sealed class CastingBarController : UiController
{
    public const int UiId = 0x4e72;

    public override string Movie => "uicastingbar";
    protected override string EntityMethod => "startCasting";
    protected override bool IsDialog => false;              // a UIHeadUpDisplay
    /// <summary>m_58 = 0 in the UICastingBar ctor (uicameraeditdialog.cpp:907): hidden at load (vfunc_8(m_58)).</summary>
    public override bool VisibleOnLoad => false;
    public override string TypeName => "CastingBar";        // GetTypeName (@0x1418e1100)

    /// <summary>Timer::FUN_1408688e0: the millisecond clock the cast's timer runs on.</summary>
    public Func<long> NowMs = () => Environment.TickCount64;

    ICastingEventListener listener;     // +0x70
    object text1, text2, time1, time2;  // +0x78 .. +0xa8
    bool active;                        // +0xb8
    string end = "";                    // +0xc0
    bool finished;                      // +0xc8
    int period;                         // +0xd0 m_timer: Timer::m_nPeriod (0 = not armed)
    long start;                         //        Timer::m_nStart

    /// <summary>GetListener (@0x1417f5e90).</summary>
    public ICastingEventListener Listener => listener;

    /// <summary>IsCasting (@0x1417f5f90): Timer::IsRunning, armed and not expired.</summary>
    public bool IsCasting => period != 0 && NowMs() - start < period;

    bool Expired => period != 0 && NowMs() - start >= period;

    /// <summary>vfunc_3 (@0x1418e1150): hiding drops the cast without calling the caster.</summary>
    public void Hide()
    {
        ReleaseWidgets();
        listener = null;
        active = false;
        end = "";
        finished = false;
        period = 0;
    }

    /// <summary>vfunc_28 (@0x1418e11f0): "OnCastingComplete".</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (!string.Equals(name, "OnCastingComplete", StringComparison.OrdinalIgnoreCase)) return false;
        OnCastingComplete();
        return true;
    }

    /// <summary>vfunc_15 (@0x1418e1260), each frame: an expired timer finishes the cast with the end text.</summary>
    public void Process()
    {
        if (Expired) FinishCasting(end);
    }

    /// <summary>StartCasting (@0x1418e12b0).</summary>
    public void StartCasting(ICastingEventListener l, CastingParam p) => Begin(l, p);

    /// <summary>StartCastingText (@0x1418e12f0): a cast from loose arguments, the other fields the param's defaults.</summary>
    public void StartCastingText(ICastingEventListener l, string text, int duration, bool showTime, bool activeBar, string endText) =>
        Begin(l, new CastingParam { Text = text, Duration = duration, ShowTime = showTime, Active = activeBar, End = endText });

    /// <summary>FUN_1418e1390 (@0x1418e1390): stop with the end text, without calling the caster.</summary>
    public void Cancel() => CancelCasting(end);

    /// <summary>RestartCasting (@0x1418e13c0).</summary>
    public void RestartCasting(ICastingEventListener l, CastingParam p)
    {
        Cancel();
        Begin(l, p);
    }

    /// <summary>SetCastText (@0x1418e1400): both caption lines.</summary>
    public void SetCastText(string text)
    {
        if (text1 != null) Host.Set(text1, "text", text);
        if (text2 != null) Host.Set(text2, "text", text);
    }

    /// <summary>SetCastTime (@0x1418e14b0): both time lines.</summary>
    public void SetCastTime(string text)
    {
        if (time1 != null) Host.Set(time1, "text", text);
        if (time2 != null) Host.Set(time2, "text", text);
    }

    /// <summary>UpdateValue (@0x1418e1560): the movie's "updateValue".</summary>
    public void UpdateValue(float value)
    {
        if (Host?.Entity != null) Host.Invoke("updateValue", value);
    }

    /// <summary>FUN_1418e15d0 (@0x1418e15d0): a running cast's caster is told it was replaced, then the timer starts and, with
    /// the movie and an active bar, the text fields are bound and the movie's startCasting runs.</summary>
    void Begin(ICastingEventListener l, CastingParam p)
    {
        listener?.OnCastingCancelled();
        listener = l;
        active = p.Active;
        end = p.End ?? "";
        finished = false;
        period = p.Duration;            // Timer::Start
        start = NowMs();
        if (Host?.Entity != null && p.Active)
        {
            BindTextFields(p.ShowTime);
            Host.Invoke("startCasting", p.Start, p.Scale, p.Text ?? "", p.Duration, p.ShowTime, p.Str18, p.Str20, p.N10);
        }
    }

    /// <summary>CancelCasting (@0x1418e18a0).</summary>
    void CancelCasting(string text)
    {
        InvokeStopCasting(text);
        EndCasting();
    }

    /// <summary>InvokeStopCasting (@0x1418e18e0): only an active, unfinished cast stops the movie's gauge.</summary>
    void InvokeStopCasting(string text)
    {
        if (Host?.Entity != null && active && !finished) Host.Invoke("stopCasting", (object)(text ?? ""));
    }

    /// <summary>FinishCasting (@0x1418e19c0).</summary>
    void FinishCasting(string text)
    {
        InvokeStopCasting(text);
        OnCastingComplete();
    }

    /// <summary>OnCastingComplete (@0x1418e1a00): the caster's OnCastingFinished, then the reset.</summary>
    void OnCastingComplete()
    {
        listener?.OnCastingFinished();
        EndCasting();
    }

    /// <summary>EndCasting (@0x1418e1a50).</summary>
    void EndCasting()
    {
        period = 0;                     // Timer::Reset
        listener = null;
        finished = true;
        active = false;
        ReleaseWidgets();
    }

    /// <summary>ReleaseWidgets (@0x1418e1ab0).</summary>
    void ReleaseWidgets() => text1 = text2 = time1 = time2 = null;

    /// <summary>BindTextFields (@0x1418e1b20).</summary>
    void BindTextFields(bool showTime)
    {
        if (showTime)
        {
            time1 = Host.Child("m_timetext1");
            time2 = Host.Child("m_timetext2");
        }
        text1 = Host.Child("m_text1");
        text2 = Host.Child("m_text2");
    }
}
