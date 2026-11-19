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
/// <see cref="SkipTextTip"/> (FUN_1418fcb70, uichatballoon.cpp:531). The arrows resolve their places through the
/// host's wiring: the world projection of UIGuideEventArrow and the dialog paths of UIGuideEventArrowDialog kind 0,
/// the game menu's included.
/// </para>
/// <para>
/// STILL-OPEN: the dialog arrow kinds past 0 (1-12: the skill slots, the inventory, the minimap, the house marks, the
/// party camera, the messenger, the cube palette), the focus dialog gate (FUN_141960510, a scene list no table fills)
/// and the scene service's busy state of UpdateArrowSound. The ChromaSDK lighting calls and vfunc_26 (the movie's
/// unload, which clears the child scripts) are not ported.
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
        foreach (var a in arrows.Values) a.Push();          // vfunc_1 per arrow: updateArrow, or hideArrow when the lookup fails
        UpdateArrowSound();
        CheckSummaryExpiry();
    }

    /// <summary>
    /// UpdateArrowSound (@0x14195be30), every 300 ms (m_timer): the loop effect plays while the movie reports an arrow
    /// and no scene gate holds (FUN_1422b9ea0, the scene service's busy state; the cut scenes it reports are not ported,
    /// so the gate reads false). The effect is the common resource's tutorial_Sound_arrow value
    /// (table/commonResource.xml) the client's m_strTutorialSoundArrow member holds.
    /// </summary>
    void UpdateArrowSound()
    {
        if (arrowSoundTick != 0 && NowMs() - arrowSoundTick < 300) return;
        arrowSoundTick = NowMs();
        bool playing = loopEffect != 0;
        bool wanted = Rt.ToBoolean(Host.Invoke(Host.Entity, "hasGuideArrow"));     // m_hasArrow->InvokeSelf
        if (playing == wanted) return;
        if (playing)
        {
            StopLoopEffect?.Invoke(loopEffect);
            loopEffect = 0;
        }
        else if (PlayLoopEffect != null)
            loopEffect = PlayLoopEffect(Model.Tables.CommonResource("tutorial_Sound_arrow"));
    }
    uint arrowSoundTick;                          // m_timer
    int loopEffect;                               // m_sound: the loop effect's handle

    /// <summary>vfunc_3 (@0x1419599a0), leaving the game scene: the movie unloads, the tips are dropped and the loop
    /// effect stops.</summary>
    public void Hide()
    {
        tip = 0;
        tipQueue.Clear();
        guide = 0;
        expiry = 0;
        if (loopEffect != 0)
        {
            StopLoopEffect?.Invoke(loopEffect);
            loopEffect = 0;
        }
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

    /// <summary>
    /// The world arrows' gate (FUN_141274f80, unit_141274d10_uihelpers.cpp:181): a popup menu or the message box layer
    /// is open — they hide. The host wires it; null = never blocked.
    /// </summary>
    public Func<bool> ArrowBlocked;
    /// <summary>
    /// The dialog arrows' own gates (UIGuideEventArrowDialog::vfunc_2, uiguideeventarrow.cpp:569-575): the cinematic
    /// sub scene 0x6200041 or a shown message box hides them (the scene list of focusDialogName — FUN_141960510 — is
    /// always empty in the tables, so that gate passes; STILL-OPEN should one fill it).
    /// </summary>
    public Func<bool> DialogArrowBlocked;
    /// <summary>CameraMgr::FUN_140af0700, the world point's place on the screen times the UI scale, its y shifted by
    /// FUN_1412762d0 (UIGuideEventArrow::vfunc_2, uiguideeventarrow.cpp:535); null when the point is behind the camera.
    /// The host's camera wires it; null = every world arrow hides.</summary>
    public Func<System.Numerics.Vector3, (double X, double Y)?> ProjectWorld;
    /// <summary>The dialog registry (CUIService::FindObjectByName / FindObject over the open movies), the guide's
    /// own lookups; null = every dialog arrow hides.</summary>
    public Func<int, GuideDialog> DialogOfId;
    public Func<string, GuideDialog> DialogOfName;
    /// <summary>
    /// GetGameMenuPathPosition's remap (uiguideeventarrow.cpp:902): the path element is a game menu entry the table
    /// moved, so the movie's getChildObjectPos(shown, original) on the clip the walk is at gives its place — the game
    /// menu's table and movie wire it. (name, the clip) -> (x, y, width/2 to add) at the shown entry, or null when the
    /// element is no moved entry (the plain child walk applies).
    /// </summary>
    public Func<string, GuideNode, (double X, double Y, double HalfWidth)?> GameMenuChildPos;
    /// <summary>The loop effect while an arrow shows (UpdateArrowSound): the effect's name plays and a handle comes
    /// back (CEffectService FUN_1411a49c0); the host wires the effect service, null plays nothing.</summary>
    public Func<string, int> PlayLoopEffect;
    public Action<int> StopLoopEffect;

    /// <summary>
    /// AddArrow (@0x141959a60): a second add of the id only bumps the use count; a new arrow's text part names its key
    /// (or its text) and the movie's addTextArrow / addArrow builds it hidden until its position is known.
    /// </summary>
    public void AddArrow(GuideArrow arrow)
    {
        if (arrows.TryGetValue(arrow.EntityId, out var existing))
        {
            existing.UseCount++;
            return;
        }
        var a = arrows[arrow.EntityId] = arrow is GuideDialogArrow dialog ? new PaneDialogArrow(this, dialog) : new PaneWorldArrow(this, arrow);
        var info = Host.Obj(
            ("id", (uint)a.Id),
            ("align", (uint)arrow.Align),
            ("direction", (uint)arrow.Direction),
            ("x", 0f), ("y", 0f),
            ("hide", 1));            // vfunc_2 failed: the arrow starts hidden until its position resolves
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

    /// <summary>
    /// GuideKindOf (@0x141960620): the kind of a customPosType's name. The arrows the tables hold: inventoryItemID (5)
    /// and the other inventory kinds 2-4, inventoryTab (6), myInfoSkillID (1); the rest point at the minimap, the house
    /// marks, the party camera, the messenger and the cube palette.
    /// </summary>
    public static int ArrowKindOf(string name) => name switch
    {
        "myInfoSkillID" => 1,
        "inventoryDesignItem" => 2,
        "inventoryItemType" => 3,
        "inventoryIconType" => 4,
        "inventoryItemID" => 5,
        "inventoryTab" => 6,
        "minimapMyPc" => 7,
        "minimapPortalID" => 8,
        "houseMark" => 9,
        "partyCamera" => 10,
        "messenger" => 11,
        "cubeType" => 12,
        _ => 0,
    };

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

    /// <summary>An arrow the pane keeps (UIGuideEventArrow, uiguideeventarrow.cpp:519): its record, how many actions
    /// hold it, and the per-frame push of its position to the movie (vfunc_1: updateArrow with the place, hideArrow
    /// while the lookup fails).</summary>
    abstract class PaneArrow
    {
        public readonly GuideArrow Arrow;
        public int UseCount = 1;
        protected readonly GuideEventController Pane;
        protected PaneArrow(GuideEventController pane, GuideArrow arrow) { Pane = pane; Arrow = arrow; }
        public int Id => Arrow.EntityId;
        /// <summary>vfunc_2: the arrow's place on the screen, null while it does not resolve.</summary>
        public abstract (double X, double Y)? Position();
        public void Push()
        {
            if (!Pane.Loaded) return;
            if (Position() is not { } p)
            {
                Pane.Host.Invoke("hideArrow", (uint)Id);
                return;
            }
            Pane.Host.Invoke("updateArrow", Pane.Host.Obj(("id", (uint)Id), ("x", p.X), ("y", p.Y)));
        }
    }

    /// <summary>UIGuideEventArrow's own arrows (@0x14195dd10): a world offset projected to the screen.</summary>
    sealed class PaneWorldArrow : PaneArrow
    {
        public PaneWorldArrow(GuideEventController pane, GuideArrow arrow) : base(pane, arrow) { }

        /// <summary>vfunc_2 (@0x14195dee0): FUN_141274f80's gate, then the camera's projection of the offset, at the
        /// UI scale with the pitch shift.</summary>
        public override (double X, double Y)? Position()
        {
            if (Pane.ArrowBlocked?.Invoke() == true) return null;
            return Pane.ProjectWorld?.Invoke(Arrow.Offset);
        }
    }

    /// <summary>
    /// UIGuideEventArrowDialog (@0x14195e040): an arrow at a dialog's control. The kind is customPosType's
    /// (<see cref="ArrowKindOf"/>); a kind's own lookups fail while their dialog is not there, as the client's do.
    /// STILL-OPEN: the kinds past 0 (the skill slot 1, the inventory 2-6, the minimap 7-8, the house marks, the party
    /// camera, the messenger, the cube palette), whose position sources are not ported; and the focus dialog gate
    /// (FUN_141960510, the m_scenes list — no table fills it, so it always passes).
    /// </summary>
    sealed class PaneDialogArrow : PaneArrow
    {
        readonly GuideDialogArrow dialog;
        public PaneDialogArrow(GuideEventController pane, GuideDialogArrow arrow) : base(pane, arrow) => dialog = arrow;

        /// <summary>vfunc_2 (@0x14195e0e0): the gates, then the kind's place plus the offset.</summary>
        public override (double X, double Y)? Position()
        {
            if (Pane.DialogArrowBlocked?.Invoke() == true) return null;
            var kind = ArrowKindOf(dialog.PosKind);
            if (kind != 0) return null;                 // the kinds past the dialog paths (STILL-OPEN above)
            var p = TargetPosition();
            if (p == null) return null;
            return (p.Value.X + dialog.Offset.X, p.Value.Y + dialog.Offset.Y);
        }

        /// <summary>GetTargetPosition (@0x14195e320): the dialog by id, else by name; the game menu takes its own path
        /// resolution, any other dialog the element path.</summary>
        (double X, double Y)? TargetPosition()
        {
            var d = dialog.DialogId != 0 ? Pane.DialogOfId?.Invoke(dialog.DialogId) : null;
            d ??= Pane.DialogOfName?.Invoke(dialog.DialogName);
            if (d == null) return null;
            var root = d.Root;
            if (root == null) return null;
            // dynamic_cast&lt;UIGameMenuDialog*&gt;: the type name the port's registry matches
            if (d is GuideControllerDialog { TypeName: "GameMenuDialog" }) return GameMenuPathPosition(root);
            return ElementPathPosition(root);
        }

        /// <summary>
        /// GetElementPathPosition (@0x14195f870): the dialog root's place plus every named child's, the last one's
        /// half width to its right; a missing or invisible child fails.
        /// </summary>
        (double X, double Y)? ElementPathPosition(GuideNode root)
        {
            var (x, y, _) = root.Position;
            GuideNode last = null;
            var cur = root;
            foreach (var name in dialog.ChildPath)
            {
                cur = cur.Child(name);
                if (cur == null || !cur.Visible) return null;
                var p = cur.Position;
                x += p.X; y += p.Y;
                last = cur;
            }
            if (last != null) x += last.WidthCeil / 2;
            return (x, y);
        }

        /// <summary>
        /// GetGameMenuPathPosition (@0x14195fc40): the walk of <see cref="ElementPathPosition"/> whose element the
        /// menu table moved resolves through the movie's getChildObjectPos instead (the shown entry's place plus the
        /// half width), and the walk ends there.
        /// </summary>
        (double X, double Y)? GameMenuPathPosition(GuideNode root)
        {
            var (x, y, _) = root.Position;
            GuideNode last = null;
            var cur = root;
            foreach (var name in dialog.ChildPath)
            {
                if (Pane.GameMenuChildPos?.Invoke(name, cur) is { } moved)
                {
                    x += moved.X + moved.HalfWidth;
                    y += moved.Y;
                    return (x, y);
                }
                cur = cur.Child(name);
                if (cur == null || !cur.Visible) return null;
                var p = cur.Position;
                x += p.X; y += p.Y;
                last = cur;
            }
            if (last != null) x += last.WidthCeil / 2;
            return (x, y);
        }
    }
}
