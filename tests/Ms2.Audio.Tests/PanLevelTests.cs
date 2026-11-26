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
    /// keeps its points' shapes.</summary>
    [TestCase("MS2_DATA", 742, 14)]
    [TestCase("MS2_GMS2_DATA", 737, 14)]
    public void EveryPointIsACurveOrALine(string variable, int curve, int linear)
    {
        var fev = Fev(variable);
        var shapes = (int[])typeof(FevProject).GetField("envShape", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fev);
        int c = shapes.Count(s => s == DistanceRolloff.PointCurve), l = shapes.Count(s => s == DistanceRolloff.PointLinear);
        TestContext.Out.WriteLine($"{variable}: {shapes.Length} points, {c} curve, {l} linear");
        Assert.That((c, l, shapes.Length), Is.EqualTo((curve, linear, curve + linear)));
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
        Assert.That(1 / (1 - at200), Is.EqualTo(GodotSideRatio(Player3DOutput.PanningStrength(at200))).Within(1e-4f));
        Assert.That(1 / (1 - at200), Is.InRange(1.1f, 1.25f));
    }

    /// <summary>
    /// The pan law: FMOD's left / right at pan level L for a source at angle a from the front is L l3 + 1 - L, L r3 + 1 - L
    /// with the constant power l3 = cos((1 - sin a) pi / 4), r3 = sin(...); Godot's at strength s (global 0.5) keeps the
    /// power 1 + 1 with the side ratio 1 / (1 - s / 2). At s = L / 0.5 and the gain PanLevelGain(L) the side ratio is FMOD's
    /// and the power within 0.35 dB of it at every angle and level. Between the front and the side, the recordings: Godot at
    /// s = 1 (60 degrees 1.786, 45 1.578, 30 1.366) and the FMOD render of the meso pickup at 600, L 0.523 (1.833, 1.603,
    /// 1.377), within 3% (at the side Godot's 2.010 at s = 1 is the law's 1 / (1 - 0.5), FMOD's 2.095 its 1 / (1 - 0.523)).
    /// </summary>
    [Test]
    public void PanLevelMapsToGodotsStrength()
    {
        for (float l = 0; l <= 1.0001f; l += 0.05f)
        {
            float s = Player3DOutput.PanningStrength(l);
            if (l < 0.999f) Assert.That(GodotSideRatio(s), Is.EqualTo(1 / (1 - l)).Within(1e-3f * (1 / (1 - l))), $"L {l}");
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
        (int deg, float godot, float fmod)[] recorded = { (60, 1.786f, 1.833f), (45, 1.578f, 1.603f), (30, 1.366f, 1.377f) };
        foreach (var (deg, godot, fmod) in recorded)
            Assert.That(godot, Is.EqualTo(fmod).Within(0.03f * fmod), $"{deg} degrees");
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

    /// <summary>The volume and the panning strength of 32 voices with a pan envelope, the listener moving every frame:
    /// no allocation, and the cost per voice.</summary>
    [TestCase(32)]
    public void PanningAllocatesNothing(int voices)
    {
        var c = new DistanceRolloff(new[] { 0f, 1185.5f }, new[] { 1f, 0f }, panLevel: 1,
            panDistance: new[] { 0f, 73.7f, 461.9f }, panValue: new[] { 0f, 0f, 0.523f },
            shape: new[] { 1, 1 }, panShape: new[] { 1, 1, 1 });
        var g = new RolloffGains(voices);
        for (int i = 0; i < voices; i++) g.Set(i, c, 0.8f, new Vector3(30 * i, 0, 20 * i));
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
        const int frames = 20000;
        var sw = Stopwatch.StartNew();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < frames; f++) Frame(f);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        sw.Stop();
        TestContext.Out.WriteLine($"{voices} voices: {bytes} B over {frames} frames, {sw.Elapsed.TotalMilliseconds * 1e6 / (frames * (double)voices):F1} ns per voice per frame, {sets} volume sets, {pans} pan sets");
        Assert.That(bytes, Is.Zero);
        Assert.That(pans, Is.GreaterThan(0));
    }

    /// <summary>Godot 4.7's left / right at the side for a player at strength <paramref name="s"/> under the global 0.5.</summary>
    static float GodotSideRatio(float s) => 1 / (1 - s * Player3DOutput.GlobalPanningStrength);

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
