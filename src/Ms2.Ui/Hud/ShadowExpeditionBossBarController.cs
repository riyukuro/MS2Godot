using Ms2.Avm2;
using Ms2.Quest;
using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// The field game's boss gauge, UIShadowExpeditionBossBar (client/ms2/uishadowexpeditionbossbar.cpp, 0x140f9cc60-0x140f9d2a0; UI id 0x4f0e,
/// type name "ShadowExpeditionBossBar") for its movie uishadowexpeditionbossbar. The dialog holds the current and the most points and the
/// title; OnLoadComplete (@0x140f9ceb0, :186) makes an ASScoreBoardGauge of the movie's library symbol "UIScoreGaugeTypeA" (a title) or
/// "UIScoreGaugeTypeB" (no title), initialises it (its "m_gauge" child must exist) and puts it on the dialog's root; every refresh
/// (<see cref="Refresh"/>, vfunc_85 @0x140f9d2a0) is the gauge's FUN_140f9ca30 (@0x140f9ca30, :228): the gauge's setValue(cur, max, true),
/// the title text and the progress text, the percent clamped to 0..100 (string id 0x98e).
/// The ctor (@0x140f9cc60) turns the tween on and sets flag 1 (<see cref="NotClosable"/>).
/// STILL-OPEN: a most of 0 divides by zero in the client's percent (the client faults); this port leaves the gauge as it is then and logs it
/// (<see cref="NotApplied"/>).
/// </summary>
public sealed class ShadowExpeditionBossBarController : UiController, IScoreBoardDialog
{
    public const int UiId = 0x4f0e;
    /// <summary>The library symbols of ASScoreBoardGauge (OnLoadComplete :190).</summary>
    public const string GaugeWithTitle = "UIScoreGaugeTypeA", GaugeWithoutTitle = "UIScoreGaugeTypeB";
    /// <summary>FormatStringId(0x98e, percent): the gauge's progress text.</summary>
    public const int ProgressStringId = 0x98e;
    /// <summary>vfunc_43 (@0x140f9ce00): the entity's ASGameEntity::vfunc_2(1, 0, 0x8c), top centre and this many pixels down.</summary>
    public const int TopOffset = 0x8c;

    public override string Movie => "uishadowexpeditionbossbar";
    protected override string EntityMethod => "alignment";
    /// <summary>GetTypeName (@0x140f9cdb0).</summary>
    public override string TypeName => "ShadowExpeditionBossBar";
    /// <summary>SetFlag(1, 1) in the ctor: CloseAllDialogs and the Esc key pass it by.</summary>
    public override bool NotClosable => true;

    /// <summary>m_nCur and m_nMax (the constructor zeroes them) and m_strTitle.</summary>
    public int Cur { get; private set; }
    public int Max { get; private set; }
    public string Title { get; private set; } = "";

    /// <summary>m_pGauge (the ASScoreBoardGauge clip) and its "m_gauge" child.</summary>
    object gauge, gaugeBar;

    /// <summary>The gauge clip, for the tests and the scripted runs.</summary>
    public object Gauge => gauge;

    /// <summary>Called when the gauge would divide by a most of 0 (the client's integer divide faults there).</summary>
    public Action<string> NotApplied;

    public ShadowExpeditionBossBarController()
    {
        Unloaded += OnUnloaded;
    }

    /// <summary>SetCurMax (@0x140f9d1f0).</summary>
    public void SetCurMax(int cur, int max) { Cur = cur; Max = max; }

    /// <summary>SetTitle (@0x140f9d230).</summary>
    public void SetTitle(string title) => Title = title ?? "";

    /// <summary>vfunc_84 (@0x140f9d280): the current points only.</summary>
    public void SetCur(int cur) => Cur = cur;

    /// <summary>vfunc_43 (@0x140f9ce00): UIDialog's layout, then the entity at the top centre, 0x8c down.</summary>
    public override void Layout(double stageW, double stageH)
    {
        base.Layout(stageW, stageH);
        if (Host?.Entity == null) return;
        Host.Align(Host.Entity, 1, 0, TopOffset);
    }

    /// <summary>
    /// OnLoadComplete (@0x140f9ceb0): UIDialog's, the gauge symbol by the title (non-empty = type A), Init, onto the root, then the
    /// first refresh (:197 runs it with or without a gauge; FUN_140f9ca30 is the gauge's own).
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        string linkage = Title.Length == 0 ? GaugeWithoutTitle : GaugeWithTitle;
        object clip = null;
        Host.RT.Guard("score gauge " + linkage, () => clip = Host.RT.Construct(Host.RT.GetClass(linkage), Array.Empty<object>()));
        if (clip == null) { Host.RT.NoteMissing("boss bar: " + linkage); return; }
        gaugeBar = Host.Child("m_gauge", clip);                              // ASScoreBoardGauge::Init (@0x140f9c890, :125)
        if (gaugeBar == null) { Host.RT.NoteMissing("boss bar: m_gauge of " + linkage); return; }
        gauge = clip;
        Host.Invoke(Host.Entity, "addChild", gauge);
        ShowValue();
    }

    /// <summary>vfunc_85 (@0x140f9d2a0): refresh the gauge when the entity and the gauge exist.</summary>
    public void Refresh()
    {
        if (Host?.Entity == null || gauge == null) return;
        ShowValue();
    }

    /// <summary>ASScoreBoardGauge::FUN_140f9ca30 (@0x140f9ca30, :228).</summary>
    void ShowValue()
    {
        if (Max == 0)                                                        // STILL-OPEN: the client's int division faults
        {
            NotApplied?.Invoke("shadow expedition boss bar: most 0, the client divides by zero");   // one text, so the once-log keeps it to one line
            return;
        }
        int percent = Math.Clamp(unchecked(Cur * 100) / Max, 0, 100);        // FUN_140f1ffe0 clamp; nCur * 100 is a 32 bit product in the client
        Host.Invoke(gaugeBar, "setValue", Cur, Max, true);
        SetText("m_title", Title);
        SetText("m_progressText", ClientFormat.Format(Model.Tables.Common(ProgressStringId) ?? "", percent.ToString()));
    }

    void SetText(string name, string text)
    {
        var clip = Host.Child(name, gauge);
        if (clip == null) return;
        var field = Host.Has(clip, "htmlText") ? clip : Host.Child("textField", clip);
        if (field != null) Host.Set(field, "text", text);
    }

    /// <summary>vfunc_26 (@0x140f9d0f0): the gauge leaves the root and the title is cleared; the count and the most stay.</summary>
    void OnUnloaded()
    {
        if (gauge != null && Host?.Entity != null) Host.RT.Guard("remove gauge", () => Host.Invoke(Host.Entity, "removeChild", gauge));
        gauge = null; gaugeBar = null;
        Title = "";
    }
}
