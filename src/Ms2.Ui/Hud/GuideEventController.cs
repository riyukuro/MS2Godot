using Ms2.Avm2;
using Ms2.Ui.Guide;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIGuideEvent (client/ms2/uiguideeventarrow.cpp, "GuideEvent", UI id 0x4e53, a UIHeadUpDisplay), the C++ side of
/// uiguideevent: the tutorial balloon, the summary panel and the text tips the guide's UI actions show, and the arrows
/// that point at other windows' controls. CUIService creates it at init and loads it with the game scene 0x620001d
/// (FUN_141ada6f0, uiservice.cpp:811); CGuideManager does nothing while it is missing or not loaded
/// (<see cref="GuideManager.UpdateEntities"/>, guideentity_1b.cpp).
/// <para>
/// The texts come from <see cref="GuideStrings"/> (LoadGuideStrings); the [[name:value]] tags of a text name the key
/// buttons and icons the movie builds (FUN_14195a1d0 splits them; the tag names compare case sensitively, and a tag
/// that is none of the known ones is dropped). The tip's close button calls back (onTextTipClose), and Esc runs
/// <see cref="SkipTextTip"/> (FUN_1418fcb70, uichatballoon.cpp:531).
/// </para>
/// <para>
/// STILL-OPEN: the arrows' position lookups (UIGuideEventArrow::vfunc_2's camera projection and
/// UIGuideEventArrowDialog::vfunc_2's dialog paths, uiguideeventarrow.cpp @0x14195e0e0), so every arrow adds hidden and
/// stays hidden, the client's behavior while its lookups fail; UpdateArrowSound (the tutorial_Sound_arrow effect loop
/// while an arrow shows) with them. The ChromaSDK lighting calls and vfunc_26 (the movie's unload, which clears the
/// child scripts) are not ported.
/// </para>
/// </summary>
public sealed class GuideEventController : UiController, IGuideEventPane
{
    /// <summary>The UI id (vfunc_48).</summary>
    public const int UiId = 0x4e53;

    public override string Movie => "uiguideevent";        // GetSwfFileName: "UI" + GetTypeName
    /// <summary>A method only the GuideEvent entity has (the host takes the first display object with it as m_30).</summary>
    protected override string EntityMethod => "showTextTip";
    protected override bool IsDialog => false;              // a UIHeadUpDisplay
    public override string TypeName => "GuideEvent";        // GetTypeName (@0x1419594f0)

    /// <summary>GetTickCountMs2: the millisecond tick the summary's expiry runs on (CheckSummaryExpiry).</summary>
    public Func<uint> NowMs = () => (uint)Environment.TickCount;
    /// <summary>The guide texts (LoadGuideStrings' m_strings and m_voices); null = none, every text reads empty.</summary>
    public GuideStrings Strings;
    /// <summary>PlaySystemSound(key, ""): a text tip that shows plays "system_guide_tooltip" (s_guideSound).</summary>
    public Action<string> SystemSound;
    /// <summary>CEffectService::PlayVoiceAt(GetGuideVoice(id), Point3::ZERO): the voice of a text with a voiceID; null
    /// plays nothing.</summary>
    public Action<string> PlayVoiceAt;
    /// <summary>The input device state the summary's keyAction tags read (GetGamepadState of the game option): 0 the
    /// keyboard, 1 a gamepad.</summary>
    public Func<int> GamepadState = () => 0;

    /// <summary>s_guideSound: the system sound of a text tip that shows.</summary>
    public const string TextTipSound = "system_guide_tooltip";

    int guide;                                      // m_guide: the tutorial / summary's guide id
    int tip;                                        // m_tip: the text tip showing's entity id
    readonly Queue<(int Id, int TextId)> tipQueue = new();   // m_tips: the tips waiting
    int lastTip;                                    // m_lastTip
    uint expiry;                                    // m_expiry: the summary's end tick, 0 none
    object tutorial, summary;                       // m_tutorial / m_summary: the child scripts (found on load)
    readonly Dictionary<int, PaneArrow> arrows = new();     // m_arrows: id -> the arrow, with its use count

    /// <summary>FUN_1422dc2d0 (m_4c): the movie is loaded; UpdateEntities waits on this.</summary>
    public bool Loaded => Host?.Entity != null;

    /// <summary>OnLoadComplete (@0x141959540): UIObject's, then the child scripts FUN_14195c230 finds by name.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        tutorial = Host.Child("guideTutorial");
        summary = Host.Child("guideSummary");
    }

    /// <summary>vfunc_43 (@0x141959750): the movie's alignment, which also aligns the children it shows.</summary>
    public override void Layout(double stageW, double stageH) => Host.Invoke(Host.Entity, "alignment");

    /// <summary>vfunc_28 (@0x1419598e0): the tip's close button (GuideTextTip.onTextTipClose).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (!string.Equals(name, "onTextTipClose", StringComparison.OrdinalIgnoreCase)) return false;
        AdvanceTextTip(0);
        return true;
    }

    /// <summary>vfunc_15 (@0x1419597b0), each frame: every arrow's position is pushed to the movie, then the arrow sound
    /// and the summary's expiry.</summary>
    public void Process()
    {
        if (!Loaded) return;
        foreach (var a in arrows.Values)
        {
            // vfunc_1 with vfunc_2 failed: the arrow is hidden. STILL-OPEN: the position lookups, until which every
            // arrow hides, as the client's do while their lookups fail.
            Host.Invoke("hideArrow", (uint)a.Id);
        }
        // UpdateArrowSound (@0x14195be30): no arrow can show yet, so the loop is never wanted.
        CheckSummaryExpiry();
    }

    /// <summary>vfunc_3 (@0x1419599a0), leaving the game scene: the movie unloads, the tips are dropped.</summary>
    public void Hide()
    {
        tip = 0;
        tipQueue.Clear();
        guide = 0;
        expiry = 0;
    }

    // ---- the summary ----------------------------------------------------------------------------------------------

    /// <summary>ShowSummary (@0x14195ab40): the panel's contents from the text's parts, then the movie's showSummary.
    /// An id other than the one that showed it is ignored at hide time only.</summary>
    public void ShowSummary(int entityId, int textId, int durationMs = 0)
    {
        if (!Loaded || summary == null) return;
        Host.Invoke(summary, "clearContents");
        FillText(summary, textId, summary: true);
        Host.Invoke("showSummary");
        PlayVoice(textId);
        guide = entityId;
        expiry = durationMs > 0 ? NowMs() + (uint)durationMs : 0;
    }

    /// <summary>HideSummary (@0x14195b540).</summary>
    public void HideSummary(int id)
    {
        if (!Loaded) return;
        if (id != 0 && guide != id) return;
        Host.Invoke("hideSummary");
        guide = 0;
        expiry = 0;
    }

    /// <summary>IsSummaryShowing (@0x14195b810).</summary>
    public bool SummaryShowing => summary != null && Rt.ToBoolean(Host.Invoke(summary, "isShowing"));

    /// <summary>CheckSummaryExpiry (@0x14195bfc0): the panel closes when its duration is up.</summary>
    void CheckSummaryExpiry()
    {
        if (expiry == 0) return;
        if (NowMs() >= expiry) HideSummary(guide);
    }

    // ---- the tutorial ---------------------------------------------------------------------------------------------

    /// <summary>ShowTutorial (@0x14195a520): the balloon's contents from the text's parts, then showTutorial (first) or
    /// the alignment with gotoAndPlayText (a later step).</summary>
    public void ShowTutorial(int entityId, int textId, bool first)
    {
        if (!Loaded || tutorial == null) return;
        Host.Invoke(tutorial, "clearContents");
        FillText(tutorial, textId, summary: false);
        PlayVoice(textId);
        if (first) Host.Invoke("showTutorial");
        else
        {
            Host.Invoke(tutorial, "alignment");
            Host.Invoke(tutorial, "gotoAndPlayText", "show");
        }
        guide = entityId;
    }

    /// <summary>HideTutorial (@0x14195b490).</summary>
    public void HideTutorial(int id)
    {
        if (!Loaded) return;
        if (id != 0 && guide != id) return;
        Host.Invoke("hideTutorial");
        guide = 0;
    }

    /// <summary>IsTutorialShowing (@0x14195b760).</summary>
    public bool TutorialShowing => tutorial != null && Rt.ToBoolean(Host.Invoke(tutorial, "isShowing"));

    // ---- the text tips --------------------------------------------------------------------------------------------

    /// <summary>QueueOrShowTextTip (@0x14195b330): a tip while one shows is queued; a showing one plays the sound.</summary>
    public void QueueOrShowTextTip(int entityId, int textId)
    {
        if (!Loaded) return;
        if (IsTextTipShowing) tipQueue.Enqueue((entityId, textId));
        else
        {
            Host.Invoke(Host.Entity, "showTextTip", GetGuideText((uint)textId));
            tip = entityId;
            SystemSound?.Invoke(TextTipSound);
        }
    }

    /// <summary>AdvanceTextTip (@0x14195b600): the next queued tip, or the showing one closes.</summary>
    public void AdvanceTextTip(int id)
    {
        if (id != 0 && tip != id) return;
        if (tipQueue.Count != 0)
        {
            var (next, textId) = tipQueue.Dequeue();
            if (Loaded) Host.Invoke("removeTextTip");
            QueueOrShowTextTip(next, textId);
        }
        else
        {
            if (Loaded) Host.Invoke("hideTextTip");
            tip = 0;
        }
        lastTip = tip;
    }

    /// <summary>IsTextTipShowing (@0x14195b8c0).</summary>
    public bool IsTextTipShowing => Loaded && Rt.ToBoolean(Host.Invoke("isShowTextTip"));

    /// <summary>SkipTextTip (@0x141959a20): Esc closes a showing tip; true when it did.</summary>
    public bool SkipTextTip()
    {
        if (!IsTextTipShowing) return false;
        AdvanceTextTip(0);
        return true;
    }

    /// <summary>GetLastTip (@0x14195b950): the entity id of the last closed tip.</summary>
    public int LastClosedTip => lastTip;

    // ---- the arrows -----------------------------------------------------------------------------------------------

    /// <summary>AddArrow (@0x141959a60): a second add of the id only bumps the use count; a new arrow's text part names
    /// its key (or its text) and the movie's addTextArrow / addArrow builds it hidden until its position is known.</summary>
    public void AddArrow(GuideArrow arrow)
    {
        if (arrows.TryGetValue(arrow.EntityId, out var existing))
        {
            existing.UseCount++;
            return;
        }
        var a = arrows[arrow.EntityId] = new PaneArrow(arrow);
        var info = Host.Obj(
            ("id", (uint)a.Id),
            ("align", (uint)arrow.Align),
            ("direction", (uint)arrow.Direction),
            ("x", 0f), ("y", 0f),
            ("hide", 1));            // vfunc_2 failed: no position source is ported
        var text = GetGuideText((uint)arrow.TextId);
        var part = NextPart(ref text, out bool special);
        if (special)
        {
            var (name, value) = TagNameValue(part);
            if (name == "keyText" || name == "keyAction" || name == "keyActionSub")
                info.SetDynamic("key", KeyOfTag(name, value, summary: false, gamepad: false));     // the ChromaSDK key light is not ported
        }
        else info.SetDynamic("text", part);
        if (Loaded)
        {
            if ((uint)arrow.TextId > 0)
            {
                PlayVoice(arrow.TextId);
                Host.Invoke("addTextArrow", info);
            }
            else Host.Invoke("addArrow", info);
        }
    }

    /// <summary>ReleaseArrow (@0x14195a0c0): the use count drops, and at zero the movie removes the arrow.</summary>
    public void ReleaseArrow(int id)
    {
        if (!arrows.TryGetValue(id, out var a)) return;
        if (--a.UseCount != 0) return;
        if (Loaded) Host.Invoke("removeArrow", (uint)id);
        arrows.Remove(id);
    }

    // ---- the texts -------------------------------------------------------------------------------------------------

    /// <summary>GetGuideText (@0x14195c010): the id's text with the value words replaced; "" without it.</summary>
    public string GetGuideText(uint id)
    {
        var text = Strings?.Text(id) ?? "";
        if (text.Length == 0) return text;
        return Model.Tables.ReservedWords.ReplaceValueWords(text, Model.HasLocalPlayer ? Model.Player.Name : null);
    }

    /// <summary>GetGuideVoice (@0x14195c0e0): the id's voice file; "" without it.</summary>
    public string GetGuideVoice(uint id) => Strings?.Voice(id) ?? "";

    /// <summary>PlayVoice (@0x14195bd90): the voice file of the id plays at Point3::ZERO.</summary>
    void PlayVoice(int textId)
    {
        var voice = GetGuideVoice((uint)textId);
        if (voice.Length != 0) PlayVoiceAt?.Invoke(voice);
    }

    /// <summary>
    /// The text of an id into the tutorial's or the summary's parts (ShowTutorial @0x14195a520 / ShowSummary
    /// @0x14195ab40): every plain part is added as text, every known tag as its key button (the tutorial also has the
    /// direction keys) or icon; a tag that is none of them is dropped, and only the summary answers to a gamepad with
    /// plain text.
    /// </summary>
    void FillText(object target, int textId, bool summary)
    {
        bool gamepad = GamepadState() != 0;
        var text = GetGuideText((uint)textId);
        while (text.Length != 0)
        {
            var part = NextPart(ref text, out bool special);
            if (!special)
            {
                Host.Invoke(target, "addText", part);
                continue;
            }
            var (name, value) = TagNameValue(part);
            if (name == "keyText")
                Host.Invoke(target, "addKeyButton", value);
            else if (summary && name == "icon")
                Host.Invoke(target, "addIcon", value);
            else if (!summary && name == "keyDirection")
                Host.Invoke(target, "addDirectionKey");
            else if (name == "keyAction" || name == "keyActionSub")
                Host.Invoke(target, gamepad && summary ? "addText" : "addKeyButton", KeyOfTag(name, value, summary, gamepad));
        }
    }

    /// <summary>
    /// A keyAction / keyActionSub tag's key name: FUN_141389500's action id through GetActionKeyName
    /// (keytable.cpp:1395). Only the summary on a gamepad shows the gamepad's binding (priority 2) as plain text; a
    /// keyboard takes the main binding (0), and the sub binding (1) of a keyActionSub anywhere else — the arrows and the
    /// tutorial have no gamepad branch.
    /// </summary>
    string KeyOfTag(string name, string value, bool summary, bool gamepad)
    {
        int action = GuideNames.ActionId(value);
        int priority = summary && gamepad ? 2 : name == "keyActionSub" ? 1 : 0;
        return Model.Keys.GetKeyName(Model.Keys.FindKeyByActionAndPriority(action, priority));
    }

    /// <summary>
    /// FUN_14195a1d0: the next part of a guide text, out of <paramref name="text"/>: a plain run up to the next "[[",
    /// or the "[[...]]" tag's inside (<paramref name="special"/>). What follows goes back into
    /// <paramref name="text"/>, which is empty at the end.
    /// </summary>
    static string NextPart(ref string text, out bool special)
    {
        special = false;
        if (text.Length < 5) { var all = text; text = ""; return all; }
        int i = text.IndexOf("[[", StringComparison.Ordinal);
        if (i < 0) { var all = text; text = ""; return all; }
        if (i > 0) { var head = text[..i]; text = text[i..]; return head; }
        i = text.IndexOf("]]", StringComparison.Ordinal);
        if (i < 0) { var all = text; text = ""; return all; }
        var inside = text.Substring(2, i - 2);
        text = text[(i + 2)..];
        special = true;
        return inside;
    }

    /// <summary>ParseTagNameValue (@0x14195bc50): the tag split on ":" into exactly two parts; anything else is no name
    /// and no value, so no tag answers to it.</summary>
    static (string Name, string Value) TagNameValue(string tag)
    {
        var parts = tag.Split(':', StringSplitOptions.RemoveEmptyEntries);    // FUN_14025a680 drops empty parts
        return parts.Length == 2 ? (parts[0], parts[1]) : ("", "");
    }

    /// <summary>An arrow the pane keeps (UIGuideEventArrow): its record and how many actions hold it.</summary>
    sealed class PaneArrow
    {
        public readonly GuideArrow Arrow;
        public int UseCount = 1;
        public PaneArrow(GuideArrow arrow) => Arrow = arrow;
        public int Id => Arrow.EntityId;
    }
}
