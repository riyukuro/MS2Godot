using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Quest;
using Ms2Godot.Ui;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.World;

/// <summary>
/// Talking to an npc in the offline sandbox (the client's TalkAction + UITalkManager, with the sandbox answering the server's part).
/// - Who: MyPC::FindTalkableNpc -> WorldService::FindNearestNpc, friendly 2 first, then friendly 1: within TalkableDistance plus
///   the npc's capsule radius, in front of the player (TalkableFrontOnly), nearest first; the npc must have something to say (a
///   function kind or an npc script).
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
/// The player stands still while the window is open. Keys (UITalkCinematic::vfunc_13): the action key (Space) is Next, Esc ends
/// the talk, Up / Down move the choice focus.
/// </summary>
public sealed partial class NpcTalk : Node
{
    readonly PackSet packs;
    readonly List<FieldNpcs.Placed> npcs;
    readonly Player player;
    readonly ClientConstants constants;
    static NpcScriptTable scripts;
    static GameTables tables;
    static QuestTable questTable;
    static QuestScriptTable questScripts;
    static SandboxQuests sandboxQuests;
    int flags;                         // the talk type of the server's last mode 1 / 2 (NpcTalkType)
    readonly List<int> talkQuests = new();   // the quests of the talk (QuestPacket.Talk)
    int questId;                       // the quest whose script runs, 0 for an npc script
    UiAssets ui;
    CanvasLayer layer;
    MovieDialog window;
    TalkCinematicController side;
    FieldNpcs.Placed talking;
    NpcScriptTable.Script script;      // the server's script and page (m_nParam, m_nPage)
    int page;
    bool wasScripted;
    public string PlayerName = "Player";
    public bool Open => window != null;
    public int Talks;
    /// <summary>The page shown (0 based) and its script id, for the shots.</summary>
    public int Page => page;
    public int ScriptId => script?.Id ?? 0;

    public NpcTalk(PackSet packs, List<FieldNpcs.Placed> npcs, Player player, ClientConstants constants)
    {
        this.packs = packs; this.npcs = npcs; this.player = player; this.constants = constants;
        Name = "NpcTalk";
    }
    public NpcTalk() { }

    static NpcScriptTable Scripts(PackSet packs) => scripts ??= new NpcScriptTable(packs, new GameStrings(packs, "en"));

    static QuestTable Quests(PackSet packs)
    {
        var d = StubHudData.Instance;
        return questTable ??= QuestTable.Load(packs, new GameStrings(packs, "en").Languages, d.Locale, f => !d.FeaturesOff.Contains(f));
    }

    static QuestScriptTable QuestScripts(PackSet packs) =>
        questScripts ??= new QuestScriptTable(packs, (p, c, k) => FeatureElements.Collect(packs, p, c, k));

    SandboxQuests Records => sandboxQuests ??= new SandboxQuests(Quests(packs));

    /// <summary>The sandbox record's state: 0 none, 1 started, 2 rewarded.</summary>
    int QuestState(int id) => Records.Get(id)?.State ?? 0;

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
        return Quests(packs).Quests.Where(d => (d.StartNpc == npc && r.Rules.CanStart(d.Id, npc) == true)
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
        if (state == 1 && Quests(packs).Quest(quest)?.CompleteNpc == npc) (lo, hi) = (300, 400);
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
        if (Quests(packs).Quest(quest) == null) return 0;
        var r = Records;
        if (r.Get(quest)?.IsRewarded == true) return 0x5dc;
        if (r.CanComplete(quest, npc)) return 0x5db;
        if (r.Get(quest)?.IsActive == true) return 0x5da;
        return r.Rules.CanStart(quest, npc) == true ? 0x5d9 : 0x5d8;
    }

    /// <summary>SelectStringByQuestKind (cinematiccomponent.cpp:810) with "_epic", "", "_repeat", "_event": by the quest
    /// type (FUN_14074b050), 1 and 6 by whether it repeats (FUN_1407640c0).</summary>
    static string IconSuffix(QuestData d) => d.Type switch
    {
        0 => "_epic",
        2 => "_event",
        1 or 6 => d.Repeat != 0 ? "_repeat" : "",
        _ => "",
    };

    /// <summary>The complete talk's reward figures (FUN_140761ba0 ... FUN_140761ea0, FUN_140761c60) from the quest table.</summary>
    TalkCinematicController.QuestReward RewardOf(int quest)
    {
        var t = Quests(packs);
        var d = t.Quest(quest);
        if (d == null) return null;
        var ctx = new QuestRewards.Context(StubHudData.Instance.Player.Level, 0);
        var r = new TalkCinematicController.QuestReward
        {
            Exp = t.Rewards.Exp(d, ctx), Meso = t.Rewards.Meso(d, ctx), Karma = t.Rewards.Karma(d), Lu = t.Rewards.Lu(d),
            GuildExp = t.Rewards.GuildExp(d, ctx), GuildFund = t.Rewards.GuildFund(d, ctx), FamePoint = t.Rewards.FamePoint(d),
            GuildCoin = t.Rewards.GuildCoin(d, ctx),
        };
        foreach (var x in d.Complete.ChoiceRewards) r.ChoiceRewards.Add((x.ItemId, x.Rank, x.Count));
        return r;
    }

    bool HasTalk(NpcRow r) => r.Kind != 0 || Scripts(packs).Find(r.Id) != null;

    /// <summary>WorldService::FindNearestNpc for the talk action: friendly 2 first, then 1.</summary>
    public FieldNpcs.Placed FindTalkable()
    {
        if (player == null) return null;
        float range = constants.Float("TalkableDistance", 300);
        bool frontOnly = constants.Float("TalkableFrontOnly", 1) != 0;
        var me = player.GlobalPosition;
        var face = new Vector3(Mathf.Sin(player.Visual.Rotation.Y), 0, Mathf.Cos(player.Visual.Rotation.Y));
        foreach (int friendly in new[] { 2, 1 })
        {
            FieldNpcs.Placed best = null; float bestD = float.MaxValue;
            foreach (var n in npcs)
            {
                if (n.Row.Friendly != friendly || !HasTalk(n.Row) || !n.Root.Visible) continue;
                var to = n.Position - me;
                float d = to.Length();
                if (d > range + n.Row.CapsuleRadius) continue;
                if (frontOnly && new Vector3(to.X, 0, to.Z).Dot(face) < 0) continue;
                if (d < bestD) { bestD = d; best = n; }
            }
            if (best != null) return best;
        }
        return null;
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
        talking = npc;
        ui ??= new UiAssets(packs);
        tables ??= new GameTables(packs, "en");
        if (!UiCode.Load(Settings.DataDir)) GD.PushWarning("UI code is not built for this data: the talk window runs interpreted");
        var size = (Vector2)GetWindow().Size;
        side = new TalkCinematicController
        {
            Talking = true,
            TalkNpcId = npc.Row.Id,
            Content = ContentOf(script.Contents[0]),
            ButtonSet = ButtonsOf(0),
            Flags = flags,
            QuestOf = q => Quests(packs).Quest(q) is { } d ? new(Quests(packs).QuestName(q), d.Level, IconSuffix(d)) : null,
            QuestStateString = q => StateString(q, npc.Row.Id),
            IsQuestActive = q => QuestState(q) == 1,
            IsQuestRewarded = q => QuestState(q) == 2,
            QuestRewardOf = RewardOf,
            NpcName = id => Scripts(packs).NpcDisplayName(id),
            NpcIllust = id => id == npc.Row.Id ? npc.Row.Illust ?? "" : "",
            FileExists = ImageExists,
            ClientWidth = (int)size.X, ClientHeight = (int)size.Y,
            MovieScale = UiScale.ViewportFactor(size.X),
        };
        side.QuestIds.AddRange((flags & 4) != 0 ? talkQuests : Enumerable.Empty<int>());   // Quest mode 1 before the talk
        side.SendNext += OnNext;
        // Maple2's QuestHandler Accept / Complete on the sandbox records
        side.SendQuestAccept += (q, _) => GD.Print($"talk: quest {q} accept: {(Records.Accept(q) ? "started" : "refused")}");
        side.SendQuestComplete += (q, _, _) => GD.Print($"talk: quest {q} complete: {(Records.Complete(q) ? "rewarded" : "refused")}");
        side.SendEnd += () => { };                      // NpcTalk mode 0: nothing to tell offline
        side.TalkReset += () => Sound(talking?.Row.SoundEnd, talking?.Position ?? Vector3.Zero);
        side.PlayVoice += v => Sound(v, talking?.Position ?? Vector3.Zero);
        side.CloseComplete += () => CallDeferred(nameof(Teardown));
        layer = new CanvasLayer { Layer = 20, Name = "TalkCinematic" };
        AddChild(layer);
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
        return true;
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
        layer = null; window = null; side = null; talking = null; script = null;
        player.ScriptDriven = wasScripted;
        player.Input2 = Vector2.Zero;
    }

    /// <summary>The action key: Next on the open window, or start a talk with the npc in front.</summary>
    public bool ActionKey()
    {
        if (window != null)
        {
            side.Key(TalkCinematicController.TalkKey.Next);
            return true;
        }
        return Start(FindTalkable());
    }

    public override void _Input(InputEvent e)
    {
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
        side.Key(key.Value);
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (window != null) player.Input2 = Vector2.Zero;
    }
}
