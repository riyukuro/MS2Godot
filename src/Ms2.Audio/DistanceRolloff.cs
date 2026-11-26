using System.Numerics;

namespace Ms2.Audio;

/// <summary>The FMOD rolloff a 3D event's mode at +0x34 asks for: FMOD_3D_CUSTOMROLLOFF 0x04000000, FMOD_3D_LINEARROLLOFF
/// 0x00200000, FMOD_3D_LOGROLLOFF 0x00100000 (fmod.h FMOD_MODE; log is FMOD's default when none is set).</summary>
public enum RolloffModel { Custom, Linear, Log }

/// <summary>
/// A 3D event's volume by listener distance. With FMOD_3D_CUSTOMROLLOFF it is the event's volume envelope (effect 0xc) on
/// its "(distance)" parameter, read from the FEV's EPRP points (<see cref="FevProject"/>), and FMOD's min / max rolloff does
/// not apply. With linear or log rolloff it is FMOD's model on the event's min / max distance (linear: 1 at the min to 0 at
/// the max; log: min / d from the min to the max, held past it, the rolloff scale being 1), times the envelope when the
/// event has one. The client leaves the 3D math to FMOD (CSoundService::FUN_1417aee20 sets set3DSettings(1, 1, 1),
/// soundservice.cpp:520). Every complex 3D event of MS2Sound.fev (KMS2 and GMS2) sets the custom bit; the log bit is only
/// on 2D events, which FMOD does not attenuate, and no event sets the linear bit (DistanceRolloffTests).
/// Points are linear (shape 1 on every one of them) and the gain holds the first / last point's value outside them.
/// </summary>
public sealed class DistanceRolloff
{
    readonly float[] distance, gain;

    /// <param name="distance">The envelope's distances (may be empty for a linear or log rolloff without one).</param>
    /// <param name="gain">The envelope's gains.</param>
    /// <param name="model">The event's rolloff bit.</param>
    /// <param name="min">The event's min distance (linear and log).</param>
    /// <param name="max">The event's max distance (linear and log).</param>
    public DistanceRolloff(float[] distance, float[] gain, RolloffModel model = RolloffModel.Custom, float min = 0, float max = 0)
    {
        if (distance.Length != gain.Length || distance.Length == 0 && model == RolloffModel.Custom)
            throw new ArgumentException("rolloff needs matching points");
        if (model != RolloffModel.Custom && !(max > min && min >= 0)) throw new ArgumentException("rolloff needs a min under the max");
        this.distance = distance;
        this.gain = gain;
        Model = model;
        Min = min;
        Max = max;
        Silent = float.PositiveInfinity;
        if (gain.Length > 0 && gain[^1] == 0)
        {
            int i = gain.Length - 1;
            while (i > 0 && gain[i - 1] == 0) i--;
            Silent = distance[i];
        }
        if (model == RolloffModel.Linear) Silent = MathF.Min(Silent, max);
        if (model == RolloffModel.Log && min == 0) Silent = MathF.Min(Silent, 0);   // min / d with a min of 0
    }

    public RolloffModel Model { get; }
    /// <summary>The min and max distance of a linear or log rolloff.</summary>
    public float Min { get; }
    public float Max { get; }

    public int Count => distance.Length;
    public float DistanceOf(int i) => distance[i];
    public float GainOf(int i) => gain[i];

    /// <summary>The distance from which the gain stays 0 (infinity when the last point is not 0).</summary>
    public float Silent { get; }

    /// <summary>The gain at <paramref name="d"/> world units from the listener.</summary>
    public float GainAt(float d)
    {
        float g = Model switch
        {
            RolloffModel.Linear => d <= Min ? 1f : d >= Max ? 0f : 1f - (d - Min) / (Max - Min),
            RolloffModel.Log => d <= Min ? 1f : Min / MathF.Min(d, Max),
            _ => 1f,
        };
        return distance.Length == 0 ? g : g * PointsAt(d);
    }

    float PointsAt(float d)
    {
        if (d <= distance[0]) return gain[0];
        int n = distance.Length;
        if (d >= distance[n - 1]) return gain[n - 1];
        int i = 1;
        while (distance[i] < d) i++;
        float d0 = distance[i - 1], d1 = distance[i];
        if (d1 <= d0) return gain[i];
        return gain[i - 1] + (gain[i] - gain[i - 1]) * (d - d0) / (d1 - d0);
    }

    public override string ToString() =>
        (Model == RolloffModel.Custom ? "" : $"{Model.ToString().ToLowerInvariant()} {Min:0.#}..{Max:0.#} ")
        + string.Join(" ", Enumerable.Range(0, distance.Length).Select(i => $"({distance[i]:0.#},{gain[i]:0.###})"));
}

/// <summary>
/// The per-frame rolloff of a set of 3D voices without the engine: each voice's curve, own gain and place, and the volume
/// last given to its player. <see cref="Update"/> says when a voice's volume moved by <see cref="StepDb"/> or more, so the
/// player's volume is set only then. Allocation-free after construction.
/// </summary>
public sealed class RolloffGains
{
    /// <summary>The smallest change that is passed to a player (dB).</summary>
    public const float StepDb = 0.05f;
    /// <summary>The volume of a silent voice (dB).</summary>
    public const float SilentDb = -80f;

    DistanceRolloff[] curve;
    float[] own, appliedDb;
    Vector3[] at;

    public RolloffGains(int capacity)
    {
        curve = new DistanceRolloff[capacity];
        own = new float[capacity];
        appliedDb = new float[capacity];
        at = new Vector3[capacity];
    }

    public int Capacity => curve.Length;

    /// <summary>Voice <paramref name="i"/> now plays with <paramref name="rolloff"/> (null: none) at <paramref name="ownGain"/>.</summary>
    public void Set(int i, DistanceRolloff rolloff, float ownGain, Vector3 position)
    {
        if (i >= curve.Length) Grow(Math.Max(i + 1, curve.Length * 2));
        curve[i] = rolloff;
        own[i] = ownGain;
        at[i] = position;
        appliedDb[i] = float.NaN;
    }

    public void Move(int i, Vector3 position) => at[i] = position;
    public void Clear(int i) => curve[i] = null;
    public bool Has(int i) => curve[i] != null;
    public float AppliedDb(int i) => appliedDb[i];

    /// <summary>The volume of voice <paramref name="i"/> for a listener at <paramref name="listener"/>: true with it in
    /// <paramref name="volumeDb"/> when the player's volume should change.</summary>
    public bool Update(int i, Vector3 listener, out float volumeDb)
    {
        var c = curve[i];
        volumeDb = 0;
        if (c == null) return false;
        volumeDb = ToDb(own[i] * c.GainAt(Vector3.Distance(listener, at[i])));
        float last = appliedDb[i];
        if (!float.IsNaN(last) && MathF.Abs(volumeDb - last) < StepDb) return false;
        appliedDb[i] = volumeDb;
        return true;
    }

    /// <summary>The volume of <paramref name="gain"/> in dB, <see cref="SilentDb"/> at the lowest.</summary>
    public static float ToDb(float gain) => gain <= 1e-4f ? SilentDb : MathF.Max(SilentDb, 20f * MathF.Log10(gain));

    void Grow(int n)
    {
        Array.Resize(ref curve, n);
        Array.Resize(ref own, n);
        Array.Resize(ref appliedDb, n);
        Array.Resize(ref at, n);
    }
}
