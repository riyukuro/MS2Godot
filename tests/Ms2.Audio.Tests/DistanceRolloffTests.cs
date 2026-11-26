using System.Diagnostics;
using System.Numerics;
using Ms2.Audio;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>
/// The distance rolloff of every event of MS2Sound.fev on MS2_DATA (KMS2_DEV or KMS2) and MS2_GMS2_DATA (GMS2): each complex
/// 3D event's volume envelope (effect 0xc, built-in) on its "(distance)" parameter, the gain the client's FMOD plays it at
/// (every one of them sets FMOD_3D_CUSTOMROLLOFF). The old falloff (Godot's inverse distance from a unit size of the min
/// distance or 300 / 60) is far from it: ambient 11100001 is half volume at 2250, where 60 / 2250 gave 0.03.
/// </summary>
public class DistanceRolloffTests
{
    [TestCase("MS2_DATA", 5906, 5900, 5881)]
    [TestCase("MS2_GMS2_DATA", 5734, 5728, 5709)]
    public void EveryComplex3DEventHasItsCurve(string variable, int complex3d, int startFull, int endSilent)
    {
        var fev = Fev(variable);
        int n3d = 0, withCurve = 0, full = 0, silent = 0, withSound = 0, soundNoCurve = 0;
        var bad = new List<string>();
        foreach (var ev in fev.AllEvents)
        {
            if (ev.IsSimple || !ev.Is3D)
            {
                if (ev.Rolloff != null) bad.Add($"2D or simple with a curve: {ev.FullPath}");
                continue;
            }
            n3d++;
            if (ev.Defs.Count > 0) withSound++;
            var c = ev.Rolloff;
            if (c == null)
            {
                if (ev.Defs.Count > 0) { soundNoCurve++; TestContext.Out.WriteLine($"no curve: {ev.FullPath} {ev.MinDistance}..{ev.MaxDistance}"); }
                continue;
            }
            withCurve++;
            if (c.GainOf(0) == 1) full++;
            if (c.GainOf(c.Count - 1) == 0) silent++;
            if (c.DistanceOf(0) < ev.MinDistance || c.DistanceOf(c.Count - 1) > ev.MaxDistance + 0.01f)
                bad.Add($"curve outside {ev.MinDistance}..{ev.MaxDistance}: {ev.FullPath} {c}");
            for (int i = 1; i < c.Count; i++)
                if (c.DistanceOf(i) < c.DistanceOf(i - 1) || c.GainOf(i) < 0 || c.GainOf(i) > 1) { bad.Add($"bad points: {ev.FullPath} {c}"); break; }
        }
        TestContext.Out.WriteLine($"{variable}: {n3d} complex 3D, {withSound} with a sound, {withCurve} with a curve ({full} start at 1, {silent} end at 0), {soundNoCurve} sounding without");
        foreach (var b in bad.Take(20)) TestContext.Out.WriteLine(b);
        Assert.That(bad, Is.Empty);
        // Voice/ko/Monster/60000700 has no "(distance)" parameter (its record holds 1 / 1): it keeps the old falloff
        Assert.That((n3d, withCurve, full, silent), Is.EqualTo((complex3d, complex3d - 1, startFull, endSilent)));
        Assert.That(soundNoCurve, Is.EqualTo(1));
    }

    /// <summary>The rolloff bits of every event's mode word (+0x34): custom on every complex 3D event and on most others, log
    /// only on 2D events (not attenuated), linear on none, and never two of them.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void RolloffBitsOfEveryEvent(string variable)
    {
        var fev = Fev(variable);
        var counts = new SortedDictionary<string, int>();
        foreach (var ev in fev.AllEvents)
        {
            int m = ev.Mode;
            string bits = ((m & FevProject.CustomRolloff) != 0 ? "custom" : "") + ((m & FevProject.LinearRolloff) != 0 ? "linear" : "")
                + ((m & FevProject.LogRolloff) != 0 ? "log" : "");
            string k = $"{(ev.IsSimple ? "simple" : "complex")} {(ev.Is3D ? "3d" : "2d")} {(bits == "" ? "none" : bits)}";
            counts[k] = counts.GetValueOrDefault(k) + 1;
        }
        foreach (var (k, n) in counts) TestContext.Out.WriteLine($"{variable}: {n} {k}");
        Assert.That(counts.Keys.Where(k => k.EndsWith("custom") || k.EndsWith("log") || k.EndsWith("linear") || k.EndsWith("none")), Is.EquivalentTo(counts.Keys));
        Assert.That(counts.Keys.Where(k => k.Contains(" 3d ") && !k.EndsWith("custom")), Is.Empty);
        Assert.That(counts.Keys.Where(k => k.EndsWith("linear")), Is.Empty);
        Assert.That(fev.AllEvents.Count(e => e.Is3D && e.RolloffModel != RolloffModel.Custom), Is.Zero);
    }

    /// <summary>Tria's town ambience (the field's MS2Sound entity AMB_City_0) is a 2D event: no curve, not attenuated.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void TownAmbienceIs2D(string variable)
    {
        var ev = Fev(variable).AllEvents.First(e => e.Name == "11100015" && e.Group.EndsWith("/AMB"));
        Assert.That((ev.Is3D, ev.IsSimple, ev.Rolloff), Is.EqualTo((false, true, (DistanceRolloff)null)));
        Assert.That(new SoundClip { Event = ev }.Is2D, Is.True);
    }

    [Test]
    public void LinearAndLogFollowFmodsModel()
    {
        var lin = new DistanceRolloff(System.Array.Empty<float>(), System.Array.Empty<float>(), RolloffModel.Linear, 100, 1100);
        Assert.That(lin.GainAt(50), Is.EqualTo(1f));
        Assert.That(lin.GainAt(600), Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(lin.GainAt(2000), Is.EqualTo(0f));
        Assert.That(lin.Silent, Is.EqualTo(1100f));
        var log = new DistanceRolloff(System.Array.Empty<float>(), System.Array.Empty<float>(), RolloffModel.Log, 100, 1000);
        Assert.That(log.GainAt(100), Is.EqualTo(1f));
        Assert.That(log.GainAt(400), Is.EqualTo(0.25f).Within(1e-5f));
        Assert.That(log.GainAt(5000), Is.EqualTo(0.1f).Within(1e-5f));   // held past the max
        Assert.That(log.Silent, Is.EqualTo(float.PositiveInfinity));
        // a linear event's "(distance)" envelope multiplies the model
        var both = new DistanceRolloff(new[] { 0f, 1100f }, new[] { 1f, 0.5f }, RolloffModel.Linear, 100, 1100);
        Assert.That(both.GainAt(600), Is.EqualTo(0.5f * (1 - 0.5f * 600 / 1100)).Within(1e-5f));
    }

    [Test]
    public void AmbientIsHalfVolumeHalfWay()
    {
        var c = Event("MS2_DATA", "AMB", "11100001");
        Assert.That(c.GainAt(0), Is.EqualTo(1f));
        Assert.That(c.GainAt(2250), Is.EqualTo(0.5f).Within(1e-4f));   // the old falloff: 60 / 2250 = 0.027
        Assert.That(c.GainAt(4500), Is.EqualTo(0f));
        Assert.That(c.GainAt(9000), Is.EqualTo(0f));
        Assert.That(c.Silent, Is.EqualTo(4500f));
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void HitSoundsKeepTheirShape(string variable)
    {
        // full -> 0 at 68.9% of the max (2449 events on KMS), e.g. Attack/Hit/00001001
        var a = Event(variable, "Hit", "00001001");
        TestContext.Out.WriteLine($"00001001 {a}");
        float zero = a.Silent;
        Assert.That(a.GainAt(0), Is.EqualTo(1f));
        Assert.That(a.GainAt(zero / 2), Is.EqualTo(0.5f).Within(1e-3f));
        Assert.That(a.GainAt(zero + 1), Is.EqualTo(0f));
        Assert.That(zero / a.DistanceOf(a.Count - 1), Is.EqualTo(0.689f).Within(0.001f));
        // full to 34.4%, then -> 0 at 68% (1550 events), e.g. Hit/00008001
        var b = Event(variable, "Hit", "00008001");
        TestContext.Out.WriteLine($"00008001 {b}");
        float max = b.DistanceOf(b.Count - 1);
        Assert.That(b.GainAt(0.34f * max), Is.EqualTo(1f));
        Assert.That(b.GainAt(0.68f * max), Is.EqualTo(0f).Within(1e-3f));
        Assert.That(b.GainAt(0.512f * max), Is.EqualTo(0.5f).Within(0.01f));
    }

    [Test]
    public void SetsAVolumeOnlyWhenItChanges()
    {
        var c = new DistanceRolloff(new[] { 0f, 4500f }, new[] { 1f, 0f });
        var g = new RolloffGains(2);
        g.Set(0, c, 1f, new Vector3(2250, 0, 0));
        Assert.That(g.Update(0, Vector3.Zero, out float db), Is.True);
        Assert.That(db, Is.EqualTo(-6.02f).Within(0.01f));
        Assert.That(g.Update(0, Vector3.Zero, out _), Is.False);                 // the same place: no set
        Assert.That(g.Update(0, new Vector3(1, 0, 0), out _), Is.False);         // under StepDb
        Assert.That(g.Update(0, new Vector3(500, 0, 0), out db), Is.True);
        Assert.That(db, Is.EqualTo(RolloffGains.ToDb(1f - 1750f / 4500f)).Within(1e-3f));
        g.Move(0, new Vector3(9000, 0, 0));
        Assert.That(g.Update(0, Vector3.Zero, out db), Is.True);
        Assert.That(db, Is.EqualTo(RolloffGains.SilentDb));
        Assert.That(g.Update(1, Vector3.Zero, out _), Is.False);                 // no curve: the player keeps its own
    }

    /// <summary><paramref name="voices"/> voices live (up to the client's 100 channels), the listener moving every frame: no
    /// allocation, and the cost per voice.</summary>
    [TestCase(32)]
    [TestCase(EffectVoiceSlots.ClientChannels)]
    public void LiveVoicesAllocateNothing(int voices)
    {
        var c = new DistanceRolloff(new[] { 0f, 0f, 1548f, 3060f, 4500f }, new[] { 1f, 1f, 1f, 0f, 0f });
        var g = new RolloffGains(voices);
        for (int i = 0; i < voices; i++) g.Set(i, c, 0.8f, new Vector3(100 * i, 0, 50 * i));
        int sets = 0;
        void Frame(int f)
        {
            var l = new Vector3(37 * f % 3000, 0, 11 * f % 2000);
            for (int i = 0; i < voices; i++) if (g.Update(i, l, out _)) sets++;
        }
        for (int f = 0; f < 200; f++) Frame(f);
        const int frames = 20000;
        var sw = new Stopwatch();
        long before = GC.GetAllocatedBytesForCurrentThread();
        sw.Start();
        for (int f = 0; f < frames; f++) Frame(f);
        sw.Stop();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        double nsPerVoice = sw.Elapsed.TotalMilliseconds * 1e6 / (frames * (double)voices);
        TestContext.Out.WriteLine($"{voices} voices: {bytes} B over {frames} frames, {nsPerVoice:F1} ns per voice per frame, {sets} volume sets");
        Assert.That(bytes, Is.Zero);
    }

    static DistanceRolloff Event(string variable, string groupPart, string name)
    {
        var fev = Fev(variable);
        var ev = fev.AllEvents.FirstOrDefault(e => e.Name == name && !e.IsSimple && e.Is3D && e.Group.Contains("/" + groupPart));
        Assert.That(ev, Is.Not.Null, name);
        Assert.That(ev.Rolloff, Is.Not.Null, ev.FullPath);
        TestContext.Out.WriteLine($"{ev.FullPath} {ev.MinDistance}..{ev.MaxDistance}");
        return ev.Rolloff;
    }

    static readonly Dictionary<string, FevProject> loaded = new();

    /// <summary>
    /// The data a case names: MS2_GMS2_DATA as set; MS2_DATA is the KMS case, so when it is the GMS2 data (the same folder as
    /// MS2_GMS2_DATA) MS2_KMS_DATA stands in, and with that unset the case is ignored (the GMS2 case covers that data).
    /// </summary>
    static string DataOf(string variable)
    {
        var d = Environment.GetEnvironmentVariable(variable);
        if (variable != "MS2_DATA" || string.IsNullOrEmpty(d)) return d;
        var gms2 = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(gms2) || Path.GetFullPath(gms2).TrimEnd('/') != Path.GetFullPath(d).TrimEnd('/')) return d;
        var kms = Environment.GetEnvironmentVariable("MS2_KMS_DATA");
        if (string.IsNullOrEmpty(kms)) Assert.Ignore("MS2_DATA is the GMS2 data and MS2_KMS_DATA is not set");
        return kms;
    }

    static FevProject Fev(string variable)
    {
        var d = DataOf(variable);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore($"{variable} is not set");
        var f = Path.Combine(d, "Sound", "MS2Sound.fev");
        if (!File.Exists(f)) Assert.Ignore($"{variable} has no Sound/MS2Sound.fev");
        lock (loaded) return loaded.TryGetValue(f, out var p) ? p : loaded[f] = FevProject.Load(f);
    }
}
