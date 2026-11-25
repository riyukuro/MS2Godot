using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Formats.World;
using Ms2.Quest;
using Ms2Godot.Net;
using Ms2Godot.Ui;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.World;

/// <summary>
/// Talking to an npc in the offline sandbox (the client's TalkAction + UITalkManager, with the sandbox answering the server's part).
/// - Who: MyPC::FindTalkableNpc -> WorldService::FindNearestNpc, friendly 2 first, then friendly 1: within TalkableDistance plus
///   the npc's capsule radius, in front of the player (TalkableFrontOnly), nearest first; then TalkAction's check on that npc
///   (<see cref="NpcQuestMarks.HasTalk"/>): a quest for it, a function kind, an npc script, or a started quest's talk. With
///   mouse control a click picks an npc too (<see cref="FindClickTarget"/>, walked to by <see cref="Movement.MouseMove"/>).
/// - The window: uitalkcinematic run by its own ActionScript (<see cref="MovieDialog"/>) with UITalkCinematic's C++ side
///   (<see cref="TalkCinematicController"/>), the movie a plain talk opens (UITalkManager::OnTalkPacket mode 1 -> FUN_141b1ff80,
///   uitalkcinematic.cpp:1456: sub scene 0x6200031).
/// - What (the server's part): the client sends NpcTalk (mode 1); the server answers with the script and page and the button
///   set. The sandbox opens one of the npc's randomPick scripts (picked at random, as the server picks among them), else its
///   &lt;select&gt;, else its first &lt;script&gt;; a page with choices gets button set 9 (SelectableDistractor), a page with more after
///   it 4 (Next), the last 3 (Close). Mode 2 goes to the next page, or for a choice to one of the scripts its goto lists; past
///   the last page (or a choice without a script) the talk ends (mode 0). The server's script conditions are not here.
/// - Quests (the Maple2 server's NpcTalkHandler.HandleTalk / HandleContinue, NpcScriptManager, QuestManager.GetAvailableQuests): the npc's
///   quests are those it starts that the player can start (the client's own check, CQuestManager::vfunc_1 on questdata.xml's
///   require, <see cref="SandboxQuests"/>) and the started ones it completes. With quests the talk has flag 4 and the quest list (Quest mode 1,
///   questmanager_2.cpp:996); with quests and a talk script and a &lt;select&gt;, flag 8 and the select page, whose last page lists
///   the quests then the talk (button set 5). Picking a quest opens its quest script (script/quest/questscript*.xml): 100-199
///   when not started, 200-299 when started, 300-399 when started and this npc completes it; the last page of each gets 6
///   (accept), 8 (progress) or 7 (complete). Accept (Quest mode 2) starts the quest, Complete (Quest mode 4) marks it
///   rewarded (<see cref="SandboxQuests"/>: session records, every condition counted as met, nothing given; the reward panel
///   is display only).
/// - Sounds: npcSoundStart when the talk opens (CNpc::PlayStartSound), each page's voiceID (ShowPage), npcSoundEnd when it ends
///   (UITalkManager::ResetTalk -> CNpc::PlayEndSound), all at the npc.
/// - Live (a <see cref="LiveSession"/>): the server plays its part. The talk key sends NpcTalk mode 1 with the npc's object id
///   (uitalkcinematic.cpp:1432); the server's mode 1 opens the window and mode 2 shows the next page, each with the talk type,
///   script id, page and button set, and the client finds the script itself (UITalkManager::FindTalkScript, :1945: type 8 the
///   npc's select talk, 4 the quest script of the first quest Quest mode 1 listed, 2 or 0x20 the npc's script with that id, 1 its
///   job talk); mode 0 ends it, mode 3 carries the talk rewards. The quest state is the session's quest book (the login list and
///   the quest packets, <see cref="LiveQuestState"/>), and the window's sends go out as NpcTalk 0 / 2 / 7 and Quest 2 / 4 / 0x12.
/// The player stands still while the window is open. Keys (UITalkCinematic::vfunc_13): the action key (Space) is Next, Esc ends
/// the talk, Up / Down move the choice focus.
/// </summary>
public sealed partial class NpcTalk : Node
{
    readonly PackSet packs;
    readonly List<FieldNpcs.Placed> npcs;
    readonly Player player;
    readonly ClientConstants constants;
    readonly LiveSession live;             // null offline
    LiveQuestState liveQuests;             // the live session's quest state, for the client's checks
    QuestRules liveRules;
    FieldNpcs.Placed liveRequested;        // NpcTalk mode 1 sent, the server has not answered yet
    ulong liveRequestedAt;
    FieldNpcs.Placed liveTalking;          // the server opened a talk with this npc (m_nTalking)
    static NpcScriptTable scripts;
    static GameTables tables;
    static System.Collections.Generic.Dictionary<int, int> continentShadows;

    /// <summary>CFieldData::GetShadowContinent (fielddata.cpp:817) for the field <paramref name="mapId"/>: its continent's
    /// shadowContinent, 0 without a row.</summary>
    int ShadowContinentOf(int mapId)
    {
        try
        {
            continentShadows ??= PortalAction.ContinentShadows(packs);
            int code = FieldData.FindById(packs, mapId)?.ContinentCode ?? 0;
            return continentShadows.TryGetValue(code, out int v) ? v : 0;
        }
        catch { return 0; }
    }
    static QuestTable questTable;
    static QuestScriptTable questScripts;
    static SandboxQuests sandboxQuests;
    int flags;                         // the talk type of the server's last mode 1 / 2 (NpcTalkType)
    readonly List<int> talkQuests = new();   // the quests of the talk (QuestPacket.Talk)
    int questId;                       // the quest whose script runs, 0 for an npc script
    int subMode;                       // m_nSubMode: the last mode 3 packet's sub, 0 again once a mode 2 is sent
    UiAssets ui;
    CanvasLayer layer;
    MovieDialog window;
    TalkCinematicController side;
    FieldNpcs.Placed talking;
    NpcScriptTable.Script script;      // the server's script and page (m_nParam, m_nPage)
    int page;
    bool wasScripted;
    string playerName = "Player";
    /// <summary>$MyPCName: the live character's name, else the sandbox's.</summary>
    public string PlayerName { get => live?.Me.User?.Name ?? playerName; set => playerName = value; }
    public bool Open => window != null;
    /// <summary>UITalkManager::m_nNpcId / m_nTalking: the npc the talk is with, null when there is no talk.</summary>
    public FieldNpcs.Placed TalkingNpc => talking;
    /// <summary>The npc scripts (LoadNpcTalks), the monologues included.</summary>
    public NpcScriptTable ScriptTable => Scripts(packs);
    /// <summary>A content's text with its object and value words replaced, for this character.</summary>
    public string ResolveText(string text) => Scripts(packs).Resolve(text, PlayerName, player?.Gender);
    /// <summary>CEffectService::PlayVoiceAt: the voice event at a place.</summary>
    public static void PlayVoiceAt(string voice, Vector3 at) => Sound(voice, at);
    /// <summary>The page shown sends a quest packet on next: button set 6 accept (Quest 2), 7 complete (Quest 4), 8 progress (Quest 0x12).</summary>
    public bool OnQuestPage => side != null && side.ButtonSet is 6 or 7 or 8;
    public int Talks;
    /// <summary>The page shown (0 based) and its script id, for the shots.</summary>
    public int Page => page;
    public int ScriptId => script?.Id ?? 0;
    /// <summary>NpcTalk mode 3 sub 1 (UITalkManager::SetNpcNameTag): the talk npc's name tag text, for its emoticon.</summary>
    public event Action<FieldNpcs.Placed, string> NpcNameTag;
    /// <summary>NpcTalk mode 10 sub 1 (OnTalkPacket, uitalkcinematic.cpp:1054-1062): the npc's balloon line, for its chat
    /// balloon (the npc by the packet's field object id, WorldService::FindNpcById's m_148 key).</summary>
    public event Action<FieldNpcs.Placed, string> SpecialNpcBalloon;
    /// <summary>The talk page was shown (FUN_141b204d0, uitalkcinematic.cpp:1562-1565): the talk npc and m_nSubMode.</summary>
    public event Action<FieldNpcs.Placed, int> TalkPageShown;

    public NpcTalk(PackSet packs, List<FieldNpcs.Placed> npcs, Player player, ClientConstants constants, LiveSession live = null)
    {
        this.packs = packs; this.npcs = npcs; this.player = player; this.constants = constants; this.live = live;
        Name = "NpcTalk";
    }

    /// <summary>The field's talk in the tree (the mouse controller's npc pick reads it).</summary>
    public static NpcTalk Current { get; private set; }

    public override void _EnterTree()
    {
        Current = this;
        Ui.MessageBoxLayer.Ensure(this);    // the accept's unrequire-group box (CUIService::CreateAllDialogs has the one layer)
        if (live == null) return;
        live.NpcTalkReceived += OnLiveTalk;
        live.IsTalking = () => liveTalking != null;
        live.TalkEnded += OnTalkEndedByUi;
        live.Quests.NpcOffer += OnQuestOffer;
        live.Quests.Started += OnLiveQuestStarted;          // the popup's UIQuestEventListener slots 1 and 3
        live.Quests.ProgressUpdated += OnLiveQuestProgress;
    }

    /// <summary>A talk ui's close sent NpcTalk 0 (SendNpcTalkMode0): the talk is over (m_nTalking = 0) and ResetTalk hides the
    /// talk window.</summary>
    void OnTalkEndedByUi() { liveTalking = null; liveRequested = null; live.TalkNpcId = 0; Teardown(); }

    public override void _ExitTree()
    {
        if (Current == this) Current = null;
        if (window != null) { window = null; MovieLayerHost.Layers.LeaveTalk(); }   // a field left with the talk open
        ClosePopupMovie();
        if (live == null) return;
        live.NpcTalkReceived -= OnLiveTalk;
        live.TalkEnded -= OnTalkEndedByUi;
        live.IsTalking = () => false;
        live.Quests.NpcOffer -= OnQuestOffer;
        live.Quests.Started -= OnLiveQuestStarted;
        live.Quests.ProgressUpdated -= OnLiveQuestProgress;
    }
    public NpcTalk() { }

    static NpcScriptTable Scripts(PackSet packs) => scripts ??= new NpcScriptTable(packs, new GameStrings(packs, GameOptions.StringLanguage), e => FeatureElements.IsAvailable(packs, e));

    /// <summary>The client's quest data (QuestDataTable), loaded once.</summary>
    public static QuestTable ClientQuests(PackSet packs)
    {
        return questTable ??= DataCache.Quests(packs, DataCache.QuestConfig.Current(packs));
    }

    static QuestScriptTable QuestScripts(PackSet packs) =>
        questScripts ??= new QuestScriptTable(packs, (p, c, k) => FeatureElements.Collect(packs, p, c, k));

    SandboxQuests Records => sandboxQuests ??= new SandboxQuests(ClientQuests(packs));

    /// <summary>The character's quest records as the client's checks read them: the live session's, else the sandbox's.</summary>
    IQuestContext QuestContext => live != null ? LiveQuests : Records;
    QuestRules Rules => live != null ? (liveRules ??= new QuestRules(ClientQuests(packs), LiveQuests)) : Records.Rules;

    LiveQuestState LiveQuests => liveQuests ??= new LiveQuestState(live.Quests, ClientQuests(packs), live.GameSession, live.CharacterId,
        live.Me.User?.Level ?? StubHudData.Instance.Player.Level, live.FieldId, null)
    { JobGroup = live.Me.User?.JobCode };

    /// <summary>The record's state: 0 none, 1 started, 2 rewarded.</summary>
    int QuestState(int id) => QuestContext.Get(id)?.State ?? 0;

    /// <summary>
    /// FUN_140746ba0(quest, npc, 0) (questdata.cpp @0x140746ba0) is 0: the character has the quest's record, started (state 1),
    /// the npc completes it (m_2ec), and every condition is met (FUN_1407456d0).
    /// </summary>
    bool CanComplete(int quest, int npc) =>
        QuestContext.Get(quest) is { IsActive: true, Complete: true } && ClientQuests(packs).Quest(quest) is { } d && (npc == 0 || d.CompleteNpc == npc);

    static bool FeatureOn(string name) => !StubHudData.Instance.FeaturesOff.Contains(name);

    /// <summary>FUN_14074b840 (questdata.cpp @0x14074b840): the QuestProgress feature, no conditions, start npc = complete npc.</summary>
    bool AcceptRedirects(int quest) =>
        FeatureOn("QuestProgress") && ClientQuests(packs).Quest(quest) is { } d && d.Conditions.Count == 0 && d.StartNpc == d.CompleteNpc;

    /// <summary>FUN_14074b950 (questdata.cpp @0x14074b950): the QuestProgress feature and one following quest that starts at
    /// this quest's complete npc; that quest, else 0.</summary>
    int CompleteRedirectTo(int quest)
    {
        var t = ClientQuests(packs);
        if (!FeatureOn("QuestProgress") || t.Quest(quest) is not { } d || d.Following.Count != 1) return 0;
        return t.Quest((int)d.Following[0]) is { } next && next.StartNpc == d.CompleteNpc ? (int)d.Following[0] : 0;
    }

    /// <summary>
    /// Maple2's QuestManager.GetAvailableQuests in quest id order (the npc's quests that can start, then the started ones it
    /// completes), with the start check the client's (CQuestManager::vfunc_1, <see cref="QuestRules.CanStart(int, int)"/>):
    /// the Maple2 server reads the require from its own ingest, which lost quest 90000000's job (01, Newbie), so it offers quests the
    /// client then shows as not startable. Only quests with a talk script.
    /// </summary>
    List<int> AvailableQuests(int npc)
    {
        var qs = QuestScripts(packs);
        var r = Records;
        return ClientQuests(packs).Quests.Where(d => (d.StartNpc == npc && r.Rules.CanStart(d.Id, npc) == true)
                                               || (d.CompleteNpc == npc && QuestState(d.Id) == 1))
                                   .Select(d => d.Id).Where(id => qs.ScriptIds(id).Any()).ToList();
    }

    /// <summary>The Maple2 server GetQuestScriptState: the first script of 100-199 for a quest not started, 200-299 for a started one,
    /// 300-399 when this npc completes it.</summary>
    NpcScriptTable.Script QuestScript(int quest, int npc)
    {
        var ids = QuestScripts(packs).ScriptIds(quest).ToList();
        int state = QuestState(quest);
        var (lo, hi) = state == 0 ? (100, 200) : state == 1 ? (200, 300) : (0, 0);
        if (state == 1 && ClientQuests(packs).Quest(quest)?.CompleteNpc == npc) (lo, hi) = (300, 400);
        int id = ids.FirstOrDefault(i => i >= lo && i < hi);
        return id == 0 ? null : QuestScripts(packs).Find(quest, id);
    }

    /// <summary>One of the npc's randomPick scripts (the Maple2 server GetInitialScriptType Script), null when it has none.</summary>
    static NpcScriptTable.Script PickScript(NpcScriptTable.Npc n)
    {
        var picks = n?.Scripts.Where(x => x.Kind == "script" && x.RandomPick).ToList();
        return picks is { Count: > 0 } ? picks[Random.Shared.Next(picks.Count)] : null;
    }

    /// <summary>
    /// FUN_1416e6100(quest, 1) (questuidata.cpp:421) on the sandbox records, with the talk npc: rewarded 0x5dc, completable
    /// 0x5db (FUN_140746ba0), started 0x5da (IsQuestActive), can start 0x5d9 (vfunc_1), else 0x5d8 (s_quest_state_init, an
    /// empty string: "A New Path (/Lv. 10)" in the capture of a character the require keeps out).
    /// </summary>
    int StateString(int quest, int npc)
    {
        if (ClientQuests(packs).Quest(quest) == null) return 0;
        var r = QuestContext;
        if (r.Get(quest)?.IsRewarded == true) return 0x5dc;
        if (CanComplete(quest, npc)) return 0x5db;
        if (r.Get(quest)?.IsActive == true) return 0x5da;
        return Rules.CanStart(quest, npc) == true ? 0x5d9 : 0x5d8;
    }

    static NpcQuestMarks questMarks;

    /// <summary>The npcs' quest lists over the client's quest data (QuestDataTable m_430), built once.</summary>
    public static NpcQuestMarks QuestMarks(PackSet packs) => questMarks ??= new NpcQuestMarks(ClientQuests(packs));

    /// <summary>
    /// CQuestManager::GetNpcQuestState for the npc on the character's records (the live session's, else the sandbox's): the
    /// mark its overhead emoticon shows (CNpc's RefreshQuestState).
    /// </summary>
    public NpcQuestMarks.Mark QuestMark(int npcId) => QuestMarks(packs).Get(npcId, QuestContext, Rules);

    /// <summary>The sandbox: start a quest on its records, its conditions met or not (the emoticon shots).</summary>
    public bool SandboxAccept(int questId, bool conditionsMet) => live == null && Records.Accept(questId, conditionsMet);

    /// <summary>The sandbox: drop a quest's record (the emoticon shots step one quest through its states).</summary>
    public void SandboxForget(int questId) { if (live == null) Records.Forget(questId); }

    /// <summary>The complete talk's reward figures (FUN_140761ba0 ... FUN_140761ea0, FUN_140761c60) from the quest table.</summary>
    TalkCinematicController.QuestReward RewardOf(int quest)
    {
        var t = ClientQuests(packs);
        var d = t.Quest(quest);
        if (d == null) return null;
        var ctx = new QuestRewards.Context(StubHudData.Instance.Player.Level, 0);
        var r = new TalkCinematicController.QuestReward
        {
            Exp = t.Rewards.Exp(d, ctx),
            Meso = t.Rewards.Meso(d, ctx),
            Karma = t.Rewards.Karma(d),
            Lu = t.Rewards.Lu(d),
            GuildExp = t.Rewards.GuildExp(d, ctx),
            GuildFund = t.Rewards.GuildFund(d, ctx),
            FamePoint = t.Rewards.FamePoint(d),
            GuildCoin = t.Rewards.GuildCoin(d, ctx),
        };
        foreach (var x in d.Complete.ChoiceRewards) r.ChoiceRewards.Add((x.ItemId, x.Rank, x.Count));
        return r;
    }

    /// <summary>TalkAction::vfunc_1's check (actionkeyobject.cpp:1385-1398) on the found npc, on the character's quest records.</summary>
    bool HasTalk(NpcRow r) => QuestMarks(packs).HasTalk(r.Id, r.Kind, Scripts(packs).Find(r.Id) != null, QuestContext, Rules,
        (quest, script) => QuestScripts(packs).Find(quest, script) != null);

    /// <summary>
    /// MyPC::FindTalkableNpc (mypc.cpp:2825, @0x141529640): WorldService::FindNearestNpc friendly 2, else friendly 1; then
    /// TalkAction::vfunc_1 (actionkeyobject.cpp:1382-1405) talks to that npc only if it has something to say. The nearest npc
    /// is picked first: one with nothing to say keeps the key from a talkable npc further away, as in the client.
    /// TalkAction refuses a hidden npc (GameObject::FUN_14123c660, m_unk_b0, actionkeyobject.cpp:1385; GameObject::vfunc_23 sets
    /// it with the entity's visible property, here the root's Visible) even when a shown one stands further away.
    /// </summary>
    public FieldNpcs.Placed FindTalkable()
    {
        if (player == null) return null;
        var n = FindNearestNpc(2) ?? FindNearestNpc(1);
        return n != null && n.Root.Visible && HasTalk(n.Row) ? n : null;
    }

    /// <summary>
    /// WorldService::FindNearestNpc(friendly, TalkableDistance, TalkableFrontOnly, 1) (worldservice_1.cpp:6438): the npcs of that
    /// friendly value within the range plus their capsule radius (m_rangeBonus, +0x2d8), not behind the player's facing plane
    /// unless the npc has property tag 8 "dummy_npc" (:6467, npcdata.cpp's tag list), nearest first. It does not look at
    /// visibility.
    /// </summary>
    FieldNpcs.Placed FindNearestNpc(int friendly)
    {
        float range = constants.Float("TalkableDistance", 300);
        bool frontOnly = constants.Float("TalkableFrontOnly", 1) != 0;
        var me = player.GlobalPosition;
        var face = new Vector3(Mathf.Sin(player.Visual.Rotation.Y), 0, Mathf.Cos(player.Visual.Rotation.Y));
        FieldNpcs.Placed best = null; float bestD = float.MaxValue;
        foreach (var n in live != null ? live.TalkNpcs : npcs)
        {
            if (n.Row.Friendly != friendly) continue;
            var to = n.Position - me;
            float d = to.Length();
            if (d > range + n.Row.CapsuleRadius) continue;
            if (frontOnly && !n.Row.HasTag("dummy_npc") && new Vector3(to.X, 0, to.Z).Dot(face) < 0) continue;
            if (d < bestD) { bestD = d; best = n; }
        }
        return best;
    }

    /// <summary>
    /// CMouseInteractNpc::FindTarget (mouseinteract.cpp:307) for a click ray (Godot space): the first npc, in object id order
    /// (the npc map), that is shown, not an enemy, passes <see cref="NpcMousePick.Picks"/> (its kind, npc talks or shown quest
    /// mark, while a guide event does not restrict talking) and whose sphere the ray passes through. No distance limit: the
    /// pick range (m_pickRange) is the hover cursor's only.
    /// STILL-OPEN: the talk interval (UITalkManager::IsTalkIntervalElapsed) and holding (PCBase::vfunc_117, HoldAction) are not
    /// ported, as for the talk key.
    /// </summary>
    public FieldNpcs.Placed FindClickTarget(Vector3 origin, Vector3 dir)
    {
        if (player == null) return null;
        bool talkAllowed = Ms2.Ui.Guide.GuideManager.Current?.IsRestricted(Ms2.Ui.Guide.GuideLimit.ActionTalk) != true;
        foreach (var n in (live != null ? live.TalkNpcs : npcs).OrderBy(x => x.ObjectId))
        {
            if (!n.Root.Visible || n.Row.Friendly == 0) continue;
            if (!NpcMousePick.Picks(n.Row.Kind, Scripts(packs).Find(n.Row.Id) != null, QuestMark(n.Row.Id).State, false, talkAllowed, false)) continue;
            float half = n.Row.CapsuleHeight * 0.5f;
            var c = n.Position + new Vector3(0, half, 0);
            if (NpcMousePick.RayHitsSphere(new(origin.X, origin.Y, origin.Z), new(dir.X, dir.Y, dir.Z), new(c.X, c.Y, c.Z), half)) return n;
        }
        return null;
    }

    /// <summary>CMouseController::UpdateHover's activate (unit_140ad1860_mousecontroller.cpp:705-715): when the talk key's npc is
    /// the clicked one, the talk starts (CMouseInteract::vfunc_4, ActivateCurrentAction) and true is returned.</summary>
    public bool TalkIfCurrent(FieldNpcs.Placed clicked)
    {
        if (clicked == null || window != null || FindTalkable() != clicked) return false;
        Start(clicked);
        return true;
    }

    /// <summary>The script the server opens a plain talk with (see the class summary).</summary>
    static NpcScriptTable.Script FirstScript(NpcScriptTable.Npc n)
    {
        if (n == null) return null;
        var picks = n.Scripts.Where(x => x.Kind == "script" && x.RandomPick).ToList();
        if (picks.Count > 0) return picks[Random.Shared.Next(picks.Count)];
        return n.Select ?? n.Scripts.FirstOrDefault(x => x.Kind == "script");
    }

    /// <summary>The page as the client builds it (CCinematicContent): the text with its words, the illust, the voice, the choices.</summary>
    TalkCinematicController.TalkContent ContentOf(NpcScriptTable.Content c)
    {
        var st = Scripts(packs);
        var t = new TalkCinematicController.TalkContent
        {
            Script = st.Resolve(c.Text, PlayerName, player?.Gender),
            Illust = c.Illust ?? "",
            Voice = c.VoiceId ?? "",
            QuestId = c.QuestId,
            Redirect = c.Redirect,
            OpenTalkReward = c.OpenTalkReward,
        };
        foreach (var (text, go) in c.Distractors) t.Distractors.Add((st.Resolve(text, PlayerName, player?.Gender), go.Length > 0));
        return t;
    }

    /// <summary>The Maple2 server NpcScriptManager.GetButton: the page's own set, 9 with choices, 4 with pages after it; on the last page 5
    /// for a select talk, 6 / 8 / 7 for a quest's 100 / 200 / 300 script, else 3.</summary>
    int ButtonsOf(int i)
    {
        var c = script.Contents[i];
        if (c.ButtonSet != 0) return c.ButtonSet;
        if (c.Distractors.Count > 0) return 9;
        if (i + 1 < script.Contents.Count) return 4;
        if ((flags & 8) != 0) return 5;
        if (questId != 0) return (script.Id / 100) switch { 1 => 6, 2 => 8, 3 => 7, _ => 3 };
        return 3;
    }

    /// <summary>A neutral page for an npc whose only talk is its function (a shop, a storage ...), which the sandbox does not open.</summary>
    static NpcScriptTable.Script Neutral() => new() { Kind = "script", Contents = { new NpcScriptTable.Content { Text = "..." } } };

    public bool Start(FieldNpcs.Placed npc)
    {
        if (window != null || npc == null) return false;
        if (live != null) return RequestLiveTalk(npc);
        var n = Scripts(packs).Find(npc.Row.Id);
        // Maple2's HandleTalk: the quests (flag 4), the talk script (flag 2), and with more than one of them and a select, flag 8
        talkQuests.Clear();
        talkQuests.AddRange(AvailableQuests(npc.Row.Id));
        var pick = PickScript(n);
        flags = 0; questId = 0;
        if (talkQuests.Count > 0) flags |= 4;
        if (pick != null) flags |= 2;
        if (talkQuests.Count > 0 && pick != null && n?.Select != null) flags |= 8;
        if ((flags & 8) != 0) script = n.Select;
        else if ((flags & 4) != 0)
        {
            flags = 4;
            questId = talkQuests[0];
            script = QuestScript(questId, npc.Row.Id);
        }
        else script = FirstScript(n);
        if (script == null || script.Contents.Count == 0) { script = Neutral(); questId = 0; }
        page = 0;
        OpenWindow(npc, ButtonsOf(0), (flags & 4) != 0 ? talkQuests : Enumerable.Empty<int>());
        return true;
    }

    /// <summary>
    /// The window on <see cref="script"/>'s <see cref="page"/> (FUN_141b1ff80: SetSubScene 0x6200031, the npc's start sound), with the
    /// button set and the quest list (Quest mode 1 before the talk) the server gave.
    /// </summary>
    void OpenWindow(FieldNpcs.Placed npc, int buttonSet, IEnumerable<int> quests)
    {
        talking = npc;
        ui ??= new UiAssets(packs);
        tables ??= GameTables.Shared(packs, GameOptions.StringLanguage);
        if (!UiCode.Load(Settings.DataDir)) GD.PushWarning("UI code is not built for this data: the talk window runs interpreted");
        var size = (Vector2)ScreenMode.PixelSize(GetWindow());
        side = new TalkCinematicController
        {
            Talking = true,
            TalkNpcId = npc.Row.Id,
            Content = ContentOf(script.Contents[page]),
            ButtonSet = buttonSet,
            Flags = flags,
            QuestOf = q => ClientQuests(packs).Quest(q) is { } d ? new(ClientQuests(packs).QuestName(q), d.Level, NpcQuestMarks.Suffix(d)) : null,
            QuestStateString = q => StateString(q, npc.Row.Id),
            IsQuestActive = q => QuestState(q) == 1,
            IsQuestRewarded = q => QuestState(q) == 2,
            QuestRewardOf = RewardOf,
            AcceptRedirects = AcceptRedirects,
            CompleteRedirectTo = CompleteRedirectTo,
            HasSummonPortal = q => ClientQuests(packs).Quest(q)?.SummonPortalField is > 0,
            HasUnrequireGroup = q => ClientQuests(packs).Quest(q)?.Require.UnrequireGroup.Length > 0,
            NpcName = id => Scripts(packs).NpcDisplayName(id),
            NpcIllust = id => id == npc.Row.Id ? npc.Row.Illust ?? "" : "",
            FileExists = ImageExists,
            ClientWidth = (int)size.X,
            ClientHeight = (int)size.Y,
            MovieScale = UiScale.ViewportFactor(size.X),
        };
        side.QuestIds.AddRange(quests);
        if (live != null)
        {
            // the client's sends; Quest 2 / 4 carry the talk npc's object id (UITalkManager::GetNpcId is the npc's object id)
            int obj = npc.ObjectId;
            side.SendNext += pick => live.SendTalk(new NpcTalkNextRequest(pick));
            side.SendEnd += () => { liveTalking = null; live.SendTalk(new NpcTalkEndRequest()); };
            side.SendQuestAccept += (q, _) => { GD.Print($"talk: quest {q} accept sent"); live.SendTalk(new QuestAcceptRequest(q, obj)); };
            side.SendQuestComplete += (q, _, choice) => { GD.Print($"talk: quest {q} complete sent"); live.SendTalk(new QuestCompleteRequest(q, obj, choice)); };
            side.SendQuestRedirect += (q, kind) => live.SendTalk(new NpcTalkQuestRedirectRequest(q, kind));
            side.SendQuestProgress += q => live.SendTalk(new QuestProgressRequest(q));
            side.SendTakeBoat += _ => live.SendTalk(new TakeBoatRequest(obj));
            // CPaneltyResolveCinematicComponent::vfunc_2's reads: the RevivalConfirm count, level, meso and the field's shadow continent
            side.SendResolvePenalty += _ => live.SendTalk(new ResolvePenaltyRequest(obj));
            side.RevivalConfirmCount = () => live.Me.RevivalConfirmCount;
            side.PlayerLevel = () => live.Me.User?.Level ?? 0;
            side.Meso = () => live.Me.Meso;
            side.ShadowContinent = () => ShadowContinentOf(live.FieldId ?? 0);
            side.SystemNotice += id => live.SystemNoticeSink?.Invoke(tables.Common(id));
        }
        else
        {
            side.SendNext += OnNext;
            // Maple2's QuestHandler Accept / Complete on the sandbox records
            side.SendQuestAccept += (q, _) =>
            {
                bool started = Records.Accept(q);
                GD.Print($"talk: quest {q} accept: {(started ? "started" : "refused")}");
                if (started) PopupDialog().OnQuestStarted(q);      // the server's Quest mode 2: the popup's listener slot 1
            };
            side.SendQuestComplete += (q, _, _) => GD.Print($"talk: quest {q} complete: {(Records.Complete(q) ? "rewarded" : "refused")}");
            side.SendQuestRedirect += OnQuestRedirect;
            side.SendQuestProgress += q => GD.Print($"talk: quest {q} progress (Quest mode 0x12; the Maple2 server does not handle it)");
            side.SendEnd += () => { };                      // NpcTalk mode 0: nothing to tell offline
            side.SendTakeBoat += _ => GD.Print("talk: TakeBoat not sent (offline)");
            side.SendResolvePenalty += _ => GD.Print("talk: ResolvePenalty not sent (offline)");
            side.SystemNotice += id => GD.Print($"talk: system notice {tables.Common(id)}");
        }
        side.SendNext += _ => subMode = 0;              // SendNpcTalkMode2 (uitalkcinematic.cpp:1675)
        // UITalkManager::GetTalkNpc (uitalkcinematic.cpp:1778) is FindNpcById each time; ShowPage's voice (:467) and the page's
        // emoticon (:1559-1563) need that npc and do nothing when it has left
        side.PageShown += () => { if (TalkNpcNow() is { } npc) TalkPageShown?.Invoke(npc, subMode); };
        side.TalkReset += () =>
        {
            // UITalkManager::ResetTalk (uitalkcinematic.cpp:1507) finds the npc again by id (FindNpcById, :1523) and plays its end
            // sound only if it is still there; a talk that ended because the npc left has no one to play it
            var npc = TalkNpcNow();
            if (npc != null) Sound(npc.Row.SoundEnd, npc.Position);
        };
        side.PlayVoice += v => { if (TalkNpcNow() is { } npc) Sound(v, npc.Position); };
        side.PlaySystemSound += key => FieldAudio.Instance?.PlayUi(key);
        side.CloseComplete += () => CallDeferred(nameof(Teardown));
        layer = new CanvasLayer { Layer = 20, Name = "TalkCinematic" };
        AddChild(layer);
        MovieLayerHost.Layers.EnterTalk();             // the field's scene gives way to the talk scene (0x6200031)
        wasScripted = player.ScriptDriven;
        player.ScriptDriven = true;
        player.Input2 = Vector2.Zero;
        Sound(npc.Row.SoundStart, npc.Position);          // CNpc::PlayStartSound (FUN_141b1ff80)
        var model = new UiModel(tables);
        model.Player.Level = StubHudData.Instance.Player.Level;
        model.Player.JobGroup = StubHudData.Instance.Player.Job;     // the complete talk's reward items (FUN_140748050)
        window = new MovieDialog(ui, side, model, size);
        layer.AddChild(window);
        Talks++;
        GD.Print($"talk npc {npc.Row.Id} {npc.Row.Name}: script {script.Id} ({script.Contents.Count} page(s)), illust {npc.Row.Illust}");
    }

    /// <summary>Maple2's NpcTalkHandler.HandleQuest for the sandbox: NpcTalk mode 7 opens the quest's script for this npc.</summary>
    void OnQuestRedirect(int quest, short kind)
    {
        if (side == null || talking == null) return;
        flags = 4;
        questId = quest;
        script = QuestScript(quest, talking.Row.Id);
        if (script == null || script.Contents.Count == 0) { side.OnServerEnd(); return; }
        page = 0;
        ServerPage();
    }

    // ---- live -------------------------------------------------------------------------------------------------------

    /// <summary>The talk key on a live npc (uitalkcinematic.cpp:1432): NpcTalk mode 1 with its object id; the window opens on the
    /// server's answer.</summary>
    bool RequestLiveTalk(FieldNpcs.Placed npc)
    {
        if (liveTalking != null) return false;
        if (liveRequested != null && Time.GetTicksMsec() - liveRequestedAt < 3000) return false;
        liveRequested = npc; liveRequestedAt = Time.GetTicksMsec();
        live.SendTalk(new NpcTalkBeginRequest(npc.ObjectId));
        GD.Print($"talk: NpcTalk 1 sent to npc {npc.Row.Id} (object {npc.ObjectId})");
        return true;
    }

    /// <summary>
    /// The npc ResetTalk (uitalkcinematic.cpp:1507) plays PlayEndSound on, and GetTalkNpc (:1778) returns: FindNpcById(m_nNpcId) (:1523), so null when the npc has
    /// left the field. Offline (<paramref name="find"/> null) the sandbox's npcs never leave and the talked-to npc is used.
    /// </summary>
    public static FieldNpcs.Placed ResetTalkNpc(FieldNpcs.Placed talking, Func<int, FieldNpcs.Placed> find)
        => talking == null ? null : find == null ? talking : find(talking.ObjectId);

    /// <summary>UITalkManager::GetTalkNpc (uitalkcinematic.cpp:1778): the talk's npc found again by its id, null when it has left.</summary>
    FieldNpcs.Placed TalkNpcNow() => ResetTalkNpc(talking, live == null ? null : LiveNpc);

    FieldNpcs.Placed LiveNpc(int objectId) => live.TalkNpcs.FirstOrDefault(x => x.ObjectId == objectId);

    /// <summary>Quest mode 1 (questmanager_2.cpp:996): FUN_141b217b0 clears the talk's quest ids, FUN_141b217e0 adds each one.</summary>
    void OnQuestOffer(int npcObject, IReadOnlyList<int> ids)
    {
        talkQuests.Clear();
        talkQuests.AddRange(ids);
        GD.Print($"talk: Quest 1 from object {npcObject}: {string.Join(" ", ids)}");
    }

    /// <summary>UITalkManager::FindTalkScript (uitalkcinematic.cpp:1945) on the talk type and the server's script id.</summary>
    NpcScriptTable.Script FindTalkScript(FieldNpcs.Placed npc, int param)
    {
        var n = Scripts(packs).Find(npc.Row.Id);
        if ((flags & 8) != 0) return n?.Select;                                    // GetNpcPickTalk(npc, 0)
        if ((flags & 4) != 0) return talkQuests.Count > 0 ? QuestScripts(packs).Find(talkQuests[0], param) : null;   // FindQuestTalk(m_ids[0], param)
        if ((flags & 2) != 0 || (flags & 0x20) != 0) return n?.ById(param);         // FUN_140855cd0 -> FUN_140856820(npc, param)
        if ((flags & 1) != 0) return n?.Scripts.FirstOrDefault(x => x.Kind == "job");   // GetNpcPickTalk(npc, 1)
        return null;
    }

    /// <summary>UITalkManager::OnTalkPacket (uitalkcinematic.cpp:971) for the window.</summary>
    void OnLiveTalk(NpcTalkMessage m)
    {
        if (NpcTalkMessage.SubMode(m) is { } sub) subMode = sub;
        switch (m)
        {
            case NpcTalkMessage.Open o:
                {
                    liveRequested = null;
                    var npc = LiveNpc(o.ObjectId);
                    if (npc == null || window != null) { GD.Print($"talk: mode 1 for object {o.ObjectId}: {(npc == null ? "no such npc" : "a talk is open")}"); return; }
                    flags = o.Flags; page = o.Page;
                    questId = (flags & 4) != 0 && talkQuests.Count > 0 ? talkQuests[0] : 0;
                    script = FindTalkScript(npc, o.ScriptId);
                    liveTalking = npc;
                    live.TalkNpcId = o.ObjectId;
                    if (script == null || page >= script.Contents.Count)
                    {
                        // FUN_141b1ff80 (uitalkcinematic.cpp:1456-1477) without a script opens the npc's talk ui
                        // (CNpc::GetTalkUiId, unit_14108e1e0_npcactorandvibrate.cpp:1327: kind 1 -> 0x4e3b shop). Closing
                        // the shop sends SendNpcTalkMode0 (uishopdialog.cpp:711).
                        if (npc.Row.Kind == 1)
                        {
                            GD.Print($"talk: npc {npc.Row.Id} opens shop talk ui 0x4e3b");
                            live.RaiseTalkUiOpen(Ms2.Ui.Shop.ShopController.UiId);
                            return;
                        }
                        GD.Print($"talk: npc {npc.Row.Id} type {flags} script {o.ScriptId}: no talk script (its talk ui is not ported), NpcTalk 0 sent");
                        liveTalking = null;
                        live.TalkNpcId = 0;
                        live.SendTalk(new NpcTalkEndRequest());
                        return;
                    }
                    OpenWindow(npc, o.ButtonSet, (flags & 4) != 0 || (flags & 8) != 0 ? talkQuests : Enumerable.Empty<int>());
                    return;
                }
            case NpcTalkMessage.Next n:
                LivePage(n.Flags, n.QuestId, n.ScriptId, n.Page, n.ButtonSet);
                return;
            case NpcTalkMessage.Reload r:
                LivePage(r.Flags, r.QuestId, r.ScriptId, r.Page, r.ButtonSet);
                return;
            case NpcTalkMessage.ReloadNoQuest r:
                talkQuests.Clear();
                LivePage(r.Flags, null, r.ScriptId, r.Page, r.ButtonSet);
                return;
            case NpcTalkMessage.Close:
                if (liveTalking?.Row.Kind == 1) live.RaiseTalkUiClose(Ms2.Ui.Shop.ShopController.UiId);
                liveRequested = null; liveTalking = null; live.TalkNpcId = 0;
                side?.OnServerEnd();
                return;
            case NpcTalkMessage.OpenDialog d:
                // FUN_141b1eb90: the dialog by its type name, its argument first (SetModeByName for the enchant dialog)
                live.RaiseTalkDialogOpen(d.Dialog, d.Argument);
                return;
            case NpcTalkMessage.NameTag t:
                // GetTalkNpc: the npc of the talk's object id (m_nNpcId, set by mode 1)
                if ((liveTalking ?? talking) is { } tagged) NpcNameTag?.Invoke(tagged, t.Text);
                return;
            case NpcTalkMessage.SpecialNpc s:
                // sub 1 (uitalkcinematic.cpp:1054-1062): FindNpcById is by the FIELD OBJECT id (worldservice_1.cpp:6176,
                // keyed by GetObjectId at :3159), as mode 1's object id; its balloon kept, then the first content's line
                // in it (PCBase::ShowChatBalloon); the other subs open the special npc's own talk page (m_nSpecialNpc),
                // which is not ported
                if (s.Sub != 1 || s.Script.Contents.Count == 0)
                {
                    GD.Print($"talk: mode 10 sub {s.Sub}: the special npc's talk page is not ported");
                    return;
                }
                var special = LiveNpc(s.NpcId);
                if (special == null) { GD.Print($"talk: mode 10 for object {s.NpcId}: no such npc"); return; }
                SpecialNpcBalloon?.Invoke(special, s.Script.Contents[0].Text);
                return;
            case NpcTalkMessage.RewardMeso x: side?.OnRewardMeso(x.Amount); return;
            case NpcTalkMessage.RewardMerat x: side?.OnRewardMerat(x.Amount); return;
            case NpcTalkMessage.RewardExp x: side?.OnRewardExp(x.Amount); return;
            case NpcTalkMessage.RewardAffinity x: side?.OnRewardAffinity(x.Amount, x.Jackpot); return;
            default:
                GD.Print($"talk: {m} not handled by the talk window");
                return;
        }
    }

    /// <summary>
    /// Modes 2 / 8 / 9 (FUN_141b204d0 @0x141b204d0, ReloadScript): the quest list becomes the packet's id (mode 9 clears it), the
    /// script is found again and ShowCurrentPage (uitalkcinematic.cpp:1636-1663) shows its page. With no script it opens the npc's
    /// talk ui (not ported, see OnLiveTalk) or returns 0, and the talk ends (SendNpcTalkMode0); a page past the script's end
    /// returns 1: nothing is shown and nothing is sent.
    /// </summary>
    void LivePage(int newFlags, int? quest, int scriptId, int newPage, int buttonSet)
    {
        flags = newFlags; page = newPage;
        if (quest is { } q) { talkQuests.Clear(); talkQuests.Add(q); }
        questId = (flags & 4) != 0 && talkQuests.Count > 0 ? talkQuests[0] : 0;
        var npc = talking ?? liveTalking;
        if (npc == null) return;
        script = FindTalkScript(npc, scriptId);
        if (script == null)
        {
            GD.Print($"talk: type {flags} script {scriptId}: not in the client's scripts, the talk ends");
            if (side != null) side.EndTalk();
            else { liveTalking = null; live.SendTalk(new NpcTalkEndRequest()); }
            return;
        }
        if (page >= script.Contents.Count)
        {
            GD.Print($"talk: type {flags} script {scriptId} page {page}: past the script's {script.Contents.Count} page(s), nothing shown");
            return;
        }
        if (side == null)
        {
            OpenWindow(npc, buttonSet, talkQuests.ToList());
            return;
        }
        side.Flags = flags;
        side.QuestIds.Clear(); side.QuestIds.AddRange(talkQuests);
        side.OnServerPage(ContentOf(script.Contents[page]), buttonSet);
    }

    /// <summary>FUN_141b70ea0 for an illust path (./Data/Resource/Image/...): the image is in the data.</summary>
    bool ImageExists(string path)
    {
        int at = path.IndexOf("resource/image/", StringComparison.OrdinalIgnoreCase);
        return at >= 0 && packs.Exists("Resource/Image", path[(at + "resource/image/".Length)..].ToLowerInvariant());
    }

    /// <summary>The server's answer to NpcTalk mode 2: the next page, a choice's script, or the end (mode 0).</summary>
    void OnNext(int pick)
    {
        if (side == null || script == null) return;
        if ((flags & 8) != 0)
        {
            // Maple2's HandleContinue on a select: the quests first (BeginQuest), then the talk (EnterTalk)
            if ((flags & 4) != 0 && pick >= 0 && pick < talkQuests.Count)
            {
                flags = 4;
                questId = talkQuests[pick];
                script = QuestScript(questId, talking.Row.Id);
            }
            else
            {
                flags = 2;
                questId = 0;
                script = PickScript(Scripts(packs).Find(talking.Row.Id));
            }
            if (script == null || script.Contents.Count == 0) { side.OnServerEnd(); return; }
            page = 0;
            ServerPage();
            return;
        }
        var c = script.Contents[page];
        if (c.Distractors.Count > 0)
        {
            var go = pick >= 0 && pick < c.Distractors.Count ? c.Distractors[pick].Goto : Array.Empty<int>();
            var n = Scripts(packs).Find(talking.Row.Id);
            var targets = go.Select(id => questId != 0 ? QuestScripts(packs).Find(questId, id) : n?.ById(id))
                            .Where(x => x != null && x.Contents.Count > 0).ToList();
            if (targets.Count == 0) { side.OnServerEnd(); return; }
            script = targets[Random.Shared.Next(targets.Count)];
            page = 0;
        }
        else if (page + 1 < script.Contents.Count) page++;
        else { side.OnServerEnd(); return; }
        ServerPage();
    }

    /// <summary>The server's mode 2 (Continue): the talk type, the quest (m_ids), the page and its button set.</summary>
    void ServerPage()
    {
        side.Flags = flags;
        if (questId != 0) { side.QuestIds.Clear(); side.QuestIds.Add(questId); }
        side.OnServerPage(ContentOf(script.Contents[page]), ButtonsOf(page));
    }

    /// <summary>The player ends the talk (Esc: NpcTalk mode 0 and ResetTalk).</summary>
    public void End()
    {
        if (side == null && live != null && liveTalking != null) { liveTalking = null; live.SendTalk(new NpcTalkEndRequest()); return; }
        if (side == null) return;
        if (side.Talking) side.Key(TalkCinematicController.TalkKey.End);
        if (side.Talking) side.EndTalk();       // before the movie is loaded the key does nothing
    }

    static void Sound(string path, Vector3 at)
    {
        if (!string.IsNullOrEmpty(path)) FieldAudio.Instance?.PlayEventAt(path, at);
    }

    void Teardown()
    {
        if (window == null) return;
        window.Host.Unload();
        layer?.QueueFree();
        layer = null; window = null; side = null; talking = null; script = null; liveTalking = null;
        MovieLayerHost.Layers.LeaveTalk();             // and back to the field's scene
        player.ScriptDriven = wasScripted;
        player.Input2 = Vector2.Zero;
    }

    /// <summary>The action key: Next on the open window, or start a talk with the npc in front.</summary>
    public bool ActionKey()
    {
        PopupKey(PopupNpcTalkController.PopupKey.Talk);    // the popup's vfunc_13 does not take the key
        if (window != null)
        {
            side.Key(TalkCinematicController.TalkKey.Next);
            return true;
        }
        return Start(FindTalkable());
    }

    public override void _Input(InputEvent e)
    {
        if (Ms2.Ui.FieldMoveCover.Up) return;       // the talk ended at the move; the next field's has not begun
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) PopupKey(PopupNpcTalkController.PopupKey.Escape);
        if (window == null || side == null) return;
        if (e is not InputEventKey { Pressed: true } k) return;
        TalkCinematicController.TalkKey? key = k.Keycode switch
        {
            Key.Escape when !k.Echo => TalkCinematicController.TalkKey.End,
            Key.Up => TalkCinematicController.TalkKey.Up,
            Key.Down => TalkCinematicController.TalkKey.Down,
            _ => null,
        };
        if (key == null) return;
        var vp = GetViewport();   // taken before the dispatch, which can change the scene
        side.Key(key.Value);
        if (IsInstanceValid(vp)) vp.SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (window != null) player.Input2 = Vector2.Zero;
        EndTalkIfNpcGone();
    }

    /// <summary>
    /// UITalkManager::EndTalkIfNpcGone (uitalkcinematic.cpp:1612), each frame from UISceneGame::vfunc_15 (uiscenegame.cpp:237):
    /// during a talk, the npc is found again by its object id (WorldService::FindNpcById), and the talk ends (SendNpcTalkMode0)
    /// when it is gone or out of talk range: CNpc::IsInTalkRange (npcactorandvibrate.cpp:1379), always in range with property
    /// tag 0x1a, else the distance from the player (FUN_14123daa0 from WorldService's focus position) within TalkableDistance
    /// (default 400) plus the npc's capsule radius. An npc re-added under the same object id is the one talked to from then on.
    /// It also ends while a guide event restricts talking (CGuideManager::IsRestricted(2), :1627).
    /// Live only: the sandbox's npcs never leave.
    /// </summary>
    void EndTalkIfNpcGone()
    {
        if (live == null || player == null) return;
        var npc = talking ?? liveTalking;
        if (npc == null) return;
        var now = LiveNpc(npc.ObjectId);
        if (now != null && now != npc)
        {
            if (talking == npc) talking = now;
            if (liveTalking == npc) liveTalking = now;
        }
        bool inRange = now != null && (now.Row.HasTag("26")
            || (now.Position - player.GlobalPosition).Length() <= constants.Float("TalkableDistance", 400) + now.Row.CapsuleRadius);
        bool restricted = inRange && Ms2.Ui.Guide.GuideManager.Current?.IsRestricted(Ms2.Ui.Guide.GuideLimit.ActionTalk) == true;
        if (inRange && !restricted) return;
        GD.Print($"talk: npc {npc.Row.Id} (object {npc.ObjectId}) {(now == null ? "is gone" : restricted ? "is held by the guide" : "is out of talk range")}, the talk ends");
        if (side != null && side.Talking) side.EndTalk();
        else { liveTalking = null; talking = null; live.SendTalk(new NpcTalkEndRequest()); }
    }
}
