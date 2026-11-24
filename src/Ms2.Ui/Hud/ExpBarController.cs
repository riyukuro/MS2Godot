using System.Globalization;

namespace Ms2.Ui.Hud;

/// <summary>UIExpBar (ms2/client/ms2/uiexpbar.cpp): the exp bar along the bottom edge.</summary>
public sealed class ExpBarController : UiController
{
    public override string Movie => "uiexpbar";
    protected override string EntityMethod => "setExp";
    protected override bool IsDialog => false;          // a UIObject, not a UIDialog

    /// <summary>CGameOption status m_bShowPoint: the percent label on the bar.</summary>
    public bool ShowPoint = true;

    /// <summary>vfunc_43 (uiexpbar.cpp:188): width = screen width x movie scale (the stage width; ASDisplayObject::SetWidthFloat
    /// sets the AS width, which the CLIK component takes as its size), then ASGameEntity::vfunc_25 = Invoke("alignment")
    /// (asgameentity.cpp:26). The bar symbol is placed at 1.04 scale, so it reaches past both screen edges, centred.</summary>
    public override void Layout(double stageW, double stageH)
    {
        Host.Set(Host.Entity, "width", stageW);
        Host.Invoke("alignment");
    }

    /// <summary>OnLoadComplete (uiexpbar.cpp:199): UpdateExp(3), the point label, the status listener.</summary>
    public override void OnLoadComplete()
    {
        UpdateExp(3);
        Host.SetVisible(Host.Child("m_point"), ShowPoint);
        RideVisibility();
    }

    public override void OnStatus(StatusEvent e)
    {
        if (e is StatusEvent.Exp or StatusEvent.Level) UpdateExp(3);
        if (e == StatusEvent.Riding) RideVisibility();          // vfunc_oa0_20 (uiexpbar.cpp:253-256)
    }

    /// <summary>FUN_141932430 (uiexpbar.cpp:168-184): the bar hides while the own mount's hidePCInfo is set.</summary>
    void RideVisibility() => Host.SetVisible(Host.Entity, RidingUi.PcInfoShown(Pc.Riding ? Pc.Ridee : null));

    /// <summary>UpdateExp (uiexpbar.cpp:115): setExp(cur %, rest %, text, flags); text only with flag 1.</summary>
    public void UpdateExp(uint flags)
    {
        if (flags == 0) return;
        long max = Model.NextLevelExp;
        float cur = max == 0 ? 0 : (float)(Pc.Exp * 100) / max;
        float rest = max == 0 ? 0 : (float)(Pc.RestExp * 100) / max;
        // FormatStringId(0x98e, "{0:.2f}"): the string is built into the client; its text is the percent with a % sign
        string text = (flags & 1) != 0 ? cur.ToString("0.00", CultureInfo.InvariantCulture) + "%" : "";
        Host.Invoke("setExp", cur, rest, text, flags);
    }

    /// <summary>vfunc_45 "getExp" (uiexpbar.cpp:210): the bar's tooltip text, cur / next and the rest exp line.</summary>
    public override IDictionary<string, object> GetUserData(string name, object[] args)
    {
        if (name != "getExp") return null;
        // FormatStringId(0x769, cur, next): built-in string, "cur / next"
        string text = $"{Thousands(Pc.Exp)} / {Thousands(Model.NextLevelExp)}";
        if (Pc.RestExp > 0 && Model.NextLevelExp > 0)
        {
            float pct = Math.Min(100f, (float)((Pc.Exp + Pc.RestExp) * 100) / Model.NextLevelExp);
            pct = Math.Max(0.01f, pct);
            var fmt = Model.Tables.Common("s_tooltip_rest_exp");
            if (fmt != null) text += Format(fmt, Pc.Level, pct, Model.RestExpRatePercent);
        }
        return new Dictionary<string, object> { ["text"] = text };
    }

    public override bool OnCallback(string name, object[] args)
    {
        if (string.Equals(name, "OnClickBar", StringComparison.OrdinalIgnoreCase)) { BarClicked?.Invoke(); return true; }   // ToggleDialog(0x4efa)
        return false;
    }

    /// <summary>OnClickBar: the client toggles UI 0x4efa.</summary>
    public event Action BarClicked;

    /// <summary>The client's {0} / {1:.2f} placeholders.</summary>
    static string Format(string fmt, params object[] a)
    {
        var s = fmt;
        for (int i = 0; i < a.Length; i++)
        {
            s = System.Text.RegularExpressions.Regex.Replace(s, "\\{" + i + "(?::\\.?(\\d+)f)?\\}", m =>
                m.Groups[1].Success ? Convert.ToDouble(a[i], CultureInfo.InvariantCulture).ToString("F" + m.Groups[1].Value, CultureInfo.InvariantCulture)
                                    : Convert.ToString(a[i], CultureInfo.InvariantCulture));
        }
        return s;
    }
}
