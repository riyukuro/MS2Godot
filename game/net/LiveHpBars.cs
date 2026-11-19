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
/// state every bar draws rank 1 and the friendly gauge — the optimizer's spawn gate (FUN_14123dbd0) and the mount's
/// tag-hiding (the ridee's m_nNameTagHide).
/// </summary>
public sealed partial class LiveHpBars : CanvasLayer
{
    sealed class Entry
    {
        public Node3D Root; public Func<Vector3> Anchor; public float Offset;
        public NpcHpBar Npc; public NpcRow Row;          // npcs
        public PcHpBar Pc;                              // players
    }

    readonly Dictionary<Node3D, Entry> bars = new();
    readonly UiMovie movie;
    readonly float npcOffset, pcOffset, distance;
    float u = 1;
    Field field;
    Camera3D camera;

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
    public override void _EnterTree() => u = UiScale.For(GetViewport().GetVisibleRect().Size);

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
        var bar = new NpcHpBar(npc, movie, u, this, spawnCur, spawnMax);
        bars[npc.Root] = new Entry { Root = npc.Root, Anchor = anchor, Offset = npcOffset, Npc = bar, Row = npc.Row };
        return bar;
    }

    /// <summary>A party member's bar (PC::vfunc_80), hung from the Eff_UI dummy of the pc table's row for its gender.</summary>
    public PcHpBar AddPc(Node3D root, Func<Vector3> anchor)
    {
        Remove(root);
        if (movie == null) return null;
        var bar = new PcHpBar(movie, u, this);
        bars[root] = new Entry { Root = root, Anchor = anchor, Offset = pcOffset, Pc = bar };
        return bar;
    }

    /// <summary>The actor left the field (ReleaseEntity): its bar goes with it.</summary>
    public void Remove(Node3D root)
    {
        if (root != null && bars.Remove(root, out var e)) { e.Npc?.Release(); e.Pc?.Release(); }
    }

    /// <summary>FUN_1419923f0: the option of the actor's kind; a friendly-2 npc shares the party option (vfunc_116).</summary>
    bool Shown(Entry e) => e.Pc != null ? PartyGauge : e.Row != null && (e.Row.Friendly == 2 ? PartyGauge : MonsterGauge);

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
            Vector2? p = null;
            if (layer && Shown(e))
            {
                var a = e.Anchor();
                // vfunc_8's m_54: the actor's distance from the camera mode's point, HpBarDistance at most
                if (!camera.IsPositionBehind(a) && eye.DistanceTo(e.Root.GlobalPosition) <= distance)
                {
                    var s = camera.UnprojectPosition(a);
                    s.Y += (e.Offset + (field == null ? 0 : PlayerNameTag.PitchShift(field, a))) * u;
                    p = s;
                }
            }
            e.Npc?.Update(p);
            e.Pc?.Update(p);
        }
        if (gone != null) foreach (var r in gone) Remove(r);
    }
}

/// <summary>
/// One party-member player's head bar: UIHpBar::Entity of a PC. Rank 1 (FUN_141992680 without PvP points), the
/// friendly gauge (both relation ids are 1 without PvP, FUN_141992580). An hp change shows it (FUN_141992230's PC
/// branch) and the battle stance or a hurt spawn activates it (FUN_141992180 through UpdateHpBar), whose keep-alive is
/// never cleared: once active it stays up at full HP too, as for the own PC's <see cref="PlayerHpBar"/>. vfunc_8 hides
/// it while it is not shown, dead (hp 0) or inactive; the stance, damage or the keep-alive shows it again.
/// </summary>
public sealed class PcHpBar
{
    readonly HpBarClip clip;
    public bool Active, Shown, InBattle;
    long cur, max;
    bool dirty;

    public PcHpBar(UiMovie movie, float u, Node parent) => clip = new HpBarClip(movie, u, parent);

    /// <summary>PC::vfunc_10: a hurt spawn activates the bar without showing it; the spawn stance is kept.</summary>
    public void Spawn(long cur, long max, bool inBattle)
    {
        InBattle = inBattle;
        if (cur != max) Activate(cur, max);
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

    /// <summary>UIHpBar::Entity::vfunc_8 for a PC: not shown or dead (hp 0) hides; the battle stance, damage or the
    /// keep-alive shows.</summary>
    bool Hidden => !Shown || cur <= 0 || !(InBattle || cur != max || Active);

    /// <summary>One frame (UIHudEntity::vfunc_15): an inactive entity is skipped, vfunc_8 decides, vfunc_6 places at the
    /// screen point, vfunc_7 hides without one.</summary>
    public void Update(Vector2? screen)
    {
        if (!Active) return;
        if (screen == null || Hidden) { clip.Hide(); return; }
        clip.Draw(screen.Value, 1, true, cur, max, ref dirty);
    }

    /// <summary>The player left the world (ReleaseEntity).</summary>
    public void Release() => clip.Free();
}
