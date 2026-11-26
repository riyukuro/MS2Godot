using System.Globalization;
using Ms2.Avm2;
using Ms2.Avm2.Flash;

namespace Ms2.Ui.Hud;

/// <summary>
/// CMainSystem's frame rate (OnTick @0x141474080, mainsystem.cpp:540-561): each tick after the first adds the time since the
/// last one and counts a frame; once more than a second has gathered, the rate is frames over that time and both start
/// again. The first tick (no last time yet) only starts them. GetFps (@0x141473210) gives the last rate, 0 before one.
/// </summary>
public sealed class FrameRateMeter
{
    /// <summary>
    /// CMainSystem's own meter: one for the process, ticked every frame from start-up whatever the scene (the game's frame
    /// rate clock), so a dialog opened at game entry already has a rate.
    /// </summary>
    public static FrameRateMeter Process { get; } = new();

    double lastTime, fpsTime;
    uint frames;

    /// <summary>m_fFps.</summary>
    public float Fps { get; private set; }

    /// <summary>One OnTick at <paramref name="now"/> seconds (GetServiceManagerTime).</summary>
    public void Tick(double now)
    {
        if (lastTime > 0.0)
        {
            fpsTime += now - lastTime;
            frames++;
            if (fpsTime > 1.0)
            {
                Fps = (float)(frames / fpsTime);
                fpsTime = 0;
                frames = 0;
            }
        }
        else
        {
            fpsTime = 0;
            frames = 0;
        }
        lastTime = now;
    }
}

/// <summary>
/// UIPerformanceDialog (0x4e7d, movie uiperformancedialog, uipartydialog.cpp:1425-1475): the frame rate and the time sync
/// round trip in one line, "{0:0.1f}({1})". The ctor sets SetFlag(0, 1) (always on) and no SetTweenEnabled; OnLoadComplete
/// makes the window top-most and finds m_text; vfunc_72, the update CUIService runs each frame for an open dialog
/// (uiservice.cpp:1923-1934), writes CMainSystem::GetFps and FUN_141473460 (m_i74, the time sync average) into it.
/// The options' "show frame rate" (gameoption.cpp:1339), left Ctrl+P (uiservice.cpp:2879) and the game scene's open list
/// when that option is saved on (uiservice.cpp:744) open it.
/// </summary>
public sealed class PerformanceDialogController : UiController
{
    public const int UiId = 0x4e7d;                                 // vfunc_48 (@0x141a7cae0)

    public override string Movie => "uiperformancedialog";
    protected override string EntityMethod => "alignment";
    public override string TypeName => "PerformanceDialog";         // GetTypeName (@0x141a7c5b0)
    protected override bool TweenEnabled => false;
    public override bool AlwaysOn => true;                          // ctor SetFlag(0, 1)

    AsObject text;                                                  // m_text
    float shownFps = float.NaN;
    int shownLatency = int.MinValue;

    /// <summary>The line last written into m_text, for checks; null before the first update.</summary>
    public string Text { get; private set; }

    /// <summary>OnLoadComplete (@0x141a7c600): UIDialog's, SetTopMostEnable(1), then m_text.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        Host.Invoke("setTopMostEnable", true);                      // FUN_141923680(1): ASWindow::SetTopMostEnable
        text = Host.Child("m_text") as AsObject;
        shownFps = float.NaN; shownLatency = int.MinValue;
    }

    /// <summary>
    /// vfunc_72 (@0x141a7c6e0): m_text = StringFormat("{0:0.1f}({1})", fps, latency). The client writes it every frame; the
    /// same values give the same line, so it is written only when one of them changed (the meter changes once a second).
    /// </summary>
    public void Update(float fps, int latency)
    {
        if (text == null) return;
        if (fps.Equals(shownFps) && latency == shownLatency) return;
        shownFps = fps; shownLatency = latency;
        Text = Line(fps, latency);
        Host.Set(text, "text", Text);
    }

    /// <summary>"{0:0.1f}({1})": the rate with one decimal (snprintf "%.1f"), the latency as an unsigned number.</summary>
    public static string Line(float fps, int latency) =>
        ((double)fps).ToString("F1", CultureInfo.InvariantCulture) + "(" + ((uint)latency).ToString(CultureInfo.InvariantCulture) + ")";
}
