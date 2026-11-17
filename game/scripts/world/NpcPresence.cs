using Godot;
using Ms2.Formats.World;
using Ms2Godot.Ui;

namespace Ms2Godot.World;

/// <summary>
/// What the client adds to every npc it spawns, here for the sandbox's stand-in npcs:
/// - The body capsule (CNpc::vfunc_124 -> NxCapsuleController): &lt;capsule radius height&gt; from the npc table, standing on the
///   npc's position, in collision group 4; it blocks the local player unless the row sets capsule ignore.
/// - The name tag (UINameTag, the uinametag movie's NameTag): only for rows with nametag="1"; the name and, on the line above,
///   the npc title (npctitle.xml), font size 18, in the colour NpcRow.TagColorKey picks (friendly, or by class for hostile
///   npcs; NameTagColorDead once a hostile npc dies). It sits OffsetNpcNametag above the model's top, is
///   drawn in screen space, fully opaque up to 700 away, fading to 40% at 1500 and hidden beyond. Like every movie the tag is
///   drawn at the UI scale (UiScale), and NameTag.setData centres m_name vertically (TextFieldEx VALIGN_CENTER), as for the
///   local player's tag (PlayerNameTag).
/// </summary>
public sealed partial class NpcPresence : CanvasLayer
{
    const float HideDistance = 1500, OpaqueDistance = 700;

    /// <summary>CNpc::vfunc_126: the body's PhysX collision group, 4 (5 with capsule ignore, which gets no body here).</summary>
    const int NpcGroup = 4;

    sealed class Tag { public FieldNpcs.Placed Npc; public MovieClipNode Node; public Vector2 Size; public Vector3 Root, Head; public bool On; }

    readonly List<Tag> tags = new();
    readonly Dictionary<FieldNpcs.Placed, NpcHpBar> bars = new();
    readonly ClientConstants constants;
    float tagOffset;
    float u = 1;                          // the movies' draw scale (UiScale)
    Camera3D camera;
    /// <summary>The local player: the fade distance is measured from it (the sandbox camera sits further back than the client's).</summary>
    public Node3D Viewer;
    public int Bodies, NameTags;
    /// <summary>The npcs <see cref="Build"/> was given.</summary>
    public IReadOnlyList<FieldNpcs.Placed> Npcs = Array.Empty<FieldNpcs.Placed>();

    /// <summary>The world point the npc's name tag, HP bar and damage numbers hang from (see the class summary).</summary>
    public Vector3 Anchor(FieldNpcs.Placed n) => n.Root.GlobalPosition + new Vector3(0, n.Height + tagOffset, 0);

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
        var color = TagColor(n.Row, true);
        foreach (var t in tags)
            if (t.Npc == n)
                foreach (var clip in new[] { t.Node.FindClip("m_name"), t.Node.FindClip("m_guild") })
                    foreach (var c in clip?.AllClips() ?? [])
                        foreach (var f in c.TextFields) f.Label.LabelSettings.FontColor = color;
    }

    /// <summary>
    /// The name tag colour (CNpc's per-frame update, NpcRow.TagColorKey) from the colour constants, black for the colour 0.
    /// CNpc::vfunc_122 (hostile to the local player) stands in as friendly 0: every row with friendly set gets
    /// NameTagColorFriendly either way.
    /// </summary>
    Color TagColor(NpcRow row, bool dead)
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

    public NpcPresence(ClientConstants constants) { this.constants = constants; Name = "NpcPresence"; Layer = 5; Offset = UiScale.MovieOrigin; }
    public NpcPresence() { }

    public void Build(IEnumerable<FieldNpcs.Placed> npcs, UiAssets ui)
    {
        tagOffset = constants.Float("OffsetNpcNametag", 260);
        if (IsInsideTree()) u = UiScale.For(GetViewport().GetVisibleRect().Size);
        UiMovie movie = null, barMovie = null;
        try { movie = new UiMovie(ui, "uinametag"); } catch (Exception e) { GD.PushWarning("name tag movie: " + e.Message); }
        try { barMovie = new UiMovie(ui, "uihpbar"); } catch (Exception e) { GD.PushWarning("hp bar movie: " + e.Message); }
        foreach (var n in npcs)
        {
            if (!n.Row.CapsuleIgnore) AddBody(n);
            if (n.Row.Nametag && movie != null && !string.IsNullOrEmpty(n.Row.Name)) AddTag(n, movie, TagColor(n.Row, false));
        }
        // the bars after the tags: which of the two the client draws on top is not known (both are separate movies)
        foreach (var n in npcs)
            if (n.Row.HpBar && barMovie != null) bars[n] = new NpcHpBar(n, barMovie, u, this);
        Npcs = npcs.ToList();
    }

    void AddBody(FieldNpcs.Placed n)
    {
        float radius = Math.Max(1, n.Row.CapsuleRadius), height = Math.Max(2 * radius, n.Row.CapsuleHeight);
        var body = new StaticBody3D { Name = "Capsule" };
        body.SetMeta("ms2_group", NpcGroup);
        body.SetMeta(Movement.MoveConfig.ControllerMeta, true);   // the npc body is a GameObjectController (CNpc m_unk_150)
        body.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = radius, Height = height }, Position = new Vector3(0, height / 2, 0) });
        n.Root.AddChild(body);
        Bodies++;
    }

    void AddTag(FieldNpcs.Placed n, UiMovie movie, Color color)
    {
        var node = movie.BuildClass("NameTag", u) ?? movie.BuildClass("UINameTag", u);
        if (node == null) return;
        // setData {name, guild = title, visibleSymbol false, fontSize 18, fontColor}: no symbol or skin for npcs; m_name is
        // already size 18 and is centred vertically (VALIGN_CENTER)
        foreach (var hide in new[] { "m_symbol", "m_bgSkin" }) node.SetNamedVisible(hide, false);
        var nameClip = node.FindClip("m_name");
        SetLabel(nameClip, n.Row.Name, null, color);
        foreach (var c in nameClip?.AllClips() ?? []) foreach (var t in c.TextFields) t.AlignCenter();
        SetLabel(node.FindClip("m_guild"), n.Row.Title ?? "", null, color);
        node.Scale = new Vector2(u, u);
        AddChild(node);
        tags.Add(new Tag { Npc = n, Node = node });
        NameTags++;
    }

    internal static void SetLabel(MovieClipNode label, string text, int? size, Color color)
    {
        if (label == null) return;
        foreach (var c in label.AllClips())
            foreach (var t in c.TextFields)
            {
                t.Text = text;
                t.Label.LabelSettings = (LabelSettings)t.Label.LabelSettings.Duplicate();
                t.Label.LabelSettings.FontColor = color;
                t.Label.LabelSettings.OutlineSize = 3;
                t.Label.LabelSettings.OutlineColor = new Color(0, 0, 0, 0.85f);
                if (size != null) t.Label.LabelSettings.FontSize = size.Value;
            }
    }

    // The range and facing test (and the fade) are re-evaluated a few times a second; a tag in range only follows its head
    // on screen every frame. An npc's position is read once per refresh instead of every frame.
    int frame;
    const int RefreshEvery = 4;

    public override void _Process(double delta)
    {
        camera ??= GetViewport().GetCamera3D();
        if (camera == null) return;
        bool refresh = frame++ % RefreshEvery == 0;
        Vector3 eye = default;
        if (refresh) eye = Viewer != null && IsInstanceValid(Viewer) ? Viewer.GlobalPosition : camera.GlobalPosition;
        foreach (var t in tags)
        {
            if (refresh)
            {
                t.Root = t.Npc.Root.GlobalPosition;
                t.Head = Anchor(t.Npc);
                float d = eye.DistanceTo(t.Root);
                bool on = d <= HideDistance && !camera.IsPositionBehind(t.Head);
                if (on != t.On) { t.On = on; t.Node.Visible = on; }
                if (on)
                {
                    float alpha = d <= OpaqueDistance ? 1f : ((HideDistance - d) * 0.05f + 40f) / 100f;
                    t.Node.Modulate = new Color(1, 1, 1, Mathf.Clamp(alpha, 0, 1));
                }
            }
            if (t.On) t.Node.Position = camera.UnprojectPosition(t.Head).Round();
        }
        foreach (var b in bars.Values)
        {
            if (!b.Active) continue;
            var a = Anchor(b.Npc);
            b.Update(camera.IsPositionBehind(a) ? null : camera.UnprojectPosition(a));   // float origin, as SetPositionFloat
        }
    }
}
