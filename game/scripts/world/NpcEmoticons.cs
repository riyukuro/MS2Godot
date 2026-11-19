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
///   (CommonDepth 4) hides them all with the field's layers.
/// - The quest kind comes from the character's quest records through <see cref="NpcTalk.QuestMark"/> (offline the
///   sandbox's, live the session's QuestBook).
/// STILL-OPEN: the officer npcs' alliance symbol (CFameTable GetSymbolByOfficerNpc) is not loaded, so those npcs show
/// their kind's movie or none; the chat balloon test (Actor.BalloonActive) is not fed, as the npcs' balloons are not
/// ported.
/// </summary>
public sealed partial class NpcEmoticons : CanvasLayer
{
    sealed class Npc { public FieldNpcs.Placed Placed; public EmoticonController.Actor Actor; public bool Spawned; }

    readonly UiAssets assets;
    readonly ClientConstants constants;
    readonly NpcTalk talk;
    readonly bool live;
    readonly Dictionary<FieldNpcs.Placed, Npc> npcs = new();
    readonly Dictionary<EmoticonController.Actor, Npc> byActor = new();
    readonly NpcQuestMarks marks;
    EmoticonController controller;
    MovieDialog movie;
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
            QuestOf = quests.Quest,
            ProgressiveLoading = ProgressiveLoading.Value,
            SceneHidesEmoticons = () => MovieLayerHost.Layers.InTalk,
        };
        try
        {
            movie = new MovieDialog(assets, controller, new UiModel(tables), new Vector2(1280, 720));
            AddChild(movie);
        }
        catch (Exception e) { GD.PushWarning("emoticon movie: " + e.Message); controller = null; movie = null; }
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
        npcs[n] = byActor[a] = new Npc { Placed = n, Actor = a };
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
            npcs.Remove(gone);
        }
        foreach (var n in now) Add(n);
    }

    public override void _EnterTree()
    {
        sizeSource = GetViewport();
        sizeSource.SizeChanged += Relayout;
        Relayout();
    }

    public override void _ExitTree()
    {
        if (sizeSource != null) { sizeSource.SizeChanged -= Relayout; sizeSource = null; }
    }

    void Relayout()
    {
        var size = GetViewport().GetVisibleRect().Size;
        u = UiScale.For(size);
        movie?.Relayout(size);
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
            var a = n.Actor;
            if (!n.Spawned)
            {
                // the actor's add, then the spawn (EnterWorld waits for the preload response queued after ActorAdded)
                if (n.Placed.Body == null || !IsInstanceValid(n.Placed.Body)) continue;
                EmoticonController.OnActorAdded(a);
                controller.SpawnNpc(a, () => QuestMark(a));
                n.Spawned = true;
            }
            controller.NpcTick(a, () => QuestMark(a));
        }
        bool layer = MovieLayerHost.Layers.IsVisible(CommonDepth.Emoticon);
        controller.Process(Project);
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
    }
}
