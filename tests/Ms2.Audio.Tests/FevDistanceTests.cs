using Ms2.Audio;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>
/// The 3D min / max distance of every event of MS2Sound.fev on MS2_DATA (KMS2_DEV or KMS2) and MS2_GMS2_DATA (GMS2):
/// the record's +0x38 and +0x3c when its mode (+0x34) is 3D (FMOD_3D 0x10). 2D events hold FMOD Designer's defaults
/// 1 / 10000 there and take none.
/// </summary>
public class FevDistanceTests
{
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void Every3DEventHasItsRange(string variable)
    {
        var fev = Fev(variable);
        int total = 0, threeD = 0, ranged = 0, noRange = 0;
        var bad = new List<string>();
        var maxes = new SortedDictionary<float, int>();
        foreach (var ev in fev.AllEvents)
        {
            total++;
            if (!ev.Is3D)
            {
                if (ev.MinDistance != 0 || ev.MaxDistance != 0) bad.Add($"2D {ev.FullPath}: {ev.MinDistance} / {ev.MaxDistance}");
                continue;
            }
            threeD++;
            if (ev.IsSimple) continue;   // the 3D simple events hold 1 / 1, no range
            if (ev.MinDistance == 0 && ev.MaxDistance == 0) { noRange++; TestContext.Out.WriteLine($"no range: {ev.FullPath}"); continue; }   // a record holding 1 / 1, as the simple ones
            if (!(ev.MinDistance >= 0 && ev.MinDistance < ev.MaxDistance)) { bad.Add($"3D {ev.FullPath}: {ev.MinDistance} / {ev.MaxDistance}"); continue; }
            ranged++;
            maxes[ev.MaxDistance] = maxes.GetValueOrDefault(ev.MaxDistance) + 1;
        }
        TestContext.Out.WriteLine($"{variable}: {total} events, {threeD} 3D, {ranged} complex 3D with a range, {noRange} without, bad {bad.Count}");
        TestContext.Out.WriteLine("max distances: " + string.Join(", ", maxes.Select(kv => $"{kv.Key}x{kv.Value}")));
        foreach (var b in bad.Take(20)) TestContext.Out.WriteLine(b);
        Assert.That(bad, Is.Empty);
        Assert.That(ranged, Is.GreaterThan(1000));
        Assert.That(noRange, Is.LessThanOrEqualTo(1));   // Voice/ko/Monster/60000700's record holds 1 / 1
    }

    [Test]
    public void AnAmbientEventTakesPlus38AndPlus3C()
    {
        var fev = Fev("MS2_DATA");
        var ev = fev.EventsNamed("11100001").FirstOrDefault(e => !e.IsSimple && e.Is3D);
        Assert.That(ev, Is.Not.Null);
        Assert.That((ev.MinDistance, ev.MaxDistance), Is.EqualTo((0f, 4500f)), ev.FullPath);   // +0x44 holds 60
    }

    static FevProject Fev(string variable)
    {
        var d = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore($"{variable} is not set");
        var f = Path.Combine(d, "Sound", "MS2Sound.fev");
        if (!File.Exists(f)) Assert.Ignore($"{variable} has no Sound/MS2Sound.fev");
        return FevProject.Load(f);
    }
}
