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
/// pc.cpp:4455-4466, <see cref="Ms2.Ui.Hud.PcTagColors.Of"/>): NameTagColorPC, NameTagColorMyPC for the local player,
/// NameTagColorPCHostile for a player the local player's vfunc_122 finds hostile (<see cref="Hostile"/>), NameTagColorParty for
/// a member of the local player's party. Not ported: the enchant symbol (FUN_141661e40, the equipment's grade).</item>
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
        public float Extra;                           // a pc's m_f170 (the emotion states' offsetNameTag)
        public NpcRow Row; public bool Dead;          // npcs
        public bool Hidden;                           // npcs: the actor's hidden flag (m_unk_b0, its hide reasons)
        public long CharacterId; public bool Self;    // players
        public int ObjectId;                          // players: the FieldAddUser object id
        public Color Color;
        public string Title = "", Name = "";           // players: the title prefix ("{title} ") and PC::vfunc_77's name
        public Movement.RideData Ride;                 // players: the mount's row while riding
        public ushort Insignia; public bool InsigniaShown;   // players: PC m_n10f8 / m_n10fc (FUN_141661e00 / FUN_141661e20)
        public int Level, PrestigeLevel; public bool GuildLine;
        public byte Team, PvpMode; public int PvpPoint;      // players: m_unk_af8 and the PvP info (FieldAddUser)
        public readonly NameTagSymbolLine Symbol = new();   // players: m_symbol
    }

    readonly Dictionary<Node3D, Tag> tags = new();
    readonly ClientConstants constants;
    readonly PackSet packs;
    readonly UiMovie movie;
    readonly GameTables tables;
    readonly float npcOffset, pcOffset;
    readonly Color pcColor, myColor, partyColor, hostileColor;
    float u = 1;
    Field field;
    Camera3D camera;
    /// <summary>The one the fade distance is measured from: the local player (found by its group when not set), else the camera.</summary>
    public Node3D Viewer;
    /// <summary>Whether a character is in the local player's party (CPartyManager::IsMemberByCharId with a party).</summary>
    public Func<long, bool> InParty;
    /// <summary>Whether the local player's PC::vfunc_122 finds the player of an object id hostile (pc.cpp:4461).</summary>
    public Func<int, bool> Hostile;
    public int Count => tags.Count;
    /// <summary>The tag colour of an actor root now (the live run's view); null without a tag.</summary>
    public Color? ColorOf(Node3D root) => tags.TryGetValue(root, out var t) ? t.Color : null;
    /// <summary>NameTagColorPCHostile as read, for checks.</summary>
    public Color HostileColor => hostileColor;

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
        var (hr, hg, hb) = constants.Color("NameTagColorPCHostile", (pcColor.R, pcColor.G, pcColor.B));   // no key: the plain colour
        hostileColor = new Color(hr, hg, hb);
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

    /// <summary>The npc's hidden flag (GameObject::FUN_14123c660, m_unk_b0): UIHudEntity::Entity::vfunc_8
    /// (uihudentity.cpp:200-210) hides the tag of a hidden actor that rides nothing.</summary>
    public void SetHidden(Node3D root, bool hidden)
    {
        if (tags.TryGetValue(root, out var t)) t.Hidden = hidden;
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
        string name = user.Name ?? "", prefix = "";
        if (add.Title != 0 && tables.TitleName(add.Title) is { Length: > 0 } title) prefix = title + " ";
        string guild = GuildText(user.GuildName);
        var color = self ? myColor : pcColor;
        var node = NpcPresence.BuildTag(movie, u, prefix + name, guild, color);
        if (node == null) return;
        var dummies = EffectDummyTable.Load(packs).Pc(user.Gender);
        var t = new Tag
        {
            Root = root, Node = node, Offset = pcOffset, CharacterId = user.CharacterId, ObjectId = add.ObjectId, Self = self, Color = color,
            Title = prefix, Name = name,
            Insignia = (ushort)add.Insignia, InsigniaShown = add.InsigniaShown, Level = user.Level, PrestigeLevel = user.PrestigeLevel,
            GuildLine = guild.Length > 0,
            Team = add.Team, PvpPoint = add.PvpPoint, PvpMode = add.PvpMode,
            Anchor = () => DummyAnchor.Point(dummies, "Eff_UI", model, rootName) ?? root.GlobalPosition,
        };
        Symbol(t);
        Add(t);
    }

    /// <summary>
    /// The Insignia packet (CSceneStateGame::OnNametag, scenestategame.cpp:7314-7331): PC::FUN_141661dc0 sets the player's
    /// symbol and shown flag, then FUN_141a60f00 has UINameTag refresh that actor's entity (UIHudEntity::FUN_140f49cb0 ->
    /// Entity::FUN_140f48d80 -> FUN_141a5ff60, setData again), so the symbol line is built anew. False when the player has no tag.
    /// </summary>
    public bool SetSymbol(Node3D root, ushort symbol, bool shown)
    {
        if (root == null || !tags.TryGetValue(root, out var t) || t.Row != null) return false;
        t.Insignia = symbol; t.InsigniaShown = shown;
        Symbol(t);
        return true;
    }

    /// <summary>
    /// Another PC's level or adventure level changed (OnLevelUp -> PC::vfunc_131, OnPrestige kind 2 -> vfunc_140, pc.cpp): each
    /// refreshes its UINameTag entity (FUN_141a60f00), so a level symbol's text follows. False when the player has no tag.
    /// </summary>
    public bool SetLevels(Node3D root, int level, int adventureLevel)
    {
        if (root == null || !tags.TryGetValue(root, out var t) || t.Row != null) return false;
        t.Level = level; t.PrestigeLevel = adventureLevel;
        Symbol(t);
        return true;
    }

    /// <summary>
    /// A player's guild name changed (PC::vfunc_129, pc.cpp:1511: m_strNameDC8, then FUN_141a60f00 refreshes its UINameTag
    /// entity, setData again): the guild line "&lt;guild&gt;" above the name, empty for none, and the symbol line, which sits
    /// higher with a guild line. False when the player has no tag.
    /// </summary>
    public bool SetGuild(Node3D root, string guildName)
    {
        if (root == null || !tags.TryGetValue(root, out var t) || t.Row != null) return false;
        string guild = GuildText(guildName);
        NpcPresence.SetLabel(t.Node.FindClip("m_guild"), guild, null, t.Color);
        t.GuildLine = guild.Length > 0;
        Symbol(t);
        return true;
    }

    /// <summary>setData's guild line (UINameTag::Entity::FUN_141a5ff60): StringFormat("&lt;{0}&gt;", GetGuildName()), "" for none.</summary>
    internal static string GuildText(string guildName) => string.IsNullOrEmpty(guildName) ? "" : "<" + guildName + ">";

    /// <summary>The text of a tag's guild line (the test's view), null without a tag.</summary>
    public string ShownGuild(Node3D root) =>
        root != null && tags.TryGetValue(root, out var t) ? LineText(t.Node.FindClip("m_guild")) : null;

    /// <summary>The first text field's text under a tag line, "" with none.</summary>
    internal static string LineText(MovieClipNode line)
    {
        foreach (var c in line?.AllClips() ?? [])
            foreach (var f in c.TextFields) return f.Text ?? "";
        return "";
    }

    /// <summary>The field's property 5 symbols (team 1, team 2), null without one: the field properties' view.</summary>
    public Func<(string Team1, string Team2)?> FieldSymbol = () => null;

    /// <summary>Field property 5 applied (WorldService::FUN_141bcabf0): every player's tag sets its symbol line again.</summary>
    public void RefreshFieldSymbols()
    {
        foreach (var t in tags.Values)
            if (t.Row == null) Symbol(t);
    }

    /// <summary>The mark class a player's symbol line shows (the test's view), null for none.</summary>
    public string ShownMark(Node3D root) => root != null && tags.TryGetValue(root, out var t) ? t.Symbol.ShownMark(t.Node) : null;

    /// <summary>The players' symbol lines set again when the Display Player Symbol option changed since they were set.</summary>
    public void RefreshSymbols()
    {
        foreach (var t in tags.Values)
            if (t.Row == null && t.Symbol.OptionChanged) t.Symbol.Refresh(t.Node, movie, u, tables.NametagSymbols);
    }

    /// <summary>The levels a player's symbol line was last set with; (-1, -1) without a tag. The test's view.</summary>
    public (int Level, int AdventureLevel) SymbolLevels(Node3D root) =>
        root != null && tags.TryGetValue(root, out var t) ? t.Symbol.SetLevels : (-1, -1);

    /// <summary>The symbol the player's tag shows (the test's view); 0 when none or no tag.</summary>
    public ushort ShownSymbol(Node3D root) => root != null && tags.TryGetValue(root, out var t) ? t.Symbol.Shown(t.Node) : (ushort)0;

    /// <summary>The marks under the tag's m_symbol.m_mark (the test's view; setSymbol keeps one at most).</summary>
    public int MarkCount(Node3D root) =>
        root != null && tags.TryGetValue(root, out var t) && t.Node.FindClip("m_symbol")?.FindClip("m_mark") is { } mark ? mark.GetChildCount() : -1;

    /// <summary>The tag's symbol line (<see cref="NameTagSymbolLine"/>).</summary>
    void Symbol(Tag t) =>
        t.Symbol.Set(t.Node, movie, u, tables.NametagSymbols, t.Insignia, t.InsigniaShown, t.Level, t.PrestigeLevel, t.GuildLine,
                     NameTagSymbolLine.FieldOrTeam(FieldSymbol(), t.Team, t.PvpPoint, t.PvpMode));

    void Add(Tag t)
    {
        t.Node.Visible = false;
        AddChild(t.Node);
        tags[t.Root] = t;
    }

    /// <summary>
    /// The player's ride on (its mount's row) or off (null): PCBase::vfunc_120 / vfunc_121 refresh the tag
    /// (vfunc_82()->FUN_140f48d80, pcbase.cpp:3335-3339), whose name is PC::vfunc_77, the mount's name (string table 0x31)
    /// for a row with showRideeNameTag (pc.cpp:1528-1538). While riding the tag moves by the default rider's nameTagOffsetY
    /// (PCBase::vfunc_89, pcbase.cpp:2339-2346) and hides with its nameTagHide (UINameTag::Entity::vfunc_8,
    /// uinametag.cpp:327-333).
    /// </summary>
    public void Ride(Node3D root, Movement.RideData ride)
    {
        if (root == null || !tags.TryGetValue(root, out var t) || t.Row != null) return;
        t.Ride = ride;
        string name = ride is { ShowRideeNameTag: true } ? tables.StringTableText(0x31, ride.Id) : t.Name;
        if (t.Node.FindClip("m_name") is { } clip) NpcPresence.SetLabel(clip, t.Title + name, null, t.Color);
    }

    /// <summary>PCBase m_f170 (FUN_141670330) of a player's tag: added to OffsetPcNametag (PCBase::vfunc_89).</summary>
    public void SetExtraOffset(Node3D root, float offset)
    {
        if (root != null && tags.TryGetValue(root, out var t)) t.Extra = offset;
    }

    /// <summary>A scripted live run's line: the tag's emotion offset (m_f170) and its last screen point.</summary>
    public string WatchLine(Node3D root) =>
        root != null && tags.TryGetValue(root, out var t) ? $"extra {t.Extra} at {t.Node?.Position.ToString() ?? "-"} shown {t.Node?.Visible}" : "none";

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
        RefreshSymbols();
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
                var c = Ms2.Ui.Hud.PcTagColors.Of(t.Self, !t.Self && Hostile != null && Hostile(t.ObjectId),
                                                  !t.Self && InParty != null && InParty(t.CharacterId)) switch
                {
                    Ms2.Ui.Hud.PcTagColor.MyPc => myColor,
                    Ms2.Ui.Hud.PcTagColor.Hostile => hostileColor,
                    Ms2.Ui.Hud.PcTagColor.Party => partyColor,
                    _ => pcColor,
                };
                if (c != t.Color) { t.Color = c; NpcPresence.Recolor(t.Node, c); }
            }
            var head = t.Anchor();
            float d = eye.DistanceTo(t.Root.GlobalPosition);
            bool on = layer && !t.Hidden && Shown(t) && t.Ride is not { NameTagHide: true } && NpcPresence.InRange(d) && !camera.IsPositionBehind(head);
            t.Node.Visible = on;
            if (!on) continue;
            t.Node.Modulate = new Color(1, 1, 1, NpcPresence.TagAlpha(d));
            var p = camera.UnprojectPosition(head);
            p.Y += (t.Offset + t.Extra + (t.Ride?.NameTagOffsetY ?? 0) + (field == null ? 0 : PlayerNameTag.PitchShift(field, head))) * u;
            t.Node.Position = UiScale.StagePoint(p, u);
        }
        if (gone != null) foreach (var r in gone) if (tags.Remove(r, out var t)) t.Node.QueueFree();
    }
}
