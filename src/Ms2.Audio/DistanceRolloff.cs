using System.Numerics;

namespace Ms2.Audio;

/// <summary>The FMOD rolloff a 3D event's mode at +0x34 asks for: FMOD_3D_CUSTOMROLLOFF 0x04000000, FMOD_3D_LINEARSQUAREROLLOFF
/// 0x00400000, FMOD_3D_LINEARROLLOFF 0x00200000, FMOD_3D_INVERSEROLLOFF 0x00100000 (fmod.h FMOD_MODE; inverse is FMOD's default
/// when none is set). FMOD Ex picks them in that order (the channel's 3D volume, fmodex64.dll @0x1800c47b1: mode &amp; 0x4700000,
/// custom only when it is the one bit, linear and linear square on (max - d) / (max - min), the square squared, inverse otherwise).</summary>
public enum RolloffModel { Custom, Linear, Inverse, LinearSquare }

/// <summary>
/// A 3D event's volume by listener distance. With FMOD_3D_CUSTOMROLLOFF it is the event's volume envelope (effect 0xc) on
/// its "(distance)" parameter, read from the FEV's EPRP points (<see cref="FevProject"/>), and FMOD's min / max rolloff does
/// not apply. With linear or log rolloff it is FMOD's model on the event's min / max distance (linear: 1 at the min to 0 at
/// the max; log: min / d from the min to the max, held past it, the rolloff scale being 1), times the envelope when the
/// event has one. The client leaves the 3D math to FMOD (CSoundService::FUN_1417aee20 sets set3DSettings(1, 1, 1),
/// soundservice.cpp:520). Every complex 3D event of MS2Sound.fev (KMS2 and GMS2) sets the custom bit; the inverse bit is
/// only on 2D events, which FMOD does not attenuate, and no event sets the linear or linear square bit (DistanceRolloffTests).
/// Points are linear (shape 1 on every one of them) and the gain holds the first / last point's value outside them.
/// </summary>
public sealed class DistanceRolloff
{
    readonly float[] distance, gain;

    /// <param name="distance">The envelope's distances (may be empty for a linear, linear square or inverse rolloff without one).</param>
    /// <param name="gain">The envelope's gains.</param>
    /// <param name="model">The event's rolloff bit.</param>
    /// <param name="min">The event's min distance (the models other than custom).</param>
    /// <param name="max">The event's max distance (the models other than custom).</param>
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
        if (model is RolloffModel.Linear or RolloffModel.LinearSquare) Silent = MathF.Min(Silent, max);
        if (model == RolloffModel.Inverse && min == 0) Silent = MathF.Min(Silent, 0);   // min / d with a min of 0
    }

    public RolloffModel Model { get; }
    /// <summary>The min and max distance of a rolloff other than custom.</summary>
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
            RolloffModel.LinearSquare => d <= Min ? 1f : d >= Max ? 0f : Square((Max - d) / (Max - Min)),
            RolloffModel.Inverse => d <= Min ? 1f : Min / MathF.Min(d, Max),
            _ => 1f,
        };
        return distance.Length == 0 ? g : g * PointsAt(d);
    }

    static float Square(float f) => f * f;

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
/// player's volume is set only then. A voice set to stop past its curve's <see cref="DistanceRolloff.Silent"/> distance (a
/// looping one) also says when it crosses that distance (<see cref="PastSilentChanged"/>), so its player is stopped while
/// nothing of it can be heard and played again when the listener comes back (Godot's StreamPaused is not used: a pause set
/// within about a frame of play() is dropped, measured with the Dummy driver, while Stop is not). Allocation-free after
/// construction.
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
    bool[] stops, past, crossed;

    public RolloffGains(int capacity)
    {
        curve = new DistanceRolloff[capacity];
        own = new float[capacity];
        appliedDb = new float[capacity];
        at = new Vector3[capacity];
        stops = new bool[capacity];
        past = new bool[capacity];
        crossed = new bool[capacity];
    }

    public int Capacity => curve.Length;

    /// <summary>Voice <paramref name="i"/> now plays with <paramref name="rolloff"/> (null: none) at <paramref name="ownGain"/>,
    /// on this side of its silent distance; with <paramref name="stopsPastSilent"/> it is to stop past it.</summary>
    public void Set(int i, DistanceRolloff rolloff, float ownGain, Vector3 position, bool stopsPastSilent = false)
    {
        if (i >= curve.Length) Grow(Math.Max(i + 1, curve.Length * 2));
        curve[i] = rolloff;
        own[i] = ownGain;
        at[i] = position;
        appliedDb[i] = float.NaN;
        stops[i] = stopsPastSilent;
        past[i] = false;
        crossed[i] = false;
    }

    public void Move(int i, Vector3 position) => at[i] = position;
    public void Clear(int i) => curve[i] = null;
    public bool Has(int i) => curve[i] != null;
    public float AppliedDb(int i) => appliedDb[i];
    /// <summary>Whether voice <paramref name="i"/> is past its curve's silent distance and set to stop there.</summary>
    public bool IsPast(int i) => past[i];

    /// <summary>True once after an <see cref="Update"/> in which voice <paramref name="i"/> crossed its curve's silent distance
    /// (only for a voice set to stop there), with <paramref name="isPast"/> the side it is on now.</summary>
    public bool PastSilentChanged(int i, out bool isPast)
    {
        isPast = past[i];
        if (!crossed[i]) return false;
        crossed[i] = false;
        return true;
    }

    /// <summary>The volume of voice <paramref name="i"/> for a listener at <paramref name="listener"/>: true with it in
    /// <paramref name="volumeDb"/> when the player's volume should change.</summary>
    public bool Update(int i, Vector3 listener, out float volumeDb)
    {
        var c = curve[i];
        volumeDb = 0;
        if (c == null) return false;
        float d = Vector3.Distance(listener, at[i]);
        if (stops[i] && d >= c.Silent != past[i]) { past[i] = !past[i]; crossed[i] = true; }
        volumeDb = ToDb(own[i] * c.GainAt(d));
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
        Array.Resize(ref stops, n);
        Array.Resize(ref past, n);
        Array.Resize(ref crossed, n);
    }
}

/// <summary>
/// What a Godot 4.7 AudioStreamPlayer3D puts out (scene/3d/audio_stream_player_3d.cpp, _update_panning), without the engine,
/// so a test can check the gain heard and not only the volume given: the volume and the model's attenuation in dB, held under
/// the max dB (3), times 1 - d / max distance when the max distance is above 0 (that factor is applied with every model,
/// Disabled too), and nothing past the max distance. The same factor drives a high shelf over the attenuation filter's cutoff
/// (5000 Hz) of (1 - min(1, factor)) times the attenuation filter dB (-24 by default). Checked against the real output of a
/// player on its own bus (an AudioEffectCapture, 500 Hz and 12 kHz sines, the Dummy driver): a curve player at -6 dB with a
/// max distance of 4467 put out 0.26 at 2250 instead of 0.5 at 500 Hz, and 0.004 at 12 kHz.
/// (the shelf is not modelled exactly: the 12 kHz output fell further than the shelf alone gives). So a player whose gain is
/// the event's curve has the model Disabled, a max distance of 0 and a filter dB of 0 (<see cref="CurveMaxDistance"/>,
/// <see cref="CurveFilterDb"/>): its output was then the curve's gain at both frequencies.
/// </summary>
public static class Player3DOutput
{
    /// <summary>Godot's default max dB and attenuation filter dB.</summary>
    public const float MaxDb = 3f, DefaultFilterDb = -24f;
    /// <summary>The attenuation filter dB of a player whose gain is a curve's: no high shelf.</summary>
    public const float CurveFilterDb = 0f;

    /// <summary>The max distance of a player whose gain is <paramref name="curve"/>'s: 0, so Godot adds no fade of its own
    /// (past the curve's silent distance a looping voice is stopped instead, <see cref="RolloffGains"/>).</summary>
    public static float CurveMaxDistance(DistanceRolloff curve) => 0f;

    /// <summary>The factor of the attenuation, before the filter: the gain heard below the filter's cutoff, for a player
    /// with the model Disabled at <paramref name="volumeDb"/>, <paramref name="distance"/> from the listener.</summary>
    public static float Gain(float volumeDb, float maxDistance, float distance, float maxDb = MaxDb)
    {
        if (maxDistance > 0 && distance > maxDistance) return 0;
        float g = MathF.Pow(10f, MathF.Min(volumeDb, maxDb) / 20f);
        return maxDistance > 0 ? g * MathF.Max(0, 1 - distance / maxDistance) : g;
    }

    /// <summary>The high shelf's gain in dB over the cutoff for an attenuation factor of <paramref name="gain"/>.</summary>
    public static float ShelfDb(float filterDb, float gain) => (1 - MathF.Min(1, gain)) * filterDb;
}
