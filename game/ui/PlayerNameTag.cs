using Godot;
using Ms2.Formats.World;
using Ms2Godot.World;

namespace Ms2Godot.Ui;

/// <summary>
/// The own player's name tag. PC::vfunc_82 creates the PC's UINameTag entity (CreateNameTagEntity) with font size 0x12 and
/// NameTagColorFriendly; every frame PC::vfunc_53 sets the colour by relation, NameTagColorMyPC for the local player.
/// <list type="bullet">
/// <item>Data (UINameTag::Entity::FUN_141a5ff60, setData): the name, the guild as "&lt;guild&gt;" (none here), visibleSymbol
/// and visibleName from the game options, no symbol (no field symbol, PvP team or name tag item), font size and colour, no
/// skin.</item>
/// <item>Anchor (UIHudEntity::Entity::vfunc_10): the world point of the actor's "Eff_UI" node, the player's row of
/// Table/pcEffectDummy.xml for its gender placed by DummyWorld (on Scene Root at (0, 0, 170) in the data, straight up the
/// world); the actor's position when it has no such node.</item>
/// <item>Place (vfunc_6): the anchor's screen point times the movie scale (into stage units), plus vfunc_9 =
/// PCBase::vfunc_89 = OffsetPcNametag (m_f16c) + m_f170 (0 unless set) down, plus FUN_1412762d0(anchor); the movie is drawn
/// at the UI scale (UiScale), so the shifts and the clip shrink with it on a window under 1920 wide.</item>
/// <item>FUN_1412762d0, while the camera is set up: floor((-40 + 70 x clamp(pitch in degrees, 0, 90) / 90) x r), with
/// pitch the camera mode's m_6c and r = min distance (m_58) / d once the ground distance d from the camera mode's position
/// to the anchor is past it, else 1.</item>
/// </list>
/// Fully opaque: an own tag is always within 700 of its viewer.
/// </summary>
public sealed partial class PlayerNameTag : CanvasLayer
{
    readonly MovieClipNode node;
    readonly float offset;
    readonly Ms2.Formats.Pack.PackSet packs;
    IReadOnlyList<EffectDummy> dummies;  // the player's effect dummies (gender row of the pc table)
    Node3D model;                        // the player's model root node
    float u = 1;                         // the movies' draw scale (UiScale)
    Field field;
    Player player;
    Camera3D camera;

    public PlayerNameTag(UiAssets ui, ClientConstants constants, string name, Vector2 window)
    {
        Name = "PlayerNameTag";
        Layer = 5;                       // with the npcs' tags (NpcPresence), under the HUD
        Offset = UiScale.MovieOrigin;    // the movie's viewport origin (GFxMovie::vfunc_7); vfunc_6 maps the screen point straight to stage units
        offset = constants.Float("OffsetPcNametag", 30);
        packs = ui.Packs;
        var (r, g, b) = constants.Color("NameTagColorMyPC", (1f, 0.812f, 0.106f));
        var color = new Color(r, g, b);
        try
        {
            var movie = new UiMovie(ui, "uinametag");
            u = UiScale.For(window);
            node = movie.BuildClass("NameTag", u) ?? movie.BuildClass("UINameTag", u);
        }
        catch (Exception e) { GD.PushWarning("player name tag: " + e.Message); }
        if (node == null) return;
        foreach (var hide in new[] { "m_symbol", "m_bgSkin" }) node.SetNamedVisible(hide, false);
        // setData: TextFieldEx.setVerticalAlign(m_name.textField, VALIGN_CENTER), then the name; the field is already size 0x12
        var nameClip = node.FindClip("m_name");
        NpcPresence.SetLabel(nameClip, name, null, color);
        foreach (var c in nameClip?.AllClips() ?? []) foreach (var t in c.TextFields) t.AlignCenter();
        NpcPresence.SetLabel(node.FindClip("m_guild"), "", null, color);
        node.Scale = new Vector2(u, u);
        node.Visible = false;
        AddChild(node);
    }

    public PlayerNameTag() { }

    public override void _Process(double delta)
    {
        if (node == null) return;
        if (player == null || field == null)
        {
            field ??= Find<Field>(GetTree().Root);
            player ??= Find<Player>(GetTree().Root);
            if (player == null || field == null) return;
            dummies = EffectDummyTable.Load(packs).Pc(player.Gender);
            model = player.Visual?.GetNodeOrNull<Node3D>("Model");
        }
        camera ??= GetViewport().GetCamera3D();
        if (camera == null) return;
        var anchor = DummyAnchor.Point(dummies, "Eff_UI", model, player.RootName) ?? player.GlobalPosition;
        bool on = !camera.IsPositionBehind(anchor);
        node.Visible = on;
        if (!on) return;
        var p = camera.UnprojectPosition(anchor);
        p.Y += (offset + HeightAdjust(anchor)) * u;      // vfunc_6 works in stage units: screen point / u, then the shifts
        node.Position = p.Round();
    }

    /// <summary>FUN_1412762d0: the zoom-dependent shift of a hud entity at <paramref name="anchor"/>.</summary>
    float HeightAdjust(Vector3 anchor)
    {
        if (field.CameraEye is not { } eye) return 0;
        float pitch = Mathf.Clamp(Mathf.RadToDeg(field.CameraPitch), 0, 90);
        float d = new Vector2(anchor.X - eye.X, anchor.Z - eye.Z).Length();
        float r = d > Field.MinCameraDistance ? Field.MinCameraDistance / d : 1f;
        return Mathf.Floor((-40f + 70f * pitch / 90f) * r);
    }

    static T Find<T>(Node n) where T : Node
    {
        if (n is T t) return t;
        foreach (var c in n.GetChildren()) if (Find<T>(c) is { } f) return f;
        return null;
    }
}
