using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui;
using Ms2Godot.Ui;
using Ms2Godot.World;

namespace Ms2Godot.Net;

/// <summary>
/// The name tags of a live or replayed field's other players and npcs: the client's UINameTag (the uinametag movie's NameTag),
/// drawn in screen space like the offline npcs' tags (NpcPresence) and the own tag (PlayerNameTag).
/// <list type="bullet">
/// <item>Npcs (rows with nametag="1"): the name and, on the line above, the npc title; the colour NpcRow.TagColorKey picks,
/// NameTagColorDead once a hostile npc dies; shown by DisplayMonsterName (hostile rows, off by default) or DisplayNpcName.</item>
/// <item>Players (UINameTag::Entity::FUN_141a5ff60): the name, "{title name} {name}" with a title; the guild as
/// "&lt;guild&gt;" on the line above; the name tag item's symbol (FUN_141661e00 / FUN_141661e20, the packet's insignia
/// and its shown flag) with the level for "level" symbols; shown by DisplayFriendlyName. Colour per frame (PC::vfunc_53,
/// pc.cpp:4455-4466): NameTagColorPC, NameTagColorMyPC for the local player, NameTagColorParty for a member of the local
/// player's party. Not ported: NameTagColorPCHostile (vfunc_122 needs a PvP source), the field-property and PvP team
/// symbols, the enchant symbol (FUN_141661e40, the equipment's grade).</item>
/// <item>Place (UIHudEntity::Entity::vfunc_6 / vfunc_10): the actor's Eff_UI dummy, its screen point in stage units, moved
/// down by OffsetNpcNametag or OffsetPcNametag and FUN_1412762d0 (the camera-pitch shift); fully opaque up to 700 from the
/// viewer, fading to 40% at 1500, hidden beyond (m_54) and behind the camera.</item>
/// </list>
/// </summary>
public sealed partial class LiveNameTags : CanvasLayer, IMovieLayer
{
    sealed class Tag
    {
        public Node3D Root; public Func<Vector3> Anchor; public MovieClipNode Node; public float Offset;
        public NpcRow Row; public bool Dead;          // npcs
        public long CharacterId; public bool Self;    // players
        public Color Color;
    }

    readonly Dictionary<Node3D, Tag> tags = new();
    readonly ClientConstants constants;
    readonly PackSet packs;
    readonly UiMovie movie;
    readonly GameTables tables;
    readonly float npcOffset, pcOffset;
    readonly Color pcColor, myColor, partyColor;
    float u = 1;
    Field field;
    Camera3D camera;
    /// <summary>The one the fade distance is measured from: the local player (found by its group when not set), else the camera.</summary>
    public Node3D Viewer;
    /// <summary>Whether a character is in the local player's party (CPartyManager::IsMemberByCharId with a party).</summary>
    public Func<long, bool> InParty;
    public int Count => tags.Count;

    public LiveNameTags(PackSet packs)
    {
        Name = "LiveNameTags";
        Layer = 5;                       // with the npcs' and the own tag, under the HUD
        Offset = UiScale.MovieOrigin;
        MovieLayerHost.Bind(this, CommonDepth.NameTag);   // CUIService::HideWorldUI loads the name tag (0x4e5a) into layer 2
        this.packs = packs;
        constants = FieldContents.Constants(packs);
        npcOffset = constants.Float("OffsetNpcNametag", 5);
        pcOffset = constants.Float("OffsetPcNametag", 30);
        pcColor = Rgb("NameTagColorPC", (1f, 1f, 1f));
        myColor = Rgb("NameTagColorMyPC", (1f, 0.812f, 0.106f));
        partyColor = Rgb("NameTagColorParty", (0.549f, 0.686f, 1f));
        tables = GameTables.Shared(packs, GameOptions.StringLanguage);
        try { movie = new UiMovie(FieldContents.Ui(packs), "uinametag"); }
        catch (Exception e) { GD.PushWarning("live name tags: " + e.Message); }
    }

    public LiveNameTags() { }

    Color Rgb(string key, (float, float, float) def) { var (r, g, b) = constants.Color(key, def); return new Color(r, g, b); }

    /// <summary>The movies' draw scale (UiScale) for the window; the tags are built once the layer is in the tree.</summary>
    public override void _EnterTree()
    {
        u = UiScale.For(GetViewport().GetVisibleRect().Size);
        AddToGroup(IMovieLayer.Group);
    }

    /// <summary>
    /// <see cref="IMovieLayer"/>: the new viewport scale for the built tags and the ones to come.
    /// STILL-OPEN: the built clips keep the glyphs they were built with and are only scaled.
    /// </summary>
    public void Resize(Vector2 window)
    {
        u = UiScale.For(window);
        foreach (var t in tags.Values) t.Node.Scale = new Vector2(u, u);
    }

    /// <summary>An npc's tag (FieldAddNpc), hung from its model's Eff_UI dummy.</summary>
    public void AddNpc(Node3D root, Node3D visual, NpcRow row)
    {
        Remove(root);
        if (movie == null || row == null || !row.Nametag || string.IsNullOrEmpty(row.Name)) return;
        var color = NpcPresence.TagColor(constants, row, false);
        var node = NpcPresence.BuildTag(movie, u, row.Name, row.Title, color);
        if (node == null) return;
        var model = visual?.GetNodeOrNull<Node3D>("Model");
        Add(new Tag
        {
            Root = root, Node = node, Offset = npcOffset, Row = row, Color = color,
            Anchor = () => DummyAnchor.Point(row.EffectDummies, "Eff_UI", model, "Scene Root") ?? root.GlobalPosition,
        });
    }

    /// <summary>A hostile npc died: its tag turns NameTagColorDead (the tag stays with the body).</summary>
    public void Died(Node3D root)
    {
        if (!tags.TryGetValue(root, out var t) || t.Row == null || t.Dead) return;
        t.Dead = true;
        t.Color = NpcPresence.TagColor(constants, t.Row, true);
        NpcPresence.Recolor(t.Node, t.Color);
    }

    /// <summary>A player's tag (FieldAddUser), hung from the Eff_UI dummy of the pc table's row for its gender.</summary>
    public void AddPc(Node3D root, Node3D model, string rootName, Ms2.Net.Packets.FieldAddUser add, bool self)
    {
        Remove(root);
        if (movie == null) return;
        var user = add.User;
        string name = user.Name ?? "";
        if (add.Title != 0 && tables.TitleName(add.Title) is { Length: > 0 } title) name = title + " " + name;
        string guild = string.IsNullOrEmpty(user.GuildName) ? "" : "<" + user.GuildName + ">";
        var color = self ? myColor : pcColor;
        var node = NpcPresence.BuildTag(movie, u, name, guild, color);
        if (node == null) return;
        Symbol(node, add, guild.Length > 0);
        var dummies = EffectDummyTable.Load(packs).Pc(user.Gender);
        Add(new Tag
        {
            Root = root, Node = node, Offset = pcOffset, CharacterId = user.CharacterId, Self = self, Color = color,
            Anchor = () => DummyAnchor.Point(dummies, "Eff_UI", model, rootName) ?? root.GlobalPosition,
        });
    }

    /// <summary>
    /// setSymbol for the name tag item (FUN_141a5ff60's last branch): with the insignia shown, the symbol table's icon
    /// (GetIconRanked) as the class nametag_mark_&lt;icon&gt; in m_symbol.m_mark, its text the level for a level symbol
    /// (condition 1: the PC's level; 0xb: the adventure level); m_symbol at y -88 under a guild line, else -66. An enchant
    /// symbol (condition 2) needs the equipment's grade and is left out, as are the field-property and PvP team symbols.
    /// </summary>
    void Symbol(MovieClipNode node, Ms2.Net.Packets.FieldAddUser add, bool guildLine)
    {
        var table = tables.NametagSymbols;
        ushort id = (ushort)add.Insignia;
        if (!add.InsigniaShown || table.Get(id) is not { } sym || sym.Condition == NametagSymbols.Condition.Enchant) return;
        var player = new NametagSymbols.Player(add.User.Level, add.User.PrestigeLevel, 0, Array.Empty<uint>(), 0);
        string icon = table.IconRanked(id, player);
        var symbol = node.FindClip("m_symbol");
        var mark = symbol?.FindClip("m_mark");
        if (string.IsNullOrEmpty(icon) || mark == null) return;
        var built = movie.BuildClass("nametag_mark_" + icon, u);
        if (built == null) return;
        string level = sym.Condition switch
        {
            NametagSymbols.Condition.Level => add.User.Level.ToString(System.Globalization.CultureInfo.InvariantCulture),
            NametagSymbols.Condition.AdventureLevel => add.User.PrestigeLevel.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => "",
        };
        if (level.Length > 0) built.SetText(level);
        mark.AddChild(built);
        node.SetNamedVisible("m_symbol", GameOptions.Current?.StatusOption?.DisplayPlayerSymbol ?? true);
        symbol.Position = new Vector2(symbol.Position.X, guildLine ? -88 : -66);
    }

    void Add(Tag t)
    {
        t.Node.Visible = false;
        AddChild(t.Node);
        tags[t.Root] = t;
    }

    /// <summary>The actor left the field: its tag goes with it.</summary>
    public void Remove(Node3D root)
    {
        if (root != null && tags.Remove(root, out var t)) t.Node.QueueFree();
    }

    /// <summary>UINameTag::Entity::FUN_141a60920: the game option that shows the tag (all on without the option store).</summary>
    static bool Shown(Tag t)
    {
        if (t.Row != null) return NpcPresence.NameShown(t.Row);
        var status = GameOptions.Current?.StatusOption;
        if (status == null) return true;
        return t.Self ? status.DisplayMyName : status.DisplayFriendlyName;
    }

    public override void _Process(double delta)
    {
        camera ??= GetViewport().GetCamera3D();
        if (camera == null) return;
        field ??= GetTree().GetFirstNodeInGroup(Field.Group) as Field;
        if (Viewer == null || !IsInstanceValid(Viewer)) Viewer = GetTree().GetFirstNodeInGroup(Player.Group) as Node3D;
        var eye = Viewer != null && IsInstanceValid(Viewer) ? Viewer.GlobalPosition : camera.GlobalPosition;
        bool layer = MovieLayerHost.Layers.IsVisible(CommonDepth.NameTag);
        List<Node3D> gone = null;
        foreach (var t in tags.Values)
        {
            if (!IsInstanceValid(t.Root)) { (gone ??= new()).Add(t.Root); continue; }
            if (t.Row == null)
            {
                // PC::vfunc_53: the relation colour every frame
                var c = t.Self ? myColor : InParty != null && InParty(t.CharacterId) ? partyColor : pcColor;
                if (c != t.Color) { t.Color = c; NpcPresence.Recolor(t.Node, c); }
            }
            var head = t.Anchor();
            float d = eye.DistanceTo(t.Root.GlobalPosition);
            bool on = layer && Shown(t) && NpcPresence.InRange(d) && !camera.IsPositionBehind(head);
            t.Node.Visible = on;
            if (!on) continue;
            t.Node.Modulate = new Color(1, 1, 1, NpcPresence.TagAlpha(d));
            var p = camera.UnprojectPosition(head);
            p.Y += (t.Offset + (field == null ? 0 : PlayerNameTag.PitchShift(field, head))) * u;
            t.Node.Position = UiScale.StagePoint(p, u);
        }
        if (gone != null) foreach (var r in gone) if (tags.Remove(r, out var t)) t.Node.QueueFree();
    }
}
