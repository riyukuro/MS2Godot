using Godot;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;
using Ms2Godot.Ui;
using Ms2Godot.World;

namespace Ms2Godot.Net;

/// <summary>
/// The live field's UIBuffBalloon (the uibuffballoon movie, ui id 0x4ea4): the icon of a balloon buff over an actor's head,
/// run by <see cref="BuffBalloonController"/>. Each actor's AdditionalBuffList asks <see cref="Of"/> for its balloon
/// (PC::vfunc_84 @0x1416572b0, CNpc::vfunc_84 @0x1410902f0) when a level with iconBalloonOnOff is added or removed.
/// <list type="bullet">
/// <item>Layer: CUIService::HideWorldUI loads 0x4ea4 with LoadSwf(3) (uiservice.cpp:949), the HP bar's depth.</item>
/// <item>Offset (vfunc_9, PCBase m_f168): OffsetPcChatballoon for a player (pc.cpp:661), OffsetNpcChatballoon for an npc
/// (unit_14108e1e0_npc.cpp:492).</item>
/// <item>Distance (Entity m_54): BuffBallonDistance (constantsblocks.cpp:740), against the actor's distance to the local
/// player (GameObject m_unk_bc).</item>
/// <item>Teardown (PCBase::vfunc_16, pcbase.cpp:893): an actor whose node is gone or replaced releases its balloon to the pool.</item>
/// </list>
/// STILL-OPEN: Entity::vfunc_8's action state 0x3f test and the riding / FUN_141674bc0 tests are not fed (never hidden by
/// them); PC::vfunc_84's ProgressiveLoading test is not fed (a live player's model counts as added, as for the chat balloons).
/// </summary>
public sealed partial class LiveBuffBalloons : CanvasLayer, IMovieLayer
{
    /// <summary>What the field gives for an object id: its node, its Eff_UI anchor, whether it is a player, and its IsDead.</summary>
    public readonly record struct Target(Node3D Root, Func<Vector3> Anchor, bool Pc, Func<bool> Dead);

    sealed class Tracked { public BuffBalloonController.Actor Actor; public Node3D Root; public Func<Vector3> Anchor; public Func<bool> Dead; }

    readonly BuffBalloonController controller;
    readonly MovieDialog movie;
    readonly float pcOffset, npcOffset;
    readonly Dictionary<int, Tracked> actors = new();
    readonly Dictionary<BuffBalloonController.Actor, Tracked> byActor = new();
    readonly List<int> gone = new();
    readonly Func<BuffBalloonController.Actor, (float, float, float)?> project;
    Camera3D camera;
    Field field;
    float u = 1;
    bool layer;

    /// <summary>The field's actor of an object id; null when there is none.</summary>
    public Func<int, Target?> Resolve;
    /// <summary>The local player's object id (IsUpperTargetId).</summary>
    public Func<int> SelfObjectId = () => -1;
    /// <summary>The local player: the distance is measured from it.</summary>
    public Func<Node3D> Viewer;

    /// <summary>The live field's layer, while it is in the tree.</summary>
    public static LiveBuffBalloons Live { get; private set; }
    /// <summary>The controller (null when the movie did not load).</summary>
    public BuffBalloonController Controller => controller;

    public LiveBuffBalloons(Ms2.Formats.Pack.PackSet packs)
    {
        Name = "LiveBuffBalloons";
        Layer = 5;                       // with the name tags and the HP bars
        Offset = UiScale.MovieOrigin;
        MovieLayerHost.Bind(this, CommonDepth.HpBar);   // uiservice.cpp:949: LoadSwf(3)
        var constants = FieldContents.Constants(packs);
        pcOffset = constants.Float("OffsetPcChatballoon", 210);
        npcOffset = constants.Float("OffsetNpcChatballoon", 210);
        project = Project;
        var c = new BuffBalloonController
        {
            VisibleDistance = constants.Float("BuffBallonDistance", 2500),
            IsUpperTarget = id => id != 0 && id == SelfObjectId(),
        };
        if (HudScene.GateTickMs != null) c.TickMs = HudScene.GateTickMs;
        try
        {
            var tables = GameTables.Shared(packs, GameOptions.StringLanguage);
            movie = new MovieDialog(FieldContents.Ui(packs), c, new UiModel(tables), new Vector2(1280, 720));
            AddChild(movie);
            controller = c;
        }
        catch (Exception e) { GD.PushWarning("buff balloon movie: " + e.Message); movie = null; }
    }

    public LiveBuffBalloons() { }

    public override void _EnterTree()
    {
        Live = this;
        AddToGroup(IMovieLayer.Group);
        Resize(GetViewport().GetVisibleRect().Size);
    }

    public override void _ExitTree()
    {
        if (Live == this) Live = null;
    }

    /// <summary><see cref="IMovieLayer"/>: the movie laid out for the new window.</summary>
    public void Resize(Vector2 window)
    {
        u = UiScale.For(window);
        movie?.Relayout(window);
    }

    /// <summary>PC::vfunc_84 / CNpc::vfunc_84: the balloon of the object's actor, made on first use; null without one.</summary>
    public IBuffBalloon Of(int objectId)
    {
        if (controller == null || objectId == 0) return null;
        var t = Track(objectId);
        return t == null ? null : controller.BalloonOf(t.Actor);
    }

    Tracked Track(int objectId)
    {
        if (Resolve?.Invoke(objectId) is not { } target || target.Root == null || !IsInstanceValid(target.Root)) return null;
        if (actors.TryGetValue(objectId, out var t))
        {
            if (t.Root == target.Root) return t;
            Drop(objectId);
        }
        t = new Tracked
        {
            Actor = new BuffBalloonController.Actor { ObjectId = objectId, Offset = target.Pc ? pcOffset : npcOffset },
            Root = target.Root, Anchor = target.Anchor, Dead = target.Dead,
        };
        actors[objectId] = t;
        byActor[t.Actor] = t;
        return t;
    }

    void Drop(int objectId)
    {
        if (!actors.Remove(objectId, out var t)) return;
        byActor.Remove(t.Actor);
        controller.Remove(t.Actor);
    }

    public override void _Process(double delta)
    {
        if (controller == null || actors.Count == 0) return;
        camera ??= GetViewport().GetCamera3D();
        if (camera == null) return;
        field ??= GetTree().GetFirstNodeInGroup(Field.Group) as Field;
        var viewer = Viewer?.Invoke();
        var eye = viewer != null && IsInstanceValid(viewer) ? viewer.GlobalPosition : camera.GlobalPosition;
        foreach (var (id, t) in actors)
        {
            if (!IsInstanceValid(t.Root)) { gone.Add(id); continue; }
            t.Actor.Distance = eye.DistanceTo(t.Root.GlobalPosition);
            t.Actor.Dead = t.Dead?.Invoke() ?? false;
        }
        if (gone.Count > 0) { foreach (var id in gone) Drop(id); gone.Clear(); }
        layer = MovieLayerHost.Layers.IsVisible(CommonDepth.HpBar);
        controller.Process(project);
    }

    /// <summary>UIHudEntity's projection of the actor's vfunc_10 anchor ("Eff_UI"), in whole twips at the movie scale, and
    /// FUN_1412762d0's camera-pitch shift there.</summary>
    (float, float, float)? Project(BuffBalloonController.Actor a)
    {
        if (!layer || !byActor.TryGetValue(a, out var t) || !IsInstanceValid(t.Root)) return null;
        var anchor = t.Anchor?.Invoke() ?? t.Root.GlobalPosition;
        if (camera.IsPositionBehind(anchor)) return null;
        var p = camera.UnprojectPosition(anchor);
        float shift = field == null ? 0 : PlayerNameTag.PitchShift(field, anchor);
        return ((float)(Math.Floor(p.X / u * 20.0) / 20.0), (float)(Math.Floor(p.Y / u * 20.0) / 20.0), shift);
    }
}
