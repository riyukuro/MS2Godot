using Ms2.Net.Packets;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIMassiveEvent (client/ms2/uimassiveevent.cpp, "MassiveEvent", UI id 0x4e4c, a UIObject on movie layer 0x1e): the
/// event banner over the field that the MassiveEvent packet drives (<see cref="Apply"/>): the stage counter, the messages
/// (win, lose, the trigger's event texts) and the step message with its countdown. CUIService registers it at init
/// (uiservice.cpp:2333) and loads it with the game scene 0x620001d (uiservice.cpp:813); the scene's exit clears its stage
/// (UISceneGamePlay::vfunc_56, uiscenegame.cpp:1153).
/// <para>
/// STILL-OPEN: kind 7's event introduce dialog (UIEventIntroduce 0x4f06, OnRound / OnFinalRound; not ported, so kind 7 goes
/// to <see cref="Unapplied"/>); the ChromaSDK lighting of messages 2 and 5 (SetState(9)); vfunc_2's micro-game check
/// (FUN_1412755a0, which skips the load), which no ported micro game sets.
/// </para>
/// </summary>
public sealed class MassiveEventController : UiController
{
    /// <summary>The UI id (vfunc_48).</summary>
    public const int UiId = 0x4e4c;

    public override string Movie => "uimassiveevent";       // GetSwfFileName: "UI" + GetTypeName
    /// <summary>A method only the event's entity has (the host takes the first display object with it as m_30).</summary>
    protected override string EntityMethod => "clearAndSetPanel";
    protected override bool IsDialog => false;
    /// <summary>The constructor clears m_58 (uimassiveevent.cpp:52); vfunc_43 (<see cref="Layout"/>) shows it again.</summary>
    public override bool VisibleOnLoad => false;
    public override string TypeName => "MassiveEvent";      // GetTypeName (@0x1419f5740)

    /// <summary>GetTickCountMs2: the millisecond tick the countdown runs on.</summary>
    public Func<uint> NowMs = () => (uint)Environment.TickCount;
    /// <summary>PlaySystemSound(key, ""); null plays nothing.</summary>
    public Action<string> SystemSound;
    /// <summary>FUN_1412751b0(text, 0x13): message type 6's chat line of kind 0x13 (<see cref="TalkKind.SystemEvent"/>).</summary>
    public Action<string, int> ChatLine;
    /// <summary>StringIdInterface::FUN_141024e60: the text of a StringIdInterface (NoticeController.Text); null gives "".</summary>
    public Func<StringIdText, string> Text;
    /// <summary>A packet this pane reads but does not apply (kind 7, see the STILL-OPEN above).</summary>
    public Action<MassiveEventMessage> Unapplied;
    /// <summary>DAT_143af62d4 / DAT_143af62d8, the client size, and GFxManager::GetMovieScale(m_28), read at each layout (vfunc_43).</summary>
    public Func<(int Width, int Height, double MovieScale)> ClientSize = () => (1280, 720, 1);

    /// <summary>The sounds of ShowMessage (uimassiveevent.cpp:43-46).</summary>
    public const string FailSound = "massive_fail", SuccessSound = "massive_success", BattleWinSound = "BattleField_Win", PcWinSound = "PC_win_game";
    /// <summary>The count sound's effect (PlayCountSound, :148-160) and its voice key, "Count0{0}" of the second.</summary>
    public const string CountEffectSound = "Count_NumEffect", CountVoiceFormat = "Count0{0}";
    /// <summary>The common string of kind 8's step message (GetStringCommon(0x974), :303).</summary>
    public const int PvpCountdownStringId = 0x974;
    /// <summary>ShowStepMessage's step number text key (@0x1419f6bc0).</summary>
    public const string StepNumberKey = "s_massive_event_message_start";

    int remain;            // m_nRemain (ctor: 0)
    uint deadline;         // m_nDeadline
    int soundType;         // m_nType (ctor: 0)
    bool stage;            // m_bStage (ctor: 0)
    object counter, time;  // m_spCounter / m_spTime (found on load)

    /// <summary>The seconds the countdown shows (m_nRemain); 0 when none runs.</summary>
    public int Remaining => remain;
    /// <summary>m_bStage: the last setStage had a second value other than 0.</summary>
    public bool StageShown => stage;
    /// <summary>FUN_1422dc2d0: the movie is loaded.</summary>
    public bool Loaded => Host?.Entity != null;

    /// <summary>OnLoadComplete (@0x1419f5800): UIObject's, then m_counter and its "time" clip.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        counter = Host.Child("m_counter");
        time = counter != null ? Host.Child("time", counter) : null;
    }

    /// <summary>vfunc_43 (@0x1419f5920): the movie aligned (vfunc_2(5, 0, 0)), setStageSize(client w, h, movie scale), shown.</summary>
    public override void Layout(double stageW, double stageH)
    {
        if (Host.Entity == null) return;
        Host.Align(Host.Entity, 5, 0, 0);
        var (w, h, scale) = ClientSize();
        Host.Invoke("setStageSize", w, h, scale);
        Host.SetVisible(Host.Entity, true);
    }

    /// <summary>vfunc_15 (@0x1419f5a30): the countdown's step, while the movie is loaded (FUN_1419f5dd0).</summary>
    public void Tick()
    {
        if (!Loaded) return;
        UpdateCountdown();
    }

    /// <summary>
    /// FUN_1419f5dd0 (@0x1419f5dd0): with a countdown on, at its deadline it ends (endCount); otherwise a new second
    /// ((deadline - now) / 1000 + 1) shows its digit.
    /// </summary>
    void UpdateCountdown()
    {
        if (remain == 0) return;
        uint now = NowMs();
        if (deadline <= now)
        {
            remain = 0;
            Host.Invoke("endCount");
            return;
        }
        int sec = (int)((deadline - now) / 1000) + 1;
        if (remain != sec)
        {
            remain = sec;
            ShowDigit();
        }
    }

    /// <summary>The MassiveEvent packet (FUN_1419f5e90 @0x1419f5e90, :183), by its kind.</summary>
    public void Apply(MassiveEventMessage m)
    {
        switch (m.Kind)
        {
            case MassiveEventMessage.Stage: SetStage(m.StageNumber, m.A, m.B, m.C); break;                                  // :218
            case MassiveEventMessage.Text: ShowMessage(6, TextOf(m.Message), m.Duration); break;                                // :246
            case MassiveEventMessage.StepKey: ShowStepMessage(ServerString(m.Key), m.Step, m.Duration, m.SoundType); break;  // :254
            case MassiveEventMessage.StepText: ShowStepMessage(TextOf(m.Message), m.Step, m.Duration, m.SoundType); break;      // :265
            case MassiveEventMessage.Win: ShowMessage(2, TextOf(m.Message), m.Duration); break;                                 // :275
            case MassiveEventMessage.Lose: ShowMessage(1, TextOf(m.Message), m.Duration); break;                                // :283
            case MassiveEventMessage.Round: if (m.Goal > 0) Unapplied?.Invoke(m); break;                                     // :228
            case MassiveEventMessage.TypedKey: ShowMessage(m.MessageType, ServerString(m.Key), m.Duration); break;           // :291
            case MassiveEventMessage.PvpCountdown: ShowStepMessage(Model.Tables.Common(PvpCountdownStringId), 0, m.Duration, 2); break;   // :301
        }
    }

    string TextOf(StringIdText t) => t == null || Text == null ? "" : Text(t) ?? "";

    /// <summary>CServerString's trigger group GetString (FUN_140a87690): a "$key$" text's string, any other text as it is.</summary>
    string ServerString(string key) => Model.Tables.TriggerStrings.GetString(key);

    /// <summary>
    /// ShowMessage (@0x1419f6810, :308): the panel (<see cref="SetPanel"/>), then the type's sounds: 0 and 1 fail; 2 the
    /// pc win, success and battle win; 5 the battle win; 6 also a chat line of kind 0x13 with "\n" made a line break.
    /// </summary>
    public void ShowMessage(int type, string text, int duration)
    {
        SetPanel(type, text, duration);
        switch (type)
        {
            case 0:
            case 1:
                SystemSound?.Invoke(FailSound);
                break;
            case 2:
                SystemSound?.Invoke(PcWinSound);
                SystemSound?.Invoke(SuccessSound);
                SystemSound?.Invoke(BattleWinSound);
                break;
            case 5:
                SystemSound?.Invoke(BattleWinSound);
                break;
            case 6:
                ChatLine?.Invoke((text ?? "").Replace("\\n", "\n"), TalkKind.SystemEvent);
                break;
        }
    }

    /// <summary>FUN_1419f6a70 (@0x1419f6a70): m_subType, m_text ("\n" made a line break) and m_duration, then clearAndSetPanel.</summary>
    void SetPanel(int type, string text, int duration)
    {
        if (Host?.Entity == null) return;
        Host.Set(Host.Entity, "m_subType", type);
        Host.Set(Host.Entity, "m_text", (text ?? "").Replace("\\n", "\n"));
        Host.Set(Host.Entity, "m_duration", duration);
        Host.Invoke("clearAndSetPanel");
    }

    /// <summary>
    /// ShowStepMessage (@0x1419f6bc0): m_nType, then m_subType 8, the text, m_stepNum, m_strStepNum (the step number text of
    /// <see cref="StepNumberKey"/>) and m_centerAlign false, clearAndSetPanel, and the countdown of the duration in seconds.
    /// </summary>
    public void ShowStepMessage(string text, int step, int duration, int type)
    {
        if (Host?.Entity == null) return;
        soundType = type;
        Host.Set(Host.Entity, "m_subType", 8);
        Host.Set(Host.Entity, "m_text", (text ?? "").Replace("\\n", "\n"));
        Host.Set(Host.Entity, "m_stepNum", step);
        Host.Set(Host.Entity, "m_strStepNum", ClientFormat.Format(Model.Tables.Common(StepNumberKey) ?? "", step));
        Host.Set(Host.Entity, "m_centerAlign", false);
        Host.Invoke("clearAndSetPanel");
        StartCountdown(duration);
    }

    /// <summary>StartCountdown (@0x1419f5ae0): m_nRemain and its deadline, then the first digit.</summary>
    void StartCountdown(int seconds)
    {
        remain = seconds;
        deadline = NowMs() + (uint)(remain * 1000);
        ShowDigit();
    }

    /// <summary>
    /// FUN_1419f5b30 (@0x1419f5b30): a second up to 9 shows frame 10 - second of the time clip's m_number and m_numberOutline
    /// (gotoAndStop), plays the clip from frame 1 and the count sound.
    /// </summary>
    void ShowDigit()
    {
        const int max = 9;
        if ((uint)remain > max) return;
        int digit = max - remain + 1;
        if (time != null)
        {
            if (Host.Child("m_number", time) is { } n) Host.Invoke(n, "gotoAndStop", digit);
            if (Host.Child("m_numberOutline", time) is { } o) Host.Invoke(o, "gotoAndStop", digit);
            Host.Invoke(time, "gotoAndPlay", 1);
        }
        PlayCountSound();
    }

    /// <summary>PlayCountSound (@0x1419f5c80): type 1 the voice of the second ("Count0" + the second) and the effect, type 2 the effect.</summary>
    void PlayCountSound()
    {
        switch (soundType)
        {
            case 1:
                SystemSound?.Invoke(ClientFormat.Format(CountVoiceFormat, (uint)remain));
                SystemSound?.Invoke(CountEffectSound);
                break;
            case 2:
                SystemSound?.Invoke(CountEffectSound);
                break;
        }
    }

    /// <summary>SetStage (@0x1419f6df0): setStage(stage, a, b, c); m_bStage is whether a is not 0.</summary>
    public void SetStage(int number, int a, int b, int c)
    {
        if (Host?.Entity == null) return;
        Host.Invoke("setStage", number, a, b, c);
        stage = a != 0;
    }

    /// <summary>ClearStage (@0x1419f5ab0): SetStage(0, 0, 0, 0); the game scene's exit runs it.</summary>
    public void ClearStage() => SetStage(0, 0, 0, 0);

    /// <summary>HidePanel (@0x1419f6eb0): onHidePanel.</summary>
    public void HidePanel()
    {
        if (Host?.Entity == null) return;
        Host.Invoke("onHidePanel");
    }
}
