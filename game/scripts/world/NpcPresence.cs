using Godot;
using Ms2.Formats.World;
using Ms2Godot.Ui;

namespace Ms2Godot.World;

/// <summary>
/// What the client adds to every npc it spawns, here for the sandbox's stand-in npcs:
/// - The body capsule (CNpc::vfunc_124 -> NxCapsuleController): &lt;capsule radius height&gt; from the npc table, standing on the
///   npc's position, in collision group 4; it blocks the local player unless the row sets capsule ignore.
/// - The name tag (UINameTag, the uinametag movie's NameTag): only for rows with nametag="1"; the name and, on the line above,
///   the npc title (npctitle.xml), font size 18 in NameTagColorFriendly. It sits OffsetNpcNametag above the model's top, is
///   drawn in screen space, fully opaque up to 700 away, fading to 40% at 1500 and hidden beyond.
/// </summary>
public sealed partial class NpcPresence : CanvasLayer
{
    const float HideDistance = 1500, OpaqueDistance = 700;

    /// <summary>CNpc::vfunc_126: the body's PhysX collision group, 4 (5 with capsule ignore, which gets no body here).</summary>
    const int NpcGroup = 4;

    sealed class Tag { public FieldNpcs.Placed Npc; public MovieClipNode Node; public Vector2 Size; public Vector3 Root, Head; public bool On; }

    readonly List<Tag> tags = new();
    readonly ClientConstants constants;
    float tagOffset;
    Camera3D camera;
    /// <summary>The local player: the fade distance is measured from it (the sandbox camera sits further back than the client's).</summary>
    public Node3D Viewer;
    public int Bodies, NameTags;

    public NpcPresence(ClientConstants constants) { this.constants = constants; Name = "NpcPresence"; Layer = 5; }
    public NpcPresence() { }

    public void Build(IEnumerable<FieldNpcs.Placed> npcs, UiAssets ui)
    {
        tagOffset = constants.Float("OffsetNpcNametag", 260);
        var (r, g, b) = constants.Color("NameTagColorFriendly", (0.635f, 1f, 0.549f));
        var color = new Color(r, g, b);
        UiMovie movie = null;
        try { movie = new UiMovie(ui, "uinametag"); } catch (Exception e) { GD.PushWarning("name tag movie: " + e.Message); }
        foreach (var n in npcs)
        {
            if (!n.Row.CapsuleIgnore) AddBody(n);
            if (n.Row.Nametag && movie != null && !string.IsNullOrEmpty(n.Row.Name)) AddTag(n, movie, color);
        }
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
        var node = movie.BuildClass("NameTag") ?? movie.BuildClass("UINameTag");
        if (node == null) return;
        // setData {name, guild = title, visibleSymbol false, fontSize 18, fontColor}: no symbol or skin for npcs
        foreach (var hide in new[] { "m_symbol", "m_bgSkin" }) node.SetNamedVisible(hide, false);
        SetLabel(node.FindClip("m_name"), n.Row.Name, 18, color);
        SetLabel(node.FindClip("m_guild"), n.Row.Title ?? "", null, color);
        AddChild(node);
        tags.Add(new Tag { Npc = n, Node = node });
        NameTags++;
    }

    static void SetLabel(MovieClipNode label, string text, int? size, Color color)
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
                t.Head = t.Root + new Vector3(0, t.Npc.Height + tagOffset, 0);
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
    }
}
