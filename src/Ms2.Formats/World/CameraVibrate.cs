using System.Numerics;

namespace Ms2.Formats.World;

/// <summary>
/// The camera shake a vibrate part (type 0xc, CVibrateEffect, vibrateeffectparam.cpp) adds: CVibrateCameraEffect
/// (camera.cpp:688-737), without the engine. Each frame the camera manager moves the camera mode's eye by
/// <see cref="Update"/>; all points are client space.
/// </summary>
public sealed class CameraVibrate
{
    /// <summary>The direction before the first pick: g_143a08cf8, UNIT_Z.</summary>
    static readonly Vector3 StartDir = Vector3.UnitZ;

    public readonly double Start, End, FadeStart;
    public readonly float Frequency, Amplitude, MinScale;
    public readonly bool Vertical;
    Vector3 dir = StartDir;
    bool negative = true;   // m_bNegative starts at 1

    /// <summary>The constructor @0x140ae9450 (camera.cpp:688-694).</summary>
    public CameraVibrate(double start, double end, float frequency, float amplitude, float minScale, double fadeStart,
        bool vertical = true)
    {
        Start = start; End = end; FadeStart = fadeStart;
        Frequency = frequency; Amplitude = amplitude; MinScale = minScale;
        Vertical = vertical;
    }

    /// <summary>
    /// CVibrateEffect::vfunc_3 @0x141b72bd0 (vibrateeffectparam.cpp:154-172): the shake of a vibrate part made at game time
    /// <paramref name="now"/> (CEffect::vfunc_3, effectservice.cpp:358-370: the start is the request's time plus the part's
    /// start delay): from the start for the part's duration at its frequency and amplitude, damping its min scale, fading
    /// from the start plus its damping time, vertical.
    /// </summary>
    public static CameraVibrate For(EffectPart part, double now)
    {
        double start = now + part.StartDelay;
        return new CameraVibrate(start, start + part.Duration, part.VibrateFrequency, part.VibrateAmplitude,
            part.VibrateDamping, start + part.VibrateDampingTime, true);
    }

    /// <summary>The direction of the last pick (unit, or UNIT_Z before the first).</summary>
    public Vector3 Direction => dir;

    /// <summary>
    /// vfunc_1 @0x140ae9550 (camera.cpp:703-737) at game time <paramref name="time"/>: true before the start (the eye is not
    /// moved) and inside [start, end), false from the end on (the manager then drops it).
    /// - fade = 1, or 1 - (time - fadeStart) / (end - fadeStart) from the fade start on; scale = (fade * (1 - minScale) +
    ///   minScale) * amplitude; wave = sinf(phase * frequency * 2 * 3.1415927) * scale, phase = (time - start) / (end - start).
    /// - When not vertical, or when the wave's sign is not the last pick's, the direction is picked again: x and z (and y
    ///   when vertical) each rand01 * (+-1), the sign GetRandomRange(2, 0) as (n - 0.5) * 2 and rand01 FUN_140768090
    ///   ([0, 1)), in that order (x sign, z sign, x, z, then y sign, y), then unitized (a length below 1e-6 gives zero).
    /// - The eye moves by direction * wave.
    /// <paramref name="random"/> gives the client's draws: <see cref="Random.Next(int)"/> (2) for a sign and
    /// <see cref="Random.NextSingle"/> for rand01.
    /// </summary>
    public bool Update(double time, ref Vector3 eye, Random random)
    {
        if (time < Start) return true;
        if (time >= End) return false;
        float fade = 1f;
        if (time >= FadeStart) fade = 1f - (float)((time - FadeStart) / (End - FadeStart));
        float scale = (fade * (1f - MinScale) + MinScale) * Amplitude;
        float phase = (float)((time - Start) / (End - Start));
        float wave = MathF.Sin(phase * Frequency * 2f * 3.1415927f) * scale;
        if (!Vertical || negative != wave < 0f)
        {
            float sx = (random.Next(2) - 0.5f) * 2f;
            float sz = (random.Next(2) - 0.5f) * 2f;
            dir.X = random.NextSingle() * sx;
            dir.Z = random.NextSingle() * sz;
            if (Vertical)
            {
                float sy = (random.Next(2) - 0.5f) * 2f;
                dir.Y = random.NextSingle() * sy;
            }
            float l = dir.Length();
            dir = l > 1e-6f ? dir / l : Vector3.Zero;
            negative = wave < 0f;
        }
        eye += dir * wave;
        return true;
    }
}

/// <summary>
/// CameraMgr's camera effects (m_effect.m_effects, camera.cpp): <see cref="Add"/> (AddEffect @0x140af0de0) queues one when the
/// effects are on, and each frame after the mode's update <see cref="Update"/> (FUN_140af1de0 @0x140af1de0) runs only the
/// first: while it runs the mode takes the moved eye (vfunc_3), and once it ends it is dropped (the next one runs the next
/// frame). The list emptied resets the effect state (FUN_140af1a90; the saved setting it restores is a camera-move effect's,
/// not ported). The queue keeps its room, so a frame allocates nothing.
/// </summary>
public sealed class CameraEffects
{
    readonly List<CameraVibrate> effects = new(8);
    readonly Random random;

    public CameraEffects(Random random = null) => this.random = random ?? Random.Shared;

    /// <summary>m_244 (EnableCameraEffect, 1 at construction; the trigger action FUN_140af7be0 sets it).</summary>
    public bool EnableCameraEffect = true;

    /// <summary>FUN_140aed0f0 @0x140aed0f0 (camera.cpp:1288-1293): m_244 and the camera option's vibrateEffect (m_b01).</summary>
    public bool On(bool vibrateOption) => EnableCameraEffect && vibrateOption;

    /// <summary>The queued effects, the running one first.</summary>
    public int Count => effects.Count;
    public CameraVibrate this[int i] => effects[i];

    /// <summary>AddEffect: queued only when <see cref="On"/>.</summary>
    public void Add(CameraVibrate effect, bool vibrateOption)
    {
        if (On(vibrateOption)) effects.Add(effect);
    }

    /// <summary>FUN_140af1de0 at game time <paramref name="time"/> on the mode's eye: true when the first effect ran and moved
    /// the eye (the mode then takes it); a finished one is dropped and the eye is left.</summary>
    public bool Update(double time, ref Vector3 eye)
    {
        if (effects.Count == 0) return false;
        var before = eye;
        if (effects[0].Update(time, ref eye, random)) return eye != before;
        eye = before;
        effects.RemoveAt(0);
        return false;
    }

    /// <summary>FUN_140af1a90's clear (and CameraMgr::FUN_140aead90 leaving the field).</summary>
    public void Clear() => effects.Clear();
}
