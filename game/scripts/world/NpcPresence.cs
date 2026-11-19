using Godot;
using Ms2.Formats.World;
using Ms2Godot.Ui;

namespace Ms2Godot.World;

/// <summary>
/// What the client adds to every npc it spawns, here for the sandbox's stand-in npcs:
/// - The body capsule (CNpc::vfunc_124 -> NxCapsuleController): &lt;capsule radius height&gt; from the npc table, standing on the
///   npc's position, in collision group 4; it blocks the local player unless the row sets capsule ignore.
/// - The name tag (UINameTag, the uinametag movie's NameTag): only for rows with nametag="1", and shown while the game
///   options let it (DisplayMonsterName for hostile rows, off by default; DisplayNpcName for the others); the name and, on the line above,
///   the npc title (npctitle.xml), font size 18, in the colour NpcRow.TagColorKey picks (friendly, or by class for hostile
///   npcs; NameTagColorDead once a hostile npc dies). It is drawn in screen space, fully opaque up to 700 away, fading to
///   40% at 1500 and hidden beyond. Like every movie the tag is drawn at the UI scale (UiScale), and NameTag.setData
///   centres m_name vertically (TextFieldEx VALIGN_CENTER), as for the local player's tag (PlayerNameTag).
/// - Place of the tag and the HP bar: both hang from the npc's "Eff_UI" effect dummy (UIHudEntity::Entity::vfunc_10; the
///   npc's position when the model has none). On screen, in stage units, the tag moves down by PCBase::vfunc_89 =
///   m_f16c + m_f170 and the bar by m_f164 + m_f174; the CNpc ctor sets m_f16c = OffsetNpcNametag, m_f164 = OffsetNpcHpbar
///   and both others to 0 (5 and 5 in the client's constants). Then both get FUN_1412762d0, the camera-pitch shift.
/// </summary>
public sealed partial class NpcPresence : CanvasLayer
{
    const float HideDistance = 1500, OpaqueDistance = 700;

    /// <summary>CNpc::vfunc_126: the body's PhysX collision group, 4 (5 with capsule ignore, which gets no body here).</summary>
    const int NpcGroup = 4;

    sealed class Tag { public FieldNpcs.Placed Npc; public MovieClipNode Node; public Vector2 Size; }

    readonly List<Tag> tags = new();
    readonly Dictionary<FieldNpcs.Placed, NpcHpBar> bars = new();
    /// <summary>The bar anchors: each npc's Eff_UI row and model node looked up once, the point per frame.</summary>
    readonly Dictionary<FieldNpcs.Placed, Func<Vector3>> barAnchor = new();
    /// <summary>ReleaseEntity's free list (UIHpBar's m_78): a bar whose npc left is kept for the next one.</summary>
    readonly Stack<MovieClipNode> barPool = new();
    readonly ClientConstants constants;
    float tagOffset, barOffset;
    Field field;
    float u = 1;                         // the movies' draw scale (UiScale)
    Camera3D camera;
    /// <summary>The local player: the fade distance is measured from it (the sandbox camera sits further back than the client's).</summary>
    public Node3D Viewer;
    public int Bodies, NameTags;
    /// <summary>The npcs <see cref="Build"/> was given.</summary>
    public IReadOnlyList<FieldNpcs.Placed> Npcs = Array.Empty<FieldNpcs.Placed>();

    /// <summary>The npc's Eff_UI point, which its name tag and HP bar hang from; its position when the model has no such dummy.</summary>
    public Vector3 Anchor(FieldNpcs.Placed n) => Dummy(n, "Eff_UI") ?? n.Root.GlobalPosition;

    static readonly NodePath ModelPath = "Model";

    /// <summary>The world point of one of the npc's effect dummies (on its model root, "Scene Root" in the files), null without it.</summary>
    public static Vector3? Dummy(FieldNpcs.Placed n, string name) =>
        DummyAnchor.Point(n.Row.EffectDummies, name, n.Body?.GetNodeOrNull<Node3D>(ModelPath), "Scene Root");

    /// <summary>FUN_1412762d0 for a hud entity at <paramref name="anchor"/> (0 before the field is found).</summary>
    public float Shift(Vector3 anchor) => field == null ? 0 : PlayerNameTag.PitchShift(field, anchor);

    /// <summary>CNpc::vfunc_66: the npc's battle stance; entering it makes the HP bar active with the current HP.</summary>
    public void Stance(FieldNpcs.Placed n, bool battle, long cur, long max)
    {
        if (battle && bars.TryGetValue(n, out var b) && !b.Dead) b.Activate(cur, max);
    }

    /// <summary>PCBase::vfunc_112 on the npc: a damage record; one by the local player shows the HP bar.</summary>
    public void Damaged(FieldNpcs.Placed n, bool byLocalPlayer)
    {
        if (bars.TryGetValue(n, out var b) && !b.Shown) b.Shown = byLocalPlayer;
    }

    /// <summary>OnStat with the HP (vfunc_74).</summary>
    public void Stat(FieldNpcs.Placed n, long cur, long max)
    {
        if (bars.TryGetValue(n, out var b)) b.Stat(cur, max);
    }

    /// <summary>The npc died: its HP bar hides, and a hostile npc's name tag turns NameTagColorDead.</summary>
    public void Died(FieldNpcs.Placed n)
    {
        if (bars.TryGetValue(n, out var b)) b.Dead = true;
        var color = TagColor(constants, n.Row, true);
        foreach (var t in tags)
            if (t.Npc == n) Recolor(t.Node, color);
    }

    /// <summary>
    /// The name tag colour (CNpc's per-frame update, NpcRow.TagColorKey) from the colour constants, black for the colour 0.
    /// CNpc::vfunc_122 (hostile to the local player) stands in as friendly 0: every row with friendly set gets
    /// NameTagColorFriendly either way.
    /// </summary>
    internal static Color TagColor(ClientConstants constants, NpcRow row, bool dead)
    {
        var key = row.TagColorKey(hostile: row.Friendly == 0, dead);
        if (key == null) return Colors.Black;
        var (r, g, b) = constants.Color(key, key switch
        {
            "NameTagColorMob" => (1f, 0.545f, 0.545f),
            "NameTagColorNamed" or "NameTagColorBoss" => (1f, 0f, 0f),
            "NameTagColorDead" => (0.741f, 0.741f, 0.741f),
            _ => (0.635f, 1f, 0.549f),
        });
        return new Color(r, g, b);
    }

    /// <summary>
    /// UINameTag::Entity::FUN_141a60920 for an npc: CNpc::vfunc_116 (the row's friendly value) 0 shows the tag by the
    /// DisplayMonsterName option (off by default, FUN_14125bdd0 gameoptionstruct.cpp:1347), any other by DisplayNpcName.
    /// </summary>
    internal static bool NameShown(NpcRow row)
    {
        var status = GameOptions.Current?.StatusOption;
        if (status == null) return true;
        return row.Friendly == 0 ? status.DisplayMonsterName : status.DisplayNpcName;
    }

    /// <summary>UIHudEntity's alpha (vfunc_6) at distance <paramref name="d"/>: 100 up to 700, then (1500 - d) x 0.05 + 40, as 0..1.</summary>
    internal static float TagAlpha(float d) => d <= OpaqueDistance ? 1f : Mathf.Clamp(((HideDistance - d) * 0.05f + 40f) / 100f, 0, 1);

    /// <summary>UIHudEntity::vfunc_8's range test: the tag hides past m_54 = 1500.</summary>
    internal static bool InRange(float d) => d <= HideDistance;

    /// <summary>
    /// A uinametag NameTag with setData's lines: <paramref name="name"/> (centred vertically, VALIGN_CENTER) and
    /// <paramref name="guild"/> on the line above, both in <paramref name="color"/>, the field's own size (0x12); no symbol
    /// and no skin (m_bg hidden). Null when the movie has no such class.
    /// </summary>
    internal static MovieClipNode BuildTag(UiMovie movie, float u, string name, string guild, Color color)
    {
        var node = movie.BuildClass("NameTag", u) ?? movie.BuildClass("UINameTag", u);
        if (node == null) return null;
        foreach (var hide in new[] { "m_symbol", "m_bgSkin" }) node.SetNamedVisible(hide, false);
        var nameClip = node.FindClip("m_name");
        SetLabel(nameClip, name, null, color);
        foreach (var c in nameClip?.AllClips() ?? []) foreach (var t in c.TextFields) t.AlignCenter();
        SetLabel(node.FindClip("m_guild"), guild ?? "", null, color);
        node.Scale = new Vector2(u, u);
        return node;
    }

    /// <summary>setData's fontColor again (UINameTag::Entity::FUN_141a5e500): both lines.</summary>
    internal static void Recolor(MovieClipNode node, Color color)
    {
        foreach (var clip in new[] { node.FindClip("m_name"), node.FindClip("m_guild") })
            foreach (var c in clip?.AllClips() ?? [])
                foreach (var f in c.TextFields) f.Label.LabelSettings.FontColor = color;
    }

    public NpcPresence(ClientConstants constants) { this.constants = constants; Name = "NpcPresence"; Layer = 5; Offset = UiScale.MovieOrigin; }
    public NpcPresence() { }

    public void Build(IEnumerable<FieldNpcs.Placed> npcs, UiAssets ui)
    {
        tagOffset = constants.Float("OffsetNpcNametag", 260);   // the defaults are the client's constants block
        barOffset = constants.Float("OffsetNpcHpbar", 120);
        if (IsInsideTree()) u = UiScale.For(GetViewport().GetVisibleRect().Size);
        UiMovie movie = null, barMovie = null;
        try { movie = new UiMovie(ui, "uinametag"); } catch (Exception e) { GD.PushWarning("name tag movie: " + e.Message); }
        try { barMovie = new UiMovie(ui, "uihpbar"); } catch (Exception e) { GD.PushWarning("hp bar movie: " + e.Message); }
        foreach (var n in npcs)
        {
            if (!n.Row.CapsuleIgnore) AddBody(n);
            if (n.Row.Nametag && movie != null && !string.IsNullOrEmpty(n.Row.Name)) AddTag(n, movie, TagColor(constants, n.Row, false));
        }
        // the bars after the tags: which of the two the client draws on top is not known (both are separate movies)
        foreach (var n in npcs)
            if (n.Row.HpBar && barMovie != null)
            {
                bars[n] = new NpcHpBar(n, barMovie, u, this, pool: barPool);
                // the anchor's row and model node are looked up once (UIHudEntity::Entity caches its node, m_20);
                // a row with another parent keeps the full walk
                var modelNode = n.Body?.GetNodeOrNull<Node3D>("Model");
                var effUi = modelNode != null ? EffectDummyTable.Find(n.Row.EffectDummies, "Eff_UI") : null;
                barAnchor[n] = effUi != null && effUi.ParentNode == "Scene Root"
                    ? (Func<Vector3>)(() => DummyAnchor.Point(effUi, modelNode, "Scene Root") ?? n.Root.GlobalPosition)
                    : () => Dummy(n, "Eff_UI") ?? n.Root.GlobalPosition;
            }
        Npcs = npcs.ToList();
    }

    void AddBody(FieldNpcs.Placed n)
    {
        float radius = Math.Max(1, n.Row.CapsuleRadius), height = Math.Max(2 * radius, n.Row.CapsuleHeight);
        var body = new StaticBody3D { Name = "Capsule", CollisionLayer = Movement.MoveConfig.NpcLayer };   // group 4
        body.SetMeta(FieldBuilder.GroupMeta, NpcGroup);
        body.SetMeta(Movement.MoveConfig.ControllerMeta, true);   // the npc body is a GameObjectController (CNpc m_unk_150)
        body.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = radius, Height = height }, Position = new Vector3(0, height / 2, 0) });
        n.Root.AddChild(body);
        Bodies++;
    }

    void AddTag(FieldNpcs.Placed n, UiMovie movie, Color color)
    {
        // setData {name, guild = title, visibleSymbol false, fontSize 18, fontColor}: no symbol or skin for npcs
        var node = BuildTag(movie, u, n.Row.Name, n.Row.Title, color);
        if (node == null) return;
        AddChild(node);
        tags.Add(new Tag { Npc = n, Node = node });
        NameTags++;
    }

    /// <summary>setData's text and fontColor on a name tag line. The dark edge round the letters is the field's own drop shadow
    /// (NameTagName / NameTagGuild: black, blur 3, strength 10, no distance), which the movie build already draws (GlyphShadow);
    /// the line gets no outline of its own.</summary>
    internal static void SetLabel(MovieClipNode label, string text, int? size, Color color)
    {
        if (label == null) return;
        foreach (var c in label.AllClips())
            foreach (var t in c.TextFields)
            {
                t.Text = text;
                t.Label.LabelSettings = (LabelSettings)t.Label.LabelSettings.Duplicate();
                t.Label.LabelSettings.FontColor = color;
                if (size != null) t.Label.LabelSettings.FontSize = size.Value;
            }
    }

    public override void _Process(double delta)
    {
        camera ??= GetViewport().GetCamera3D();
        if (camera == null) return;
        field ??= GetTree().GetFirstNodeInGroup(Field.Group) as Field;
        // every frame: the range, facing and fade tests with the npc where it is now, then the tag at its head
        var eye = Viewer != null && IsInstanceValid(Viewer) ? Viewer.GlobalPosition : camera.GlobalPosition;
        // the tags and the bars are drawn in their CommonDepth layers: CUIService::HideWorldUI loads the name tag (0x4e5a)
        // into layer 2 and the HP bar (0x4e36) into layer 3, which the NPC talk hides
        bool tagLayer = MovieLayerHost.Layers.IsVisible(Ms2.Ui.CommonDepth.NameTag);
        bool barLayer = MovieLayerHost.Layers.IsVisible(Ms2.Ui.CommonDepth.HpBar);
        foreach (var t in tags)
        {
            var head = Anchor(t.Npc);
            float d = eye.DistanceTo(t.Npc.Root.GlobalPosition);
            bool on = tagLayer && NameShown(t.Npc.Row) && InRange(d) && !camera.IsPositionBehind(head);
            t.Node.Visible = on;
            if (!on) continue;
            t.Node.Modulate = new Color(1, 1, 1, TagAlpha(d));
            var p = camera.UnprojectPosition(head);
            p.Y += (tagOffset + Shift(head)) * u;
            t.Node.Position = UiScale.StagePoint(p, u);
        }
        foreach (var b in bars.Values)
        {
            if (!b.Active) continue;
            var a = barAnchor.TryGetValue(b.Npc, out var point) ? point() : Anchor(b.Npc);
            if (!barLayer || camera.IsPositionBehind(a)) { b.Update(null); continue; }
            var p = camera.UnprojectPosition(a);
            p.Y += (barOffset + Shift(a)) * u;
            b.Update(p);
        }
    }
}
