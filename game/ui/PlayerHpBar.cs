using Godot;
using Ms2.Formats.World;

namespace Ms2Godot.Ui;

/// <summary>
/// The own PC's head HP bar (UIHpBar::Entity from MyPC::vfunc_80, made when the hp-bar game option is on).
/// <list type="bullet">
/// <item>Active (m_4c, and the TimerEntity keep-alive m_60) through PCBase::UpdateHpBar = FUN_141992180 with the current
/// HP: on spawn when hurt (PC::vfunc_10) and on every battle-stance change (PC's stance setter); never on a dead PC.</item>
/// <item>Shown (m_b8): FUN_141992230 (OnStat) for a PC sets it on any HP change; PCBase::vfunc_112 sets it on a hit by the
/// local player while it is not set.</item>
/// <item>Hidden (vfunc_8) when not shown or dead; in battle stance or below full HP it shows; otherwise TimerEntity::vfunc_8
/// hides only with the keep-alive off and its timer stopped. OnStat sets the timer period m_64 to 2000 ms at full HP (0
/// below), and each redraw restarts the timer with it, but only TimerEntity::vfunc_3 clears the keep-alive, and nothing
/// calls it on an HP bar: once active, the bar stays up at full HP too. Only code outside the decompiled source calling
/// TimerEntity::vfunc_3 on the PC HP bar could hide it there.</item>
/// <item>Rank 1 (100 with PvP points, not here), label friendly (the own side); width (int)(cur / max x 39), no tween.</item>
/// <item>Place: the Eff_UI point the own name tag hangs from (<see cref="PlayerNameTag.Anchor"/>), plus vfunc_9 =
/// OffsetPcHpbar (m_f164) + m_f174 (0) + the mount's offset (none) down, plus FUN_1412762d0, in stage units.</item>
/// </list>
/// </summary>
public sealed partial class PlayerHpBar : CanvasLayer
{
    readonly HpBarClip clip;
    readonly PlayerNameTag tag;
    readonly float offset, u = 1;
    long cur, max;
    bool active, keepAlive, shown, dead, battle, dirty;
    int period;                         // m_64: the timer period OnStat sets
    double timerLeft;                   // m_68, ms; read only while the keep-alive is off
    Camera3D camera;

    public PlayerHpBar(UiAssets ui, ClientConstants constants, PlayerNameTag tag, Vector2 window)
    {
        Name = "PlayerHpBar";
        Layer = 5;                       // with the name tags; added after the own tag
        MovieLayerHost.Bind(this, Ms2.Ui.CommonDepth.HpBar);     // CUIService::HideWorldUI loads the HP bar (0x4e36) into layer 3
        Offset = UiScale.MovieOrigin;
        this.tag = tag;
        offset = constants.Float("OffsetPcHpbar", 32);
        u = UiScale.For(window);
        UiMovie movie = null;
        try { movie = new UiMovie(ui, "uihpbar"); } catch (Exception e) { GD.PushWarning("player hp bar movie: " + e.Message); }
        clip = new HpBarClip(movie, u, this);
    }

    public PlayerHpBar() { }

    /// <summary>The field was swapped under the HUD (HudScene.SwapField): the new field's camera is found again.</summary>
    public void FieldChanged() => camera = null;

    /// <summary>PC::vfunc_10: on spawn, a hurt PC's bar is made active (not shown).</summary>
    public void Spawn(long cur, long max)
    {
        this.cur = cur; this.max = max;
        if (cur != max) UpdateHpBar();
    }

    /// <summary>The PC's battle stance changed: UpdateHpBar(false).</summary>
    public void Stance(bool battle)
    {
        this.battle = battle;
        UpdateHpBar();
    }

    /// <summary>FUN_141992230 (OnStat, PCBase::vfunc_74) for a PC.</summary>
    public void Stat(long cur, long max)
    {
        bool changed = dirty || this.cur != cur || this.max != max;
        this.cur = cur; this.max = max;
        if (active && changed) dirty = true;
        if (!changed) return;
        dirty = true;
        shown = true;
        period = cur == max ? 2000 : 0;
    }

    /// <summary>PCBase::vfunc_112: a hit; one by the local player shows the bar.</summary>
    public void Hit(bool byLocalPlayer)
    {
        if (!shown) shown = byLocalPlayer;
    }

    public void Died(bool isDead) => dead = isDead;

    /// <summary>PCBase::UpdateHpBar: FUN_141992180 with the current HP, unless dead.</summary>
    void UpdateHpBar()
    {
        if (dead) return;
        keepAlive = true;                // FUN_140f49470: m_60 = 1
        active = true;                   // vfunc_2: m_4c = 1, and the timer started with m_64
        timerLeft = period;
    }

    /// <summary>UIHpBar::Entity::vfunc_8 for the own PC: true hides.</summary>
    bool Hidden()
    {
        if (!shown || dead) return true;
        if (battle || cur != max) return false;
        return !keepAlive && timerLeft <= 0;          // TimerEntity::vfunc_8; the keep-alive stays on once active
    }

    public override void _Process(double delta)
    {
        if (!active) return;
        if (timerLeft > 0) timerLeft -= delta * 1000;
        camera ??= GetViewport().GetCamera3D();
        if (camera == null || tag?.Anchor is not { } anchor || camera.IsPositionBehind(anchor) || Hidden()) { clip.Hide(); return; }
        var p = camera.UnprojectPosition(anchor);
        p.Y += (offset + tag.HeightAdjust(anchor)) * u;
        if (dirty && period > 0) timerLeft = period;   // vfunc_6 restarts the timer when it redraws the width
        clip.Draw(p, 1, true, cur, max, ref dirty);    // SetPositionFloat: the origin is not snapped to whole pixels
    }
}
