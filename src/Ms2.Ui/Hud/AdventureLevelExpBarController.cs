using System.Globalization;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIAdventureLevelExpBar (ms2/unnamed/unit_140ba6b20_uiadventurelevelexpbar.cpp, "AdventureLevelExpBar", UI 0x4f66): the
/// prestige exp bar over the exp bar, shown with feature 0xbd.
/// </summary>
public sealed class AdventureLevelExpBarController : UiController
{
    public override string Movie => "uiadventurelevelexpbar";
    protected override string EntityMethod => "setPoint";
    protected override bool IsDialog => false;          // a UIHeadUpDisplay, not a UIDialog

    public const int Feature = 0xbd;

    /// <summary>CGameOption status m_bShowPoint: the percent label on the bar.</summary>
    public bool ShowPoint = true;
    /// <summary>DAT_1439f8ccc: the exp bar's height (FUN_1418dd0c0 = it - 2).</summary>
    public int ExpBarHeight = 21;

    /// <summary>vfunc_43 (@0x140ba7360): width = screen width x movie scale, setExpBarHeight(FUN_1418dd0c0()), then
    /// ASGameEntity::vfunc_25 = Invoke("alignment").</summary>
    public override void Layout(double stageW, double stageH)
    {
        Host.Set(Host.Entity, "width", stageW);
        Host.Invoke("setExpBarHeight", (uint)(ExpBarHeight - 2));
        Host.Invoke("alignment");
    }

    /// <summary>OnLoadComplete (@0x140ba72b0): without feature 0xbd the bar hides (vfunc_3); else SetPoint(0), the point
    /// label by the option, and the mount check (FUN_140ba7130).</summary>
    public override void OnLoadComplete()
    {
        if (!Model.IsFeatureEnabled(Feature)) { Host.SetVisible(Host.Entity, false); return; }
        SetPoint(0);
        Host.SetVisible(Host.Child("m_point"), ShowPoint);
        RideVisibility();
    }

    /// <summary>FUN_140ba7130 (uiadventurelevelexpbar.cpp:182-196): the bar hides while the own mount's hidePCInfo is set;
    /// OnLoadComplete and the riding status event (vfunc_oa0_20, :243-246) run it.</summary>
    void RideVisibility() => Host.SetVisible(Host.Entity, RidingUi.PcInfoShown(Pc.Riding ? Pc.Ridee : null));

    /// <summary>The status listener, added only with feature 0xbd (OnLoadComplete, :213-215).</summary>
    public override void OnStatus(StatusEvent e)
    {
        if (e == StatusEvent.Riding && Model.IsFeatureEnabled(Feature)) RideVisibility();
    }

    long ExpPerLevel => Model.Tables.AdventureLevelLvUpExp;

    /// <summary>GetDisplayedExp (@0x140ba6ff0): the player's adventure level at or below DAT_143ab2960 shows a full level,
    /// the level just above it DAT_143ab2968, any other 0.</summary>
    long DisplayedExp
    {
        get
        {
            var p = Pc;
            if ((uint)p.AdventureLevel <= (uint)p.AdventureFilledLevel) return ExpPerLevel;
            if (p.AdventureLevel == p.AdventureFilledLevel + 1) return p.AdventureNextExp;
            return 0;
        }
    }

    /// <summary>SetPoint (@0x140ba6cf0): setPoint(cur %, displayed %, StringFormat("0", gain) = "0", level &gt;=
    /// AdventureLevelStartLevel).</summary>
    public void SetPoint(long gain)
    {
        float per = ExpPerLevel;
        float cur = (float)(Pc.AdventureExp * 100) / per;
        float next = (float)(DisplayedExp * 100) / per;
        Host.Invoke("setPoint", cur, next, "0", Pc.Level >= Model.Tables.AdventureLevelStartLevel);
    }

    /// <summary>vfunc_45 "getExp" (@0x140ba74a0): s_tooltip_adventure_exp with cur and the exp per level, each with the
    /// thousands separator (FUN_1409404f0 = FUN_14083cb20(%I64d)).</summary>
    public override IDictionary<string, object> GetUserData(string name, object[] args)
    {
        if (name != "getExp") return null;
        var fmt = Model.Tables.Common("s_tooltip_adventure_exp") ?? "";
        string text = fmt.Replace("{0}", Thousands(Pc.AdventureExp)).Replace("{1}", Thousands(ExpPerLevel));
        return new Dictionary<string, object> { ["text"] = text };
    }

    /// <summary>vfunc_28 "OnClickBar" (@0x140ba7660): ToggleDialog(0x4f67) with feature 0xfb, else 0x4f87.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (string.Equals(name, "OnClickBar", StringComparison.OrdinalIgnoreCase)) { BarClicked?.Invoke(); return true; }
        return false;
    }

    public event Action BarClicked;
}
