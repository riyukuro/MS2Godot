using Ms2.Avm2;
using Ms2.Ui.Items;

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
        /// <summary>m_questId (the page of a quest script, talkscript.cpp:595), m_redirect and m_openTalkReward (scriptcontent.cpp:255).</summary>
        public int QuestId, Redirect;
        public bool OpenTalkReward;
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
    /// <summary>m_nFlags, the talk type of the server's mode 1 / 2 (:976): 1 dialog, 2 talk, 4 quest, 8 select, 0x10 component, 0x20 pick talk.</summary>
    public int Flags;
    /// <summary>
    /// m_ids (FUN_141b217e0 :1854): the quests of the talk, which the quest packet's mode 1 sets (questmanager_2.cpp:996,
    /// FUN_1416ce140) and the server's mode 2 replaces with the one quest it opened.
    /// </summary>
    public List<int> QuestIds = new();

    /// <summary>
    /// UITalkManager::CTalkRewardData (m_reward, :929), what the talk handed out so far: the server's mode 3 sub modes 5-9
    /// (OnRewardItems :1250, OnRewardMerat, OnRewardMeso, OnRewardExp, OnRewardAffinity) add to it, FillTalkReward shows
    /// it on a page with openTalkReward and clears it (FUN_141b1dbd0 :941), as ResetTalk does. Display only: the server
    /// grants what it sends here with its own packets.
    /// </summary>
    public sealed class TalkReward
    {
        public long Merat, Meso;
        public int Exp, Affinity;
        public bool Jackpot;
        public List<Item> Items = new();
        public void Clear() { Items.Clear(); Merat = Meso = 0; Exp = Affinity = 0; Jackpot = false; }
    }
    public readonly TalkReward Reward = new();

    /// <summary>OnRewardItems (:1250): each item with its amount; a meso item (CItem::IsMeso, item.cpp:857) adds to the meso
    /// unless dialog 0x4e98 is open (the HUD's reward notify is not ported).</summary>
    public void OnRewardItems(IEnumerable<(Item Item, bool IsMeso)> items)
    {
        foreach (var (it, meso) in items)
        {
            if (meso) Reward.Meso += it.Count;
            else Reward.Items.Add(it);
        }
    }
    public void OnRewardMerat(long v) => Reward.Merat += v;          // :1280
    public void OnRewardMeso(long v) => Reward.Meso += v;            // :1290
    public void OnRewardExp(int v) => Reward.Exp += v;               // :1300
    public void OnRewardAffinity(int v, bool jackpot) { Reward.Affinity += v; if (jackpot) Reward.Jackpot = true; }   // :1307

    /// <summary>A quest of the talk's choice list (component 5): the name (GetQuestName), its level (m_level), the kind
    /// suffix of its icon (SelectStringByQuestId: "_epic", "", "_repeat", "_event").</summary>
    public sealed record QuestEntry(string Name, int Level, string IconSuffix);
    /// <summary>GetQuestData + the quest's text: null for an unknown quest.</summary>
    public Func<int, QuestEntry> QuestOf = _ => null;
    /// <summary>FUN_1416e6100(quest, 1) (questuidata.cpp:421): the quest's state string id with the talk npc: 0x5dc rewarded,
    /// 0x5db completable, 0x5da in progress, 0x5d9 can be started, 0x5d8 not yet.</summary>
    public Func<int, int> QuestStateString = _ => 0;
    /// <summary>CQuestManagerCommon IsQuestActive / IsQuestRewarded.</summary>
    public Func<int, bool> IsQuestActive = _ => false, IsQuestRewarded = _ => false;

    /// <summary>
    /// The complete talk's reward (CQuestCompleteCinematicComponent::vfunc_5, cinematiccomponent.cpp:664): exp
    /// (FUN_140761ba0), meso (FUN_140761cc0), karma, lu, guild exp, guild fund, fame points, the quest's choice rewards
    /// (m_complete.m_choiceRewards, all of them: the component keeps those of the player's job group) and the guild coins
    /// (FUN_140761c60).
    /// </summary>
    public sealed class QuestReward
    {
        public long Exp, Meso, Karma, Lu, GuildExp, GuildFund;
        public int FamePoint, GuildCoin;
        public List<(int ItemId, int Rarity, int Count)> ChoiceRewards = new();
    }
    /// <summary>The guild coin item the complete talk adds for a quest's guild coins (cinematiccomponent.cpp:699).</summary>
    public const int GuildCoinItem = 0x1c9c6dd;
    public Func<int, QuestReward> QuestRewardOf = _ => null;

    /// <summary>SendQuestAccept (:577): Quest mode 2 with the quest and the talk npc (UITalkManager::GetNpcId).</summary>
    public event Action<int, int> SendQuestAccept;
    /// <summary>CQuestCompleteCinematicComponent::vfunc_2 (:630): Quest mode 4 with the quest, the talk npc and the choice (0).</summary>
    public event Action<int, int, int> SendQuestComplete;
    /// <summary>NpcTalk mode 7 (cinematiccomponent.cpp:586, :652): the quest and 2 after an accept, the following quest and 0 after a
    /// complete; the talk goes on with that quest's script instead of mode 2.</summary>
    public event Action<int, short> SendQuestRedirect;
    /// <summary>CQuestProgressCinematicComponent::vfunc_2 (:611): Quest mode 0x12 with the quest.</summary>
    public event Action<int> SendQuestProgress;
    /// <summary>FUN_14074b840 (questdata.cpp @0x14074b840): with the QuestProgress feature, a quest without conditions whose start
    /// npc is its complete npc; a redirect page's accept then sends mode 7.</summary>
    public Func<int, bool> AcceptRedirects = _ => false;
    /// <summary>FUN_14074b950 (questdata.cpp @0x14074b950): with the QuestProgress feature, the one following quest when it starts
    /// at this quest's complete npc, else 0; a redirect page's complete then sends mode 7 with it.</summary>
    public Func<int, int> CompleteRedirectTo = _ => 0;
    /// <summary>The quest data's &lt;summonPortal&gt; fieldID (m_318.m_0) is set: the progress page sends Quest mode 0x12.</summary>
    public Func<int, bool> HasSummonPortal = _ => false;
    /// <summary>The quest's data has unrequire groups (m_require.m_unrequireGroup is not empty): its accept asks first.</summary>
    public Func<int, bool> HasUnrequireGroup = _ => false;
    /// <summary>FUN_140d73c60: show a box on the message box layer.</summary>
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;

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
        Reward.Clear();                 // m_reward.FUN_141b1dbd0 (:1537)
    }

    // ---- the cinematic components (cinematiccomponent.cpp) -----------------------------------------------------------

    /// <summary>
    /// CreateComponent (:700): 6 CQuestAccept, 7 CQuestComplete, 8 CQuestProgress, 4 CNext, 3 CClose, 9
    /// CSelectableDistractor, 5 CSelectableTalk here; any other set (job, beauty, ugc sign, roulette ...) is not ported,
    /// and an unknown one ends the talk (SendNpcTalkMode0).
    /// </summary>
    Component CreateComponent(int set)
    {
        switch (set)
        {
            case 6: return new Component(this, Component.Kind.QuestAccept);
            case 7: return new Component(this, Component.Kind.QuestComplete);
            case 8: return new Component(this, Component.Kind.QuestProgress);
            case 4: return new Component(this, Component.Kind.Next);
            case 3: return new Component(this, Component.Kind.Close);
            case 9: return new Component(this, Component.Kind.SelectableDistractor);
            case 5: return new Component(this, Component.Kind.SelectableTalk);
        }
        EndTalk();
        return null;
    }

    /// <summary>
    /// CQuestAcceptCinematicComponent::vfunc_2 (cinematiccomponent.cpp:533): nothing for a quest already active; a quest with
    /// unrequire groups (m_require.m_unrequireGroup) first asks with a type 1 box of s_alliance_quest_unreq_group_warn
    /// (:540-547) and accepts only on its first button (OnMsgBoxResult, :561: any other button ends the talk with
    /// SendNpcTalkMode0); else SendQuestAccept at once.
    /// </summary>
    void QuestAccept(TalkContent c)
    {
        if (IsQuestActive(c.QuestId)) return;
        int quest = c.QuestId;
        bool redirect = c.Redirect != 0;
        if (QuestOf(quest) != null && HasUnrequireGroup(quest))
        {
            ShowBox(MessageBoxQueue.CreateWithCallback(MessageBoxData.CommonByKey("s_alliance_quest_unreq_group_warn") ?? "", 1, (button, _) =>
            {
                if (button != 0) EndTalk();
                else SendAccept(quest, redirect);
                return true;
            }));
            return;
        }
        SendAccept(quest, redirect);
    }

    /// <summary>SendQuestAccept (:577): Quest mode 2, then NpcTalk mode 7 for a redirect the quest allows, else mode 2 of the talk.</summary>
    void SendAccept(int quest, bool redirect)
    {
        SendQuestAccept?.Invoke(quest, TalkNpcId);
        if (redirect && AcceptRedirects(quest)) { SendQuestRedirect?.Invoke(quest, 2); return; }
        RequestNext(0);
    }

    /// <summary>CQuestCompleteCinematicComponent::vfunc_2 (:630): nothing for an unknown or rewarded quest; else Quest mode 4
    /// (quest, npc, choice 0), then (no redirect) mode 2 of the talk.</summary>
    void QuestComplete(TalkContent c)
    {
        if (QuestOf(c.QuestId) == null) return;
        if (IsQuestRewarded(c.QuestId)) return;
        SendQuestComplete?.Invoke(c.QuestId, TalkNpcId, 0);
        if (c.Redirect != 0 && CompleteRedirectTo(c.QuestId) is var next and not 0) { SendQuestRedirect?.Invoke(next, 0); return; }
        RequestNext(0);
    }

    static string Num(long v) => NumberText.Group(v.ToString(System.Globalization.CultureInfo.InvariantCulture));   // FUN_1409404f0

    string ItemSlot(Item item, string key)
    {
        if (item == null) return null;
        Model.ItemSlots[key] = new ItemInfoSlot(key, item, 0, 0x11, 0x800);   // CreateItemInfoSlot(key, item, 0, 0x11, 0x800)
        return key;
    }

    /// <summary>
    /// FillTalkReward (:353): with the page's openTalkReward, a "reward" of what the talk handed out: title
    /// s_talk_reward_title (0x94b), "exp" the merat, "meso", "mood" (title s_manufacture_cinematic_title 0x7c1;
    /// s_manufacture_cinematic_mood_plus / _minus 0x7c4 / 0x7c5 with the value), "affinityExp" (the npc's maid: not here),
    /// and reward1.. the items as slots "&lt;type name&gt;_&lt;n&gt;"; then the data is cleared. True when anything was shown.
    /// </summary>
    bool FillTalkReward(AsObject page)
    {
        if (shown == null || !shown.OpenTalkReward) return false;
        bool any = false;
        var h = Host;
        var reward = h.Obj();
        h.Set(reward, "title", Button("s_talk_reward_title"));
        if (Reward.Merat != 0) { any = true; h.Set(reward, "exp", Num(Reward.Merat)); }
        if (Reward.Meso != 0) { any = true; h.Set(reward, "meso", Num(Reward.Meso)); }
        if (Reward.Exp != 0)
        {
            h.Set(reward, "title", Button("s_manufacture_cinematic_title"));
            any = true;
            string mood = "";
            if (Reward.Exp > 0) mood = Ms2.Quest.ClientFormat.Format(Button("s_manufacture_cinematic_mood_plus"), Reward.Exp);
            else if (Reward.Exp < 0) mood = Ms2.Quest.ClientFormat.Format(Button("s_manufacture_cinematic_mood_minus"), Math.Abs(Reward.Exp));
            h.Set(reward, "mood", mood);
        }
        if (Reward.Affinity != 0)
        {
            h.Set(reward, "title", Button("s_manufacture_cinematic_title"));
            any = true;
            h.Set(reward, "affinityExp", "");       // the talk npc's maid (CNpc::GetMaid): no maid npcs here
        }
        for (int i = 0; i < Reward.Items.Count; i++)
        {
            any = true;
            string key = $"{TypeName}_{i + 1}";
            ItemSlot(Reward.Items[i], key);
            h.Set(reward, $"reward{i + 1}", key);
        }
        Reward.Clear();
        if (any) h.Set(page, "reward", reward);
        return any;
    }

    /// <summary>
    /// CQuestCompleteCinematicComponent::vfunc_5 (:664): "reward" titled s_quest_talk_reward_title (0x5d7) with exp and meso
    /// (with separators), karma, lu, guildExp, guildFund, famePoint when not 0, the items (FillRewardItems :333: slots
    /// "&lt;type name&gt;_&lt;i&gt;" as reward&lt;i+1&gt;), then the button s_quest_talk_complete (0x5d4).
    /// </summary>
    void FillQuestReward(AsObject page, int questId)
    {
        var r = QuestRewardOf(questId);
        if (r == null) return;
        var h = Host;
        var reward = h.Obj();
        h.Set(reward, "title", Button("s_quest_talk_reward_title"));
        if (r.Exp != 0) h.Set(reward, "exp", Num(r.Exp));
        if (r.Meso != 0) h.Set(reward, "meso", Num(r.Meso));
        if (r.Karma != 0) h.Set(reward, "karma", (double)r.Karma);
        if (r.Lu != 0) h.Set(reward, "lu", (double)r.Lu);
        if (r.GuildExp != 0) h.Set(reward, "guildExp", (double)r.GuildExp);
        if (r.GuildFund != 0) h.Set(reward, "guildFund", (double)r.GuildFund);
        if (r.FamePoint != 0) h.Set(reward, "famePoint", r.FamePoint);
        // :692: the choice rewards with an item that suits the player's job group (FUN_140748050, ItemLimit::IsJobRecommended),
        // then the guild coins (:697) as the coin item with its rank as the rarity (GetItemRank) and the coins as the count
        var items = r.ChoiceRewards.Where(x => x.ItemId != 0 && Model.Tables.Items.Get(x.ItemId) is { } d
                                               && d.IsJobRecommended(Model.Player.JobGroup)).ToList();
        if (r.GuildCoin != 0) items.Add((GuildCoinItem, Model.Tables.Items.Get(GuildCoinItem)?.Rank ?? 0, r.GuildCoin));
        for (int i = 0; i < items.Count; i++)      // FillRewardItems (:333)
        {
            var (id, rarity, count) = items[i];
            var item = Item.Create(Model.Tables.Items, id, rarity);
            if (item == null) continue;
            item.SetCountClamped(count);
            string key = $"{TypeName}_{i}";
            ItemSlot(item, key);
            h.Set(reward, $"reward{i + 1}", key);
        }
        h.Set(page, "reward", reward);
        h.Set(page, "button", Button("s_quest_talk_complete"));
    }

    /// <summary>
    /// CSelectableTalkCinematicComponent::vfunc_5 (:849), the choice list: with flag 4 each quest of the talk with its
    /// state's icon (talk_icon_quest_complete / _progress / _accept, plus the kind suffix) and s_quest_distactor_quest_name
    /// (name, state string, level); with flag 2 the talk choice (talk_icon_distractor, s_action_talk_normal_cinematic
    /// 0x62e). The pick talk's choices (flag 0x20) and the job choice (flag 1) are not offered here.
    /// </summary>
    void FillTalkChoices(AsObject page)
    {
        var h = Host;
        var list = new List<object>();
        if ((Flags & 4) != 0)
        {
            foreach (int q in QuestIds)
            {
                int state = QuestStateString(q);
                if (state == 0x5dc) state = 0x5d9;
                string icon = state switch
                {
                    0x5db => "talk_icon_quest_complete",
                    0x5da => "talk_icon_quest_progress",
                    0x5d9 => "talk_icon_quest_accept",
                    _ => "talk_icon_quest_progress",
                };
                var e = QuestOf(q);
                if (e == null) continue;
                string text = Ms2.Quest.ClientFormat.Format(Button("s_quest_distactor_quest_name"), e.Name, Button(StateKey(state)), e.Level);
                list.Add(h.Obj(("iconClass", icon + e.IconSuffix), ("text", text)));
            }
        }
        if ((Flags & 2) != 0) list.Add(h.Obj(("iconClass", "talk_icon_distractor"), ("text", Button("s_action_talk_normal_cinematic"))));
        h.Set(page, "distractor", new AsArray(h.RT, list));
    }

    /// <summary>The common string keys of the quest states 0x5d8-0x5dc (commonstringtable.cpp:3109).</summary>
    static string StateKey(int id) => id switch
    {
        0x5d8 => "s_quest_state_init", 0x5d9 => "s_quest_state_beginAble", 0x5da => "s_quest_state_progress",
        0x5db => "s_quest_state_completeAble", 0x5dc => "s_quest_state_complete", _ => "",
    };

    /// <summary>FUN_14107dfa0 (:418): the common string with its key names (GameStrings.Common replaces $key:N$).</summary>
    string Button(string key) => Model.Tables.Common(key) ?? "";

    sealed class Component
    {
        public enum Kind { Next, Close, SelectableDistractor, SelectableTalk, QuestAccept, QuestComplete, QuestProgress }
        readonly TalkCinematicController owner;
        readonly Kind kind;
        bool started;            // m_nStarted
        bool reward;             // m_nReward (CNext / CClose: FillTalkReward showed something)

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
                case Kind.Next:                 // :453
                    reward = owner.FillTalkReward(page);
                    h.Set(page, "button", owner.Button("s_quest_talk_progress"));
                    break;
                case Kind.Close:                // :484; CQuestProgress has no vfunc_5 of its own
                case Kind.QuestProgress:
                    reward = owner.FillTalkReward(page);
                    h.Set(page, "button", owner.Button("s_quest_talk_end"));
                    break;
                case Kind.QuestAccept:          // :557, 0x5d3
                    h.Set(page, "button", owner.Button("s_quest_talk_accept"));
                    break;
                case Kind.QuestComplete:        // :664
                    owner.FillQuestReward(page, c.QuestId);
                    break;
                case Kind.SelectableTalk:       // :849
                    owner.FillTalkChoices(page);
                    h.Set(page, "button", owner.Button("s_quest_talk_progress"));
                    break;
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
            if (kind == Kind.QuestProgress)
            {
                // CQuestProgressCinematicComponent::vfunc_2 (:604) skips the base: nothing once started (it never sets the
                // flag), and a next requests mode 2, Quest mode 0x12 first for a quest whose m_318.m_0 is set (:611). The
                // client's mode 2 goes once until the answer (m_nPending), but its 0x12 goes on every next; here a next while
                // the mode 2 is pending sends nothing, so Space and the button in one press cannot send 0x12 twice.
                if (started || msg != 0x493e2 || owner.pending) return;
                if (owner.Content is { } pc && owner.QuestOf(pc.QuestId) != null && owner.HasSummonPortal(pc.QuestId)) owner.SendQuestProgress?.Invoke(pc.QuestId);
                owner.RequestNext(0);
                return;
            }
            if (msg == 0x493e2)
            {
                if (started) return;
                started = true;
            }
            if (msg == 1) { owner.EndTalk(); return; }
            if (msg == 0x79) { owner.Host.Invoke("onFocusUp"); return; }
            if (msg == 0x7e) { owner.Host.Invoke("onFocusDown"); return; }
            if (msg != 0x493e2) return;
            switch (kind)
            {
                case Kind.SelectableDistractor:
                case Kind.SelectableTalk:
                    owner.Host.Invoke("onSelect");          // CDistractor (:497): SelectOption
                    break;
                case Kind.Close:
                    // :466: the reward sound; FUN_141b213c0 (the npc preview) is not a plain talk, so mode 2
                    owner.RequestNext(0);
                    break;
                case Kind.QuestAccept:
                    owner.QuestAccept(owner.shown);
                    break;
                case Kind.QuestComplete:
                    owner.QuestComplete(owner.shown);
                    break;
                default:
                    owner.RequestNext(0);                   // CNext (:437)
                    break;
            }
        }

        /// <summary>vfunc_1: CSelectableDistractor (:722) requests mode 2 with the choice; the others do nothing (:281).</summary>
        public void Select(int index)
        {
            if (kind is Kind.SelectableDistractor or Kind.SelectableTalk) owner.RequestNext(index);   // :722, :781
        }
    }
}
