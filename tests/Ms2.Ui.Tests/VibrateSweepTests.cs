using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// Every vibrate part of every effect xml, on each data set:
/// - its freq, amp, damping (1 when absent) and dampingTime (0 when absent) read as the xml has them;
/// - it is made exactly when it loops or has a duration and the duration is past the damping time
///   (CVibrateEffectParam::vfunc_2);
/// - the shake of each made one, run frame by frame from before its start to past its end: the eye is left before the start,
///   every offset is at most the amplitude (times the damping when that is above 1), the run ends at the end and not before,
///   and a frame allocates nothing.
/// </summary>
public class VibrateSweepTests
{
    static float Attr(XElement e, string name, float absent) =>
        float.TryParse((string)e.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : absent;

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryVibratePartReadsMakesAndShakesWithinItsAmplitude(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        var effects = new EffectTable(packs);
        int xmls = 0, parts = 0, made = 0, frames = 0;
        long bytes = 0;
        var badRead = new List<string>();
        var badMade = new List<string>();
        var badShake = new List<string>();
        var random = new Random(7);
        foreach (var entry in packs.List("Xml"))
        {
            if (!entry.StartsWith("effect/", StringComparison.OrdinalIgnoreCase) || !entry.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
            var file = entry.Substring("effect/".Length);
            var list = effects.Get(file);
            if (list == null) continue;
            xmls++;
            var elements = PackXml.Parse(packs.Read("Xml", entry)).Descendants("effect").ToList();
            Assert.That(elements.Count, Is.EqualTo(list.Count), $"{file}: a part per element");
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p.TypeId != EffectSkipTable.TypeVibrate) continue;
                parts++;
                var e = elements[i];
                float freq = Attr(e, "freq", 0), amp = Attr(e, "amp", 0), damping = Attr(e, "damping", 1), dampingTime = Attr(e, "dampingTime", 0);
                if (p.VibrateFrequency != freq || p.VibrateAmplitude != amp || p.VibrateDamping != damping || p.VibrateDampingTime != dampingTime)
                    badRead.Add($"{file} #{i}: read {p.VibrateFrequency}/{p.VibrateAmplitude}/{p.VibrateDamping}/{p.VibrateDampingTime} for {freq}/{amp}/{damping}/{dampingTime}");

                float duration = Attr(e, "duration", 0);
                bool loop = string.Equals((string)e.Attribute("loop"), "true", StringComparison.OrdinalIgnoreCase) || (string)e.Attribute("loop") == "1";
                bool want = (loop || MathF.Abs(duration) > 1e-4f) && duration > dampingTime;
                bool got = EffectRandom.ParamMade(list, p);
                if (got != want) badMade.Add($"{file} #{i}: made {got} for duration {duration} dampingTime {dampingTime} loop {loop}");
                if (!got) continue;
                made++;

                // the run: start 1, a frame each 1/60 s from 0.5 to past the end
                float bound = amp * MathF.Max(1f, damping) + 1e-3f;
                long least = long.MaxValue;
                for (int pass = 0; pass < 3 && least != 0; pass++)
                {
                    var run = CameraVibrate.For(p, 1 - p.StartDelay);
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    string why = Run(run, bound, random, ref frames);
                    least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
                    if (why != null) { badShake.Add($"{file} #{i}: {why}"); break; }
                }
                bytes += least;
            }
        }
        TestContext.Progress.WriteLine($"{env}: {xmls} effect xmls, {parts} vibrate parts, {made} made, {frames} frames, {bytes} B");
        foreach (var s in badRead.Concat(badMade).Concat(badShake).Take(30)) TestContext.Progress.WriteLine("  " + s);
        Assert.That(parts, Is.GreaterThan(500), "the vibrate parts are found");
        Assert.That(badRead, Is.Empty, "each part reads as its xml");
        Assert.That(badMade, Is.Empty, "made exactly when the duration is past the damping time");
        Assert.That(badShake, Is.Empty, "each shake stays within its amplitude and ends at its end");
        Assert.That(bytes, Is.EqualTo(0), "a frame allocates nothing");
    }

    /// <summary>One shake's frames; null when every frame is right, else what was wrong.</summary>
    static string Run(CameraVibrate v, float bound, Random random, ref int frames)
    {
        var at = new Vector3(100, 200, 300);
        for (int f = 0; ; f++)
        {
            double t = 0.5 + f / 60.0;
            var eye = at;
            bool running = v.Update(t, ref eye, random);
            frames++;
            if (t < v.Start)
            {
                if (!running || eye != at) return $"moved or ended before the start at {t}";
                continue;
            }
            if (t >= v.End) return running ? $"still running past the end at {t}" : null;
            if (!running) return $"ended before the end at {t}";
            float off = Vector3.Distance(eye, at);
            if (!(off <= bound)) return $"offset {off} past {bound} at {t}";
        }
    }
}
