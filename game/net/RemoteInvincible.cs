using Godot;
using Ms2.Ui.Hud;

namespace Ms2Godot.Net;

/// <summary>
/// PC's invincible time on another player (PC::FUN_141661370 and FUN_1416615f0, pc.cpp:3384-3431 and :2612-2624). The client
/// runs both for every PC: OnRevival (scenestategame.cpp:7389-7409) starts it on whichever PC the packet names, and PC's update
/// (pc.cpp:4468) runs the blink for each PC. The custom buff icon is MyPC's only (the DynamicCast at pc.cpp:3386), so a remote
/// gets the times, the kept effect (m_strInvincibleEffect at the actor, m_n1060) and the blink's SetNodeAlpha on its tree.
/// OnInvincibleEffect (scenestategame.cpp:4280-4286) starts it on the MyPC only.
/// </summary>
public sealed class RemoteInvincible
{
    public readonly InvincibleBlink Blink = new();
    /// <summary>m_n1060: the kept effect's handle, 0 when none.</summary>
    public int Effect;

    /// <summary>FUN_141661370(1, <paramref name="durationMs"/>, <paramref name="timerId"/>): the times start now and the effect
    /// spawns at the actor. A restart overwrites the handle and leaves the old effect running, as the m_n1060 store does
    /// (pc.cpp:3420).</summary>
    public void Start(int durationMs, int timerId, uint now, Node3D actor, World.RegionSkills fx, string effect)
    {
        Blink.Start(durationMs, timerId, now);
        Effect = fx != null && GodotObject.IsInstanceValid(fx) && !string.IsNullOrEmpty(effect) ? fx.SpawnOn(effect, actor, Ms2.Audio.SoundMusicEffectState.CasterSystem) : 0;   // kind 0 (pc.cpp:3419)
    }

    /// <summary>FUN_1416615f0 each frame: the blink's alpha on <paramref name="tree"/>; past the end FUN_141661370(0, 0, 0)
    /// (the effect goes) and vfunc_42(1.0, true).</summary>
    public void Tick(uint now, Node tree, World.RegionSkills fx)
    {
        if (Tick(now, tree)) End(tree, fx);
    }

    /// <summary>The blink frame alone: true past the end, where the caller runs <see cref="End"/> (so the effect layer is
    /// looked up only on that frame).</summary>
    public bool Tick(uint now, Node tree)
    {
        if (!Blink.Active || !Blink.Tick(now, out float alpha, out bool ended)) return false;
        if (ended) return true;
        if (GodotObject.IsInstanceValid(tree)) NodeFade.Set(tree, alpha, false);
        return false;
    }

    /// <summary>FUN_141661370(0, 0, 0) and vfunc_42(1.0, true). Also called when the PC leaves while invincible: ~PC frees
    /// the kept effect m_n1060 (pc.cpp:764-768), so it does not outlive the body it follows.</summary>
    public void End(Node tree, World.RegionSkills fx)
    {
        Blink.Stop();
        if (Effect != 0 && fx != null && GodotObject.IsInstanceValid(fx)) fx.FadeOut(Effect, 0);
        Effect = 0;
        if (tree != null && GodotObject.IsInstanceValid(tree)) NodeFade.Set(tree, 1f, true);
    }
}
