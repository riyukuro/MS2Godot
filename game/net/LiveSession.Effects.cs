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
    /// <summary>The own player's node was bound (<see cref="OwnPlayer"/> set).</summary>
    public event Action OwnPlayerBound;
    /// <summary>FUN_141661370's custom buff icon of MyPC (pc.cpp:3388-3406): the running timer id to remove (0 = none), the new
    /// timer id (0 on the end) and its end server tick (0 = no icon).</summary>
    public event Action<int, int, int> InvincibleBuffChanged;
    /// <summary>The running timer's icon end tick, for a HUD made after the start.</summary>
    public int InvincibleBuffEnd { get; private set; }
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
            LogOneTime(m);
            OneTimeEffectReceived?.Invoke(m);
        }
        else if (game.Is(p, Inbound.TimeScale))
        {
            // OnTimeScale @0x14173a5e0 (scenestategame.cpp:8465): StartTimeScale or FUN_141bd9380
            var t = TimeScale.Read(p.Reader());
            bool set = t.Start ? TimeScaleRamp.Start(t.From, t.To, t.DurationMs, t.Curve, TickMs) : TimeScaleRamp.Stop();
            if (set) GameClock.SetScale(TimeScaleRamp.Scale);
        }
        else return false;
        return true;
    }

    /// <summary>PC::FUN_141661370(1, the timer's constant, timerId) of the own player: the old timer's icon removed and the new
    /// one's added (ending at <see cref="InvincibleBlink.BuffEnd"/>), then the times and the effect.</summary>
    void StartInvincible(int timerId)
    {
        var c = Constants();
        int old = Invincible.TimerId;
        InvincibleBuffEnd = InvincibleBlink.BuffEnd(timerId, ServerTick, c);
        InvincibleBuffChanged?.Invoke(old, timerId, InvincibleBuffEnd);
        Invincible.Start(InvincibleBlink.TickFor(timerId, c), timerId, TickMs);
        InvincibleChanged?.Invoke(true);
    }

    /// <summary>The field's one-time effect log keeps what a node made now has to replay: a remove leaves no effect of its id
    /// (CTriggerService drops the handle), so it drops the id's logged adds and is not kept itself.</summary>
    void LogOneTime(OneTimeEffect m)
    {
        if (m.Add) { OneTimeEffectLog.Add(m); return; }
        for (int i = OneTimeEffectLog.Count - 1; i >= 0; i--)
            if (OneTimeEffectLog[i].Id == m.Id) OneTimeEffectLog.RemoveAt(i);
    }

    /// <summary>The world tick's time scale ramp (worldservice_1.cpp:9883), each frame whether or not a game link is up, so a
    /// ramp the game started still ends after the link went.</summary>
    void TickTimeScale()
    {
        if (TimeScaleRamp.Running && TimeScaleRamp.Tick(TickMs)) GameClock.SetScale(TimeScaleRamp.Scale);
    }

    /// <summary>PC::FUN_1416615f0's invincible end and blink, each frame of the game.
    /// STILL-OPEN: the blink alpha (vfunc_42, GameObject SetNodeAlpha: 0.5 / 0.9, then 1.0 at the end) has no character alpha
    /// path in the character shader, so it is not drawn.</summary>
    void TickCombatEffects()
    {
        uint now = TickMs;
        int timer = Invincible.TimerId;
        if (Invincible.Active && Invincible.Tick(now, out _, out bool ended) && ended)
        {
            // FUN_141661370(0, 0, 0): the running timer's icon removed, then the effect
            InvincibleBuffEnd = 0;
            InvincibleBuffChanged?.Invoke(timer, 0, 0);
            InvincibleChanged?.Invoke(false);
        }
    }

    /// <summary>The game went (the link lost, back to the login server, the quit, the session's end): a running time scale
    /// change ends with it, as the game scene state's WorldService does.</summary>
    void StopCombatEffects()
    {
        if (TimeScaleRamp.Stop()) GameClock.SetScale(1);
    }
}
