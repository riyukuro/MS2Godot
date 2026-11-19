using Ms2.Avm2;

namespace Ms2.Ui.Hud;

/// <summary>
/// UISystemText (ms2/client/ms2/uisystemtext.cpp, "SystemText", UI id 0x4e41, a UIHeadUpDisplay), the C++ side of
/// uisystemtext: the HUD's system lines across the upper middle of the screen (the red warnings, the info, quest, GM,
/// letter, emergency, interact and guild megaphone lines, the AI warning). The movie's SystemText entity queues, plays
/// and fades each line; this side picks the AS method by the message type and parks a message shown before the movie
/// is loaded.
/// vfunc_3's own part runs at a field unload (<see cref="ForgetParked"/>). STILL-OPEN: CUIService::HideWorldUI /
/// ShowWorldUI's calls of vfunc_2 / vfunc_3 (uiservice.cpp:960, :995) are not ported: this HUD has no world UI hide. The field name line
/// (CUIService::FUN_141adba40, uiservice.cpp:1346: ShowMessage(the field's name, 7, 0) on field entry when
/// GetFieldNameId != 0) is not fed.
/// </summary>
public sealed class SystemTextController : UiController
{
    public override string Movie => "uisystemtext";       // GetSwfFileName: "UI" + GetTypeName (uiswfentity.cpp:100)
    protected override string EntityMethod => "appendWarningMessage";
    protected override bool IsDialog => false;              // a UIHeadUpDisplay
    public override string TypeName => "SystemText";        // GetTypeName (:149)

    /// <summary>The message types ShowMessage takes (the AS method each one calls, :236).</summary>
    public const int Warning = 1, Warning2 = 2, Info = 3, Info2 = 4, Quest = 5, Gm = 6, FieldName = 7, Letter = 8,
        Emergency = 9, Interact = 10, GuildMegaphone = 11, AiWarning = 0xc;

    /// <summary>GetTickCountMs2: the millisecond tick the timed messages expire by.</summary>
    public Func<uint> TickMs = () => 0;
    /// <summary>The target HP bar (UIUpperHpBar, 0x4e3d) the lines are placed under (vfunc_43).</summary>
    public UiController UpperHpBar;
    /// <summary>PlaySystemSound(key, ""): the emergency line plays "emergency_notice" (s_emergencyNotice, :160).</summary>
    public event Action<string> SystemSound;

    int parkedType;             // m_70: the parked message's type, 0 = none
    string parkedText = "";     // m_78
    uint parkedExpiry;          // m_80: the parked timed message's expiry tick
    readonly SortedDictionary<int, uint> timed = new();     // m_88: type -> expiry tick (std::map<int, unsigned int>)

    /// <summary>
    /// vfunc_3 (@0x141b15e80, uisystemtext.cpp:225) after UIHeadUpDisplay::vfunc_3's unload: the parked message is
    /// forgotten (m_78.Empty(), m_70 = 0; m_80 is left). UISceneGame::vfunc_56 (WorldUnLoadComplete, @0x141ac78f0) runs
    /// it at a field unload, so a line parked during the old field is not shown in the next one.
    /// </summary>
    public void ForgetParked()
    {
        parkedText = "";
        parkedType = 0;
    }

    /// <summary>The parked message (m_70, m_78), for the tests.</summary>
    public (int Type, string Text) Parked => (parkedType, parkedText);

    bool Loaded => Host?.Entity != null;     // MS2Ptr<ASGameEntity>(dynamic_cast(m_30)).get()

    /// <summary>TSingleton&lt;CReservedWordTable&gt;::ReplaceValueWords, run on every text before the movie gets it, with the
    /// local player's name. STILL-OPEN: the player's gender ($male / $female) is not in this model, so those words are
    /// empty, as in the item tooltip.</summary>
    string ReplaceValueWords(string text) =>
        Model.Tables.ReservedWords.ReplaceValueWords(text, Model.HasLocalPlayer ? Model.Player.Name : null);

    /// <summary>
    /// OnLoadComplete (:155): UIObject's, then the parked message replayed: a timed one (type 0xc) only while before its
    /// expiry, with the time left, any other by ShowMessage(text, type, 0); the parked fields are cleared.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (parkedType == 0) return;
        if (parkedType == AiWarning)
        {
            uint now = TickMs();
            if (now < parkedExpiry) ShowTimedMessage(parkedText, parkedType, (int)(parkedExpiry - now));
        }
        else ShowMessage(parkedText, parkedType, 0);
        parkedText = "";
        parkedType = 0;
        parkedExpiry = 0;
    }

    /// <summary>
    /// vfunc_15 (:177), each frame: with the movie loaded, the timed messages past their expiry are erased; an expired
    /// AI warning first calls the movie's onAiWarningMessage (the next queued line, or the fade).
    /// </summary>
    public void Process()
    {
        if (!Loaded || timed.Count == 0) return;
        uint now = TickMs();
        foreach (var (type, expiry) in timed.ToList())
        {
            if (expiry > now) continue;
            if (type == AiWarning) Host.Invoke("onAiWarningMessage");
            timed.Remove(type);
        }
    }

    /// <summary>
    /// vfunc_43 (:201): the movie aligned top centre (ASDisplayObject::vfunc_2(1, 0, top)); the top is 100, or, when the
    /// upper HP bar's movie is loaded, its root's y + ceil(height) (GetHeightCeil, asdisplayobject.cpp:546) + 10.
    /// </summary>
    public override void Layout(double stageW, double stageH)
    {
        if (!Loaded) return;
        int top = 100;
        if (UpperHpBar?.Host?.Entity is { } upper)
        {
            float y = (float)Rt.ToNumber(UpperHpBar.Host.Get(upper, "y"));
            int h = (int)Math.Ceiling(Rt.ToNumber(UpperHpBar.Host.Get(upper, "height")));
            top = (int)(y + (float)h + 10.0f);
        }
        Host.Align(Host.Entity, 1, 0, top);
    }

    /// <summary>
    /// ShowMessage (@0x141b15ec0): with the movie loaded, the AS method of the type gets the text after
    /// ReplaceValueWords; the emergency line (9) also gets the option and plays "emergency_notice". A type with no method
    /// invokes the empty name, which the movie does not have. Without the movie the type and text are parked.
    /// </summary>
    public void ShowMessage(string text, int type, int option)
    {
        text ??= "";
        if (!Loaded)
        {
            parkedType = type;
            parkedText = text;
            return;
        }
        string method = type switch
        {
            Info => "appendInfoMessage", Info2 => "appendInfoMessage2",
            Warning => "appendWarningMessage", Warning2 => "appendWarningMessage2",
            Quest => "appendQuestMessage", Gm => "setGmMessage", FieldName => "setFieldName",
            Letter => "appendLetterMessage", Emergency => "appendEmergencyMessage",
            Interact => "appendInteractMessage", GuildMegaphone => "appendGuildMegaphone",
            _ => "",
        };
        string filtered = ReplaceValueWords(text);
        if (type == Emergency)
        {
            Host.Invoke(Host.Entity, method, filtered, option);
            SystemSound?.Invoke("emergency_notice");
        }
        else Host.Invoke(Host.Entity, method, filtered);
    }

    /// <summary>
    /// ShowTimedMessage (@0x141b16270): only the AI warning (0xc) has a method, appendAiWarningMessage. With the movie
    /// loaded the text goes to it; a timed message of the same type still up is ended first (onAiWarningMessage) and
    /// replaced, and the type expires at now + duration. Without the movie the type, text and expiry are parked.
    /// </summary>
    public void ShowTimedMessage(string text, int type, int duration)
    {
        text ??= "";
        uint expiry = unchecked((uint)duration + TickMs());
        if (!Loaded)
        {
            parkedType = type;
            parkedText = text;
            parkedExpiry = expiry;
            return;
        }
        if (type != AiWarning) return;
        Host.Invoke(Host.Entity, "appendAiWarningMessage", ReplaceValueWords(text));
        if (timed.ContainsKey(type))
        {
            Host.Invoke("onAiWarningMessage");      // it->first == 0xc
            timed.Remove(type);
        }
        timed[type] = expiry;
    }

    /// <summary>The timed messages' expiry ticks (m_88), for the tests.</summary>
    public IReadOnlyDictionary<int, uint> Timed => timed;
}
