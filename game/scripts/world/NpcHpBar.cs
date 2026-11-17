using Godot;
using Ms2.Formats.World;
using Ms2Godot.Ui;

namespace Ms2Godot.World;

/// <summary>
/// An npc's head HP bar (UIHpBar::Entity, the uihpbar movie's hpbar_entity_HPBar), for rows with hpBar="1".
/// <list type="bullet">
/// <item>Active (m_4c) once the npc enters battle stance (CNpc::vfunc_66 with a stance, FUN_141992180), or from spawn
/// for property tag 2 always_display_hp_bar, which is also shown from spawn. Leaving the stance changes nothing: an npc
/// bar has no time-out (the full-HP 2000 ms period of FUN_141992230 is set for PCs only, and hides nothing, see
/// <see cref="Ms2Godot.Ui.PlayerHpBar"/>), it goes only with the npc's death or removal, or while its anchor is behind the
/// camera.</item>
/// <item>Shown (m_b8) once the local player damages the npc (PCBase::vfunc_112); others' hits never show it.</item>
/// <item>Drawn by <see cref="HpBarClip"/>: rank = the row's class (10 with big_hp_bar), label friendly for rows with
/// friendly 1, else hostile.</item>
/// <item>Place: the npc's Eff_UI point on screen, plus OffsetNpcHpbar and the camera shift in stage units, the same
/// anchor and shift as the npc's name tag (<see cref="NpcPresence"/> places both).</item>
/// </list>
/// </summary>
public sealed class NpcHpBar
{
    public readonly FieldNpcs.Placed Npc;
    public bool Active, Shown, Dead;
    long cur, max;
    bool dirty;
    readonly HpBarClip clip;

    public NpcHpBar(FieldNpcs.Placed npc, UiMovie movie, float u, Node parent)
    {
        Npc = npc;
        clip = new HpBarClip(movie, u, parent);
        if (npc.Row.AlwaysDisplayHpBar) { Shown = true; Activate(cur, max); }
    }

    /// <summary>The rank of the bar (FUN_141992680): the row's class, 10 with big_hp_bar.</summary>
    public int Rank => Npc.Row.BigHpBar ? 10 : Npc.Row.Class;

    /// <summary>FUN_141992180: the HP, changed or not, and the bar made active.</summary>
    public void Activate(long cur, long max)
    {
        dirty = dirty || this.cur != cur || this.max != max;
        this.cur = cur; this.max = max;
        Active = true;
    }

    /// <summary>FUN_141992230 (OnStat, vfunc_74) for an npc: the HP; a change while active is drawn on the next frame.</summary>
    public void Stat(long cur, long max)
    {
        bool changed = dirty || this.cur != cur || this.max != max;
        this.cur = cur; this.max = max;
        if (Active && changed) dirty = true;
    }

    /// <summary>One frame (UIHudEntity::vfunc_15) with the anchor's screen point, or null when it cannot be projected.</summary>
    public void Update(Vector2? screen)
    {
        if (!Active) return;
        if (screen == null || !Shown || Dead) { clip.Hide(); return; }
        clip.Draw(screen.Value, Rank, Npc.Row.Friendly == 1, cur, max, ref dirty);
    }

    /// <summary>The npc left the world (ReleaseEntity).</summary>
    public void Release()
    {
        clip.Free();
        Active = Shown = false;
    }
}
