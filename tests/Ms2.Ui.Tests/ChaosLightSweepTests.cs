using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// Every chaoslight part of every effect xml, on each data set:
/// - it has a param; its layer, blink and key count read as the xml has them, the keys start one after another from the
///   start delay, the loop index is 0 only for loop="true" with a key, and the param's own values are the xml's plain
///   numbers (the defaults when absent);
/// - its run, frame by frame from the request to past its end: the layers go on at the start delay, every value the layers
///   hold stays between the smallest and largest of the param's and keys' values (the colour may also be 0 while a blink
///   is off), a part that does not loop ends just after its duration and not before, and a frame allocates nothing.
/// </summary>
public class ChaosLightSweepTests
{
    static float F(XElement e, string name, float absent) =>
        float.TryParse((string)e.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : absent;

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryChaosLightPartReadsAndRunsWithinItsValues(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        var effects = new EffectTable(packs);
        int xmls = 0, parts = 0, keyed = 0, blinks = 0, frames = 0;
        var layerIds = new SortedDictionary<int, int>();
        long bytes = 0;
        var badRead = new List<string>();
        var badRun = new List<string>();
        var run = new ChaosLightRun();
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
                var part = list[i];
                if (part.TypeId != EffectSkipTable.TypeChaosLight) continue;
                parts++;
                var e = elements[i];
                var p = part.ChaosLight;
                if (p == null) { badRead.Add($"{file} #{i}: no param"); continue; }
                string why = Read(e, part, p);
                if (why != null) { badRead.Add($"{file} #{i}: {why}"); continue; }
                if (p.Keys.Length > 0) keyed++;
                layerIds[p.Layer] = layerIds.GetValueOrDefault(p.Layer) + 1;
                if (p.Blink) blinks++;

                long least = long.MaxValue;
                for (int pass = 0; pass < 3 && least != 0; pass++)
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    why = Run(run, part, p, ref frames);
                    least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
                    if (why != null) { badRun.Add($"{file} #{i}: {why}"); break; }
                }
                bytes += least;
            }
        }
        TestContext.Progress.WriteLine($"{env}: {xmls} effect xmls, {parts} chaoslight parts, {keyed} with keys, {blinks} blinking, {frames} frames, {bytes} B");
        TestContext.Progress.WriteLine($"{env}: layer ids " + string.Join(", ", layerIds.Select(kv => $"{kv.Key} x{kv.Value}")));
        foreach (var s in badRead.Concat(badRun).Take(30)) TestContext.Progress.WriteLine("  " + s);
        Assert.That(parts, Is.GreaterThan(500), "the chaoslight parts are found");
        Assert.That(badRead, Is.Empty, "each part reads as its xml");
        Assert.That(badRun, Is.Empty, "each run stays within its values and ends at its end");
        Assert.That(bytes, Is.EqualTo(0), "a frame allocates nothing");
        // ChaosLayers keeps a layer below the base's id (-1) after the base, where the client puts it under it
        Assert.That(layerIds.Keys.First(), Is.GreaterThanOrEqualTo(-1), "no layer id below the base's");
    }

    static string Read(XElement e, EffectPart part, EffectChaosLightParam p)
    {
        int layer = int.TryParse((string)e.Attribute("layer"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int l) ? l : 0;
        if (p.Layer != layer) return $"layer {p.Layer} for {layer}";
        // XmlGetAttr(bool): a "true", "yes" or "1" start
        bool blink = (string)e.Attribute("blink") is { Length: > 0 } b && (b.StartsWith("true", StringComparison.OrdinalIgnoreCase)
            || b.StartsWith("yes", StringComparison.OrdinalIgnoreCase) || b[0] == '1');
        if (p.Blink != blink) return $"blink {p.Blink} for {(string)e.Attribute("blink")}";
        var keys = e.Elements("transition").ToList();
        if (p.Keys.Length != keys.Count) return $"{p.Keys.Length} keys for {keys.Count}";
        float at = part.StartDelay;
        for (int k = 0; k < keys.Count; k++)
        {
            if (p.Keys[k].Start != at) return $"key {k} starts {p.Keys[k].Start} for {at}";
            at += F(keys[k], "duration", 0);
        }
        int loopIndex = keys.Count > 0 && string.Equals((string)e.Attribute("loop"), "true", StringComparison.OrdinalIgnoreCase) ? 0 : -1;
        if (p.LoopIndex != loopIndex) return $"loop index {p.LoopIndex} for {loopIndex}";
        var v = p.Values;
        if (v.Boost != F(e, "fresnelBoost", 26.09f) || v.Exponent != F(e, "fresnelExponent", 5.36f) || v.Dimmer != F(e, "dimmer", 0f))
            return $"values {v.Boost}/{v.Exponent}/{v.Dimmer}";
        var rgb = ((string)e.Attribute("rgb"))?.Split(',');
        if (rgb is { Length: 3 } && rgb.All(c => float.TryParse(c.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
        {
            float r = float.Parse(rgb[0].Trim(), CultureInfo.InvariantCulture), g = float.Parse(rgb[1].Trim(), CultureInfo.InvariantCulture),
                bl = float.Parse(rgb[2].Trim(), CultureInfo.InvariantCulture);
            if (v.R != r || v.G != g || v.B != bl) return $"rgb {v.R},{v.G},{v.B} for {(string)e.Attribute("rgb")}";
        }
        return null;
    }

    static void Span(in ChaosLightValues v, Span<float> lo, Span<float> hi)
    {
        Span<float> c = stackalloc float[] { v.Boost, v.Exponent, v.Dimmer, v.R, v.G, v.B };
        for (int i = 0; i < 6; i++) { lo[i] = MathF.Min(lo[i], c[i]); hi[i] = MathF.Max(hi[i], c[i]); }
    }

    /// <summary>One part's frames from the request at 100 s; null when every frame is right, else what was wrong.</summary>
    static string Run(ChaosLightRun run, EffectPart part, EffectChaosLightParam p, ref int frames)
    {
        Span<float> lo = stackalloc float[6], hi = stackalloc float[6];
        lo.Fill(float.MaxValue); hi.Fill(float.MinValue);
        Span(p.Values, lo, hi);
        for (int k = 0; k < p.Keys.Length; k++) Span(p.Keys[k].Values, lo, hi);
        if (p.Blink) for (int c = 3; c < 6; c++) { lo[c] = MathF.Min(lo[c], 0); hi[c] = MathF.Max(hi[c], 0); }
        Span<float> got = stackalloc float[6];
        const double request = 100;
        bool on = run.Begin(p, part.StartDelay, part.Duration, part.Loop, request);
        if (on != (part.StartDelay == 0)) return $"on at once {on} for a start delay of {part.StartDelay}";
        double end = request + part.StartDelay + part.Duration;
        double last = part.Loop ? request + part.StartDelay + 30 : end + 0.5;
        for (int f = 1; ; f++)
        {
            double t = request + f / 60.0;
            bool alive = run.Tick(t, out _);
            frames++;
            if (!part.Loop && t > end) return alive ? $"alive past its end at {t - request:F3}" : null;
            if (!alive) return $"ended before its end at {t - request:F3}";
            if (run.Started != (t >= request + part.StartDelay)) return $"started {run.Started} at {t - request:F3}";
            var v = run.Layer;
            got[0] = v.Boost; got[1] = v.Exponent; got[2] = v.Dimmer; got[3] = v.R; got[4] = v.G; got[5] = v.B;
            for (int c = 0; c < 6; c++)
                if (!(got[c] >= lo[c] - 1e-4f && got[c] <= hi[c] + 1e-4f)) return $"value {c} {got[c]} outside {lo[c]}..{hi[c]} at {t - request:F3}";
            if (part.Loop && t > last) return null;
        }
    }
}
