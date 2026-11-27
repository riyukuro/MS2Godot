using Godot;
using Ms2.Formats.World;
using Ms2.Quest;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2Godot.Ui;

namespace Ms2Godot.World;

/// <summary>
/// The overhead emoticons of the field's npcs (UIEmoticon): the uiemoticon movie runs once, as the client's HUD entity
/// does, and <see cref="EmoticonController"/> keeps one emoticon_entity_body clip per npc in it.
/// - Spawn: CNpc's spawn makes the entity (UIEmoticon::SpawnNpc, the friendly npcs only); the actor leaving the field
///   releases it (UIHudEntityManager::Release), and its clip waits in the pool for the next one.
/// - The actor: its add (GameObject::vfunc_12) comes before the spawn, so the spawn's vfunc_83 passes its ProgressiveLoading
///   test. The entity's OnAdded adds the model (ActorAddedMessage queued) before its abstract OnCreate ends and the
///   gameobject.flat's PreloadAssets queues the EntityPreloadRequest (EntityManager PreloadEntityAssets); the spawn
///   (CNpc::vfunc_10) runs only on the preload response's EnterWorld (OnAssetsLoaded, SendEntityEnterWorldNotification).
///   Here the npc's body being placed is the add (the live field's stand-in body for an npc with no model counts), and the
///   spawn follows it on the same frame.
/// - Every frame (UIHudEntity::vfunc_15): CNpc::vfunc_53's quest tick (once a second for an npc with quests), then each
///   clip at the npc's "Eff_UI" dummy (Entity::vfunc_10; the npc's position without one) projected to the stage, with the
///   camera-pitch shift (FUN_1412762d0) and the npc offset (OffsetNpcEmoticon).
/// - Distance: from the local player, as the name tags measure it (<see cref="NpcPresence"/>), against
///   EmoticonVisibleDistance.
/// - The talk scene (<see cref="MovieLayers.InTalk"/>) shows the status kind only, and the Emoticon layer
///   (CommonDepth 4) hides them all with the field's layers. The talk's name tag (NpcTalk mode 3 sub 1) makes the talk
///   npc's emoticon that kind with its text; a page shown drops it unless the last mode 3 packet was that name tag
///   (<see cref="NpcTalk"/>).
/// - The quest kind comes from the character's quest records through <see cref="NpcTalk.QuestMark"/> (offline the
///   sandbox's, live the session's QuestBook).
/// - An officer npc kind (fameDef.xml's officerNpcKind) shows its alliance's symbol movie (<see cref="FameTable"/>).
/// - The npcs' chat balloons (UIChatBalloon, <see cref="ChatBalloonController"/>) share the layer: one balloon actor per
///   npc, its entity made on first use by CNpc::vfunc_85 (<see cref="ChatBalloonController.NpcBalloon"/>), which the
///   emoticon's hide test asks for each frame (<see cref="EmoticonController.Actor.Balloon"/>). The balloon sits in
///   CommonDepth ChatBalloon (7).
/// - The players' chat balloons (a live field): the own PC and the remote layer's players get a balloon actor each,
///   kept by object id as the client's PC map m_118 is; a chat line shows over its speaker through
///   <see cref="ChatSpeakerBalloon"/> (UIMessengerBrowser::ShowChatBalloon, wired from the HUD's chat). A PC's balloon is
///   PC::vfunc_85 (<see cref="ChatBalloonController.PcBalloon"/>), its offset OffsetPcChatballoon (the PC constructor's
///   m_f168, pc.cpp:661), its picture the PC's profile image (FUN_141028be0(id, picture, 1)).
/// - The idle monologues (<see cref="NpcMonologue"/>): each npc's frame runs CNpc::UpdateMonologue, which shows a
///   &lt;monologue&gt; of the npc's scripts in its balloon. A mob is a CNpc as well (FieldAddNpc), so the live field's mobs
///   run it too (345 mob rows have monologues); the offline field's region spawn previews (Mob) are not client objects and
///   get none.
/// - The special npc lines (NpcTalk mode 10 sub 1, OnTalkPacket uitalkcinematic.cpp:1054): the npc's balloon is kept and the
///   packet script's first line shown in it (the npc by the packet's field object id); the mode's other subs (the special
///   npc's own talk page) are not ported.
/// - A PC's worn chat balloon gem (PCGemEquip) is gated by PC::CanUseItem (<see cref="PcItemUse"/>) as the client gates
///   FUN_1412712e0 / FUN_141270740: the packet record's expiry and binds, the data's PC bang / vip / wedding flags against
///   the owner's state from its add packet.
/// </summary>
public sealed partial class NpcEmoticons : CanvasLayer
{
    sealed class Npc
    {
        public FieldNpcs.Placed Placed;
        public EmoticonController.Actor Actor;
        public ChatBalloonController.Actor Balloon;
        public NpcMonologue.Npc Monologue;
        public bool Spawned;
        /// <summary>The quest mark asked for again every second (made once, as the tick runs every frame).</summary>
        public Func<NpcQuestMarks.Mark> Quest;
    }

    sealed class Pc
    {
        public ChatBalloonController.Actor Balloon;
        public string Name = "";
        public Node3D Root;
        public Func<Vector3> Anchor;
        /// <summary>The PC's worn gems (the chat balloon gem's skin, the buddy badge).</summary>
        public GemEquip Gems;
        /// <summary>The PC's side of the buddy badge (its couple effect system, PC m_spCouple11d8).</summary>
        public CouplePc Couple;
    }

    /// <summary>
    /// The couple effect systems' field (<see cref="ICoupleWorld"/>): the PCs by character id (FUN_141bd1530, the own one too),
    /// the field's limit content 0x400, the kept effects at a point, the BuddyBadge sends and the constants'
    /// CoupleEffectCheckTick and CoupleEffectCheckRadius (clientconstants.h:807-808; the client's defaults 5000 and 500).
    /// </summary>
    sealed class CoupleWorld : ICoupleWorld
    {
        readonly NpcEmoticons owner;
        CharEffectGems gems;

        public CoupleWorld(NpcEmoticons owner)
        {
            this.owner = owner;
            CheckTick = (uint)Math.Max(0, owner.constants.Int("CoupleEffectCheckTick", 5000));
            CheckRadius = owner.constants.Float("CoupleEffectCheckRadius", 500);
        }

        public CouplePc FindPc(long characterId)
        {
            foreach (var x in owner.pcs.Values)
                if (x.Couple != null && x.Couple.CharacterId == characterId) return x.Couple;
            return null;
        }

        public bool CoupleEffectLimited => Ms2Godot.Net.LiveSession.Instance?.FieldHasLimitContent(0x400) ?? false;
        public int SpawnEffect(string effect, System.Numerics.Vector3 at) => Ms2Godot.Net.LiveSession.Instance?.SpawnKeptEffectAt(effect, at) ?? 0;
        public void RemoveEffect(int id) => Ms2Godot.Net.LiveSession.Instance?.StopKeptEffect(id);
        public void Send(Ms2.Net.Wire.IOutPacket packet) => Ms2Godot.Net.LiveSession.Instance?.SendBuddyBadge(packet);
        public uint CheckTick { get; }
        public float CheckRadius { get; }
        public uint Now => unchecked((uint)Time.GetTicksMsec());
        public CharEffectGems Gems => gems ??= CharEffectGems.Load(owner.assets.Packs);
    }

    CoupleWorld coupleWorld;

    readonly UiAssets assets;
    readonly ClientConstants constants;
    readonly NpcTalk talk;
    GameTables tables;
    readonly bool live;
    readonly Dictionary<FieldNpcs.Placed, Npc> npcs = new();
    readonly Dictionary<EmoticonController.Actor, Npc> byActor = new();
    readonly Dictionary<ChatBalloonController.Actor, Npc> byBalloon = new();
    readonly Dictionary<NpcMonologue.Npc, Npc> byMonologue = new();
    readonly NpcQuestMarks marks;
    readonly SortedDictionary<int, Pc> pcs = new();             // by object id, as WorldService m_118
    readonly Dictionary<ChatBalloonController.Actor, Pc> byPcBalloon = new();
    ChatSpeakerBalloon speakers;
    EmoticonController controller;
    MovieDialog movie;
    ChatBalloonController balloons;
    MovieDialog balloonMovie;
    NpcMonologue monologue;
    Camera3D camera;
    Field field;
    Viewport sizeSource;
    int nextId = 1;
    (int Id, FieldData Data) fieldData;
    double syncIn;
    float u = 1;

    /// <summary>The local player: the distance is measured from it.</summary>
    public Node3D Viewer;
    /// <summary>The controller (null when the movie did not load).</summary>
    public EmoticonController Controller => controller;
    /// <summary>The chat balloons' controller (null when the movie did not load).</summary>
    public ChatBalloonController Balloons => balloons;
    /// <summary>The chat lines' balloons over the players (null when the balloon movie did not load).</summary>
    public ChatSpeakerBalloon Speakers => speakers;
    /// <summary>The live field's layer, while it is in the tree (the HUD's chat shows its lines through it).</summary>
    public static NpcEmoticons Live { get; private set; }
    /// <summary>The per-frame cost (quest ticks and placing, not the movie's own frame) in ms, summed, and the frames counted.</summary>
    public double FrameMs;
    public int Frames;

    /// <summary>
    /// <paramref name="spawned"/>: the sandbox's npcs; null for a live field, whose npcs come and go with the session's
    /// (FieldAddNpc / FieldRemoveNpc).
    /// </summary>
    public NpcEmoticons(UiAssets assets, ClientConstants constants, NpcTalk talk, IEnumerable<FieldNpcs.Placed> spawned)
    {
        this.assets = assets; this.constants = constants; this.talk = talk;
        Name = "NpcEmoticons";
        Layer = 5;          // the npc name tags' layer; added after them, so drawn above (CommonDepth Emoticon 4 > NameTag 2)
        marks = NpcTalk.QuestMarks(assets.Packs);
        live = spawned == null;
        Build();
        if (spawned != null) foreach (var n in spawned) Add(n);
    }
    public NpcEmoticons() { }

    /// <summary>
    /// COptimizer::LoadOptimization (@0x14161c490) runs once: JoinPath(GetOptionFolder(), "Optimization.xml")
    /// (optimizer.cpp:381), the folder GameOption.xml is in (GetOptionFolder "../../Config/" @0x1412755b0,
    /// unit_141274d10_uihelpers.cpp:401-404; GetOptionFilePath gameoption.cpp:1700), not the client's root.
    /// </summary>
    /// <summary>The PC model node's name the balloon anchor reads every frame.</summary>
    static readonly NodePath ModelPath = "Model";

    static readonly Lazy<bool> ProgressiveLoading = new(() =>
        Ms2.Formats.Options.Optimization.ProgressiveLoading(System.IO.Path.GetDirectoryName(GameOptions.FilePath)));

    void Build()
    {
        var tables = GameTables.Shared(assets.Packs, GameOptions.StringLanguage);
        this.tables = tables;
        var quests = NpcTalk.ClientQuests(assets.Packs);
        controller = new EmoticonController
        {
            VisibleDistance = constants.Float("EmoticonVisibleDistance", 2500),
            NpcOffset = constants.Float("OffsetNpcEmoticon", -30),
            PcOffset = constants.Float("OffsetPcChatballoon", -30),
            QuestOf = quests.Quest,
            // GetNpcMovieName (uiemoticon.cpp:125-126): IsOfficerNpc, then GetSymbolByOfficerNpc
            OfficerSymbol = k => tables.Fame.IsOfficerNpc(k) ? tables.Fame.GetSymbolByOfficerNpc(k) : null,
            ProgressiveLoading = ProgressiveLoading.Value,
            SceneHidesEmoticons = () => MovieLayerHost.Layers.InTalk,     // the cut scene sub scenes are not ported
        };
        if (HudScene.GateTickMs != null) controller.TickMs = HudScene.GateTickMs;
        try
        {
            movie = new MovieDialog(assets, controller, new UiModel(tables), new Vector2(1280, 720));
            AddChild(movie);
        }
        catch (Exception e) { GD.PushWarning("emoticon movie: " + e.Message); controller = null; movie = null; }
        if (controller == null) return;
        var community = GameOptions.Current.CommunityOption;
        balloons = new ChatBalloonController
        {
            Duration = (int)constants.Float("ChatBalloonDuration", 5000),
            VisibleDistance = constants.Float("ChatBalloonDistance", 2000),
            DefaultImage = tables.CommonResource("DEFAULT_PROFILE_IMAGE_PATH"),
            DisplayMyBalloon = () => community.DisplayMyChatBalloon,
            DisplayOtherBalloon = () => community.DisplayOtherChatBalloon,
            ProgressiveLoading = ProgressiveLoading.Value,
            SceneShowsBalloons = () => !MovieLayerHost.Layers.InTalk,     // the cut scene sub scenes are not ported
        };
        try
        {
            balloonMovie = new MovieDialog(assets, balloons, new UiModel(tables), new Vector2(1280, 720));
            AddChild(balloonMovie);
        }
        catch (Exception e) { GD.PushWarning("chat balloon movie: " + e.Message); balloons = null; balloonMovie = null; }
        if (balloons == null) return;
        if (HudScene.GateTickMs != null) balloons.TickMs = HudScene.GateTickMs;
        speakers = new ChatSpeakerBalloon(balloons)
        {
            Distance = constants.Float("ChatBalloonDistance", 2000),
            LocalPlayer = () => { SyncPcs(); return pcs.Values.FirstOrDefault(p => p.Balloon.IsMyPc)?.Balloon; },
            FindPc = name => pcs.Values.FirstOrDefault(p => p.Name == name)?.Balloon,
            Talking = () => talk?.TalkingNpc != null,
            Status = () => GameOptions.Current.StatusOption,
            Common = tables.Common,
            ReplaceValueWords = t => tables.ReservedWords.ReplaceValueWords(t, Ms2Godot.Net.LiveSession.Instance?.Me.User?.Name),
        };
        if (talk == null) return;
        monologue = new NpcMonologue(balloons, talk.ScriptTable)
        {
            // MS2Ticks: the tables' seconds in ms (ParseTick)
            IntervalMs = (uint)(constants.Float("MonologueInterval", 0) * 1000f),
            RandomMs = (uint)(constants.Float("MonologueRandom", 0) * 1000f),
            ShowTimeMs = (uint)(constants.Float("MonologueShowTime", 0) * 1000f),
            InSubScene = () => MovieLayerHost.Layers.InTalk,     // the cut scene sub scenes are not ported
            QuestState = m => byMonologue.TryGetValue(m, out var x) ? x.Actor.Quest.State : 4,     // m_questState, kept by RefreshQuestState
            TalkingObjectId = () => talk.TalkingNpc is { } t && npcs.TryGetValue(t, out var x) ? x.Actor.ObjectId : 0,
            Resolve = talk.ResolveText,
            PlayVoice = (m, v) =>
            {
                if (byMonologue.TryGetValue(m, out var x) && IsInstanceValid(x.Placed.Root)) NpcTalk.PlayVoiceAt(v, x.Placed.Root.GlobalPosition);
            },
        };
        // the perf gate (MS2_PERF_GATE, run with --fixed-fps): a fixed seed, so the balloons fall in the same frames every
        // run instead of in whichever measured window the tick-count seed puts them
        if (System.Environment.GetEnvironmentVariable("MS2_PERF_GATE") is { Length: > 0 }) monologue.Rand = new QuestRandom(1).Next;
    }

    void Add(FieldNpcs.Placed n)
    {
        if (controller == null || n.Mob || n.Row == null || npcs.ContainsKey(n)) return;
        var a = new EmoticonController.Actor
        {
            ObjectId = n.ObjectId != 0 ? n.ObjectId : nextId++,
            IsNpc = true,
            NpcKind = n.Row.Kind,
            NpcId = n.Row.Id,
            Friendly = n.Row.Friendly,
            Icon = n.Row.Icon ?? "",
            HasQuests = marks.HasQuests(n.Row.Id),
        };
        var x = new Npc { Placed = n, Actor = a };
        x.Quest = () => QuestMark(a);
        if (balloons != null)
        {
            var b = new ChatBalloonController.Actor
            {
                ObjectId = a.ObjectId,
                IsNpc = true,
                Portrait = n.Row.Portrait ?? "",
                // the CNpc constructor's m_f168 (npcactorandvibrate.cpp:492)
                Offset = constants.Float("OffsetNpcChatballoon", 210),
            };
            x.Balloon = b;
            byBalloon[b] = x;
            x.Monologue = new NpcMonologue.Npc { ObjectId = a.ObjectId, NpcId = n.Row.Id, Balloon = b };
            byMonologue[x.Monologue] = x;
            a.Balloon = () => balloons.NpcBalloon(b);
        }
        npcs[n] = byActor[a] = x;
    }

    void OnNameTag(FieldNpcs.Placed n, string text)
    {
        if (npcs.TryGetValue(n, out var x)) controller?.SetNpcNameTag(x.Actor, text);
    }

    /// <summary>
    /// NpcTalk mode 10 sub 1 (OnTalkPacket, uitalkcinematic.cpp:1054-1062): the npc (by the packet's field object id,
    /// WorldService::FindNpcById) has its balloon kept (FUN_1418fb6d0, so the monologue leaves it alone) and the
    /// script's first line shown in it (PCBase::ShowChatBalloon, pcbase.cpp:1664: the value words, SetText, vfunc_2).
    /// </summary>
    void OnSpecialNpcBalloon(FieldNpcs.Placed n, string text)
    {
        if (balloons == null || !npcs.TryGetValue(n, out var x)) return;
        var b = balloons.NpcBalloon(x.Balloon);
        if (b == null) return;
        ChatBalloonController.Keep(b);
        ChatBalloonController.SetText(b, tables?.ReservedWords.ReplaceValueWords(text ?? "",
            Ms2Godot.Net.LiveSession.Instance?.Me.User?.Name) ?? "");
        balloons.Start(b);
    }

    /// <summary>The balloon entity of the field object with this id: the npc whose FieldAddNpc object id it is, else the PC
    /// (WorldService's PC list by object id). Null when the object is not on the field or its balloon is not made yet.</summary>
    ChatBalloonController.Entity ObjectBalloon(int objectId)
    {
        if (balloons == null) return null;
        foreach (var kv in npcs)
            if (kv.Key.ObjectId == objectId) return balloons.NpcBalloon(kv.Value.Balloon);
        SyncPcs();
        return pcs.TryGetValue(objectId, out var pc) ? balloons.PcBalloon(pc.Balloon) : null;
    }

    /// <summary>UICutSceneCinematic::UpdateBalloons (uicutscenecinematic.cpp, FUN_141908750): the actor of a balloon group is
    /// on the field with a balloon to show it in.</summary>
    public bool ObjectBalloonReady(int objectId) => ObjectBalloon(objectId) != null;

    /// <summary>A cinematic balloon line: the object's balloon is kept (so the monologue leaves it alone), set to the line and
    /// started, as <see cref="OnSpecialNpcBalloon"/>. STILL-OPEN: Start's own fixed duration, not the line's duration.</summary>
    public void ShowObjectBalloon(int objectId, string text)
    {
        if (ObjectBalloon(objectId) is not { } b) return;
        ChatBalloonController.Keep(b);
        ChatBalloonController.SetText(b, text ?? "");
        balloons.Start(b);
    }

    /// <summary>The cinematic balloon group's hide (m_nHide): the object's balloon stops.</summary>
    public void HideObjectBalloon(int objectId)
    {
        if (ObjectBalloon(objectId) is { } b) balloons.Stop(b);
    }

    void OnTalkPage(FieldNpcs.Placed n, int subMode)
    {
        if (npcs.TryGetValue(n, out var x)) controller?.TalkPageShown(x.Actor, subMode);
    }

    NpcQuestMarks.Mark QuestMark(EmoticonController.Actor a) => talk?.QuestMark(a.NpcId) ?? NpcQuestMarks.Mark.None;

    /// <summary>A live field: the session's npcs now, the new ones spawned and the gone ones released.</summary>
    void Sync()
    {
        if (Ms2Godot.Net.LiveSession.Instance is not { } s) return;
        var now = s.TalkNpcs.ToHashSet();
        foreach (var gone in npcs.Keys.Where(k => !now.Contains(k)).ToList())
        {
            controller.Release(npcs[gone].Actor.ObjectId);
            byActor.Remove(npcs[gone].Actor);
            if (npcs[gone].Balloon is { } b) { balloons?.RemoveNpc(b); byBalloon.Remove(b); }
            if (npcs[gone].Monologue is { } m) byMonologue.Remove(m);
            npcs.Remove(gone);
        }
        foreach (var n in now) Add(n);
        SyncPcs();
    }

    /// <summary>
    /// The live field's PCs: the own one (the session's FieldAddUser object, the walkable player) and the remote layer's;
    /// a new one gets its balloon actor, a gone one's balloon is released (PCBase::vfunc_16), and each picture path is
    /// asked again (FUN_141028be0(id, picture, 1), as the balloon's open does).
    /// </summary>
    void SyncPcs()
    {
        if (!live || balloons == null || Ms2Godot.Net.LiveSession.Instance is not { } s) return;
        var now = new Dictionary<int, (long Char, string Name, string Picture, Node3D Root, Func<Vector3> Anchor, bool Me,
            IReadOnlyList<Ms2.Net.Packets.EquipItem> Gems, long PremiumTime, byte PcBangGrade, short WeddingState)>();
        if (s.SelfObjectId >= 0 && s.Me.User is { } me && Viewer is Player p && IsInstanceValid(p))
        {
            var dummies = EffectDummyTable.Load(assets.Packs).Pc(p.Gender);
            now[s.SelfObjectId] = (me.CharacterId, me.Name ?? "", me.Picture ?? "", p,
                () => DummyAnchor.Point(dummies, "Eff_UI", p.Visual?.GetNodeOrNull<Node3D>(ModelPath), p.RootName) ?? p.GlobalPosition, true, s.Me.Gems,
                s.Me.PremiumTime, me.PcBangGrade, s.Me.WeddingState);
        }
        foreach (var r in s.FieldPcs)
            if (IsInstanceValid(r.Root)) now[r.ObjectId] = (r.CharacterId, r.Name, r.Picture, r.Root, r.Anchor, false, r.Gems,
                r.PremiumTime, r.PcBangGrade, r.WeddingState);
        foreach (var gone in pcs.Keys.Where(k => !now.ContainsKey(k)).ToList())
        {
            // FieldRemoveUser: MyPC's PC::FUN_141663500 with the leaving object, then the PC's destructor (its couple
            // system's SetEffectMode(1)); the kept badge effects go with the PC's model
            MyPc()?.Couple?.UserRemoved(gone);
            pcs[gone].Gems.CharEffect?.Remove();
            balloons.RemovePc(pcs[gone].Balloon);
            byPcBalloon.Remove(pcs[gone].Balloon);
            pcs.Remove(gone);
        }
        foreach (var (id, v) in now)
        {
            if (!pcs.TryGetValue(id, out var x) || x.Root != v.Root)
            {
                if (x != null) { x.Gems.CharEffect?.Remove(); balloons.RemovePc(x.Balloon); byPcBalloon.Remove(x.Balloon); }
                var b = new ChatBalloonController.Actor
                {
                    ObjectId = id,
                    IsPc = true,
                    IsMyPc = v.Me,
                    ActorAdded = true,
                    Offset = constants.Float("OffsetPcChatballoon", 210),
                };
                pcs[id] = x = new Pc { Balloon = b, Gems = new GemEquip(balloons, b) { GemSystem = GemSystem } };
                // the buddy badge (CGemCharEffect): the wearer's effect kept on the PC's root, the couple system on the PC
                var root = v.Root;
                x.Couple = new CouplePc
                {
                    CharacterId = v.Char, ObjectId = id, Name = v.Name, IsMine = v.Me,
                    Gender = v.Me ? (Viewer as Player)?.Gender ?? 0 : s.GenderOf(id),
                    Position = () => IsInstanceValid(root) ? ClientSpace(root.GlobalPosition) : System.Numerics.Vector3.Zero,
                };
                coupleWorld ??= new CoupleWorld(this);
                // CGemCharEffect::vfunc_6 sets the query's m_nObject to 0 (itemgem.cpp:803): the system kind, not the PC's
                x.Gems.CharEffect = new CharEffectGem(coupleWorld, x.Couple,
                    e => s.SpawnKeptEffect(e, root, Ms2.Audio.SoundMusicEffectState.CasterSystem), s.StopKeptEffect);
                byPcBalloon[b] = x;
                // PC::CanUseItem of this player (pc.cpp:4683, PcItemUse) on each gem's packet record
                var use = new PcItemUse.Pc
                {
                    PcBangGrade = v.PcBangGrade,
                    PremiumTime = v.PremiumTime,
                    WeddingState = v.WeddingState,
                    Manager = v.Me ? s.Wedding : null,   // MyPC::vfunc_146: the manager's state
                    CharacterId = v.Char,
                    ServerTime = () => s.GameSession?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                };
                x.Gems.CanUse = g => g.Record is { } rec && PcItemUse.CanUseItem(rec, tables?.Items.Get(rec.Id), use);
                // PCGemEquip::ReadFromStream of the PC's packet, then FUN_141270610 at its model's load (PC::vfunc_12)
                x.Gems.Set((v.Gems ?? Array.Empty<Ms2.Net.Packets.EquipItem>()).Select(g => ((int)g.GemType, new GemEquip.Gem(g.Id, g.GemId) { Record = g })));  // perf-ok: once per PC balloon made, not per frame
                x.Gems.ApplyAll();
                // MyPC::vfunc_12 (mypc.cpp:1013-1024): the own model's load loads its type 2 gem's damage font
                if (v.Me && x.Gems.WornGemId(2) is var gem and not 0) DamageFontAsked?.Invoke(gem);
                // FieldAddUser: MyPC's PC::FUN_1416634a0 with the new PC's character id (the own one's too)
                MyPc()?.Couple?.UserAdded(v.Char);
            }
            x.Name = v.Name; x.Root = v.Root; x.Anchor = v.Anchor; x.Couple.Name = v.Name;
            x.Balloon.ProfileImage = Ms2Godot.Net.ProfileImageService.Images.Path(v.Char, v.Picture, true);
        }
    }

    /// <summary>The own PC of the field, null when not in yet.</summary>
    Pc MyPc()
    {
        foreach (var x in pcs.Values) if (x.Balloon.IsMyPc) return x;
        return null;
    }

    /// <summary>A godot position in the client's z-up space (x, -z, y).</summary>
    static System.Numerics.Vector3 ClientSpace(Vector3 g) => new(g.X, -g.Z, g.Y);

    /// <summary>
    /// OnBuddyBadge (networkservice.cpp:1187-1202) for the field's PC of the character id (none: nothing): kinds 0 and 2 its
    /// couple effect shown (PC::FUN_141663620), 1 hidden (PC::FUN_141663680), 3 the partner name given to its worn badge of
    /// the item uid (CItem::SetCoupleName).
    /// </summary>
    void OnBuddyBadge(Ms2.Net.Packets.BuddyBadgeMessage m)
    {
        SyncPcs();
        Pc x = null;
        foreach (var p in pcs.Values) if (p.Couple?.CharacterId == m.CharacterId) { x = p; break; }
        if (x == null) return;
        switch (m.Kind)
        {
            case Ms2.Net.Packets.BuddyBadgeMessage.Show:
            case Ms2.Net.Packets.BuddyBadgeMessage.ShowToNewcomer: x.Couple.Show(); break;
            case Ms2.Net.Packets.BuddyBadgeMessage.Hide: x.Couple.Hide(); break;
            case Ms2.Net.Packets.BuddyBadgeMessage.PartnerName: x.Gems.SetCoupleName(m.ItemUid, m.Name); break;
        }
    }

    /// <summary>The live checks' line on the buddy badges of the field's PCs.</summary>
    public string CoupleLine()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var x in pcs.Values)
        {
            if (x.Couple == null) continue;
            var c = x.Couple.Couple;
            sb.Append($"[{x.Name} badge={(x.Gems.Item(GemEquip.CharEffectType)?.GemId ?? 0)} wearerFx={x.Gems.CharEffect?.EffectCount ?? 0} pending={x.Couple.Pending}");
            if (c != null) sb.Append($" partner={c.PartnerId} linked={c.Linked} active={c.Active} inRange={c.InRange} fx={c.EffectId} name={c.Name}");
            sb.Append("] ");
        }
        return sb.ToString();
    }

    /// <summary>The current field's gemSystem (CFieldDataCli m_bGemSystem); true without field data.</summary>
    bool GemSystem()
    {
        int id = Ms2Godot.Net.LiveSession.Instance?.FieldId ?? 0;
        if (fieldData.Data == null || fieldData.Id != id)
        {
            FieldData d = null;
            try { d = id != 0 ? FieldData.FindById(assets.Packs, id) : null; } catch { }
            fieldData = (id, d);
        }
        return fieldData.Data?.GemSystem ?? true;
    }

    /// <summary>
    /// OnBadgeEquip (scenestategame.cpp:11006) for a PC of the field: mode 0 EquipItem (EquipByGemType: stored, then applied),
    /// mode 1 RemoveByGemType.
    /// </summary>
    void OnBadgeEquip(Ms2.Net.Packets.BadgeEquipMessage b)
    {
        SyncPcs();
        if (!pcs.TryGetValue(b.ObjectId, out var x)) return;
        if (b.Mode == 0 && b.Item is { GemType: not 0 } it)
        {
            x.Gems.Equip(it.GemType, new GemEquip.Gem(it.Id, it.GemId) { Record = it });
            // scenestategame.cpp:11031-11039: a type 2 gem (vfunc_1() == 2) of a PC in battle or of the own one loads its
            // damage font (GFxMovieDamage::LoadDamageFont), with no CanUse test
            if (it.GemType == 2 && (x.Balloon.IsMyPc || (Ms2Godot.Net.LiveSession.Instance?.InBattle(b.ObjectId) ?? false)))
                DamageFontAsked?.Invoke(it.GemId);
        }
        else if (b.Mode == 1) x.Gems.Remove(b.GemType);
    }

    /// <summary>
    /// GFxMovieDamage::LoadDamageFont's callers outside the damage text, with the gem font id: the own PC's model load
    /// (MyPC::vfunc_12, mypc.cpp:1013-1024), a type 2 gem put on (OnBadgeEquip) and a PC going into battle (PC::vfunc_66,
    /// pc.cpp:1236-1259), the last two for any PC of the field.
    /// </summary>
    public static event Action<int> DamageFontAsked;

    /// <summary>FUN_141270740(type)'s gem id of a PC of the field (<see cref="GemEquip.WornGemId"/>); 0 for no gem or no such
    /// PC (an npc caster).</summary>
    public int WornGemId(int objectId, int type) => pcs.TryGetValue(objectId, out var x) ? x.Gems.WornGemId(type) : 0;

    /// <summary>PC::vfunc_66(n) (pc.cpp:1236-1259): the emotion 1 (<see cref="Ms2.Ui.Hud.DamageFonts.StanceLoadsFont"/>) with a
    /// usable type 2 gem loads its font.</summary>
    void OnBattleStance(int objectId, int emotion)
    {
        if (Ms2.Ui.Hud.DamageFonts.StanceLoadsFont(emotion) && WornGemId(objectId, 2) is var gem and not 0) DamageFontAsked?.Invoke(gem);
    }

    public override void _EnterTree()
    {
        if (talk != null) { talk.NpcNameTag += OnNameTag; talk.TalkPageShown += OnTalkPage; talk.SpecialNpcBalloon += OnSpecialNpcBalloon; }
        if (live && Ms2Godot.Net.LiveSession.Instance is { } ls) { ls.BadgeEquipped += OnBadgeEquip; ls.BattleStanceChanged += OnBattleStance; ls.BuddyBadgeReceived += OnBuddyBadge; }
        if (live) Live = this;
        sizeSource = GetViewport();
        sizeSource.SizeChanged += Relayout;
        Relayout();
    }

    public override void _ExitTree()
    {
        if (talk != null) { talk.NpcNameTag -= OnNameTag; talk.TalkPageShown -= OnTalkPage; talk.SpecialNpcBalloon -= OnSpecialNpcBalloon; }
        if (live && Ms2Godot.Net.LiveSession.Instance is { } ls) { ls.BadgeEquipped -= OnBadgeEquip; ls.BattleStanceChanged -= OnBattleStance; ls.BuddyBadgeReceived -= OnBuddyBadge; }
        // the field's PCs go with the field: each one's couple system released (SetEffectMode(1))
        foreach (var x in pcs.Values) x.Gems.CharEffect?.Remove();
        if (sizeSource != null) { sizeSource.SizeChanged -= Relayout; sizeSource = null; }
        if (Live == this) Live = null;
    }

    void Relayout()
    {
        var size = GetViewport().GetVisibleRect().Size;
        u = UiScale.For(size);
        movie?.Relayout(size);
        balloonMovie?.Relayout(size);
    }

    public override void _Process(double delta)
    {
        if (controller == null) return;
        camera ??= GetViewport().GetCamera3D();
        if (camera == null) return;
        field ??= GetTree().GetFirstNodeInGroup(Field.Group) as Field;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (live && (syncIn -= delta) <= 0) { syncIn = 0.25; Sync(); }
        var eye = Viewer != null && IsInstanceValid(Viewer) ? Viewer.GlobalPosition : camera.GlobalPosition;
        // the perf gate: the monologue's time starts with the gate's clock (0 until gate frame 0), so its balloons open and
        // close in the same gate frames whatever the load took, not shifted into another measured window
        float monologueDelta = HudScene.GateTickMs is { } gateTickMs && gateTickMs() == 0 ? 0f : (float)delta;
        foreach (var n in npcs.Values)
        {
            if (!IsInstanceValid(n.Placed.Root)) continue;
            n.Actor.Distance = eye.DistanceTo(n.Placed.Root.GlobalPosition);
            if (n.Balloon != null) n.Balloon.Distance = n.Actor.Distance;
            var a = n.Actor;
            if (!n.Spawned)
            {
                // the actor's add, then the spawn (EnterWorld waits for the preload response queued after ActorAdded)
                if (n.Placed.Body == null || !IsInstanceValid(n.Placed.Body)) continue;
                EmoticonController.OnActorAdded(a);
                if (n.Balloon != null) n.Balloon.ActorAdded = true;
                controller.SpawnNpc(a, n.Quest);
                n.Spawned = true;
            }
            controller.NpcTick(a, n.Quest);
            if (monologue != null && n.Monologue != null) monologue.Update(n.Monologue, monologueDelta);    // CNpc::vfunc_53
        }
        // GameObject m_unk_bc of the PCs: from the focus (the local player)
        foreach (var x in pcs.Values) if (IsInstanceValid(x.Root)) x.Balloon.Distance = eye.DistanceTo(x.Root.GlobalPosition);
        // PC::vfunc (pc.cpp:4476): each PC's couple effect system's Update (only the own linked one acts)
        foreach (var x in pcs.Values) x.Couple?.Update();
        bool layer = MovieLayerHost.Layers.IsVisible(CommonDepth.Emoticon);
        controller.Process(Project);
        bool balloonLayer = MovieLayerHost.Layers.IsVisible(CommonDepth.ChatBalloon);
        balloons?.Process(ProjectBalloon);
        FrameMs += System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        Frames++;

        (float, float, float)? Project(EmoticonController.Actor a)
        {
            if (!layer) return null;
            if (!byActor.TryGetValue(a, out var n) || !IsInstanceValid(n.Placed.Root)) return null;
            var anchor = NpcPresence.Dummy(n.Placed, "Eff_UI") ?? n.Placed.Root.GlobalPosition;
            if (camera.IsPositionBehind(anchor)) return null;
            var p = camera.UnprojectPosition(anchor);
            float shift = field == null ? 0 : PlayerNameTag.PitchShift(field, anchor);
            // SetPositionFloat: the screen point times GetMovieScale, in whole twips
            return ((float)(Math.Floor(p.X / u * 20.0) / 20.0), (float)(Math.Floor(p.Y / u * 20.0) / 20.0), shift);
        }

        // UIHudEntity's projection of the actor's vfunc_10 anchor ("Eff_UI"); the balloon's vfunc_6 has no pitch shift
        (float, float)? ProjectBalloon(ChatBalloonController.Actor b)
        {
            if (!balloonLayer) return null;
            Vector3 anchor;
            if (byPcBalloon.TryGetValue(b, out var pc))
            {
                if (!IsInstanceValid(pc.Root)) return null;
                anchor = pc.Anchor?.Invoke() ?? pc.Root.GlobalPosition;
            }
            else if (!byBalloon.TryGetValue(b, out var n) || !IsInstanceValid(n.Placed.Root)) return null;
            else anchor = NpcPresence.Dummy(n.Placed, "Eff_UI") ?? n.Placed.Root.GlobalPosition;
            if (camera.IsPositionBehind(anchor)) return null;
            var p = camera.UnprojectPosition(anchor);
            return ((float)(Math.Floor(p.X / u * 20.0) / 20.0), (float)(Math.Floor(p.Y / u * 20.0) / 20.0));
        }
    }
}
