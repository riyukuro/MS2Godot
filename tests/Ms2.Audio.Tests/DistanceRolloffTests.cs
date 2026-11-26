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
    /// <summary>Every 3D event has a curve: the complex ones their "(distance)" envelopes, the simple ones (and complex
    /// Voice/ko/Monster/60000700, which has no "(distance)" parameter, its record holding 1 / 1) a flat one, gain 1 at every
    /// distance as FMOD renders them (<see cref="FlatEventsHoldTheirLevelAsFmodRenders"/>); 2D events none. The 3D Pan Level
    /// (0x400) and direct occlusion (0x2000, sub-index 0) envelopes are on every complex 3D event that has them. The pan level
    /// property is 1 on all; the one pan level under 1 is 60000700's, 0, its envelope held at its first point (FMOD's pan3d 0).
    /// Silent: the curves whose volume ends at 0, and those whose direct occlusion ends at 1; none of the 24 never silent ones
    /// (volume ending at 1, 0.132, 0.024 or 0.004) has an occlusion envelope, so the occlusion adds none.</summary>
    [TestCase("MS2_DATA", 5906, 45, 5900, 5881, 5905, 4481, 5881)]
    [TestCase("MS2_GMS2_DATA", 5734, 10, 5728, 5709, 5733, 4384, 5709)]
    public void Every3DEventHasItsCurve(string variable, int complex3d, int simple3d, int startFull, int endSilent, int withPan, int withOcclusion,
        int silentAny)
    {
        var fev = Fev(variable);
        int n3d = 0, nSimple = 0, withPoints = 0, flat = 0, full = 0, silent = 0, pan = 0, occlusion = 0, finite = 0;
        var panUnder1 = new List<string>();
        var bad = new List<string>();
        foreach (var ev in fev.AllEvents)
        {
            var c = ev.Rolloff;
            if (!ev.Is3D)
            {
                if (c != null) bad.Add($"2D with a curve: {ev.FullPath}");
                continue;
            }
            if (ev.IsSimple) nSimple++; else n3d++;
            if (c == null) { bad.Add($"3D without a curve: {ev.FullPath}"); continue; }
            if (ev.PanLevel != 1) bad.Add($"pan level property {ev.PanLevel}: {ev.FullPath}");
            if (c.PanLevel != 1) panUnder1.Add($"{ev.FullPath.Substring(ev.FullPath.IndexOf("/Sound/") + 1)} {c.PanLevel}");
            if (c.HasPanLevelEnvelope) pan++;
            if (c.HasOcclusion) occlusion++;
            if (c.Count == 0)
            {
                flat++;
                if (c.GainAt(0) != 1 || c.GainAt(1e5f) != 1) bad.Add($"flat curve not 1: {ev.FullPath} {c}");
                continue;
            }
            withPoints++;
            if (c.GainOf(0) == 1) full++;
            if (c.GainOf(c.Count - 1) == 0) silent++;
            if (c.Silent < float.PositiveInfinity) finite++;
            if (c.DistanceOf(0) < ev.MinDistance || c.DistanceOf(c.Count - 1) > ev.MaxDistance + 0.01f)
                bad.Add($"curve outside {ev.MinDistance}..{ev.MaxDistance}: {ev.FullPath} {c}");
            for (int i = 1; i < c.Count; i++)
                if (c.DistanceOf(i) < c.DistanceOf(i - 1) || c.GainOf(i) < 0 || c.GainOf(i) > 1) { bad.Add($"bad points: {ev.FullPath} {c}"); break; }
            for (float d = 0; d <= ev.MaxDistance; d += ev.MaxDistance / 64)
            {
                float g = c.GainAt(d), l = c.PanLevelAt(d);
                if (!(g >= 0 && g <= 1 && l >= 0 && l <= 1)) { bad.Add($"gain {g} pan {l} at {d}: {ev.FullPath} {c}"); break; }
            }
        }
        TestContext.Out.WriteLine($"{variable}: {n3d} complex 3D, {nSimple} simple 3D; {withPoints} with points ({full} start at 1, {silent} end at 0), "
            + $"{flat} flat; {pan} with a 3D pan level envelope, {occlusion} with a direct occlusion one, {finite} silent somewhere; "
            + $"pan level under 1: {string.Join(", ", panUnder1)}");
        foreach (var g in fev.AllEvents.Where(e => e.Is3D && e.Rolloff?.Count == 0).GroupBy(e => e.Group).OrderBy(g => g.Key))
            TestContext.Out.WriteLine($"flat: {g.Key} {g.Count()} ({string.Join(" ", g.Take(4).Select(e => e.Name))})");
        foreach (var ev in fev.AllEvents.Where(e => e.Is3D && e.IsSimple))
            TestContext.Out.WriteLine($"simple 3D: {ev.FullPath} mode 0x{ev.Mode:x} sounds {ev.Defs.Count} vol {ev.Volume} {ev.Rolloff}");
        foreach (var ev in fev.AllEvents.Where(e => e.Rolloff?.HasOcclusion == true).GroupBy(e => e.Rolloff.ToString()).OrderByDescending(g => g.Count()).Take(12))
            TestContext.Out.WriteLine($"occlusion x{ev.Count()}: {ev.First().FullPath} {ev.First().MinDistance}..{ev.First().MaxDistance} {ev.Key}");
        foreach (var g in fev.AllEvents.Where(e => e.Is3D && e.Rolloff?.Count > 0 && e.Rolloff.Silent == float.PositiveInfinity)
                     .GroupBy(e => $"volume {e.Rolloff.GainOf(e.Rolloff.Count - 1):0.###} occlusion {e.Rolloff.OcclusionAt(1e6f):0.###}"))
            TestContext.Out.WriteLine($"never silent x{g.Count()}: {g.Key} ({string.Join(" ", g.Take(3).Select(e => e.FullPath))})");
        foreach (var b in bad.Take(20)) TestContext.Out.WriteLine(b);
        Assert.That(bad, Is.Empty);
        Assert.That((n3d, nSimple, withPoints, flat, full, silent), Is.EqualTo((complex3d, simple3d, complex3d - 1, simple3d + 1, startFull, endSilent)));
        Assert.That((pan, occlusion, finite), Is.EqualTo((withPan, withOcclusion, silentAny)));
        Assert.That(panUnder1, Is.EqualTo(new[] { "Sound/Voice/ko/Monster/60000700 0" }));
    }

    /// <summary>The direct occlusion takes 1 - occ of the whole voice, not blended by the 3D Pan Level envelope: Copy 06102549 (the loader's census row: volume full to 34.4%, 0 at 68%; pan 0 to 16.3%, 0.466 at 41.1%
    /// and on; occlusion 0 to 1 at 99.6%). The old curve was the volume envelope only, and a simple 3D event had none.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void PanLevelAndOcclusionShapeTheGain(string variable)
    {
        var fev = Fev(variable);
        var ev = fev.AllEvents.FirstOrDefault(e => e.Name == "06102549 Copy" && !e.IsSimple && e.Is3D);
        if (ev == null) Assert.Ignore("no 06102549 Copy");
        var c = ev.Rolloff;
        TestContext.Out.WriteLine($"{ev.FullPath} {ev.MinDistance}..{ev.MaxDistance} {c}");
        Assert.That((c.HasPanLevelEnvelope, c.HasOcclusion), Is.EqualTo((true, true)));
        // the volume points are at 0 and 1 of the "(distance)" parameter: its range
        float at = c.DistanceOf(0), r = c.DistanceOf(c.Count - 1) - at;
        Assert.That(c.PanLevelAt(at), Is.EqualTo(0f));
        Assert.That(c.PanLevelAt(at + 0.5f * r), Is.EqualTo(0.466f).Within(1e-3f));
        Assert.That(c.GainAt(at + 0.1f * r), Is.EqualTo(1f - S(0.1f / 0.996f)).Within(2e-3f)); // pan 0: the occlusion still applies
        Assert.That(c.GainAt(at + 0.68f * r), Is.EqualTo(0f).Within(1e-3f));       // the volume envelope is not blended
        // at 30%: volume 1, occlusion S(0.3 / 0.996) (the points' shape is smoothstep), whatever the pan level
        Assert.That(c.GainAt(at + 0.3f * r), Is.EqualTo(1f - S(0.3f / 0.996f)).Within(2e-3f));
        // at 50%: the volume envelope's 1 - S(0.156 / 0.336) (the old gain), times 1 - S(0.502)
        Assert.That(c.GainAt(at + 0.5f * r), Is.EqualTo((1f - S(0.156f / 0.336f)) * (1f - S(0.5f / 0.996f))).Within(2e-3f));
        var simple = fev.AllEvents.First(e => e.IsSimple && e.Is3D);
        Assert.That(simple.Rolloff, Is.Not.Null, simple.FullPath);
        Assert.That(simple.Rolloff.GainAt(1e5f), Is.EqualTo(1f));   // FMOD holds it: FlatEventsHoldTheirLevelAsFmodRenders
        Assert.That(simple.Rolloff.Silent, Is.EqualTo(float.PositiveInfinity));
    }

    /// <summary>The rolloff bits of every event's mode word (+0x34): custom on every complex 3D event and on most others,
    /// inverse only on 2D events (not attenuated), linear and linear square on none, and never two of them.</summary>
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
                + ((m & FevProject.LinearSquareRolloff) != 0 ? "linearsquare" : "") + ((m & FevProject.InverseRolloff) != 0 ? "inverse" : "");
            string k = $"{(ev.IsSimple ? "simple" : "complex")} {(ev.Is3D ? "3d" : "2d")} {(bits == "" ? "none" : bits)}";
            counts[k] = counts.GetValueOrDefault(k) + 1;
        }
        foreach (var (k, n) in counts) TestContext.Out.WriteLine($"{variable}: {n} {k}");
        Assert.That(counts.Keys.Where(k => k.EndsWith("custom") || k.EndsWith("inverse") || k.EndsWith(" linear") || k.EndsWith("linearsquare") || k.EndsWith("none")), Is.EquivalentTo(counts.Keys));
        Assert.That(counts.Keys.Where(k => k.Contains(" 3d ") && !k.EndsWith("custom")), Is.Empty);
        Assert.That(counts.Keys.Where(k => k.EndsWith(" linear") || k.EndsWith("linearsquare")), Is.Empty);
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

    /// <summary>The direct occlusion takes the whole voice, so the gain is 0 from where it stays 1: the voice is silent there
    /// though its volume envelope is not 0.</summary>
    [Test]
    public void OcclusionOfOneIsSilent()
    {
        var c = new DistanceRolloff(new[] { 0f, 1000 }, new[] { 1f, 0.5f }, occlusionDistance: new[] { 0f, 400, 800 }, occlusionValue: new[] { 0f, 1, 1 });
        Assert.That((c.Silent, c.GainAt(600), c.GainAt(1000)), Is.EqualTo((400f, 0f, 0f)));
        var open = new DistanceRolloff(new[] { 0f, 1000 }, new[] { 1f, 0.5f }, occlusionDistance: new[] { 0f, 800 }, occlusionValue: new[] { 0f, 0.9f });
        Assert.That(open.Silent, Is.EqualTo(float.PositiveInfinity));
        var both = new DistanceRolloff(new[] { 0f, 300, 1000 }, new[] { 1f, 0, 0 }, occlusionDistance: new[] { 0f, 400 }, occlusionValue: new[] { 0f, 1 });
        Assert.That(both.Silent, Is.EqualTo(300f));
    }

    [Test]
    public void LinearSquareAndInverseFollowFmodsModel()
    {
        var lin = new DistanceRolloff(System.Array.Empty<float>(), System.Array.Empty<float>(), RolloffModel.Linear, 100, 1100);
        Assert.That(lin.GainAt(50), Is.EqualTo(1f));
        Assert.That(lin.GainAt(600), Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(lin.GainAt(2000), Is.EqualTo(0f));
        Assert.That(lin.Silent, Is.EqualTo(1100f));
        var square = new DistanceRolloff(System.Array.Empty<float>(), System.Array.Empty<float>(), RolloffModel.LinearSquare, 100, 1100);
        Assert.That(square.GainAt(50), Is.EqualTo(1f));
        Assert.That(square.GainAt(600), Is.EqualTo(0.25f).Within(1e-5f));
        Assert.That(square.GainAt(850), Is.EqualTo(0.0625f).Within(1e-5f));
        Assert.That(square.GainAt(2000), Is.EqualTo(0f));
        Assert.That(square.Silent, Is.EqualTo(1100f));
        var inverse = new DistanceRolloff(System.Array.Empty<float>(), System.Array.Empty<float>(), RolloffModel.Inverse, 100, 1000);
        Assert.That(inverse.GainAt(100), Is.EqualTo(1f));
        Assert.That(inverse.GainAt(400), Is.EqualTo(0.25f).Within(1e-5f));
        Assert.That(inverse.GainAt(5000), Is.EqualTo(0.1f).Within(1e-5f));   // held past the max
        Assert.That(inverse.Silent, Is.EqualTo(float.PositiveInfinity));
        Assert.That(new FevEvent { Mode = 0x10 | FevProject.LinearSquareRolloff }.RolloffModel, Is.EqualTo(RolloffModel.LinearSquare));
        Assert.That(new FevEvent { Mode = 0x10 }.RolloffModel, Is.EqualTo(RolloffModel.Inverse));
        // a linear event's "(distance)" envelope multiplies the model
        var both = new DistanceRolloff(new[] { 0f, 1100f }, new[] { 1f, 0.5f }, RolloffModel.Linear, 100, 1100);
        Assert.That(both.GainAt(600), Is.EqualTo(0.5f * (1 - 0.5f * 600 / 1100)).Within(1e-5f));
    }

    [Test]
    public void AmbientIsHalfVolumeHalfWay()
    {
        var c = Event("MS2_DATA", "AMB", "11100001");
        TestContext.Out.WriteLine($"11100001 {c}");
        Assert.That(c.GainAt(0), Is.EqualTo(1f));
        // the volume envelope is half way at 2250 (the old falloff: 60 / 2250 = 0.027); the direct occlusion (0 -> 1 at 4467.4)
        // takes 1 - S(0.504) of that, not blended by its pan level (0.447 from 1008.2)
        Assert.That(c.GainOf(c.Count - 1), Is.EqualTo(0f));
        Assert.That(c.GainAt(2250), Is.EqualTo(0.5f * (1 - S(2250f / 4467.4f))).Within(1e-3f));
        // the FMOD probe's render with the sound to the side: the far channel (only the 1 - L centre part of the mix) over the
        // listener-at-0 render, divided by 1 - L from Channel::get3DPanLevel, is the voice's gain
        var far = new (float D, float Ratio, float Pan)[]
        {
            (500, 0.7256f, 0.2208f), (1000, 0.4213f, 0.4471f), (1500, 0.3019f, 0.4472f), (2000, 0.1862f, 0.4472f),
            (2250, 0.1364f, 0.4472f), (2500, 0.0948f, 0.4472f), (3000, 0.0363f, 0.4472f),
        };
        foreach (var (d, ratio, pan) in far)
        {
            TestContext.Out.WriteLine($"{d}: gain {c.GainAt(d):F4}, FMOD {ratio / (1 - pan):F4}, pan level {c.PanLevelAt(d):F4} / {pan:F4}");
            Assert.That(c.PanLevelAt(d), Is.EqualTo(pan).Within(2e-3f));
            Assert.That(c.GainAt(d), Is.EqualTo(ratio / (1 - pan)).Within(ratio / (1 - pan) * 0.015f));
        }
        Assert.That(c.GainAt(4500), Is.EqualTo(0f));
        Assert.That(c.GainAt(9000), Is.EqualTo(0f));
        // silent where the occlusion reaches 1, before the volume envelope's 0 at 4500 (OcclusionOfOneIsSilent)
        Assert.That(c.Silent, Is.EqualTo(4467.4f).Within(0.1f));
        Assert.That(c.GainAt(c.Silent), Is.EqualTo(0f));
    }

    /// <summary>The flat events as the client's FMOD renders them (the probe, KMS data, a listener at 0 to 5000 in front and to
    /// the left, each step's per-ear RMS over the same step rendered at 0 in front). FMOD_EVENT_INFO.audibility stays at the
    /// event's own volume at every step and the channel's 3D min / max is 1 / 1:
    /// <list type="bullet">
    /// <item>Mob/06102549 (simple, a one-shot): 0.3548 at every step; front L 0.993 at 500, 0.996 at 1500.</item>
    /// <item>AMB/11100071 (simple, a loop): 0.7079 at every step; front 0.98 to 1.07, side near 1.386 / 1.412 / 1.398 / 1.458 at
    /// 500 / 1500 / 3500 / 5000, far 0.03 to 0.</item>
    /// <item>Voice/ko/Npc/00000794 (simple, a one-shot): 0.7943 at every step; front 1.000, side 1.4142 / 0.0021 at 500.</item>
    /// <item>Voice/ko/Monster/60000700 (complex, its envelopes on a parameter named "6000" that nothing moves): channel pan3d 0,
    /// L = R at every step and side.</item>
    /// </list>
    /// So the flat curve holds (gain 1 at every distance, never silent), the simple ones pan at level 1 (side near / front
    /// sqrt 2, far 0: <see cref="PanLevelTests"/>' FMOD law), and 60000700 not at all: its pan level envelope holds its first
    /// point, 0.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void FlatEventsHoldTheirLevelAsFmodRenders(string variable)
    {
        var fev = Fev(variable);
        foreach (var path in new[] { "Sound/Mob/06102549", "AMB/11100071", "Sound/Voice/ko/Npc/00000794", "Sound/Voice/ko/Monster/60000700" })
        {
            var ev = fev.AllEvents.FirstOrDefault(e => e.Is3D && e.FullPath.EndsWith("/" + path));
            if (ev == null) { TestContext.Out.WriteLine($"{variable}: no {path}"); continue; }
            var c = ev.Rolloff;
            Assert.That((c?.Count, c?.Silent), Is.EqualTo(((int?)0, (float?)float.PositiveInfinity)), ev.FullPath);
            float pan = path.EndsWith("60000700") ? 0 : 1;
            foreach (float d in new[] { 0f, 500, 1500, 3500, 5000 })
                Assert.That((c.GainAt(d), c.AudibilityAt(d), c.PanLevelAt(d)), Is.EqualTo((1f, 1f, pan)), $"{ev.FullPath} at {d}");
        }
        // the rendered side over front of a level 1 mono source: near 1.4142, far 0.0021 (Npc/00000794 at 500); the law the
        // port maps to Godot's strength gives sqrt 2 and 0, and that strength's far / near is 0
        var (near, far) = PanLevelTests.Fmod(1, 90);
        var (front, _) = PanLevelTests.Fmod(1, 0);
        Assert.That(near / front, Is.EqualTo(1.4142f).Within(0.01f));
        Assert.That(far / front, Is.EqualTo(0.0021f).Within(0.01f));
        Assert.That(PanLevelTests.GodotFarOverNear(Player3DOutput.PanningStrength(1)), Is.EqualTo(0f).Within(0.006f));
    }

    /// <summary>The probe's query of ambient 11100001 in front of the listener (FMOD_EVENT_INFO.audibility, the client's fmod
    /// DLL): every row is its own volume times vol(d) (1 - occ(d) L(d)), the occlusion blended by the pan level and no pan level
    /// gain. That is the measure the max playbacks steal compares, so it is not the mixed gain.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void AudibilityIsFmodsBlend(string variable)
    {
        var c = Event(variable, "AMB", "11100001");
        const float own = 0.5309f;   // the audibility at 0
        var rows = new (float D, float Audibility)[]
        {
            (500, 0.5087f), (1000, 0.4374f), (1500, 0.3471f), (2000, 0.2511f), (2250, 0.2054f), (2500, 0.1631f),
            (3000, 0.0917f), (3500, 0.0406f), (4000, 0.0103f),
        };
        Assert.That(c.AudibilityAt(0), Is.EqualTo(1f));
        foreach (var (d, audibility) in rows)
        {
            float fmod = audibility / own;
            TestContext.Out.WriteLine($"{d}: audibility {c.AudibilityAt(d):F4}, FMOD {fmod:F4}, gain {c.GainAt(d):F4}");
            Assert.That(c.AudibilityAt(d), Is.EqualTo(fmod).Within(fmod * 0.015f + 2e-4f));
        }
        // from 2000 on the mixed gain is clearly lower than the audibility (the full occlusion against 0.447 of it)
        Assert.That(c.GainAt(2250), Is.LessThan(c.AudibilityAt(2250) * 0.8f));
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
        // the volume envelope's half way, times 1 - occlusion (0 -> 1 at 97.7%, smoothstep)
        Assert.That(a.GainAt(zero / 2), Is.EqualTo(0.5f * (1 - S(zero / 2 / (0.977f * a.DistanceOf(a.Count - 1))))).Within(2e-3f));
        Assert.That(a.GainAt(zero + 1), Is.EqualTo(0f));
        Assert.That(zero / a.DistanceOf(a.Count - 1), Is.EqualTo(0.689f).Within(0.001f));
        // full to 34.4%, then -> 0 at 68% (1550 events), e.g. Hit/00008001
        var b = Event(variable, "Hit", "00008001");
        TestContext.Out.WriteLine($"00008001 {b}");
        float max = b.DistanceOf(b.Count - 1);
        // its volume envelope: 1 at 34%, 0 at 68%, half at 51.2%; times 1 - occlusion (0 -> 1 at 99.6%, smoothstep)
        Assert.That(b.GainOf(2), Is.EqualTo(1f));
        Assert.That(b.GainAt(0.34f * max), Is.EqualTo(1 - S(0.34f / 0.996f)).Within(2e-3f));
        Assert.That(b.GainAt(0.68f * max), Is.EqualTo(0f).Within(1e-3f));
        Assert.That(b.GainAt(0.512f * max), Is.EqualTo(0.5f * (1 - S(0.512f / 0.996f))).Within(0.01f));
    }

    [Test]
    public void SetsAVolumeOnlyWhenItChanges()
    {
        var c = new DistanceRolloff(new[] { 0f, 4500f }, new[] { 1f, 0f }, panLevel: 0);   // 2D: no pan level gain (PanLevelTests)
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

    /// <summary>
    /// What Godot puts out for every 3D event's curve player (<see cref="Player3DOutput"/>, the factor a Godot 4.7
    /// AudioStreamPlayer3D applies with the player's max distance and filter), not the volume it is given: at each curve
    /// point, half way between points and past the last, the output is the event's volume times its curve within 0.05 dB
    /// (or silent where the curve is), and there is no high shelf. With the max distance at the curve's silent distance and
    /// the filter at Godot's -24 dB (the old setup) Godot faded every curve a second time: ambient 11100001 at 2250 put out 0.25.
    /// </summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void CurvePlayersPutOutTheirCurve(string variable)
    {
        var fev = Fev(variable);
        int events = 0, points = 0;
        var bad = new List<string>();
        foreach (var ev in fev.AllEvents)
        {
            if (ev.Rolloff is not { } c || !ev.Is3D) continue;
            events++;
            float maxDistance = Player3DOutput.CurveMaxDistance(c);
            if (c.Count == 0)
            {
                // a flat curve: the event's volume at every distance, with no max-distance fade
                foreach (float at in new[] { 0f, 500f, 5000f, 50000f })
                {
                    points++;
                    float output = Player3DOutput.Gain(RolloffGains.ToDb(ev.Volume), maxDistance, at);
                    if (MathF.Abs(20 * MathF.Log10(output / ev.Volume)) > 0.05f) bad.Add($"{ev.FullPath} flat at {at:F0}: Godot puts out {output:F4} for {ev.Volume:F4}");
                }
                continue;
            }
            for (int i = 0; i <= c.Count; i++)
            {
                float last = c.DistanceOf(c.Count - 1);
                float d = i == c.Count ? last * 1.5f + 100 : c.DistanceOf(i);
                foreach (float at in i > 0 && i < c.Count ? new[] { d, (c.DistanceOf(i - 1) + d) / 2 } : new[] { d })
                {
                    points++;
                    float want = ev.Volume * c.GainAt(at);
                    float output = Player3DOutput.Gain(RolloffGains.ToDb(want), maxDistance, at);
                    float shelf = Player3DOutput.ShelfDb(Player3DOutput.CurveFilterDb, output);
                    bool ok = want <= 1e-4f ? output <= 1.01e-4f : MathF.Abs(20 * MathF.Log10(output / want)) <= 0.05f;
                    if (!ok || shelf != 0) bad.Add($"{ev.FullPath} at {at:F0}: curve {want:F4}, Godot puts out {output:F4}, shelf {shelf:F2} dB");
                }
            }
        }
        TestContext.Out.WriteLine($"{variable}: {events} curve events, {points} distances, {bad.Count} off");
        foreach (var b in bad.Take(10)) TestContext.Out.WriteLine(b);
        Assert.That(events, Is.GreaterThan(5000));
        Assert.That(bad, Is.Empty);
        // the old setup: max distance = silent distance, Godot's filter
        var amb = Event(variable, "AMB", "11100001");
        float old = Player3DOutput.Gain(RolloffGains.ToDb(amb.GainAt(2250)), amb.Silent, 2250);
        TestContext.Out.WriteLine($"ambient at 2250: curve {amb.GainAt(2250):F4}, old setup {old:F4}, shelf {Player3DOutput.ShelfDb(Player3DOutput.DefaultFilterDb, old):F2} dB");
        Assert.That(old, Is.EqualTo(amb.GainAt(2250) / 2).Within(0.01f));
        Assert.That(Player3DOutput.ShelfDb(Player3DOutput.DefaultFilterDb, old), Is.LessThan(-15f));
    }

    /// <summary>A looping voice stops once as the listener passes its curve's silent distance and plays once as it comes
    /// back; a voice not set to stop never says so, and a new sound on the voice starts on this side.</summary>
    [Test]
    public void StopsOnlyAcrossTheSilentDistance()
    {
        var c = new DistanceRolloff(new[] { 0f, 3000f, 4000f }, new[] { 1f, 0.2f, 0f });
        var g = new RolloffGains(2);
        g.Set(0, c, 1f, Vector3.Zero, stopsPastSilent: true);
        g.Set(1, c, 1f, Vector3.Zero);
        bool Crossing(Vector3 l, out bool past) { g.Update(0, l, out _); return g.PastSilentChanged(0, out past); }
        Assert.That(Crossing(new Vector3(3500, 0, 0), out _), Is.False);
        Assert.That(Crossing(new Vector3(4000, 0, 0), out bool p) && p, Is.True);      // at the silent distance: stop
        Assert.That(Crossing(new Vector3(9000, 0, 0), out _), Is.False);               // still past: no set
        Assert.That(g.IsPast(0), Is.True);
        Assert.That(Crossing(new Vector3(3999, 0, 0), out p) && !p, Is.True);          // back: play
        Assert.That(Crossing(new Vector3(100, 0, 0), out _), Is.False);
        g.Update(1, new Vector3(9000, 0, 0), out _);
        Assert.That(g.PastSilentChanged(1, out _), Is.False);
        Crossing(new Vector3(9000, 0, 0), out _);
        g.Set(0, c, 1f, Vector3.Zero, stopsPastSilent: true);
        Assert.That(g.IsPast(0) || g.PastSilentChanged(0, out _), Is.False);
        // a curve that never reaches 0 never stops
        g.Set(0, new DistanceRolloff(new[] { 0f, 1000f }, new[] { 1f, 0.5f }), 1f, Vector3.Zero, stopsPastSilent: true);
        Assert.That(Crossing(new Vector3(1e6f, 0, 0), out _), Is.False);
    }

    /// <summary><paramref name="voices"/> voices live (up to the client's 100 channels), half of them looping, the listener
    /// moving every frame across their silent distance: no allocation, and the cost per voice.</summary>
    [TestCase(32)]
    [TestCase(EffectVoiceSlots.ClientChannels)]
    public void LiveVoicesAllocateNothing(int voices)
    {
        // a hit event's shape: volume, 3D pan level and direct occlusion envelopes, all read per update
        var c = new DistanceRolloff(new[] { 0f, 0f, 1548f, 3060f, 4500f }, new[] { 1f, 1f, 1f, 0f, 0f },
            panDistance: new[] { 0f, 733f, 1850f }, panValue: new[] { 0f, 0f, 0.466f },
            occlusionDistance: new[] { 0f, 4482f }, occlusionValue: new[] { 0f, 1f });
        Assert.That((c.HasPanLevelEnvelope, c.HasOcclusion), Is.EqualTo((true, true)));
        var g = new RolloffGains(voices);
        for (int i = 0; i < voices; i++) g.Set(i, c, 0.8f, new Vector3(100 * i, 0, 50 * i), stopsPastSilent: i % 2 == 0);
        int sets = 0, stopsOrPlays = 0;
        void Frame(int f)
        {
            var l = new Vector3(37 * f % 9000, 0, 11 * f % 2000);
            for (int i = 0; i < voices; i++)
            {
                if (g.Update(i, l, out _)) sets++;
                if (g.PastSilentChanged(i, out _)) stopsOrPlays++;
            }
        }
        for (int f = 0; f < 200; f++) Frame(f);
        const int frames = 20000, runs = 3;
        void Run() { for (int f = 0; f < frames; f++) Frame(f); }
        var sw = Stopwatch.StartNew();
        long bytes = AllocatedBytes.LeastOf(runs, Run);
        sw.Stop();
        double nsPerVoice = sw.Elapsed.TotalMilliseconds * 1e6 / (runs * frames * (double)voices);
        TestContext.Out.WriteLine($"{voices} voices: {bytes} B over {frames} frames, {nsPerVoice:F1} ns per voice per frame, {sets} volume sets, {stopsOrPlays} stops or plays");
        Assert.That(bytes, Is.Zero);
        Assert.That(stopsOrPlays, Is.GreaterThan(0));
    }

    /// <summary>FMOD's smoothstep point shape over a segment's fraction t.</summary>
    static float S(float t) => t * t * (3 - 2 * t);

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
