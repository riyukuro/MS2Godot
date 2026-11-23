using Ms2.Avm2;
using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// UICutSceneCinematic (ms2/client/ms2/uicutscenecinematic.cpp, UI id 0x4e52, movie "uicutscenecinematic"): the C++ side of the
/// movie's CutSceneCinematic entity, on the generated route (the movie runs its own ActionScript; this class makes the calls the
/// client makes on it). The state and the timed lists are <see cref="CutSceneCinematic"/>; this runs its <see cref="ICinematicPane"/>
/// calls on the movie: setCinematic, setOpening, setIntro, setScript, hideScript, setSkip, setCaption and Close's hideScript,
/// hideDirect and "_out".
/// </summary>
public sealed class CutSceneCinematicController : UiController, ICinematicPane
{
    public override string Movie => "uicutscenecinematic";
    /// <summary>CutSceneCinematic's own method (the movie's CutSceneCinematic class).</summary>
    protected override string EntityMethod => "setCinematic";
    protected override bool IsDialog => false;                  // a UISwfEntity on layer 4
    public override string TypeName => "CutSceneCinematic";

    public readonly CutSceneCinematic State;

    /// <summary>DAT_143af62d4 / DAT_143af62d8, the client size, and GFxManager::GetMovieScale (vfunc_43 @0x141907b60).</summary>
    public int ClientWidth = 1280, ClientHeight = 720;
    public double MovieScale = 1;

    public CutSceneCinematicController(CutSceneCinematic state)
    {
        State = state;
        State.Pane = this;
    }

    object skipListenerOn;
    Func<object[], object> skipClick;

    /// <summary>
    /// OnLoadComplete (:~165): m_skip is found and its "click" (FUN_1422c2a70, asmovieclip.cpp:236) requests the skip, the mode
    /// in force is played again (setCinematic, setOpening or setIntro) and the skip prompt is set again. The client loads the movie
    /// once per OnLoadComplete; here the click listener is added once per entity object, because each Invoke wraps the delegate anew.
    /// </summary>
    public override void OnLoadComplete()
    {
        if (Host.Entity == null) return;
        if (Host.Child("m_skip") is { } skip && !ReferenceEquals(skipListenerOn, skip))
        {
            skipListenerOn = skip;
            Host.Invoke(skip, "addEventListener", "click", skipClick ??= _ => { State.RequestSkip(); return null; }, false, 0, false);
        }
        State.Replay();
    }

    /// <summary>vfunc_43 (@0x141907b60): align the entity (5, 1, 1), then setStageSize(client width, client height, movie scale).</summary>
    public override void Layout(double stageW, double stageH)
    {
        if (Host.Entity == null) return;
        Host.Align(Host.Entity, 5, 0, 0);
        Host.Invoke("setStageSize", ClientWidth, ClientHeight, MovieScale);
    }

    // ---- ICinematicPane ---------------------------------------------------------------------------------------------

    public bool Loaded => Host?.Entity != null;

    public void SetCinematic(int id, string text) { if (Host?.Entity != null) Host.Invoke(Host.Entity, "setCinematic", (uint)id, text); }
    public void SetOpening(string text, bool isOpening) { if (Host?.Entity != null) Host.Invoke(Host.Entity, "setOpening", text, isOpening); }
    public void SetIntro(string text) { if (Host?.Entity != null) Host.Invoke(Host.Entity, "setIntro", text); }
    public void SetScript(string name, string text, string illust, int align) { if (Host?.Entity != null) Host.Invoke(Host.Entity, "setScript", name, text, illust, align); }
    public void HideScript() { if (Host?.Entity != null) Host.Invoke("hideScript"); }

    /// <summary>FUN_1419080b0: hideScript, hideDirect, then gotoAndPlay("_out") on m_bgInstance and m_document (FUN_140cffa50).</summary>
    public void Close()
    {
        if (Host?.Entity == null) return;
        Host.Invoke("hideScript");
        Host.Invoke("hideDirect");
        foreach (var member in new[] { "m_bgInstance", "m_document" })
            if (Host.Child(member) is { } clip) Host.Invoke(clip, "gotoAndPlay", "_out");
    }

    public void SetSkip(bool on, string key, string label, bool skipVisible)
    {
        if (Host?.Entity == null) return;
        if (skipVisible && Host.Child("m_skip") is { } skip) Host.SetVisible(skip, true);
        Host.Invoke("setSkip", on, key, label);
    }

    public void SetSkipVisible(bool visible)
    {
        if (Host?.Entity != null && Host.Child("m_skip") is { } skip) Host.SetVisible(skip, visible);
    }

    /// <summary>OnPacketSetCaption (:727): the caption object (captionID 0, the type, the translated texts, the align, the offsets, the
    /// duration and the scale) goes to setCaption.</summary>
    public void SetCaption(CinematicPacket.Caption c, string text1, string text2)
    {
        if (Host?.Entity == null) return;
        var o = Host.Obj();
        Host.Set(o, "captionID", 0);
        Host.Set(o, "captionType", c.Type);
        Host.Set(o, "captionStr1", text1);
        Host.Set(o, "captionStr2", text2);
        Host.Set(o, "captionAlign", c.Align);
        Host.Set(o, "offsetRatioX", (double)c.OffsetX);
        Host.Set(o, "offsetRatioY", (double)c.OffsetY);
        Host.Set(o, "captionDuration", c.Duration);
        Host.Set(o, "captionScale", (double)c.Scale);
        Host.Invoke("setCaption", o);
    }
}
