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
/// Each segment between two points takes the shape of its end point (fmod_event64's envelope value 0x180010de0, which reads the
/// flags of the first point past the distance): <see cref="PointCurve"/> is a cubic Bezier through the two values with its
/// control values at the ends (0x1800151d0 at the segment's fraction t), so a smoothstep 3t^2 - 2t^3 between them;
/// <see cref="PointLinear"/> is a straight line. MS2Sound.fev has only these two (KMS2 742 curve / 14 linear points, GMS2
/// 737 / 14, DistanceRolloffTests); the FMOD probe's renders follow the smoothstep on the volume and the pan level envelope
/// (the meso pickup at 200: 0.926 of its gain at 30, a straight line would give 0.853). The gain holds the first / last
/// point's value outside them.
/// <para>
/// The event's 3D Pan Level envelope (flags bit 0x400, on the same parameter) times its 3D pan level property (record +0x9c,
/// 1 on every 3D event) goes to Channel::set3DPanLevel (fmod_event64's per-layer envelope loop 0x180025590, the call at
/// 0x180026614; fmodex's set3DPanLevel 0x1800c7030 takes 0..1 only). That level L is how far the voice is panned by its
/// direction: fmodex mixes L of the 3D pan with 1 - L of the centre, and blends each 3D volume factor f (the model's
/// distance gain here) to f * L + (1 - L) (its 3D volume, 0x1800c50b9). The event's volume envelope is not a 3D factor and
/// is not blended. <see cref="PanLevelAt"/> is that L.
/// </para>
/// <para>
/// The event's direct Occlusion envelope (flags bit 0x2000, sub-index 0, on the same parameter; any Occlusion envelope is
/// built-in whatever its unit field says, fmod_event64's loader 0x1800229c9) goes to Channel::set3DOcclusion as the direct
/// occlusion (the call at 0x18002599f; fmodex's set3DOcclusion 0x1800c6ee0 holds it to 0..1). The gain is times
/// 1 - occlusion on the whole voice (<see cref="OcclusionAt"/>): Channel::getAudibility blends the occlusion by the pan level
/// (1 - occlusion * L), but the mix does not. The FMOD probe's renders of AMB/11100001 (pan level 0.447, occlusion 0 -> 1
/// at 4467.4) to the side give the far channel, which has only the 1 - L centre part, vol * (1 - occ) * (1 - L) within 0.5%
/// from 500 to 3000 (0.1364 at 2250, the blend would give 0.214), and the total in front follows vol * (1 - occ) to 4000
/// (0.0011, the blend 0.019). The client's EventSystem::init (soundservice.cpp:523) does not ask for
/// FMOD_INIT_OCCLUSION_LOWPASS, so the occlusion is volume only. The reverb occlusion (sub-index 1) is not ported: the port
/// has no reverb.
/// </para>
/// <para>
/// A custom rolloff event without a volume envelope on "(distance)" (every simple 3D event, and a complex one without that
/// parameter) has gain 1 at every distance: fmodex's custom rolloff without a point array is a distance factor of 1
/// (0x1800c46ee), fmod_event64 sets no rolloff callback or custom points, and the listener loop starts from FLT_MAX, so no
/// max distance cuts it (<see cref="Count"/> 0).
/// </para>
/// </summary>
public sealed class DistanceRolloff
{
    /// <summary>A point's shape bits (EPRP u32 +8): the Bezier (smoothstep) segment to it, the straight one. The evaluator tests
    /// them in that order; bits 4 and 8 (two more curves) are on no MS2 point.</summary>
    public const int PointCurve = 0x1, PointLinear = 0x2;

    readonly float[] distance, gain;
    readonly float[] panDistance, panValue, occlusionDistance, occlusionValue;
    readonly int[] shape, panShape, occlusionShape;

    /// <param name="distance">The envelope's distances (empty: none, gain 1 with a custom rolloff).</param>
    /// <param name="gain">The envelope's gains.</param>
    /// <param name="model">The event's rolloff bit.</param>
    /// <param name="min">The event's min distance (the models other than custom).</param>
    /// <param name="max">The event's max distance (the models other than custom).</param>
    /// <param name="panLevel">The event's 3D pan level property.</param>
    /// <param name="panDistance">The 3D Pan Level envelope's distances (null: none).</param>
    /// <param name="panValue">Its values.</param>
    /// <param name="shape">The volume envelope's point shapes (null: linear).</param>
    /// <param name="panShape">The pan level envelope's point shapes (null: linear).</param>
    /// <param name="occlusionDistance">The direct Occlusion envelope's distances (null: none).</param>
    /// <param name="occlusionValue">Its values.</param>
    /// <param name="occlusionShape">Its point shapes (null: linear).</param>
    public DistanceRolloff(float[] distance, float[] gain, RolloffModel model = RolloffModel.Custom, float min = 0, float max = 0,
        float panLevel = 1, float[] panDistance = null, float[] panValue = null, int[] shape = null, int[] panShape = null,
        float[] occlusionDistance = null, float[] occlusionValue = null, int[] occlusionShape = null)
    {
        if (distance.Length != gain.Length
            || (panDistance?.Length ?? 0) != (panValue?.Length ?? 0) || (occlusionDistance?.Length ?? 0) != (occlusionValue?.Length ?? 0)
            || shape != null && shape.Length != distance.Length || panShape != null && panShape.Length != (panDistance?.Length ?? 0)
            || occlusionShape != null && occlusionShape.Length != (occlusionDistance?.Length ?? 0))
            throw new ArgumentException("rolloff needs matching points");
        if (model != RolloffModel.Custom && !(max > min && min >= 0)) throw new ArgumentException("rolloff needs a min under the max");
        this.distance = distance;
        this.gain = gain;
        this.shape = shape;
        Model = model;
        Min = min;
        Max = max;
        PanLevel = Math.Clamp(panLevel, 0f, 1f);
        if (panDistance?.Length > 0) { this.panDistance = panDistance; this.panValue = panValue; this.panShape = panShape; }
        if (occlusionDistance?.Length > 0)
        { this.occlusionDistance = occlusionDistance; this.occlusionValue = occlusionValue; this.occlusionShape = occlusionShape; }
        Silent = float.PositiveInfinity;
        if (gain.Length > 0 && gain[^1] == 0)
        {
            int i = gain.Length - 1;
            while (i > 0 && gain[i - 1] == 0) i--;
            Silent = distance[i];
        }
        // the model's 0 is blended up by a pan level under 1
        if (model is RolloffModel.Linear or RolloffModel.LinearSquare && FullPanFrom(max)) Silent = MathF.Min(Silent, max);
        if (model == RolloffModel.Inverse && min == 0 && FullPanFrom(0)) Silent = MathF.Min(Silent, 0);   // min / d with a min of 0
    }

    public RolloffModel Model { get; }
    /// <summary>The min and max distance of a rolloff other than custom.</summary>
    public float Min { get; }
    public float Max { get; }

    public int Count => distance.Length;
    public float DistanceOf(int i) => distance[i];
    public float GainOf(int i) => gain[i];
    /// <summary>The shape of the segment ending at point <paramref name="i"/>.</summary>
    public int ShapeOf(int i) => shape?[i] ?? PointLinear;

    /// <summary>The distance from which the gain stays 0 (infinity when the last point is not 0).</summary>
    public float Silent { get; }

    /// <summary>The event's 3D pan level property (record +0x9c).</summary>
    public float PanLevel { get; }
    /// <summary>Whether the event has a 3D Pan Level envelope.</summary>
    public bool HasPanLevelEnvelope => panDistance != null;
    /// <summary>Whether the event has a direct Occlusion envelope.</summary>
    public bool HasOcclusion => occlusionDistance != null;

    /// <summary>The direct occlusion at <paramref name="d"/>, held to 0..1 as set3DOcclusion does (0 without the envelope).</summary>
    public float OcclusionAt(float d) => occlusionDistance == null ? 0f : Math.Clamp(At(occlusionDistance, occlusionValue, occlusionShape, d), 0f, 1f);

    /// <summary>The 3D pan level at <paramref name="d"/>: the property times the envelope, held to 0..1.</summary>
    public float PanLevelAt(float d) =>
        Math.Clamp(panDistance == null ? PanLevel : PanLevel * At(panDistance, panValue, panShape, d), 0f, 1f);

    /// <summary>The pan level is 1 at every distance from <paramref name="d"/> on.</summary>
    bool FullPanFrom(float d)
    {
        if (PanLevelAt(d) < 1) return false;
        if (panDistance != null)
            for (int i = 0; i < panDistance.Length; i++)
                if (panDistance[i] > d && PanLevel * panValue[i] < 1) return false;
        return true;
    }

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
        float l = PanLevelAt(d);
        if (Model != RolloffModel.Custom && l < 1) g = g * l + (1 - l);
        if (occlusionDistance != null) g *= 1 - OcclusionAt(d);
        return distance.Length == 0 ? g : g * At(distance, gain, shape, d);
    }

    static float Square(float f) => f * f;

    static float At(float[] distance, float[] value, int[] shape, float d)
    {
        if (d <= distance[0]) return value[0];
        int n = distance.Length;
        if (d >= distance[n - 1]) return value[n - 1];
        int i = 1;
        while (distance[i] < d) i++;
        float d0 = distance[i - 1], d1 = distance[i];
        if (d1 <= d0) return value[i];
        float t = (d - d0) / (d1 - d0);
        if (shape != null && (shape[i] & PointCurve) != 0) t = t * t * (3 - 2 * t);
        return value[i - 1] + (value[i] - value[i - 1]) * t;
    }

    public override string ToString() =>
        (Model == RolloffModel.Custom ? "" : $"{Model.ToString().ToLowerInvariant()} {Min:0.#}..{Max:0.#} ")
        + (distance.Length == 0 ? "flat" : Points(distance, gain))
        + (PanLevel != 1 || panDistance != null ? $" pan {PanLevel:0.###}" + (panDistance != null ? " " + Points(panDistance, panValue) : "") : "")
        + (occlusionDistance != null ? " occlusion " + Points(occlusionDistance, occlusionValue) : "");

    static string Points(float[] d, float[] v) => string.Join(" ", Enumerable.Range(0, d.Length).Select(i => $"({d[i]:0.#},{v[i]:0.###})"));
}

/// <summary>
/// The per-frame rolloff of a set of 3D voices without the engine: each voice's curve, own gain and place, and the volume
/// last given to its player. <see cref="Update"/> says when a voice's volume moved by <see cref="StepDb"/> or more, so the
/// player's volume is set only then. A voice set to stop past its curve's <see cref="DistanceRolloff.Silent"/> distance (a
/// looping one) also says when it crosses that distance (<see cref="PastSilentChanged"/>), so its player is stopped while
/// nothing of it can be heard and played again when the listener comes back (Godot's StreamPaused is not used: a pause set
/// within about a frame of play() is dropped, measured with the Dummy driver, while Stop is not). Allocation-free after
/// construction. The voice's pan level at that distance (<see cref="DistanceRolloff.PanLevelAt"/>) gives its player's
/// panning strength (<see cref="Player3DOutput.PanningStrength"/>, said by <see cref="PanChanged"/> when it moved by
/// <see cref="PanStep"/> or more) and a gain on its volume (<see cref="Player3DOutput.PanLevelGain"/>).
/// </summary>
public sealed class RolloffGains
{
    /// <summary>The smallest change that is passed to a player (dB).</summary>
    public const float StepDb = 0.05f;
    /// <summary>The smallest panning strength change that is passed to a player.</summary>
    public const float PanStep = 0.01f;
    /// <summary>The volume of a silent voice (dB).</summary>
    public const float SilentDb = -80f;

    DistanceRolloff[] curve;
    float[] own, appliedDb, pan, appliedPan;
    Vector3[] at;
    bool[] stops, past, crossed;

    public RolloffGains(int capacity)
    {
        curve = new DistanceRolloff[capacity];
        own = new float[capacity];
        appliedDb = new float[capacity];
        pan = new float[capacity];
        appliedPan = new float[capacity];
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
        pan[i] = rolloff == null ? 1f : Player3DOutput.PanningStrength(rolloff.PanLevelAt(0));
        appliedPan[i] = float.NaN;
        stops[i] = stopsPastSilent;
        past[i] = false;
        crossed[i] = false;
    }

    public void Move(int i, Vector3 position) => at[i] = position;
    public void Clear(int i) => curve[i] = null;
    public bool Has(int i) => curve[i] != null;
    public float AppliedDb(int i) => appliedDb[i];
    /// <summary>The panning strength voice <paramref name="i"/>'s last <see cref="Update"/> asked for.</summary>
    public float Panning(int i) => pan[i];

    /// <summary>True when voice <paramref name="i"/>'s player should take <paramref name="strength"/> as its panning strength:
    /// the first time after <see cref="Set"/>, then when the last <see cref="Update"/> moved it by <see cref="PanStep"/> or more.</summary>
    public bool PanChanged(int i, out float strength)
    {
        strength = pan[i];
        if (curve[i] == null) return false;
        float last = appliedPan[i];
        if (!float.IsNaN(last) && MathF.Abs(strength - last) < PanStep) return false;
        appliedPan[i] = strength;
        return true;
    }
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
        float level = c.PanLevelAt(d);
        pan[i] = Player3DOutput.PanningStrength(level);
        volumeDb = ToDb(own[i] * c.GainAt(d) * Player3DOutput.PanLevelGain(level));
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
        Array.Resize(ref pan, n);
        Array.Resize(ref appliedPan, n);
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

    /// <summary>The project's audio/general/3d_panning_strength (not set in project.godot, so Godot's default).</summary>
    public const float GlobalPanningStrength = 0.5f;

    /// <summary>
    /// The panning strength that gives a player FMOD's stereo spread at pan level <paramref name="panLevel"/>. FMOD mixes a
    /// 3D voice as L times its 3D pan (constant power from the direction's side component: 0.707 each in front, 1 / 0 at the
    /// side) plus 1 - L times the 2D centre (1 on each channel), so the side ratio is 1 / (1 - L). Godot 4.7's player pans
    /// with constant power, 1 per channel in front, and a side ratio of 1 / (1 - s * 0.5) for a strength s under the global
    /// 0.5. Measured from the FMOD probe's render of the meso pickup (pan level 0.523 at 600) and Godot recordings at 0, 0.25,
    /// 0.5 and 1: side 2.095 / 2.010, 60 degrees 1.833 / 1.786, 45 degrees 1.603 / 1.578, 30 degrees 1.377 / 1.366, in front
    /// and behind 1 / 1 (FMOD, Godot at 1). So s = L / 0.5. FMOD reads only the direction's side component; Godot pans an
    /// elevated source at the side's ratio.
    /// </summary>
    public static float PanningStrength(float panLevel) => Math.Clamp(panLevel, 0f, 1f) / GlobalPanningStrength;

    /// <summary>
    /// The gain that brings the player's power to FMOD's at pan level <paramref name="panLevel"/>: Godot keeps 1 + 1 at every
    /// strength, FMOD has L^2 + 2 L (1 - L) (l + r) + 2 (1 - L)^2 for the 3D pan's l + r, taken at its mid value (1 + 1.414) / 2
    /// (within 0.35 dB of the front and the side). 1 at L = 0 (2D), 0.707 at L = 1; 0.815 at the meso pickup's 0.523.
    /// </summary>
    public static float PanLevelGain(float panLevel)
    {
        float l = Math.Clamp(panLevel, 0f, 1f), m = 1 - l;
        const float k = 1.2071068f;
        return MathF.Sqrt((l * l + 2 * l * m * k + 2 * m * m) / 2);
    }
}
