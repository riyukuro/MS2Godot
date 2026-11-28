namespace Ms2.Ui.Hud;

/// <summary>
/// UITimerDialog (client/ms2/uitimerdialog.cpp, "TimerDialog", UI id 0x4e3a = 20026, movie uitimerdialog on movie layer 19):
/// the room timer's countdown window (<see cref="RoomTimerManager"/> type 1). Every 100 ms of its frame it shows the
/// remaining minutes and seconds when they changed (resetTime after a new time, else setTime) and runs the common
/// resource's "timeLimit" effect while 10 seconds or less remain. The ctor (@0x141b265f0) sets SetFlag(0, 1): an always-on
/// dialog. The window's open is the host's (<see cref="OpenRequested"/>).
/// </summary>
public sealed class TimerDialogController : UiController, IRoomTimerWindow
{
    public const int UiId = 0x4e3a;

    public override string Movie => "uitimerdialog";

    /// <summary>The CommonDepth layer of the movie: UITimerDialog's ctor sets m_2c to 19 (uitimerdialog.cpp:33).</summary>
    public override int LoadLayer => CommonDepth.Timer;
    protected override string EntityMethod => "setTime";
    public override string TypeName => "TimerDialog";        // GetTypeName (@0x141b26730)
    public override bool AlwaysOn => true;                    // ctor SetFlag(0, 1)
    protected override bool TweenEnabled => !closingNow;

    /// <summary>CMainSystem::GetServerTick.</summary>
    public Func<int> ServerTick = () => 0;
    /// <summary>The ms tick of the 100 ms check (m_tick).</summary>
    public Func<uint> NowMs = () => (uint)Environment.TickCount;
    /// <summary>CEffectService FUN_1411a49c0 (a kind 0 effect of the name): its handle, 0 when none started.</summary>
    public Func<string, int> PlayEffect;
    /// <summary>CEffectService FUN_1411a6060.</summary>
    public Action<int> StopEffectOf;
    /// <summary>FUN_141924680: the host loads the movie when it is not.</summary>
    public Action OpenRequested;

    uint start, duration; int offset;     // UITimerBase m_start, m_duration, m_offset
    int shownMin, shownSec;               // m_nMin, m_nSec
    bool reset;                           // m_bReset
    bool effectRunning; int effect;       // m_nEffectRunning, m_nEffect
    uint lastCheck;                       // m_tick's m_nLast (Unk_IntervalTimer::Check @0x140268870: 0 is due at once)
    bool closingNow;

    public TimerDialogController()
    {
        Unloaded += OnUnload;
    }

    /// <summary>The text the movie was given last and through which call, for checks.</summary>
    public string ShownTime { get; private set; } = "";
    public string LastTimeCall { get; private set; } = "";
    public bool EffectRunning => effectRunning;

    /// <summary>UITimerDialog::vfunc_84 (@0x141b26ae0): UITimerBase's (@0x140edeb10: the three kept), then the next shown time
    /// is a reset.</summary>
    public void SetTime(uint s, uint d, int o)
    {
        start = s; duration = d; offset = o;
        reset = true;
    }

    /// <summary>vfunc_85 (@0x141b26b30): showIncreaseEffect.</summary>
    public void ShowIncreaseEffect()
    {
        if (Host?.Entity == null) return;
        Host.Invoke(Host.Entity, "showIncreaseEffect");
    }

    /// <summary>vfunc_86 (@0x141b26b90): showDecreaseEffect.</summary>
    public void ShowDecreaseEffect()
    {
        if (Host?.Entity == null) return;
        Host.Invoke(Host.Entity, "showDecreaseEffect");
    }

    /// <summary>UITimerBase::vfunc_87 (@0x140edeb80): nothing.</summary>
    public void SetLap(int id, int time) { }

    public void Open() => OpenRequested?.Invoke();

    /// <summary>UIDialog::vfunc_68 (@0x141924df0): stopTweens, then the close with the tween off.</summary>
    public void CloseNow()
    {
        if (Host?.Entity == null || IsClosed) return;
        Host.Invoke("stopTweens");
        closingNow = true;
        try { Close(); } finally { closingNow = false; }
    }

    /// <summary>UITimerBase FUN_140edeba0 (@0x140edeba0): the server tick, plus the offset in seconds when above 0.</summary>
    uint Now()
    {
        uint now = (uint)ServerTick();
        if (offset > 0) now += (uint)(offset * 1000);
        return now;
    }

    /// <summary>FUN_141b26c50 (@0x141b26c50): the remaining minutes and seconds; false without a duration or a tick.</summary>
    bool Remaining(out int min, out int sec)
    {
        min = sec = 0;
        if (duration == 0) return false;
        uint now = Now();
        if (now == 0) return false;
        if (start > now)
        {
            min = (int)(duration / 1000 / 60);
            sec = (int)(duration / 1000 % 60);
            return true;
        }
        uint elapsed = now - start;
        if (elapsed >= duration) return true;
        uint remain = duration - elapsed;
        min = (int)(remain / 1000 / 60);
        sec = (int)(remain / 1000 % 60);
        return true;
    }

    /// <summary>
    /// vfunc_72 (@0x141b26780), each frame while open: every 100 ms, the remaining time; a changed minute or second goes to
    /// resetTime (after a new time) or setTime as "%02d%02d"; 10 seconds or less start the low-time effect, more stop it.
    /// </summary>
    public void Update()
    {
        if (Host?.Entity == null || IsClosed) return;
        if (lastCheck != 0 && NowMs() - lastCheck < 100) return;
        lastCheck = NowMs();
        if (!Remaining(out int min, out int sec)) return;
        if (shownMin != min || shownSec != sec)
        {
            ShownTime = $"{min:00}{sec:00}";
            LastTimeCall = reset ? "resetTime" : "setTime";
            Host.Invoke(Host.Entity, LastTimeCall, ShownTime);
            reset = false;
        }
        if (!effectRunning && min == 0 && sec <= 10)
        {
            effect = PlayEffect?.Invoke(Model.Tables.CommonResource(TriggerTimerDialogController.TimeLimitEffectKey)) ?? 0;
            effectRunning = true;
        }
        else if (effectRunning && (min > 0 || sec > 10))
            StopEffect();
        shownMin = min;
        shownSec = sec;
    }

    /// <summary>StopEffect (@0x141b26bf0): an effect handle above 0 removed, and the effect counts as not running.</summary>
    void StopEffect()
    {
        if (effect <= 0) return;
        StopEffectOf?.Invoke(effect);
        effect = 0;
        effectRunning = false;
    }

    /// <summary>vfunc_26 (@0x141b26a80): UIDialog's, then the effect stopped and the shown time and reset cleared.</summary>
    void OnUnload()
    {
        StopEffect();
        shownMin = shownSec = 0;
        reset = false;
    }
}
