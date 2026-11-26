using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Ms2.Audio;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>
/// The shape of a curve's segments and the 3D pan level, as the client's FMOD plays them (fmod_event64's envelope value
/// 0x180010de0 and the stereo mix of a 3D voice), checked against the FMOD probe's renders of the same events: the meso
/// pickup (Item/09200002) at 30 to 900 to the left and at 600 from nine directions.
/// </summary>
public class PanLevelTests
{
    /// <summary>Every EPRP point's shape: the Bezier (smoothstep) curve or a straight line, nothing else; and every 3D curve
    /// keeps its points' shapes. The KMS data comes in two builds of the event project: 742 curve points (the live client's
    /// and the restored data) and 737 (the dev data's project, 60 bytes shorter: five 12-byte points); GMS2 has 737.</summary>
    [TestCase("MS2_DATA", new[] { 742, 737 }, 14)]
    [TestCase("MS2_GMS2_DATA", new[] { 737 }, 14)]
    public void EveryPointIsACurveOrALine(string variable, int[] curve, int linear)
    {
        var fev = Fev(variable);
        var shapes = (int[])typeof(FevProject).GetField("envShape", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fev);
        int c = shapes.Count(s => s == DistanceRolloff.PointCurve), l = shapes.Count(s => s == DistanceRolloff.PointLinear);
        TestContext.Out.WriteLine($"{variable}: {shapes.Length} points, {c} curve, {l} linear");
        Assert.That(curve, Does.Contain(c));
        Assert.That((l, shapes.Length), Is.EqualTo((linear, c + linear)));
        int curves = 0, curved = 0;
        foreach (var ev in fev.AllEvents)
        {
            if (ev.Rolloff is not { } r) continue;
            curves++;
            bool any = false;
            for (int i = 0; i < r.Count; i++)
            {
                Assert.That(r.ShapeOf(i), Is.EqualTo(DistanceRolloff.PointCurve).Or.EqualTo(DistanceRolloff.PointLinear), ev.FullPath);
                any |= r.ShapeOf(i) == DistanceRolloff.PointCurve;
            }
            if (any) curved++;
        }
        TestContext.Out.WriteLine($"{curves} curves, {curved} with a curve segment");
        var far = fev.AllEvents.Where(e => e.Rolloff != null).GroupBy(e => MathF.Round(e.Rolloff.PanLevelAt(e.Rolloff.Silent) * 10) / 10)
            .OrderBy(g => g.Key).Select(g => $"{g.Key:0.0}: {g.Count()}");
        TestContext.Out.WriteLine($"pan level at the silent distance: {string.Join(", ", far)}; with a pan envelope {fev.AllEvents.Count(e => e.Rolloff?.HasPanLevelEnvelope == true)}");
        Assert.That(curved, Is.GreaterThan(curves * 9 / 10));
    }

    /// <summary>
    /// The meso pickup's gain from the FMOD render (the event at 30, 100, 150, 200, 300, 400, 600 and 900 on the left; each
    /// play's power over the play at 30, over FMOD's pan power 1 + (1 - L)^2 at its pan level, square root): its two-point
    /// curve (0, 1) (1185.5, 0) is a smoothstep, not a line. A line gives 0.853 at 200 and 0.247 at 900, the render 0.926 and
    /// 0.147; the smoothstep gives the render within 0.001 at every distance. The render's left / right is 1 / (1 - L) at
    /// each distance too (200 1.149, 300 1.483, 400 1.949, 600 2.095).
    /// </summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void MesoPickupFallsOffLikeFmod(string variable)
    {
        var c = Event(variable, "Item", "09200002");
        float g30 = c.GainAt(30);
        (float d, float gain, float ratio)[] render =
        {
            (100, 0.9817f, 1.007f), (150, 0.9578f, 1.056f), (200, 0.926f, 1.149f), (300, 0.842f, 1.483f),
            (400, 0.7367f, 1.949f), (600, 0.4916f, 2.095f), (900, 0.1467f, 2.095f),
        };
        foreach (var (d, gain, ratio) in render)
        {
            Assert.That(c.GainAt(d) / g30, Is.EqualTo(gain).Within(0.002f), $"gain at {d}");
            Assert.That(1 / (1 - c.PanLevelAt(d)), Is.EqualTo(ratio).Within(0.01f), $"left / right at {d}");
        }
    }

    /// <summary>The meso pickup's pan level: 2D up to 73.7, 0.523 at 461.9 and on (its envelope), so its stereo spread is
    /// about 1.15 : 1 at 200 and 2.1 : 1 at the side past 462, where Godot's default strength put out 1.34 : 1 at every
    /// distance (the issue's "strange" pickup: 2 : 1 at 200 on the old listener).</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void MesoPickupPansLikeFmod(string variable)
    {
        var c = Event(variable, "Item", "09200002");
        TestContext.Out.WriteLine(c.ToString());
        Assert.That(c.PanLevelAt(30), Is.EqualTo(0f).Within(1e-4f));
        Assert.That(c.PanLevelAt(600), Is.EqualTo(0.523f).Within(0.002f));
        Assert.That(Player3DOutput.PanningStrength(c.PanLevelAt(30)), Is.EqualTo(0f).Within(1e-4f));
        Assert.That(Player3DOutput.PanningStrength(c.PanLevelAt(600)), Is.EqualTo(1.046f).Within(0.005f));
        // the side ratio Godot puts out at that strength is FMOD's 1 / (1 - L) (FMOD render 2.095 at 600)
        Assert.That(GodotSideRatio(Player3DOutput.PanningStrength(c.PanLevelAt(600))), Is.EqualTo(2.095f).Within(0.02f));
        float at200 = c.PanLevelAt(200);
        Assert.That(1 / (1 - at200), Is.EqualTo(GodotSideRatio(Player3DOutput.PanningStrength(at200))).Within(0.01f));
        Assert.That(1 / (1 - at200), Is.InRange(1.1f, 1.25f));
    }

    /// <summary>
    /// The pan law: FMOD's left / right at pan level L for a source at angle a from the front is L l3 + 1 - L, L r3 + 1 - L
    /// with the constant power l3 = cos((1 - sin a) pi / 4), r3 = sin(...); Godot's at strength s keeps the power 1 + 1 with
    /// the recorded side far / near (<see cref="GodotFarOverNear"/>). At the port's strength for L its far / near at the side
    /// is FMOD's 1 - L within 0.006 up to the far channel's mute at L 1, and with PanLevelGain(L) the power is within 0.35 dB
    /// of FMOD's at every angle and level. Between the front and the side, Godot's recordings at strength 1 (60 degrees
    /// 1.786, 45 1.578, 30 1.366) are FMOD's ratio at the pan level the port gives strength 1 within 3%.
    /// </summary>
    [Test]
    public void PanLevelMapsToGodotsStrength()
    {
        for (float l = 0; l <= 1.0001f; l += 0.05f)
        {
            float s = Player3DOutput.PanningStrength(l);
            Assert.That(GodotFarOverNear(s), Is.EqualTo(1 - MathF.Min(l, 1)).Within(0.006f), $"L {l}");
            float g = Player3DOutput.PanLevelGain(l);
            for (int deg = 0; deg <= 90; deg += 15)
            {
                var (fl, fr) = Fmod(l, deg);
                float fmodPower = fl * fl + fr * fr, godotPower = 2 * g * g;
                Assert.That(10 * MathF.Log10(godotPower / fmodPower), Is.InRange(-0.36f, 0.36f), $"L {l} at {deg}");
            }
        }
        Assert.That(Player3DOutput.PanLevelGain(0), Is.EqualTo(1f));
        Assert.That(Player3DOutput.PanLevelGain(1), Is.EqualTo(MathF.Sqrt(0.5f)).Within(1e-5f));
        float atOne = Enumerable.Range(0, 1001).Select(i => i / 1000f).First(x => Player3DOutput.PanningStrength(x) >= 1);
        foreach (var (deg, godot) in GodotAtStrengthOne)
        {
            var (fl, fr) = Fmod(atOne, deg);
            Assert.That(godot, Is.EqualTo(fl / fr).Within(0.03f * fl / fr), $"{deg} degrees");
        }
    }

    /// <summary>
    /// A stereo clip (536 of the KMS curve events have one, 446 of GMS2's; <see cref="StereoCurveEventsAreCounted"/>) heard as
    /// FMOD Ex mixes it: the FMOD probe's renders of AMB/11100001 (stereo, the render's channel correlation 0.318) with the listener to the
    /// side and in front, regressed on the render at the listener, give each output channel as own and other input channel
    /// (side near, side far; front each). The port's output is the curve and pan level gain times Godot's own ear gain on
    /// each input channel at its strength (the same clip recorded to the side at strength 1: 1.266 and 0.630 on its own
    /// channels, no cross-feed), from the recorded side table. Its power per ear is FMOD's within 0.5 dB and its near / far
    /// within 3%, at the render's correlation and at each of the event's two waves' own (0.146, 0.352); the mono mapping put out 1.3 dB under FMOD at 1000 with a near / far of 1.81 for FMOD's 2.20.
    /// </summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void StereoAmbientPansLikeFmod(string variable)
    {
        // FMOD's coefficients are of the mix, not of the content: each of the event's waves (the event picks one at random)
        // is checked with its own channel correlation, and the render's 0.318
        var lib = SoundLibrary.Shared(Path.Combine(DataDir(variable), "Sound"));
        var ev = lib.Fev.AllEvents.First(e => e.FullPath == "MS2Sound/Sound/AMB/11100001");
        var c = ev.Rolloff;
        Assert.That(c, Is.Not.Null);
        var rhos = new List<float> { 0.318f };
        foreach (int di in ev.Defs)
            foreach (var w in lib.Fev.Defs[di].Entries)
            {
                if (w.Kind != 0 || lib.FromWave(w) is not { } clip) continue;
                var pcm = MpegDecoder.Decode(clip);
                Assert.That(pcm.Channels, Is.EqualTo(2), clip.Name);
                float r = Player3DOutput.Correlation(pcm);
                TestContext.Out.WriteLine($"{clip.Name}: correlation {r:F3}");
                Assert.That(r, Is.InRange(-1f, 1f), clip.Name);
                rhos.Add(r);
            }
        Assert.That(rhos.Count, Is.EqualTo(3));   // the render's and the two forest waves
        float dbWorst = 0;
        foreach (float rho in rhos)
        {
            // FMOD (side): distance, near = own + other, far = own
            (float d, float own, float other, float far)[] side =
                { (500, 0.9317f, 0.2062f, 0.7254f), (1000, 0.7619f, 0.3406f, 0.4213f), (1500, 0.5459f, 0.2442f, 0.3019f), (2000, 0.3368f, 0.1505f, 0.1861f) };
            (float d, float own, float other)[] front = { (500, 0.8713f, 0.1458f), (1000, 0.6622f, 0.2408f) };
            foreach (var (d, own, other, far) in side)
            {
                float l = c.PanLevelAt(d), v = c.GainAt(d) * Player3DOutput.PanLevelGain(l, rho);
                float q = GodotFarOverNear(Player3DOutput.PanningStrength(l, rho)), gn = MathF.Sqrt(2 / (1 + q * q));
                float fmodNear = MathF.Sqrt(own * own + other * other + 2 * rho * own * other), portNear = v * gn, portFar = v * gn * q;
                float dbNear = 20 * MathF.Log10(portNear / fmodNear), dbFar = 20 * MathF.Log10(portFar / far);
                TestContext.Out.WriteLine($"rho {rho:F3} side {d}: L {l:F3} near {portNear:F4} / FMOD {fmodNear:F4} ({dbNear:+0.00;-0.00} dB), far {portFar:F4} / {far:F4} ({dbFar:+0.00;-0.00} dB), near / far {portNear / portFar:F3} / {fmodNear / far:F3}");
                Assert.That(dbNear, Is.InRange(-0.5f, 0.5f), $"near at {d}");
                Assert.That(dbFar, Is.InRange(-0.5f, 0.5f), $"far at {d}");
                Assert.That(portNear / portFar, Is.EqualTo(fmodNear / far).Within(0.03f * fmodNear / far), $"near / far at {d}");
                dbWorst = MathF.Max(dbWorst, MathF.Max(MathF.Abs(dbNear), MathF.Abs(dbFar)));
            }
            foreach (var (d, own, other) in front)
            {
                float l = c.PanLevelAt(d), port = c.GainAt(d) * Player3DOutput.PanLevelGain(l, rho);   // Godot: 1 per channel in front
                float fmod = MathF.Sqrt(own * own + other * other + 2 * rho * own * other), db = 20 * MathF.Log10(port / fmod);
                TestContext.Out.WriteLine($"rho {rho:F3} front {d}: L {l:F3} each {port:F4} / FMOD {fmod:F4} ({db:+0.00;-0.00} dB)");
                Assert.That(db, Is.InRange(-0.5f, 0.5f), $"front at {d}");
                dbWorst = MathF.Max(dbWorst, MathF.Abs(db));
            }
        }
        TestContext.Out.WriteLine($"worst {dbWorst:F2} dB");
    }

    /// <summary>The stereo mapping against FMOD Ex's stereo mix (<see cref="StereoAmbientPansLikeFmod"/>) for every pan level
    /// and channel correlation: the side's far / near is FMOD's, the power within 0.36 dB at the side and in front; a mono
    /// clip (NaN) keeps the mono mapping, and the correlation of a clip's channels is read from its samples.</summary>
    [Test]
    public void StereoMapsToGodotsStrength()
    {
        const float c = 0.70710677f;
        for (float rho = -0.5f; rho <= 1.0001f; rho += 0.1f)
            for (float l = 0; l <= 1.0001f; l += 0.05f)
            {
                float s = Player3DOutput.PanningStrength(l, rho), g = Player3DOutput.PanLevelGain(l, rho), m = 1 - MathF.Min(l, 1);
                float near = 1 + l * l + 2 * l * rho, far = m * m, a = m + l * c, b = l * c, each = a * a + b * b + 2 * rho * a * b;
                float q = GodotFarOverNear(s), gn2 = 2 / (1 + q * q);
                Assert.That(q, Is.EqualTo(MathF.Sqrt(far / near)).Within(0.006f), $"L {l} rho {rho}");
                Assert.That(10 * MathF.Log10(g * g * gn2 * (1 + q * q) / (near + far)), Is.InRange(-0.36f, 0.36f), $"side L {l} rho {rho}");
                Assert.That(10 * MathF.Log10(g * g / each), Is.InRange(-0.36f, 0.36f), $"front L {l} rho {rho}");
            }
        Assert.That(Player3DOutput.PanningStrength(0.3f, float.NaN), Is.EqualTo(Player3DOutput.PanningStrength(0.3f)));
        Assert.That(Player3DOutput.PanLevelGain(0.3f, float.NaN), Is.EqualTo(Player3DOutput.PanLevelGain(0.3f)));
        Assert.That(Player3DOutput.PanLevelGain(0, 0.4f), Is.EqualTo(1f).Within(1e-6f));
        Assert.That(Player3DOutput.PanLevelGain(1, 0.4f), Is.EqualTo(MathF.Sqrt(1.4f)).Within(1e-5f));
        var same = new Pcm16 { Rate = 8000, Channels = 2, Samples = new short[] { 100, 100, -50, -50, 30, 30 } };
        var opposite = new Pcm16 { Rate = 8000, Channels = 2, Samples = new short[] { 100, -100, -50, 50 } };
        var mono = new Pcm16 { Rate = 8000, Channels = 1, Samples = new short[] { 100, -50 } };
        Assert.That(Player3DOutput.Correlation(same), Is.EqualTo(1f).Within(1e-6f));
        Assert.That(Player3DOutput.Correlation(opposite), Is.EqualTo(-1f).Within(1e-6f));
        Assert.That(Player3DOutput.Correlation(mono), Is.NaN);
    }

    /// <summary>The 3D curve events with a stereo wave (all its waves, or some: mixed), on both regions; the GMS2 data lacks
    /// the voice banks' waves, so its count is of the waves it has.</summary>
    [TestCase("MS2_DATA", 5905, 536, 7)]
    [TestCase("MS2_GMS2_DATA", 5733, 446, 7)]
    public void StereoCurveEventsAreCounted(string variable, int curves, int stereo, int mixed)
    {
        var lib = SoundLibrary.Shared(Path.Combine(DataDir(variable), "Sound"));
        var fev = lib.Fev;
        int curve = 0, st = 0, mix = 0;
        var groups = new Dictionary<string, int>();
        foreach (var ev in fev.AllEvents)
        {
            if (!ev.Is3D || ev.Rolloff is not { Count: > 0 }) continue;
            curve++;
            int s2 = 0, s1 = 0;
            foreach (int di in ev.Defs)
                foreach (var w in fev.Defs[di].Entries)
                    if (w.Kind == 0 && lib.FromWave(w) is { } clip) { if (clip.Channels == 2) s2++; else s1++; }
            if (s2 == 0) continue;
            st++;
            if (s1 > 0) mix++;
            var g = ev.Group.Split('/').Last();
            groups[g] = groups.GetValueOrDefault(g) + 1;
        }
        TestContext.Out.WriteLine($"{variable}: {curve} curves, {st} with a stereo wave ({mix} mixed): " + string.Join(", ", groups.OrderByDescending(g => g.Value).Select(g => $"{g.Key} {g.Value}")));
        Assert.That((curve, st, mix), Is.EqualTo((curves, stereo, mixed)));
    }

    /// <summary>The project leaves audio/general/3d_panning_strength at Godot's 0.5, which the strengths assume.</summary>
    [Test]
    public void ProjectKeepsGodotsGlobalPanningStrength()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "game", "project.godot"))) dir = dir.Parent;
        if (dir == null) Assert.Ignore("no game/project.godot above the tests");
        var text = File.ReadAllText(Path.Combine(dir.FullName, "game", "project.godot"));
        Assert.That(text, Does.Not.Contain("3d_panning_strength"));
        Assert.That(Player3DOutput.GlobalPanningStrength, Is.EqualTo(0.5f));
    }

    [Test]
    public void SetsAPanningStrengthOnlyWhenItChanges()
    {
        var c = new DistanceRolloff(new[] { 0f, 1000f }, new[] { 1f, 0f }, panLevel: 0.5f,
            panDistance: new[] { 0f, 100f, 500f }, panValue: new[] { 0f, 0f, 1f });
        var g = new RolloffGains(2);
        g.Set(0, c, 1f, new Vector3(50, 0, 0));
        Assert.That(g.PanChanged(0, out float s), Is.True);                       // the first after Set
        Assert.That(s, Is.EqualTo(0f));
        Assert.That(g.PanChanged(0, out _), Is.False);
        g.Update(0, Vector3.Zero, out _);
        Assert.That(g.PanChanged(0, out _), Is.False);                            // 50: still 2D
        g.Move(0, new Vector3(500, 0, 0));
        Assert.That(g.Update(0, Vector3.Zero, out float db), Is.True);
        Assert.That(g.PanChanged(0, out s), Is.True);
        Assert.That(s, Is.EqualTo(1f).Within(1e-5f));                            // L 0.5 -> strength 1
        Assert.That(db, Is.EqualTo(RolloffGains.ToDb(c.GainAt(500) * Player3DOutput.PanLevelGain(0.5f))).Within(1e-4f));
        g.Move(0, new Vector3(501, 0, 0));
        g.Update(0, Vector3.Zero, out _);
        Assert.That(g.PanChanged(0, out _), Is.False);                            // past the envelope: the same
        Assert.That(g.PanChanged(1, out _), Is.False);                            // no curve: the player keeps its own
    }

    /// <summary>The volume and the panning strength of 32 voices with a pan envelope, half of them stereo, the listener moving
    /// every frame: no allocation, and the cost per voice.</summary>
    [TestCase(32)]
    public void PanningAllocatesNothing(int voices)
    {
        var c = new DistanceRolloff(new[] { 0f, 1185.5f }, new[] { 1f, 0f }, panLevel: 1,
            panDistance: new[] { 0f, 73.7f, 461.9f }, panValue: new[] { 0f, 0f, 0.523f },
            shape: new[] { 1, 1 }, panShape: new[] { 1, 1, 1 });
        var g = new RolloffGains(voices);
        for (int i = 0; i < voices; i++) g.Set(i, c, 0.8f, new Vector3(30 * i, 0, 20 * i), stereoCorrelation: i % 2 == 0 ? float.NaN : 0.32f);   // half stereo
        int sets = 0, pans = 0;
        void Frame(int f)
        {
            var l = new Vector3(13 * f % 1500, 0, 7 * f % 900);
            for (int i = 0; i < voices; i++)
            {
                if (g.Update(i, l, out _)) sets++;
                if (g.PanChanged(i, out _)) pans++;
            }
        }
        for (int f = 0; f < 200; f++) Frame(f);
        const int frames = 20000, runs = 3;
        void Run() { for (int f = 0; f < frames; f++) Frame(f); }
        var sw = Stopwatch.StartNew();
        long bytes = AllocatedBytes.LeastOf(runs, Run);
        sw.Stop();
        TestContext.Out.WriteLine($"{voices} voices: {bytes} B over {frames} frames, {sw.Elapsed.TotalMilliseconds * 1e6 / (runs * frames * (double)voices):F1} ns per voice per frame, {sets} volume sets, {pans} pan sets");
        Assert.That(bytes, Is.Zero);
        Assert.That(pans, Is.GreaterThan(0));
    }

    /// <summary>
    /// Godot 4.7's far / near channel for a player straight to the side at strength s under the global 0.5, recorded: a mono
    /// event played 600 to the listener's left by an AudioStreamPlayer3D on a bus captured at 0 dB (the Dummy driver), the
    /// strength stepped over these. The sum of squares of the two channels was the same at every strength (to 1e-6), so
    /// Godot's near gain is sqrt(2 / (1 + q^2)) and its far q times that. 2 mutes the far channel.
    /// </summary>
    static readonly (float s, float q)[] GodotSide =
        { (0, 1), (0.25f, 0.874f), (0.5f, 0.7485f), (1, 0.4975f), (1.25f, 0.3712f), (1.5f, 0.2456f), (1.75f, 0.1201f), (2, 0) };

    /// <summary>Godot's near / far at strength 1 from 60, 45 and 30 degrees to the left, recorded the same way.</summary>
    static readonly (int deg, float ratio)[] GodotAtStrengthOne = { (60, 1.786f), (45, 1.578f), (30, 1.366f) };

    /// <summary>Godot's far / near at the side for strength <paramref name="s"/>, between the recorded strengths.</summary>
    static float GodotFarOverNear(float s)
    {
        s = Math.Clamp(s, 0, 2);
        for (int i = 1; i < GodotSide.Length; i++)
            if (s <= GodotSide[i].s)
            {
                var (s0, q0) = GodotSide[i - 1];
                var (s1, q1) = GodotSide[i];
                return q0 + (q1 - q0) * (s - s0) / (s1 - s0);
            }
        return 0;
    }

    /// <summary>Godot's near / far at the side for strength <paramref name="s"/>.</summary>
    static float GodotSideRatio(float s) => 1 / GodotFarOverNear(s);

    /// <summary>FMOD's left and right for pan level <paramref name="l"/> and a source <paramref name="deg"/> to the left.</summary>
    static (float l, float r) Fmod(float l, int deg)
    {
        float x = MathF.Sin(deg * MathF.PI / 180);
        float a = (1 - x) * MathF.PI / 4;
        return (l * MathF.Cos(a) + 1 - l, l * MathF.Sin(a) + 1 - l);
    }

    static DistanceRolloff Event(string variable, string groupPart, string name)
    {
        var ev = Fev(variable).AllEvents.FirstOrDefault(e => e.Name == name && !e.IsSimple && e.Is3D && e.Group.Contains("/" + groupPart));
        Assert.That(ev, Is.Not.Null, name);
        Assert.That(ev.Rolloff, Is.Not.Null, ev.FullPath);
        return ev.Rolloff;
    }

    static readonly Dictionary<string, FevProject> loaded = new();

    /// <summary>The data a case names (DistanceRolloffTests.DataOf: MS2_KMS_DATA stands in when MS2_DATA is the GMS2 data).</summary>
    /// <summary>The bank's project of a region's data (<see cref="DataDir"/>), loaded once.</summary>
    internal static FevProject Fev(string variable)
    {
        var f = Path.Combine(DataDir(variable), "Sound", "MS2Sound.fev");
        lock (loaded) return loaded.TryGetValue(f, out var p) ? p : loaded[f] = FevProject.Load(f);
    }

    /// <summary>The data folder of <paramref name="variable"/> holding Sound/MS2Sound.fev (MS2_DATA's KMS one when MS2_DATA
    /// is the GMS2 data); the test is ignored without one.</summary>
    internal static string DataDir(string variable)
    {
        var d = Environment.GetEnvironmentVariable(variable);
        if (variable == "MS2_DATA" && !string.IsNullOrEmpty(d) && Environment.GetEnvironmentVariable("MS2_GMS2_DATA") is { Length: > 0 } gms2
            && Path.GetFullPath(gms2).TrimEnd('/') == Path.GetFullPath(d).TrimEnd('/'))
        {
            d = Environment.GetEnvironmentVariable("MS2_KMS_DATA");
            if (string.IsNullOrEmpty(d)) Assert.Ignore("MS2_DATA is the GMS2 data and MS2_KMS_DATA is not set");
        }
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore($"{variable} is not set");
        var f = Path.Combine(d, "Sound", "MS2Sound.fev");
        if (!File.Exists(f)) Assert.Ignore($"{variable} has no Sound/MS2Sound.fev");
        return d;
    }
}
