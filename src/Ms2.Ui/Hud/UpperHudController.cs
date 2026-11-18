using Ms2.Avm2.Flash;

namespace Ms2.Ui.Hud;

/// <summary>UIUpperHudDialog (ms2/client/ms2/uiupperhuddialog.cpp): the top bar with clock, meso, merat and the buttons.</summary>
public sealed class UpperHudController : UiController
{
    public override string Movie => "uiupperhuddialog";
    protected override string EntityMethod => "arrangePos";

    /// <summary>CMouseController::HasMode, the game pad mode, PC bang grade (-1 = none), the scrolling notice.</summary>
    public bool MouseMode, GamePadMode;
    public int PcBangGrade = -1;
    public string Notice = "";
    public bool PartySearching, HotTime;
    public int MaidReady;

    int hour = -1, minute = -1;

    AsDisplayObject Root => Host.Entity as AsDisplayObject;

    /// <summary>OnLoadComplete (uiupperhuddialog.cpp:250).</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (Model.IsFeatureEnabled(Feature.SearchTooltip) && Host.Child("m_searchButton") is { } search)
            Host.Invoke(search, "changeTooltipText", Model.Tables.Common("s_gamedictionary_tooltip_search") ?? "");
        Host.SetVisible(Host.Child("m_searchInput"), false);
        if (Model.IsFeatureEnabled(Feature.InputMode))
        {
            Host.SetVisible(Host.Child("m_keyboardMode"), !MouseMode);
            Host.SetVisible(Host.Child("m_mouseMode"), MouseMode);
        }
        if (Model.IsFeatureEnabled(Feature.GamePad))
        {
            Host.SetVisible(Host.Child("m_gamePadOn"), GamePadMode);          // SetGamePadMode
            Host.SetVisible(Host.Child("m_gamePadOff"), !GamePadMode);
        }
        if (Model.IsFeatureEnabled(Feature.VersionText) && Host.Child("m_version") is { } ver)
        {
            Host.SetVisible(ver, true);
            Host.Set(ver, "htmlText", Model.Tables.Common("s_upper_hud_version") ?? "");
        }
        // the version field is shown and multiline with or without 0xc9 (uiupperhuddialog.cpp:307)
        if (Host.Child("m_version") is { } version) { Host.SetVisible(version, true); Host.Set(version, "multiline", true); }
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

    public override void OnStatus(StatusEvent e)
    {
        // vfunc_oa0_11 / vfunc_oa0_14 (uiupperhuddialog.cpp:437, 446): the merat text only with feature 0x3d
        if (e is StatusEvent.Merat && !Model.IsFeatureEnabled(Feature.Merat)) return;
        if (e is StatusEvent.Meso or StatusEvent.Merat) { UpdateMoney(); Host.Invoke("arrangePos"); }
    }
}
