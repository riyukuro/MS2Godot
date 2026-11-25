using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2Godot.Ui;
using Ms2Godot.World;

namespace Ms2Godot.Net;

/// <summary>
/// The live field's UIHpBar (the uihpbar movie, ui id 0x4e36): the head bars over battle npcs (CNpc::vfunc_80) and
/// party-member players (PC::vfunc_80), one hpbar_entity_HPBar clip per actor, the way UIHpBar::FUN_141993d30 keeps one
/// entity per actor id, drawn in screen space like the walk path's bars (NpcPresence) and the own bar (PlayerHpBar).
/// <list type="bullet">
/// <item>Who shows one (FUN_1419923f0): an npc by DisplayMonsterHpGauge (rows with friendly="2" by DisplayPartyHpGauge,
/// CNpc::vfunc_116's value), a player by DisplayPartyHpGauge — and the pc's bar is made only for a party member
/// (MyPC::IsPartyMemberByCharId). The options are off by default in the client's option ctor; without the option store
/// loaded the bars show.</item>
/// <item>When: an npc's bar is made hidden at spawn for rows with hpBar="1" (CNpc::vfunc_80) and goes active with its
/// first stat — the client activates it on the npc's battle stance (CNpc::vfunc_66), which the server never sends —
/// and is shown by the local player's damage (PCBase::vfunc_112); a property tag 2 row's is shown and filled from the
/// spawn stat (CNpc::vfunc_12). A player's bar is made on its first stance, stat or local hit, a hurt spawn activating it
/// at once (PC::vfunc_10); an hp change shows it (FUN_141992230's PC branch) and once active it stays up at full HP too
/// (the keep-alive is never cleared, as for <see cref="PlayerHpBar"/>). An npc's death drops its bar (vfunc_8's IsDead).</item>
/// <item>Place (vfunc_6 / vfunc_10): the actor's Eff_UI dummy, moved down by OffsetNpcHpbar / OffsetPcHpbar and
/// FUN_1412762d0's camera shift, in stage units at the movie scale; hidden behind the camera and past HpBarDistance
/// (Entity::vfunc_8's m_54, the actor's camera distance; 9999999 in the shipped table).</item>
/// </list>
/// Not ported: the PvP rules (HasPvPPoint, PC::vfunc_122's relation, FUN_1419923f0's hostile branch) — without a PvP
/// state every bar draws rank 1 and the friendly gauge — and the optimizer's spawn gate (FUN_14123dbd0).
/// </summary>
public sealed partial class LiveHpBars : CanvasLayer, IMovieLayer
{
    sealed class Entry
    {
        public Node3D Root; public Func<Vector3> Anchor; public float Offset;
        public NpcHpBar Npc; public NpcRow Row;          // npcs
        public PcHpBar Pc; public long CharId;           // players
        public Movement.RideData Ride;                   // players: the mount's row while riding
        public float Extra;                              // players: PCBase m_f174 (FUN_141670360)
    }

    readonly Dictionary<Node3D, Entry> bars = new();
    readonly UiMovie movie;
    readonly float npcOffset, pcOffset, distance;
    /// <summary>ReleaseEntity's free list (UIHpBar's m_78): a bar whose actor left is kept for the next one.</summary>
    readonly Stack<MovieClipNode> pool = new();
    float u = 1;
    Field field;
    Camera3D camera;
    /// <summary>Whether a character is in the local player's party (CPartyManager::IsMemberByCharId with a party), the gate
    /// FUN_1419923f0 re-reads every frame for a player's bar; on when null.</summary>
    public Func<long, bool> InParty;

    public LiveHpBars(Ms2.Formats.Pack.PackSet packs)
    {
        Name = "LiveHpBars";
        Layer = 5;                       // with the name tags and the own PC's bar
        Offset = UiScale.MovieOrigin;
        MovieLayerHost.Bind(this, Ms2.Ui.CommonDepth.HpBar);   // CUIService::HideWorldUI loads the HP bar (0x4e36) into layer 3
        var constants = FieldContents.Constants(packs);
        npcOffset = constants.Float("OffsetNpcHpbar", 5);
        pcOffset = constants.Float("OffsetPcHpbar", 32);
        distance = constants.Float("HpBarDistance", 400);
        try { movie = new UiMovie(FieldContents.Ui(packs), "uihpbar"); }
        catch (Exception e) { GD.PushWarning("live hp bars: " + e.Message); }
    }

    public LiveHpBars() { }

    /// <summary>The movies' draw scale (UiScale) for the window; the bars are built once the layer is in the tree.</summary>
    public override void _EnterTree()
    {
        u = UiScale.For(GetViewport().GetVisibleRect().Size);
        AddToGroup(IMovieLayer.Group);
    }

    /// <summary>
    /// <see cref="IMovieLayer"/>: the new viewport scale for the built bars and the ones to come.
    /// STILL-OPEN: the built clips keep the glyphs they were built with and are only scaled.
    /// </summary>
    public void Resize(Vector2 window)
    {
        u = UiScale.For(window);
        foreach (var e in bars.Values) { e.Npc?.Rescale(u); e.Pc?.Rescale(u); }
    }

    /// <summary>An option's value; on when the option store is not loaded.</summary>
    static bool On(Func<Ms2.Formats.Options.GameOption.Status, bool> read)
    {
        var status = GameOptions.Current?.StatusOption;
        return status == null || read(status);
    }

    /// <summary>DisplayPartyHpGauge: FUN_1419923f0's party option, and PC::vfunc_80's own gate for a player's bar.</summary>
    public bool PartyGauge => On(s => s.DisplayPartyHpGauge);
    /// <summary>DisplayMonsterHpGauge: FUN_1419923f0's monster option.</summary>
    public bool MonsterGauge => On(s => s.DisplayMonsterHpGauge);

    /// <summary>An npc's bar (CNpc::vfunc_80 makes it for the row's hpBar flag), hung from its Eff_UI dummy; the spawn
    /// stat fills a property tag 2 row's always-shown bar (CNpc::vfunc_12).</summary>
    public NpcHpBar AddNpc(World.FieldNpcs.Placed npc, Func<Vector3> anchor, long spawnCur, long spawnMax)
    {
        Remove(npc.Root);
        if (movie == null || npc.Row == null || !npc.Row.HpBar) return null;
        var bar = new NpcHpBar(npc, movie, u, this, spawnCur, spawnMax, pool);
        bars[npc.Root] = new Entry { Root = npc.Root, Anchor = anchor, Offset = npcOffset, Npc = bar, Row = npc.Row };
        return bar;
    }

    /// <summary>A party member's bar (PC::vfunc_80), hung from the Eff_UI dummy of the pc table's row for its gender.</summary>
    public PcHpBar AddPc(Node3D root, Func<Vector3> anchor, long characterId)
    {
        Remove(root);
        if (movie == null) return null;
        var bar = new PcHpBar(movie, u, this, pool);
        bars[root] = new Entry { Root = root, Anchor = anchor, Offset = pcOffset, Pc = bar, CharId = characterId };
        return bar;
    }

    /// <summary>The player's ride on (the mount's row) or off (null): the bar moves by the default rider's hpBarOffsetY
    /// (PCBase::FUN_141670390, pcbase.cpp:2327-2334) and hides with its nameTagHide (UIHpBar::Entity::vfunc_8,
    /// uihpbar.cpp:204-211).</summary>
    public void Ride(Node3D root, Movement.RideData ride)
    {
        if (root != null && bars.TryGetValue(root, out var e)) e.Ride = ride;
    }

    /// <summary>PCBase m_f174 (FUN_141670360) of a player's bar: added to OffsetPcHpbar (PCBase::FUN_141670390,
    /// pcbase.cpp:2327-2334).</summary>
    public void SetExtraOffset(Node3D root, float offset)
    {
        if (root != null && bars.TryGetValue(root, out var e)) e.Extra = offset;
    }

    /// <summary>The actor left the field (ReleaseEntity): its bar goes with it.</summary>
    public void Remove(Node3D root)
    {
        if (root != null && bars.Remove(root, out var e)) { e.Npc?.Release(); e.Pc?.Release(); }
    }

    /// <summary>FUN_1419923f0, which vfunc_8 re-reads every frame: the option of the actor's kind, and for a player the
    /// party too (a member who leaves loses the bar at once; one who joins gains it at its next stat, stance or hit).
    /// A friendly-2 npc row shares the party option (CNpc::vfunc_116's value).</summary>
    bool Shown(Entry e) =>
        e.Pc != null ? PartyGauge && (InParty?.Invoke(e.CharId) ?? true)
                     : e.Row != null && (e.Row.Friendly == 2 ? PartyGauge : MonsterGauge);

    public override void _Process(double delta)
    {
        camera ??= GetViewport().GetCamera3D();
        if (camera == null || bars.Count == 0) return;
        field ??= GetTree().GetFirstNodeInGroup(Field.Group) as Field;
        var eye = field?.CameraEye ?? camera.GlobalPosition;      // the camera mode's eye: the distance is measured from it
        bool layer = MovieLayerHost.Layers.IsVisible(Ms2.Ui.CommonDepth.HpBar);
        List<Node3D> gone = null;
        foreach (var e in bars.Values)
        {
            if (!IsInstanceValid(e.Root)) { (gone ??= new()).Add(e.Root); continue; }
            // the frame loop's own gates first (vfunc_15: the layer's flag, vfunc_8's option read, the projection of
            // vfunc_10), then the bar's vfunc_8 rules, the distance check last (Entity::vfunc_8's m_54)
            Vector2? p = null;
            bool far = true;
            if (layer && Shown(e) && e.Ride is not { NameTagHide: true })
            {
                var a = e.Anchor();
                if (!camera.IsPositionBehind(a))
                {
                    far = eye.DistanceTo(e.Root.GlobalPosition) > distance;   // Entity::vfunc_8: FUN_140f48e20() > m_54
                    var s = camera.UnprojectPosition(a);
                    s.Y += (e.Offset + e.Extra + (e.Ride?.HpBarOffsetY ?? 0) + (field == null ? 0 : PlayerNameTag.PitchShift(field, a))) * u;
                    p = s;
                }
            }
            e.Npc?.Update(p, far);
            e.Pc?.Update(p, far);
        }
        if (gone != null) foreach (var r in gone) Remove(r);
    }
}

/// <summary>
/// One party-member player's head bar: UIHpBar::Entity of a PC. Rank 1 (FUN_141992680 without PvP points), the
/// friendly gauge (both relation ids are 1 without PvP, FUN_141992580). An hp change shows it (FUN_141992230's PC
/// branch) and the battle stance or a hurt spawn activates it (FUN_141992180 through UpdateHpBar), whose keep-alive is
/// never cleared: once active it stays up at full HP too, as for the own PC's <see cref="PlayerHpBar"/>. vfunc_8 hides
/// it while it is not shown or dead (hp 0) — its stance and damage rules only run inside the frame loop, which an
/// inactive entity never reaches, so the keep-alive covers them — then the distance check (Entity::vfunc_8's m_54).
/// </summary>
public sealed class PcHpBar
{
    readonly HpBarClip clip;
    public bool Active, Shown, InBattle;
    long cur, max;
    bool dirty;

    public PcHpBar(UiMovie movie, float u, Node parent, Stack<MovieClipNode> pool = null) => clip = new HpBarClip(movie, u, parent, pool);

    /// <summary>The viewport scale after a resize (<see cref="IMovieLayer"/>).</summary>
    public void Rescale(float u) => clip.Rescale(u);

    /// <summary>The spawn: PC::vfunc_10 activates a hurt spawn's bar, and OnAddUser applies the packet's emotion byte
    /// with SetEmotion (scenestategame.cpp:11188, a change from the ctor's 0), which runs UpdateHpBar. Not shown.</summary>
    public void Spawn(long cur, long max, bool inBattle)
    {
        InBattle = inBattle;
        if (cur != max || inBattle) Activate(cur, max);
    }

    /// <summary>PCBase::UpdateHpBar(false) on a stance change (SetEmotion -> the battle state, pc.cpp:1253): the hp pair
    /// and the bar made active, on entering and leaving battle alike (both switch the anim set).</summary>
    public void Stance(bool battle, long cur, long max)
    {
        InBattle = battle;
        Activate(cur, max);
    }

    /// <summary>FUN_141992180: the hp pair, marked when it changed, and the bar made active (m_4c, the keep-alive m_60).</summary>
    public void Activate(long cur, long max)
    {
        dirty = dirty || this.cur != cur || this.max != max;
        this.cur = cur; this.max = max;
        Active = true;
    }

    /// <summary>FUN_141992230 (OnStat, PCBase::vfunc_74) for a PC: a change shows the bar and redraws it. The full-HP
    /// 2000 ms period only restarts the timer that is read once the keep-alive is off, which never happens here.</summary>
    public void Stat(long cur, long max)
    {
        bool changed = dirty || this.cur != cur || this.max != max;
        this.cur = cur; this.max = max;
        if (!changed) return;
        dirty = true;
        Shown = true;
    }

    /// <summary>PCBase::vfunc_112: a hit sets the shown flag only while it is off, and only for the local player's.</summary>
    public void Hit(bool byLocalPlayer)
    {
        if (!Shown) Shown = byLocalPlayer;
    }

    /// <summary>UIHpBar::Entity::vfunc_8 for a PC: not shown or dead (hp 0) hides.</summary>
    bool Hidden => !Shown || cur <= 0;

    /// <summary>One frame (UIHudEntity::vfunc_15): an inactive entity is skipped, vfunc_8 decides, vfunc_6 places at the
    /// screen point, vfunc_7 hides without one. UIHpBar::Entity::vfunc_8 (uihpbar.cpp:184) returns "show" before the base's
    /// distance check (m_54) for a PC in battle or with hp != max, so those bars are never hidden for being far.</summary>
    public void Update(Vector2? screen, bool far = false)
    {
        if (!Active) return;
        if (Hidden || far && !(InBattle || cur != max)) { clip.Hide(); return; }
        if (screen == null) { clip.Hide(); return; }
        clip.Draw(screen.Value, 1, true, cur, max, ref dirty);
    }

    /// <summary>The player left the world (ReleaseEntity).</summary>
    public void Release() => clip.Free();
}
