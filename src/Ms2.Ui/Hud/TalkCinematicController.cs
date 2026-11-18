using Ms2.Avm2;

namespace Ms2.Ui.Hud;

/// <summary>
/// UITalkCinematic (ms2/client/ms2/uitalkcinematic.cpp, UI id 0x4e40, "TalkCinematic"), the C++ side of uitalkcinematic:
/// the npc talk window. A plain talk opens it: UITalkManager::OnTalkPacket mode 1 (:971) -> FUN_141b1ff80 (:1456) finds
/// the talk script and switches to sub scene 0x6200031, which loads this movie (vfunc_2 :186, LoadSwf(0x1c), layer 4).
/// With it come the parts of UITalkManager and the cinematic components (cinematiccomponent.cpp) a page needs: the page
/// the server chose (<see cref="Content"/>, <see cref="ButtonSet"/>), the requests back (<see cref="SendNext"/> for
/// NpcTalk mode 2, <see cref="SendEnd"/> for mode 0) and the end of the talk (<see cref="TalkReset"/>).
/// </summary>
public sealed class TalkCinematicController : UiController
{
    public override string Movie => "uitalkcinematic";
    /// <summary>TalkCinematic's own method (the movie's scaleform.ms2.entity.TalkCinematic; TalkScript also has setScript).</summary>
    protected override string EntityMethod => "getCinematicScale";
    protected override bool IsDialog => false;          // a UISwfEntity, not a UIDialog
    public override string TypeName => "TalkCinematic"; // GetTypeName (:186)

    /// <summary>
    /// A page (CCinematicContent, made from a CScriptContent by GetCinematicContent, :890): the text (m_name) with its words
    /// replaced, the content's illust (m_unk28, CScriptContent "illust", scriptcontent.cpp:64) and voice (m_unk20, "voiceID"),
    /// the image plate and the choices of a &lt;distractor&gt; page (CCinematicDistractor: the text and whether it has a goto).
    /// The left / right / speaker illusts, myTalk, otherNpcTalk and speakerNpc of quest pages are not read from npc scripts.
    /// </summary>
    public sealed class TalkContent
    {
        public string Script = "", Illust = "", Voice = "", ImagePlate = "", MovieFile = "";
        public string LeftIllust = "", RightIllust = "", SpeakerIllust = "";
        public int SpeakerNpc, MyTalk, OtherNpcTalk;
        public List<(string Text, bool HasGoto)> Distractors = new();
    }

    // ---- UITalkManager's state (uitalkcinematic.cpp:1500-1700) --------------------------------------------------------

    /// <summary>m_nTalking: set by FUN_141b1ff80 (:1462), cleared by SendNpcTalkMode0 (:1443) and ResetTalk.</summary>
    public bool Talking;
    /// <summary>m_nNpcId, the talk npc's id (GetTalkNpcId).</summary>
    public int TalkNpcId;
    /// <summary>The page the server chose (m_spScript->m_contents[m_nPage]), or null when there is none.</summary>
    public TalkContent Content;
    /// <summary>m_nSound, the server's button set (GetSound): CreateComponent (:700) picks the component by it.</summary>
    public int ButtonSet;
    bool pending;                // m_nPending

    /// <summary>FUN_1401aa6a0: the npc's "title name".</summary>
    public Func<int, string> NpcName = id => id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    /// <summary>NpcDataTableCli::GetIllust: the npc's illust name.</summary>
    public Func<int, string> NpcIllust = _ => "";
    /// <summary>FUN_141b70ea0: the file exists in the client's data (the path as FUN_140cc2c70 builds it).</summary>
    public Func<string, bool> FileExists = _ => false;

    /// <summary>SendNpcTalkMode2 (:1666): NpcTalk mode 2 with the pick (0 = next).</summary>
    public event Action<int> SendNext;
    /// <summary>SendNpcTalkMode0 (:1439): NpcTalk mode 0, the player ended the talk.</summary>
    public event Action SendEnd;
    /// <summary>ResetTalk (:1507): the talk ended; the npc's PlayEndSound and CloseTalkUi are the host's.</summary>
    public event Action TalkReset;
    /// <summary>ShowPage (:430): CEffectService::PlayVoiceAt(voice, the talk npc's position).</summary>
    public event Action<string> PlayVoice;
    /// <summary>OnCloseComplete (vfunc_28 :285): FUN_1422dc080 unloads the movie.</summary>
    public event Action CloseComplete;

    /// <summary>DAT_143af62d4 / DAT_143af62d8, the client size, and GFxManager::GetMovieScale(m_28) (vfunc_43 :231).</summary>
    public int ClientWidth = 1280, ClientHeight = 720;
    public double MovieScale = 1;

    // ---- UITalkCinematic ---------------------------------------------------------------------------------------------

    bool loaded;                 // m_nLoaded (ctor :172: 0)
    bool talkComplete = true;    // m_nTalkComplete (ctor: 1)
    TalkContent shown;           // m_spContent
    Component component;         // m_spComponent

    public bool IsLoaded => loaded;
    public bool IsTalkComplete => talkComplete;

    /// <summary>
    /// OnLoadComplete (:208): with a talk on and the entity there, init(page) with the current content's illust
    /// (FUN_141b1bfe0); the movie plays "show" and calls OnLoadComplete when the script panel comes in.
    /// </summary>
    public override void OnLoadComplete()
    {
        if (!Talking || Host.Entity == null || Content == null) return;
        var page = Host.Obj();
        Illust(page, Content);
        Host.Invoke("init", page);
    }

    /// <summary>vfunc_43 (:231): the entity aligned (vfunc_2(5, 0, 0)), then setStageSize(client w, h, movie scale).</summary>
    public override void Layout(double stageW, double stageH)
    {
        if (Host.Entity == null) return;
        Host.Align(Host.Entity, 5, 0, 0);
        Host.Invoke("setStageSize", ClientWidth, ClientHeight, MovieScale);
    }

    /// <summary>vfunc_28 (:269).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        switch (name.ToLowerInvariant())
        {
            case "onloadcomplete":
                loaded = true;
                ShowCurrent();                  // UITalkManager FUN_141b204d0; OpenPendingDialogs has none here
                return true;
            case "onclosecomplete":
                CloseComplete?.Invoke();
                return true;
            case "ontalkcomplete":
                talkComplete = true;
                return true;
            case "onend":
                Message(1);
                return true;
            case "onnext":
                Message(0x493e2);
                return true;
            case "onmoviecomplete":
                talkComplete = true;
                Message(0x493e2);
                return true;
            case "onselect":
                if (args.Length > 0)
                {
                    int index = Rt.ToInt32(args[0]);
                    if (index != -1) component?.Select(index);
                }
                return true;
        }
        return false;
    }

    /// <summary>The keys vfunc_13 (:243) takes while the window is up.</summary>
    public enum TalkKey { Next, End, Up, Down }

    /// <summary>
    /// vfunc_13 (:243): the next key (action 0x493e2) is 0x493e2; action 0x49409 or Esc is 1 (end); the up keys (0x79,
    /// action 0x30d41, 0x92) and down keys (0x7e, action 0x30d42, 0x93) move the choice focus.
    /// </summary>
    public void Key(TalkKey key) => Message(key switch
    {
        TalkKey.Next => 0x493e2, TalkKey.End => 1, TalkKey.Up => 0x79, _ => 0x7e,
    });

    /// <summary>
    /// FUN_141b1bce0 (:533): nothing before the load or a page; a next while the page is still typing skips the typing
    /// (SkipTalk: onSkip and the s_nextKey sound); nothing while the entity's canNext is false; else the component's
    /// vfunc_2.
    /// </summary>
    void Message(int msg)
    {
        if (!loaded || shown == null) return;
        if (!talkComplete && msg == 0x493e2)
        {
            Host.Invoke("onSkip");
            return;
        }
        if (Host.Entity == null) return;
        if (!Rt.ToBoolean(Host.Invoke("canNext"))) return;
        component?.Message(msg);
    }

    /// <summary>
    /// ShowPage (:430): with the movie loaded and the last page typed, the component for the button set (CreateComponent,
    /// :700), the page (the illust FUN_141b1bfe0, the image plate SetImagePlate, the component's vfunc_5) to setScript, and
    /// the page's voice at the npc.
    /// </summary>
    bool ShowPage(TalkContent content)
    {
        if (content == null || !loaded || !talkComplete) return false;
        if (Host.Entity == null) return false;
        shown = content;
        talkComplete = false;
        component = CreateComponent(ButtonSet);
        if (component == null) return false;
        var page = Host.Obj();
        Illust(page, content);
        Host.Set(page, "imagePlate", content.ImagePlate);          // SetImagePlate (:640)
        component.Fill(page);
        Host.Invoke("setScript", page);
        if (!string.IsNullOrEmpty(content.Voice)) PlayVoice?.Invoke(content.Voice);
        return true;
    }

    /// <summary>FUN_141b1b8b0 (:477): the state reset and the entity's "hide" (gotoAndPlay, asmovieclip.cpp:111).</summary>
    void Hide()
    {
        shown = null;
        talkComplete = true;
        loaded = false;
        component = null;
        if (Host?.Entity != null) Host.Invoke(Host.Entity, "gotoAndPlay", (object)"hide");
        else CloseComplete?.Invoke();
    }

    /// <summary>
    /// FUN_141b1bfe0 (:585), the page's illust: the content's left / right illusts (speaking = the speaker's), else myTalk,
    /// else the content's illust on the left, else otherNpcTalk on the right, else (no speaker npc or the talk npc) the
    /// talk npc's illust on the left; all speaking. The npc preview dialog case (FUN_141b213c0) is not a plain talk.
    /// </summary>
    void Illust(AsObject page, TalkContent c)
    {
        var illust = Host.Obj();
        if (c.LeftIllust.Length != 0 || c.RightIllust.Length != 0)
        {
            Host.Set(illust, "lNpc", IllustPath(c.LeftIllust));
            Host.Set(illust, "lNpcSpeak", c.LeftIllust == c.SpeakerIllust);
            Host.Set(illust, "rNpc", IllustPath(c.RightIllust));
            Host.Set(illust, "rNpcSpeak", c.RightIllust == c.SpeakerIllust);
        }
        else if (c.MyTalk != 0) { }            // FUN_141b1c720: the player's job illust (feature 0x96 and the option): not here
        else if (c.Illust.Length != 0)
        {
            Host.Set(illust, "lNpc", IllustPath(c.Illust));
            Host.Set(illust, "lNpcSpeak", 1);
        }
        else if (c.OtherNpcTalk != 0)
        {
            Host.Set(illust, "rNpc", IllustPath(NpcIllust(c.OtherNpcTalk)));
            Host.Set(illust, "rNpcSpeak", 1);
        }
        else if (c.SpeakerNpc == 0 || c.SpeakerNpc == TalkNpcId)
        {
            Host.Set(illust, "lNpc", IllustPath(NpcIllust(TalkNpcId)));
            Host.Set(illust, "lNpcSpeak", 1);
        }
        Host.Set(page, "illust", illust);
    }

    /// <summary>GetIllustPath (:664): FUN_140cc2c70 (commonresource.cpp:58, ILLUST_PATH + name + ".dds"), "empty" when the file is not there.</summary>
    public string IllustPath(string name)
    {
        if (string.IsNullOrEmpty(name)) return "empty";
        var path = Model.Tables.CommonResource("ILLUST_PATH") + name + ".dds";
        return FileExists(path) ? path : "empty";
    }

    // ---- UITalkManager -----------------------------------------------------------------------------------------------

    /// <summary>
    /// FUN_141b204d0 (:1542) after the load and on the server's mode 2 (:1010): ShowCurrentPage (:1636) shows the page
    /// (a page past the script's end shows nothing and counts as shown), and when ShowPage fails (not loaded, the last
    /// page still typing, no component) the talk ends (SendNpcTalkMode0); then nothing is pending.
    /// </summary>
    public bool ShowCurrent()
    {
        if (Content != null && !ShowPage(Content))
        {
            EndTalk();
            return false;
        }
        pending = false;
        return true;
    }

    /// <summary>The server's mode 2 (:1003): the next page and its button set, shown.</summary>
    public bool OnServerPage(TalkContent content, int buttonSet)
    {
        Content = content;
        ButtonSet = buttonSet;
        return ShowCurrent();
    }

    /// <summary>The server's mode 0 (:1030): the talk is over (ResetTalk).</summary>
    public void OnServerEnd()
    {
        Talking = false;
        ResetTalk();
    }

    /// <summary>SendNpcTalkMode0 (:1439): only while talking; the request, then ResetTalk.</summary>
    public void EndTalk()
    {
        if (!Talking) return;
        Talking = false;
        pending = false;
        SendEnd?.Invoke();
        ResetTalk();
    }

    /// <summary>SendNpcTalkMode2 (:1666): one request until the answer shows its page (m_nPending).</summary>
    void RequestNext(int pick)
    {
        if (pending) return;
        pending = true;
        SendNext?.Invoke(pick);
    }

    /// <summary>ResetTalk (:1507): the window hides (FUN_141b1b8b0), the npc's end sound, the talk state cleared.</summary>
    void ResetTalk()
    {
        Hide();
        TalkReset?.Invoke();
        Talking = false;
        Content = null;
        pending = false;
    }

    // ---- the cinematic components (cinematiccomponent.cpp) -----------------------------------------------------------

    /// <summary>
    /// CreateComponent (:700): 4 CNextCinematicComponent, 3 CCloseCinematicComponent, 9
    /// CSelectableDistractorCinematicComponent here; any other set (the quest, job, beauty ... components) is not ported,
    /// and an unknown one ends the talk (SendNpcTalkMode0).
    /// </summary>
    Component CreateComponent(int set)
    {
        switch (set)
        {
            case 4: return new Component(this, Component.Kind.Next);
            case 3: return new Component(this, Component.Kind.Close);
            case 9: return new Component(this, Component.Kind.SelectableDistractor);
        }
        EndTalk();
        return null;
    }

    /// <summary>FUN_14107dfa0 (:418): the common string with its key names (GameStrings.Common replaces $key:N$).</summary>
    string Button(string key) => Model.Tables.Common(key) ?? "";

    sealed class Component
    {
        public enum Kind { Next, Close, SelectableDistractor }
        readonly TalkCinematicController owner;
        readonly Kind kind;
        bool started;            // m_nStarted

        public Component(TalkCinematicController owner, Kind kind) { this.owner = owner; this.kind = kind; }

        /// <summary>
        /// vfunc_5. CCinematicComponent's (:297): "npc" (no speaker npc, myTalk or otherNpcTalk: FUN_1401aa6a0 of the talk
        /// npc; an npc spawned offline has no owner, vfunc_128), "script" and "movie". Then CNext (:453) button 0x5d5,
        /// CClose (:484) 0x5d6, CSelectableDistractor (:731) the "distractor" list (CDistractor::vfunc_6 :512, icon
        /// talk_icon_distractor) and 0x5d5 when a choice has a goto, else 0x5d6. 0x5d5 / 0x5d6 are s_quest_talk_progress /
        /// s_quest_talk_end (commonstringtable.cpp:3106). The talk rewards (FillTalkReward) need the server's reward packets.
        /// </summary>
        public void Fill(AsObject page)
        {
            var c = owner.shown;
            string npc = c.SpeakerNpc != 0 ? owner.NpcName(c.SpeakerNpc)
                : c.OtherNpcTalk != 0 ? owner.NpcName(c.OtherNpcTalk)
                : owner.NpcName(owner.TalkNpcId);
            var h = owner.Host;
            h.Set(page, "npc", npc);
            h.Set(page, "script", c.Script);
            h.Set(page, "movie", c.MovieFile);
            switch (kind)
            {
                case Kind.Next: h.Set(page, "button", owner.Button("s_quest_talk_progress")); break;
                case Kind.Close: h.Set(page, "button", owner.Button("s_quest_talk_end")); break;
                case Kind.SelectableDistractor:
                {
                    var list = new List<object>();
                    bool anyGoto = false;
                    foreach (var (text, hasGoto) in c.Distractors)
                    {
                        list.Add(h.Obj(("iconClass", "talk_icon_distractor"), ("text", text)));
                        anyGoto |= hasGoto;
                    }
                    h.Set(page, "distractor", new AsArray(h.RT, list));
                    h.Set(page, "button", owner.Button(anyGoto ? "s_quest_talk_progress" : "s_quest_talk_end"));
                    break;
                }
            }
        }

        /// <summary>
        /// vfunc_2. CCinematicComponent's (:275): one next per page (m_nStarted), 1 ends the talk (SendNpcTalkMode0), 0x79 /
        /// 0x7e move the focus. Then a next: CNext (:437) and CClose (:466) request mode 2 with 0; CDistractor (:497)
        /// picks the focused choice (SelectOption: onSelect, which calls back OnSelect with its index).
        /// </summary>
        public void Message(int msg)
        {
            if (msg == 0x493e2)
            {
                if (started) return;
                started = true;
            }
            if (msg == 1) { owner.EndTalk(); return; }
            if (msg == 0x79) { owner.Host.Invoke("onFocusUp"); return; }
            if (msg == 0x7e) { owner.Host.Invoke("onFocusDown"); return; }
            if (msg != 0x493e2) return;
            if (kind == Kind.SelectableDistractor) owner.Host.Invoke("onSelect");
            else owner.RequestNext(0);
        }

        /// <summary>vfunc_1: CSelectableDistractor (:722) requests mode 2 with the choice; the others do nothing (:281).</summary>
        public void Select(int index)
        {
            if (kind == Kind.SelectableDistractor) owner.RequestNext(index);
        }
    }
}
