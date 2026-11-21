using System;
using System.Collections.Generic;
using Ms2.Formats.Model;
using Ms2.Formats.World;

namespace Ms2.Ui.Hud;

/// <summary>
/// WorldService's game time scale change (OnTimeScale 0xF3): StartTimeScale @0x141bd92c0 (worldservice_1.cpp:8430),
/// FUN_141bd9380 (:8445, the end) and the ramp of the world tick (:9883-9903). The fields are m_9b8 (the end tick, 0 when
/// none runs), m_9c8 (the start tick), m_9c0 / m_9c4 (the scales at the start and the end) and m_9bc (the curve). Each method
/// returns true when the client calls CMainSystem::SetTimeScale or ResetTimeScale, the new scale then in <see cref="Scale"/>.
/// Ticks are GetTickCountMs2 (unsigned ms, the real clock, not the scaled one).
/// </summary>
public sealed class TimeScaleRamp
{
    uint endTick, startTick;
    float from = 1f, to = 1f;
    int curve;

    /// <summary>The game clock's scale the last call set (CMainSystem's m_fTimeScale after ResetTimeScale is 1).</summary>
    public float Scale { get; private set; } = 1f;
    /// <summary>A change runs (m_9b8 &gt; 0).</summary>
    public bool Running => endTick != 0;

    /// <summary>StartTimeScale: nothing while one runs; otherwise the end is now + (int)duration (FUN_14028d260) and the
    /// clock goes to the start scale at once.</summary>
    public bool Start(float startScale, float endScale, float durationMs, int easing, uint now)
    {
        if (endTick > 0) return false;
        from = startScale; to = endScale; curve = easing;
        endTick = now + (uint)(int)durationMs;
        startTick = now;
        Scale = startScale;
        return true;
    }

    /// <summary>FUN_141bd9380: when one runs, ResetTimeScale (scale 1) and every field back to its rest value.</summary>
    public bool Stop()
    {
        if (endTick == 0) return false;
        Clear();
        return true;
    }

    /// <summary>The world tick's part: past the end, reset; else with a curve the scale between the two by the eased
    /// fraction of the time gone (curve 0 keeps the start scale until the end).</summary>
    public bool Tick(uint now)
    {
        if (endTick == 0) return false;
        if (now >= endTick) { Clear(); return true; }
        if (curve == 0) return false;
        float elapsed = now - startTick;
        float duration = endTick - startTick;
        float k = Ease(elapsed / duration, curve);
        Scale = to * k + (1f - k) * from;
        return true;
    }

    void Clear()
    {
        endTick = 0; startTick = 0; from = 1f; to = 1f; curve = 0;
        Scale = 1f;
    }

    /// <summary>FUN_141b6fdb0 @0x141b6fdb0 (util.cpp:768): curve 2 log(t*10+1)/2.397895, curve 3 (exp(t*5)-1)/147.41316,
    /// any other t unchanged.</summary>
    public static float Ease(float t, int curve) => curve switch
    {
        2 => MathF.Log(t * 10f + 1f) / 2.397895f,
        3 => (MathF.Exp(t * 5f) - 1f) / 147.41316f,
        _ => t,
    };
}

/// <summary>
/// A player's invincible time after a revival or a portal: PC::FUN_141661370 @0x141661370 (pc.cpp:3384, the start and the
/// end), PC::FUN_1416615f0 @0x1416615f0 (:2612, each frame) and PC::FUN_1416616a0 @0x1416616a0 (:3609, the blink). The fields
/// are m_n105c (<see cref="TimerId"/>), m_n1054 (the start), m_n1058 (<see cref="Duration"/>; nonzero is FUN_140976c10, the
/// invincible test), m_n106c (the last blink frame), m_n1064 (the blink's start), m_n1068 (the blink phase, unsigned) and
/// m_b1070 (dimmed). Ticks are GetTickCountMs2.
/// </summary>
public sealed class InvincibleBlink
{
    /// <summary>OnRevival's timer id (scenestategame.cpp:7408), with UserRevivalInvincibleTick.</summary>
    public const int RevivalTimer = 100000004;
    /// <summary>OnInvincibleEffect's timer id (scenestategame.cpp:4285), with UserPortalInvincibleTick.</summary>
    public const int PortalTimer = 100000005;
    /// <summary>The compiled defaults of both lengths (constantsblocks.cpp:850, :888), ms.</summary>
    public const int DefaultTick = 10000;

    uint start, lastFrame, blinkStart, phase;
    bool dim;

    public int TimerId { get; private set; }
    public int Duration { get; private set; }
    /// <summary>FUN_140976c10: m_n1058 != 0.</summary>
    public bool Active => Duration != 0;

    /// <summary>The length a timer id's constant gives (UserRevivalInvincibleTick / UserPortalInvincibleTick).</summary>
    public static int TickFor(int timerId, ClientConstants c) => timerId switch
    {
        RevivalTimer => c?.Int("UserRevivalInvincibleTick", DefaultTick) ?? DefaultTick,
        PortalTimer => c?.Int("UserPortalInvincibleTick", DefaultTick) ?? DefaultTick,
        _ => 0,
    };

    /// <summary>The end tick of the own player's custom buff icon (AddCustomBuffEffect): the server tick plus the timer's
    /// constant; 0 (no icon) for any other timer id.</summary>
    public static int BuffEnd(int timerId, int serverTick, ClientConstants c) =>
        timerId is RevivalTimer or PortalTimer ? serverTick + TickFor(timerId, c) : 0;

    /// <summary>FUN_141661370(1, duration, timerId): the times start now; the effect is the caller's (it spawns the common
    /// resource's Revival_Invincible on the actor and keeps the handle in m_n1060). The blink phase and state carry over.</summary>
    public void Start(int durationMs, int timerId, uint now)
    {
        TimerId = timerId;
        start = lastFrame = blinkStart = now;
        Duration = durationMs;
    }

    /// <summary>FUN_141661370(0, 0, 0): the timer, the start and the length cleared (the caller removes the effect).</summary>
    public void Stop() { TimerId = 0; start = 0; Duration = 0; }

    /// <summary>FUN_1416615f0: false when not invincible. Past the end it stops, alpha 1 and <paramref name="ended"/>;
    /// otherwise the blink runs with half the length as its delay and gives 0.5 (dimmed) or 0.9.</summary>
    public bool Tick(uint now, out float alpha, out bool ended)
    {
        alpha = 1f; ended = false;
        if (!Active) return false;
        if (start + (uint)Duration < now)
        {
            Stop();
            ended = true;
            return true;
        }
        alpha = Blink((uint)Duration / 2, now);
        return true;
    }

    float Blink(uint delay, uint now)
    {
        uint elapsed = now - lastFrame;
        lastFrame = now;
        float speed = 5f;
        if (delay > 0 && delay < now - blinkStart) speed = (now - blinkStart - delay) / 1000f + 5f;
        phase = (uint)(phase + elapsed * speed);
        if (phase > 1000) { dim = !dim; phase = 0; }
        return dim ? 0.5f : 0.9f;
    }
}

/// <summary>
/// The trigger service's one-time effects (OnOneTimeEffect 0xF1): CTriggerService::FUN_141863630 @0x141863630
/// (triggerservice.cpp:885). Add: an id that has a handle already is left alone; otherwise the named effect spawns with no
/// owner and no place (the effect xml's own world placement) and its handle is kept only when it is nonzero. Remove: the
/// id's effect goes at once (CEffectService::FUN_1411a6060) and the id is dropped.
/// </summary>
public sealed class OneTimeEffects
{
    readonly Dictionary<int, int> handles = new();
    static readonly Xform Origin = new(new Vec3(0, 0, 0), new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }, 1);

    public int Count => handles.Count;
    public bool Has(int id) => handles.ContainsKey(id);

    public void Apply(int id, bool add, string name, IRegionSkillEffects fx)
    {
        if (add)
        {
            if (handles.ContainsKey(id)) return;
            int h = fx.Spawn(name ?? "", Origin, true, 0);
            if (h != 0) handles[id] = h;
            return;
        }
        if (!handles.Remove(id, out int handle)) return;
        if (handle != 0) fx.FadeOut(handle, 0);
    }

    /// <summary>A new field: the effects went with the old one.</summary>
    public void Clear() => handles.Clear();
}
