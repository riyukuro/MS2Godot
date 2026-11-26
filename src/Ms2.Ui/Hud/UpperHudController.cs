using Ms2.Avm2;
using Ms2.Avm2.Flash;

namespace Ms2.Ui.Hud;

/// <summary>UIUpperHudDialog (ms2/client/ms2/uiupperhuddialog.cpp): the top bar with clock, meso, merat and the buttons.</summary>
public sealed class UpperHudController : UiController
{
    protected override bool IsDialog => false;          // UIHeadUpDisplay is a UIObject, not a UIDialog (uiheadupdisplay.h)

    public override string Movie => "uiupperhuddialog";
    protected override string EntityMethod => "arrangePos";

    /// <summary>CMouseController::HasMode, the game pad mode, PC bang grade (-1 = none), the scrolling notice.</summary>
    public bool MouseMode, GamePadMode;
    public int PcBangGrade = -1;
    public string Notice = "";
    public bool PartySearching, HotTime;
    public int MaidReady;

    /// <summary>
    /// The short commit a non-release build was made from, shown in m_version, the field the development client's watermark
    /// uses; "" (the default) in a release build, which shows what the retail client shows there: nothing, the field the
    /// movie hides stays hidden unless the watermark feature (0xc9) is on.
    /// </summary>
    public string BuildCommit = "";

    int hour = -1, minute = -1;

    AsDisplayObject Root => Host.Entity as AsDisplayObject;

    /// <summary>GFxManager::Localize: the Flash table's text of a "$" key (the key itself when it has none).</summary>
    string Localize(string key) => Host.Player.Translate?.Invoke(key) ?? key;

    /// <summary>OnLoadComplete (uiupperhuddialog.cpp:250).</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (Host.Child("m_searchButton") is { } search)
        {
            if (Model.IsFeatureEnabled(Feature.SearchTooltip))
                Host.Invoke(search, "changeTooltipText", Localize("$s_gamedictionary_tooltip_search"));   // GFxManager::Localize (:277)
            Listen(search, "buttonClick", OnSearchClick);                          // AddClickHandler(OnSearchClick)
        }
        if (Host.Child("m_tokenPocketButton") is { } tokenPocket)
            Listen(tokenPocket, "buttonClick", () => ToggleTokenPocket?.Invoke());     // AddClickHandler(OnTokenPocketClick)
        if (Host.Child("m_fishingBookButton") is { } fishingBook)
            Listen(fishingBook, "buttonClick", () => ToggleFishingBook?.Invoke());   // AddClickHandler(OnFishingBookClick)
        if (Host.Child("m_searchInput") is { } searchInput)
        {
            Listen(searchInput, "focusOut", OnSearchInputFocusOut);                 // AddFocusOutListener(FUN_141357050)
            Host.SetVisible(searchInput, false);
        }
        if (Model.IsFeatureEnabled(Feature.InputMode))
        {
            // UIUpperHudDialog::OnLoadComplete (uiupperhuddialog.cpp:285-297) & OnInputModeClick (:519-522)
            if (Host.Child("m_keyboardMode") is { } kb)
            {
                Listen(kb, "buttonClick", () => ToggleKeySettingSelect?.Invoke());
                Host.SetVisible(kb, !MouseMode);
            }
            if (Host.Child("m_mouseMode") is { } ms)
            {
                Listen(ms, "buttonClick", () => ToggleKeySettingSelect?.Invoke());
                Host.SetVisible(ms, MouseMode);
            }
        }
        if (Model.IsFeatureEnabled(Feature.GamePad))
        {
            // OnGamePadOnClick / OnGamePadOffClick (uiupperhuddialog.cpp:525-534): the lit button turns the pad off, the other on
            if (Host.Child("m_gamePadOn") is { } padOn) Listen(padOn, "buttonClick", () => GamePadClick?.Invoke(false));
            if (Host.Child("m_gamePadOff") is { } padOff) Listen(padOff, "buttonClick", () => GamePadClick?.Invoke(true));
            ShowGamePadMode();
        }
        bool watermark = Model.IsFeatureEnabled(Feature.VersionText);
        if (watermark && Host.Child("m_version") is { } ver)
        {
            Host.SetVisible(ver, true);
            Host.Set(ver, "htmlText", Model.Tables.Common("s_upper_hud_version") ?? "");
        }
        // After the watermark branch, the development client (uiupperhuddialog.cpp:316-322) also shows the field and makes
        // it multiline with or without 0xc9, so the movie's own hidden text ("version") shows. The retail client has no such
        // block (the GMS2 x64 build reads m_version only in its watermark branch, @0x140a6612e), so the field stays hidden there. A
        // non-release build of ours is the development client's case, with the build's commit in place of the movie's text.
        if (BuildCommit.Length > 0 && Host.Child("m_version") is { } version)
        {
            Host.SetVisible(version, true);
            Host.Set(version, "multiline", true);
            Host.Set(version, "htmlText", watermark ? (Model.Tables.Common("s_upper_hud_version") ?? "") + "\n" + BuildCommit : BuildCommit);
        }
        if (Host.Child("m_maidStatusButton") is AsDisplayObject maid) Host.SetChildVisible("badge", false, maid);
        UpdateMoney();
        UpdateHotTime();
        ResetClock();
        RefreshPcBang();
        Host.SetVisible(Host.Child("m_searchDungeon"), PartySearching);      // UpdateSearchDungeon
        RefreshNotice();
        Host.Invoke("arrangePos");
        UpdateMaidReadyCount();
        Shown();
    }

    /// <summary>CUIService::ToggleDialog(0x4f0b), the dictionary, from the search button.</summary>
    public event Action ToggleDictionary;

    /// <summary>OnTokenPocketClick (@0x141356cd0, uiupperhuddialog.cpp:479): CUIService::ToggleDialog(0x4ece), the token pocket.</summary>
    public event Action ToggleTokenPocket;

    /// <summary>OnFishingBookClick (@uiupperhuddialog.cpp:507): CUIService::ToggleDialog(0x4ee6), the fishing book.</summary>
    public event Action ToggleFishingBook;

    /// <summary>OnInputModeClick (@0x141356e20, uiupperhuddialog.cpp:519-522): CUIService::ToggleDialog(0x4f65), UIKeySettingSelectDialog.</summary>
    public event Action ToggleKeySettingSelect;

    /// <summary>The pad button's click, with the mode it asks for.</summary>
    public event Action<bool> GamePadClick;

    void ShowGamePadMode()
    {
        Host.SetVisible(Host.Child("m_gamePadOn"), GamePadMode);
        Host.SetVisible(Host.Child("m_gamePadOff"), !GamePadMode);
    }

    /// <summary>UIUpperHudDialog::SetGamePadMode (@0x1413574a0, uiupperhuddialog.cpp:630-638): returns without the pad feature (0xb5).</summary>
    public void SetGamePadMode(bool mode)
    {
        GamePadMode = mode;
        if (Model.IsFeatureEnabled(Feature.GamePad)) ShowGamePadMode();
    }

    /// <summary>UIUpperHudDialog::OnSkillSetMode @0x141357330 (uiupperhuddialog.cpp:602-612), the NotifySkillSetMode listener: the
    /// keyboard and mouse mode buttons (bound with feature 0xaf, :285-297) are off while a skill set fills the quick slots.</summary>
    public void OnSkillSetMode(bool mode)
    {
        if (Host?.Entity == null || !Model.IsFeatureEnabled(Feature.InputMode)) return;
        if (Host.Child("m_keyboardMode") is { } kb) Host.Set(kb, "enabled", !mode);
        if (Host.Child("m_mouseMode") is { } ms) Host.Set(ms, "enabled", !mode);
    }

    /// <summary>UIUpperHudDialog::SetInputMode (uiupperhuddialog.cpp:641-647, called from FUN_14125bd20): updates the visible keyboard/mouse mode indicator.</summary>
    public void SetMouseMode(bool mouseMode)
    {
        MouseMode = mouseMode;
        if (Host?.Entity == null || !Model.IsFeatureEnabled(Feature.InputMode)) return;
        Host.SetVisible(Host.Child("m_keyboardMode"), !MouseMode);
        Host.SetVisible(Host.Child("m_mouseMode"), MouseMode);
    }

    void Listen(object target, string type, Action a)
    {
        Func<object[], object> f = _ => { a(); return null; };
        Host.Invoke(target, "addEventListener", type, f, false, 0, false);
    }

    /// <summary>OnSearchClick @0x141356f90: the dictionary with feature 0x2f; without it the search input shows, focused,
    /// and the button is disabled until the input loses focus.</summary>
    void OnSearchClick()
    {
        if (Model.IsFeatureEnabled(Feature.SearchTooltip)) { ToggleDictionary?.Invoke(); return; }
        if (Host.Child("m_searchInput") is { } input)
        {
            Host.Set(input, "focused", 1);                                          // SetFocused(1)
            Host.SetVisible(input, true);                                           // vfunc_3(1)
        }
        if (Host.Child("m_searchButton") is { } search) Host.Set(search, "enabled", false);
    }

    /// <summary>FUN_141357050 (uiupperhuddialog.cpp:561): the shown search input that lost focus hides, emptied, and the
    /// search button is enabled again.</summary>
    void OnSearchInputFocusOut()
    {
        if (Host.Child("m_searchInput") is not { } input || !Rt.ToBoolean(Host.Get(input, "visible"))) return;
        if (Rt.ToNumber(Host.Get(input, "focused")) != 0) return;
        Host.SetVisible(input, false);
        Host.Set(input, "text", "");                                                // SetTextW(L"")
        Host.Invoke(input, "invalidate");                                           // vfunc_24(1)
        Host.Invoke(input, "validateNow");
        if (Host.Child("m_searchButton") is { } search) Host.Set(search, "enabled", true);
    }

    /// <summary>vfunc_46 (uiupperhuddialog.cpp:462), when the HUD is shown: m_marat hides without feature 0x3d, then arrangePos.</summary>
    void Shown()
    {
        if (!Model.IsFeatureEnabled(Feature.Merat)) Host.SetVisible(Host.Child("m_marat"), false);
        Host.Invoke("arrangePos");
    }

    ulong lastTick;
    /// <summary>vfunc_15 (uiupperhuddialog.cpp:424): once a second (m_timer.Check(1000)) the clock, the maid badge and the
    /// hot time; the notice scroll every frame (no notice is set here).</summary>
    public void Process(ulong nowMs)
    {
        if (Host?.Entity == null || nowMs - lastTick < 1000) return;
        lastTick = nowMs;
        UpdateClock();
        UpdateMaidReadyCount();
        UpdateHotTime();
    }

    /// <summary>SetChildText(root, "mesoText" / "maratText", number) (uiupperhuddialog.cpp:325; status events 8 and 11).</summary>
    void UpdateMoney()
    {
        Host.SetChildText("mesoText", Thousands(Pc.Mesos), Root);
        Host.SetChildText("maratText", Thousands(Pc.Merats), Root);
    }

    /// <summary>UpdateHotTime (uiupperhuddialog.cpp:744): m_effectHotTime shows during a hot time.</summary>
    void UpdateHotTime() => Host.SetVisible(Host.Child("m_effectHotTime"), HotTime);

    void ResetClock() { hour = minute = -1; UpdateClock(); }

    /// <summary>UpdateClock (uiupperhuddialog.cpp:693): the server time as "HH:mm" in m_time's timeText.</summary>
    public void UpdateClock()
    {
        var t = Model.Now;
        if (t.Hour == hour && t.Minute == minute) return;
        hour = t.Hour; minute = t.Minute;
        if (Host.Child("m_time") is AsDisplayObject time) Host.SetChildText("timeText", $"{hour:00}:{minute:00}", time);
        if (Host.FindNamed("m_clockIcon", Root) is { } icon) Host.Invoke(icon, "gotoAndStop", "clock");
    }

    /// <summary>RefreshPcBang / SetPcBangState (uiupperhuddialog.cpp:795, 972): KR shows m_pcBangButton in a PC bang.</summary>
    void RefreshPcBang()
    {
        Host.SetVisible(Host.Child("m_pcBangButton"), PcBangGrade >= 0);
        Host.SetVisible(Host.Child("m_pcBangButtonCn1"), false);
        Host.SetVisible(Host.Child("m_pcBangButtonCn2"), false);
    }

    /// <summary>RefreshNotice (uiupperhuddialog.cpp:819): no notice = m_eventText hidden.</summary>
    void RefreshNotice()
    {
        var ev = Host.Child("m_eventText");
        Host.SetVisible(ev, !string.IsNullOrEmpty(Notice));
        if (!string.IsNullOrEmpty(Notice)) Host.Set(Host.Child("textField", ev), "htmlText", Notice);
    }

    /// <summary>UpdateMaidReadyCount (uiupperhuddialog.cpp:710) through SetButtonBadge.</summary>
    void UpdateMaidReadyCount()
    {
        if (Host.Child("m_maidStatusButton") is not AsDisplayObject maid) return;
        Host.SetChildVisible("badge", MaidReady > 0, maid);
        Host.SetChildText("badgeCount", MaidReady.ToString(), maid);
    }

    /// <summary>vfunc_49 @0x1413565c0 (uiupperhuddialog.cpp:386): the token pocket, hot time and merat tooltips.</summary>
    public override IDictionary<string, object> GetTooltip(object[] args) => Ms2.Avm2.Rt.ToString(args.ElementAtOrDefault(0)) switch
    {
        "m_tokenPocketButton" => TokenPocketTooltip(),
        "m_effectHotTime" => HotTimeTooltip(),
        "m_marat" => MeratTooltip(),
        _ => null,
    };

    /// <summary>A text tooltip shown at once (ASTooltip(1, 2), SetTooltipClass tooltipText, SetOnDelay(0), align 7).</summary>
    static AsTooltip TextTooltip(string text, int offsetX, int offsetY)
    {
        var tip = new AsTooltip();
        tip["tooltip"] = "export_common_tooltip_tooltipText";
        tip["onDelay"] = 0;
        tip.SetTooltipAlign(7);
        tip["offsetX"] = offsetX;
        tip["offsetY"] = offsetY;
        tip["text"] = text;
        return tip;
    }

    /// <summary>FUN_1413586c0 (uiupperhuddialog.cpp:845): s_upper_hud_token_pocket and a line per token currency, each
    /// behind its feature (the star point line while the star point event runs).</summary>
    AsTooltip TokenPocketTooltip()
    {
        var text = new System.Text.StringBuilder(Model.Tables.Common("s_upper_hud_token_pocket") ?? "");
        if (Model.IsFeatureEnabled(Feature.PvPRankingDuelMode)) text.Append(CurrencyLine(4));
        if (Model.IsFeatureEnabled(Feature.ItemSocketSystem)) text.Append(CurrencyLine(5));
        text.Append(CurrencyLine(6));
        if (Model.IsFeatureEnabled(Feature.HabiToken)) text.Append(CurrencyLine(7));
        if (Model.IsFeatureEnabled(Feature.ReverseRaid01)) text.Append(CurrencyLine(0xc));
        if (Model.IsFeatureEnabled(Feature.Mentoring)) { text.Append(CurrencyLine(0xd)); text.Append(CurrencyLine(0xe)); }
        if (Pc.StarPointEvent) text.Append(CurrencyLine(0xf));
        if (Model.IsFeatureEnabled(Feature.MesoMarketToken)) text.Append(CurrencyLine(0x10));
        return TextTooltip(text.ToString(), 5, 0x16);
    }

    /// <summary>FUN_141900120(currency, icon, newline) (uicommon.cpp): "\n&lt;img src='icon' vspace='v'/&gt; amount[ / cap]",
    /// the amount and cap from GetCurrencyAmountAndCap (uicommon.cpp), the icon from FUN_1418fe870.</summary>
    string CurrencyLine(int currency)
    {
        if (!Model.Tables.TokenCaps.ContainsKey(currency)) return "";
        var (icon, vspace) = TokenCurrency.Icon(currency);
        return $"\n<img src='{icon}' vspace='{vspace}'/> " + TokenCurrency.AmountText(Model, currency);
    }

    /// <summary>GetHotTimeTooltip @0x141358b20 (uiupperhuddialog.cpp:877): the game rule's hot time event texts joined by
    /// line breaks. The hot time events (GetGameRule()->GetHotTimeEvents) are not fed: <see cref="HotTimeTexts"/>.</summary>
    AsTooltip HotTimeTooltip()
    {
        var text = string.Join("\n", HotTimeTexts.Where(t => !string.IsNullOrEmpty(t)));
        return text.Length == 0 ? null : TextTooltip(text, 0x16, 8);
    }

    /// <summary>The running hot time events' texts (SAHotTimeEventContext m_28) the player may see.</summary>
    public List<string> HotTimeTexts = new();

    /// <summary>GetMeratTooltip @0x141358df0 (uiupperhuddialog.cpp:913): s_upper_hud_merat around one
    /// s_upper_hud_merat_raw line for the free name with cash + free, one per non-zero market / event kind, and with
    /// red merats (feature 0x43) s_common_meratname_red with s_common_redmerat.</summary>
    AsTooltip MeratTooltip()
    {
        string Common(string key) => Model.Tables.Common(key) ?? "";
        string line = Common("s_upper_hud_merat_raw"), fmt = Common("s_common_merat");
        string Kind(int k) => k switch
        {
            0 => Common("s_common_meratname_cash"), 1 => Common("s_common_meratname_free"),
            2 => Common("s_common_meratname_market"), 3 => Common("s_common_meratname_event"), _ => "",
        };
        string Line(string name, string value) => Ms2.Quest.ClientFormat.Format(line, name, value);
        var w = Pc.MeratWallet;
        var list = new System.Text.StringBuilder(Line(Kind(1), Ms2.Quest.ClientFormat.Format(fmt, Thousands(w[0] + w[1]))));
        for (int i = 2; i < w.Length; i++)
            if (w[i] != 0) list.Append(Line(Kind(i), Ms2.Quest.ClientFormat.Format(fmt, Thousands(w[i]))));
        if (Model.IsFeatureEnabled(Feature.RedMerat))
            list.Append(Line(Common("s_common_meratname_red"), Ms2.Quest.ClientFormat.Format(Common("s_common_redmerat"), Thousands(Pc.RedMerat))));
        return TextTooltip(Ms2.Quest.ClientFormat.Format(Common("s_upper_hud_merat"), list.ToString()), 5, 0x19);
    }

    public override void OnStatus(StatusEvent e)
    {
        // vfunc_oa0_11 / vfunc_oa0_14 (uiupperhuddialog.cpp:437, 446): the merat text only with feature 0x3d
        if (e is StatusEvent.Merat && !Model.IsFeatureEnabled(Feature.Merat)) return;
        if (e is StatusEvent.Meso or StatusEvent.Merat) { UpdateMoney(); Host.Invoke("arrangePos"); }
    }
}
