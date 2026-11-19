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
/// - The idle monologues (<see cref="NpcMonologue"/>): each npc's frame runs CNpc::UpdateMonologue, which shows a
///   &lt;monologue&gt; of the npc's scripts in its balloon. STILL-OPEN: the mobs (not in this layer) run it in the client too.
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
    }

    readonly UiAssets assets;
    readonly ClientConstants constants;
    readonly NpcTalk talk;
    readonly bool live;
    readonly Dictionary<FieldNpcs.Placed, Npc> npcs = new();
    readonly Dictionary<EmoticonController.Actor, Npc> byActor = new();
    readonly Dictionary<ChatBalloonController.Actor, Npc> byBalloon = new();
    readonly Dictionary<NpcMonologue.Npc, Npc> byMonologue = new();
    readonly NpcQuestMarks marks;
    EmoticonController controller;
    MovieDialog movie;
    ChatBalloonController balloons;
    MovieDialog balloonMovie;
    NpcMonologue monologue;
    Camera3D camera;
    Field field;
    Viewport sizeSource;
    int nextId = 1;
    double syncIn;
    float u = 1;

    /// <summary>The local player: the distance is measured from it.</summary>
    public Node3D Viewer;
    /// <summary>The controller (null when the movie did not load).</summary>
    public EmoticonController Controller => controller;
    /// <summary>The chat balloons' controller (null when the movie did not load).</summary>
    public ChatBalloonController Balloons => balloons;
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
    static readonly Lazy<bool> ProgressiveLoading = new(() =>
        Ms2.Formats.Options.Optimization.ProgressiveLoading(System.IO.Path.GetDirectoryName(GameOptions.FilePath)));

    void Build()
    {
        var tables = GameTables.Shared(assets.Packs, GameOptions.StringLanguage);
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
        if (balloons == null || talk == null) return;
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
    }

    public override void _EnterTree()
    {
        if (talk != null) { talk.NpcNameTag += OnNameTag; talk.TalkPageShown += OnTalkPage; }
        sizeSource = GetViewport();
        sizeSource.SizeChanged += Relayout;
        Relayout();
    }

    public override void _ExitTree()
    {
        if (talk != null) { talk.NpcNameTag -= OnNameTag; talk.TalkPageShown -= OnTalkPage; }
        if (sizeSource != null) { sizeSource.SizeChanged -= Relayout; sizeSource = null; }
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
                controller.SpawnNpc(a, () => QuestMark(a));
                n.Spawned = true;
            }
            controller.NpcTick(a, () => QuestMark(a));
            if (monologue != null && n.Monologue != null) monologue.Update(n.Monologue, (float)delta);    // CNpc::vfunc_53
        }
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
            if (!byBalloon.TryGetValue(b, out var n) || !IsInstanceValid(n.Placed.Root)) return null;
            var anchor = NpcPresence.Dummy(n.Placed, "Eff_UI") ?? n.Placed.Root.GlobalPosition;
            if (camera.IsPositionBehind(anchor)) return null;
            var p = camera.UnprojectPosition(anchor);
            return ((float)(Math.Floor(p.X / u * 20.0) / 20.0), (float)(Math.Floor(p.Y / u * 20.0) / 20.0));
        }
    }
}
