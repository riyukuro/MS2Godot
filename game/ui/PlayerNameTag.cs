using Godot;
using Ms2.Formats.World;
using Ms2Godot.World;

namespace Ms2Godot.Ui;

/// <summary>
/// The own player's name tag. PC::vfunc_82 creates the PC's UINameTag entity (CreateNameTagEntity) with font size 0x12 and
/// NameTagColorFriendly; every frame PC::vfunc_53 sets the colour by relation, NameTagColorMyPC for the local player.
/// <list type="bullet">
/// <item>Data (UINameTag::Entity::FUN_141a5ff60, setData): the name, the guild as "&lt;guild&gt;" (none here), visibleSymbol
/// and visibleName from the game options, the name tag item's symbol (<see cref="SetSymbol"/>; no field symbol or PvP team),
/// font size and colour, no skin.</item>
/// <item>Anchor (UIHudEntity::Entity::vfunc_10): the world point of the actor's "Eff_UI" node, the player's row of
/// Table/pcEffectDummy.xml for its gender placed by DummyWorld (on Scene Root at (0, 0, 170) in the data, straight up the
/// world); the actor's position when it has no such node.</item>
/// <item>Place (vfunc_6): the anchor's screen point times the movie scale (into stage units), plus vfunc_9 =
/// PCBase::vfunc_89 = OffsetPcNametag (m_f16c) + m_f170 (the emotion states' offsetNameTag, else 0) down, plus FUN_1412762d0(anchor); the movie is drawn
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
    readonly UiMovie movie;
    readonly NameTagSymbolLine symbol = new();
    readonly float offset;
    readonly Ms2.Formats.Pack.PackSet packs;
    IReadOnlyList<EffectDummy> dummies;  // the player's effect dummies (gender row of the pc table)
    Node3D model;                        // the player's model root node
    float u = 1;                         // the movies' draw scale (UiScale)
    Field field;
    Player player;
    Camera3D camera;
    Color color;                         // NameTagColorMyPC

    public PlayerNameTag(UiAssets ui, ClientConstants constants, string name, Vector2 window)
    {
        Name = "PlayerNameTag";
        Layer = 5;                       // with the npcs' tags (NpcPresence), under the HUD
        MovieLayerHost.Bind(this, Ms2.Ui.CommonDepth.NameTag);   // CUIService::HideWorldUI loads the name tag (0x4e5a) into layer 2
        Offset = UiScale.MovieOrigin;    // the movie's viewport origin (GFxMovie::vfunc_7); vfunc_6 maps the screen point straight to stage units
        offset = constants.Float("OffsetPcNametag", 30);
        packs = ui.Packs;
        var (r, g, b) = constants.Color("NameTagColorMyPC", (1f, 0.812f, 0.106f));
        color = new Color(r, g, b);
        try
        {
            movie = new UiMovie(ui, "uinametag");
            u = UiScale.For(window);
            node = movie.BuildClass("NameTag", u) ?? movie.BuildClass("UINameTag", u);
        }
        catch (Exception e) { GD.PushWarning("player name tag: " + e.Message); }
        if (node == null) return;
        foreach (var hide in new[] { "m_symbol", "m_bgSkin" }) node.SetNamedVisible(hide, false);
        // setData: TextFieldEx.setVerticalAlign(m_name.textField, VALIGN_CENTER), then the name; the field is already size 0x12
        var nameClip = node.FindClip("m_name");
        NpcPresence.SetLabel(nameClip, name, null, color);
        Shown = name;
        foreach (var c in nameClip?.AllClips() ?? []) foreach (var t in c.TextFields) t.AlignCenter();
        NpcPresence.SetLabel(node.FindClip("m_guild"), "", null, color);
        node.Scale = new Vector2(u, u);
        node.Visible = false;
        AddChild(node);
    }

    public PlayerNameTag() { }

    /// <summary>
    /// The window was resized: the movie's new viewport scale (GFxManager::ResizeMovies, GFxMovie::vfunc_7,
    /// gfxmovie.cpp:146), kept alive with its state. STILL-OPEN: the clips already built keep the glyphs they were built
    /// with at the old scale and are only scaled; the client's Scaleform draws them again at the new viewport scale.
    /// </summary>
    public void Resize(Vector2 window)
    {
        u = UiScale.For(window);
        if (node != null) node.Scale = new Vector2(u, u);
    }

    /// <summary>The field was swapped under the HUD (HudScene.SwapField): the old field's player, model and camera are
    /// dropped and found again in the new field; the tag hides until then.</summary>
    public void FieldChanged()
    {
        field = null; player = null; model = null; dummies = null; camera = null;
        if (node != null) node.Visible = false;
    }

    /// <summary>The PC's name changed (the own FieldAddUser, OnAddUser's SetName): setData's name line again.</summary>
    public void SetName(string name)
    {
        if (node?.FindClip("m_name") is { } nameClip) { NpcPresence.SetLabel(nameClip, name, null, color); Shown = name; }
    }

    /// <summary>
    /// The own PC's name tag symbol (the own FieldAddUser's insignia, then the Insignia packet: CSceneStateGame::OnNametag,
    /// scenestategame.cpp:7314-7331, refreshes the MyPC's UINameTag entity too, FUN_141a60f00): setData's symbol line
    /// (<see cref="NameTagSymbolLine"/>) with the PC's level and adventure level; the own tag has no guild line here.
    /// </summary>
    public void SetSymbol(ushort id, bool shown, int level, int adventureLevel) =>
        symbol.Set(node, movie, u, Ms2.Ui.GameTables.Shared(packs, GameOptions.StringLanguage).NametagSymbols, id, shown, level, adventureLevel, false);

    /// <summary>The symbol the own tag shows; 0 when none.</summary>
    public ushort ShownSymbol => symbol.Shown(node);

    /// <summary>The name the tag's m_name line shows.</summary>
    public string Shown { get; private set; }

    public override void _Process(double delta)
    {
        if (node == null) return;
        if (symbol.OptionChanged) symbol.Refresh(node, movie, u, Ms2.Ui.GameTables.Shared(packs, GameOptions.StringLanguage).NametagSymbols);
        if (player == null || field == null)
        {
            field ??= GetTree().GetFirstNodeInGroup(Field.Group) as Field;
            player ??= GetTree().GetFirstNodeInGroup(Player.Group) as Player;
            if (player == null || field == null) return;
            dummies = EffectDummyTable.Load(packs).Pc(player.Gender);
            model = player.Visual?.GetNodeOrNull<Node3D>("Model");
        }
        camera ??= GetViewport().GetCamera3D();
        if (camera == null) return;
        var anchor = DummyAnchor.Point(dummies, "Eff_UI", model, player.RootName) ?? player.GlobalPosition;
        Anchor = anchor;
        var ride = Ride;
        bool on = !camera.IsPositionBehind(anchor) && ride is not { NameTagHide: true };   // vfunc_8 (uinametag.cpp:327-333)
        node.Visible = on;
        if (!on) return;
        var p = camera.UnprojectPosition(anchor);
        // vfunc_6 works in stage units: screen point / u, then the shifts; m_f170 is the emotion states' offsetNameTag, and
        // vfunc_9 = PCBase::vfunc_89 adds the mount's default rider nameTagOffsetY (Ridee::vfunc_89, pcbase.cpp:2339-2346)
        p.Y += (offset + player.NameTagOffset + (ride?.NameTagOffsetY ?? 0) + HeightAdjust(anchor)) * u;
        node.Position = UiScale.StagePoint(p, u);
    }

    /// <summary>The own mount's row while the player rides (PCBase::GetRidee()->GetData()), else null.</summary>
    public Movement.RideData Ride => player != null && IsInstanceValid(player) ? player.Mount?.Data : null;

    /// <summary>m_f174 of the own PC (the emotion states' offsetNameTag, 0 otherwise), for the HP bar (FUN_141670390).</summary>
    public float HpBarOffset => player?.HpBarOffset ?? 0;

    /// <summary>The own PC's Eff_UI point this frame (its position when the dummy is missing), null before the player is found.</summary>
    public Vector3? Anchor { get; private set; }

    /// <summary>FUN_1412762d0: the zoom-dependent shift of a hud entity at <paramref name="anchor"/>.</summary>
    public float HeightAdjust(Vector3 anchor) => PitchShift(field, anchor);

    /// <summary>FUN_1412762d0 for any hud entity (name tags, HP bars, damage numbers) at <paramref name="anchor"/>.</summary>
    public static float PitchShift(Field field, Vector3 anchor)
    {
        if (field?.CameraEye is not { } eye) return 0;
        float pitch = Mathf.Clamp(Mathf.RadToDeg(field.CameraPitch), 0, 90);
        float d = new Vector2(anchor.X - eye.X, anchor.Z - eye.Z).Length();
        float r = d > Field.MinCameraDistance ? Field.MinCameraDistance / d : 1f;
        return Mathf.Floor((-40f + 70f * pitch / 90f) * r);
    }
}
