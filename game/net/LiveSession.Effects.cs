using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2Godot.World;

namespace Ms2Godot.Net;

// The combat effects of the scene: InvincibleEffect 0x83, OneTimeEffect 0xF1, TimeScale 0xF3, and the own player's invincible
// time after a revival.
public partial class LiveSession
{
    /// <summary>The field's OnOneTimeEffect messages in order, for a <see cref="RegionSkills"/> node made after them; cleared on
    /// a new field.</summary>
    public readonly List<OneTimeEffect> OneTimeEffectLog = new();
    /// <summary>An OnOneTimeEffect message of the current field.</summary>
    public event Action<OneTimeEffect> OneTimeEffectReceived;
    /// <summary>WorldService's time scale change; its scale drives Engine.TimeScale.</summary>
    public readonly TimeScaleRamp TimeScaleRamp = new();
    /// <summary>The own player's invincible time (PC m_n1054 .. m_b1070 of MyPC).</summary>
    public readonly InvincibleBlink Invincible = new();
    /// <summary>The own player's invincible time started (true: FUN_141661370(1, ..) spawns the effect again, the old handle
    /// overwritten) or ended (false: the effect removed).</summary>
    public event Action<bool> InvincibleChanged;
    /// <summary>The own player's node, once the field is built.</summary>
    public Player OwnPlayer => player;

    /// <summary>GetTickCountMs2: the real clock, not the scaled game clock.</summary>
    static uint TickMs => (uint)Time.GetTicksMsec();

    static ClientConstants Constants()
    {
        try { return FieldContents.Constants(PackSet.Shared(Settings.DataDir)); } catch { return null; }
    }

    void ClearFieldEffects() => OneTimeEffectLog.Clear();

    bool HandleCombatEffect(Ms2.Net.Wire.RawPacket p)
    {
        if (game.Is(p, Inbound.InvincibleEffect))
        {
            // OnInvincibleEffect @0x1417316d0 (scenestategame.cpp:4280): no body; MyPC's FUN_141661370(1,
            // UserPortalInvincibleTick, 100000005)
            if (selfObject >= 0) StartInvincible(InvincibleBlink.PortalTimer);
        }
        else if (game.Is(p, Inbound.OneTimeEffect))
        {
            // OnOneTimeEffect @0x14173a420 (scenestategame.cpp:8453): CTriggerService::FUN_141863630; the field's RegionSkills
            // node keeps the handles
            var m = OneTimeEffect.Read(p.Reader());
            OneTimeEffectLog.Add(m);
            OneTimeEffectReceived?.Invoke(m);
        }
        else if (game.Is(p, Inbound.TimeScale))
        {
            // OnTimeScale @0x14173a5e0 (scenestategame.cpp:8465): StartTimeScale or FUN_141bd9380
            var t = TimeScale.Read(p.Reader());
            bool set = t.Start ? TimeScaleRamp.Start(t.From, t.To, t.DurationMs, t.Curve, TickMs) : TimeScaleRamp.Stop();
            if (set) Engine.TimeScale = TimeScaleRamp.Scale;
        }
        else return false;
        return true;
    }

    /// <summary>PC::FUN_141661370(1, the timer's constant, timerId) of the own player.
    /// STILL-OPEN: MyPC's custom buff icon (UIMainPlayerInfo AddCustomBuffEffect / RemoveCustomBuffEffect, ending at
    /// <see cref="InvincibleBlink.BuffEnd"/>) has no buff bar here.</summary>
    void StartInvincible(int timerId)
    {
        Invincible.Start(InvincibleBlink.TickFor(timerId, Constants()), timerId, TickMs);
        InvincibleChanged?.Invoke(true);
    }

    /// <summary>The per-frame parts: the world tick's time scale ramp (worldservice_1.cpp:9883) and PC::FUN_1416615f0's
    /// invincible end and blink.
    /// STILL-OPEN: the blink alpha (vfunc_42, GameObject SetNodeAlpha: 0.5 / 0.9, then 1.0 at the end) has no character alpha
    /// path in the character shader, so it is not drawn. Engine.TimeScale also scales the UI's timers, which CMainSystem's
    /// game clock does not.</summary>
    void TickCombatEffects()
    {
        uint now = TickMs;
        if (TimeScaleRamp.Running && TimeScaleRamp.Tick(now)) Engine.TimeScale = TimeScaleRamp.Scale;
        if (Invincible.Active && Invincible.Tick(now, out _, out bool ended) && ended) InvincibleChanged?.Invoke(false);
    }

    /// <summary>The game link went: a running time scale change ends with it.</summary>
    void StopCombatEffects()
    {
        if (TimeScaleRamp.Stop()) Engine.TimeScale = 1;
    }
}
