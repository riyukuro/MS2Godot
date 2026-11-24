using System.Text.RegularExpressions;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>
/// Every random part of every effect XML (Xml.m2d effect/*.xml) makes exactly one child per play, the one the client picks.
/// The rule is written out here from the raw XML and the client's lines:
/// <list type="bullet">
/// <item>the children are the random element's direct &lt;effect&gt; children whose param CreateEffectParam makes
/// (randomeffectparam.cpp:88-93; effectservice.cpp:701-737): a known type; CEffectParam::vfunc_2 (:667-685) refuses
/// monochrome, swoosh, status, light, fog, vibrate and textureOverride with no loop and duration 0; a soundEvent's group and
/// event (each job's with byJob) must be in the FMOD project (soundeventmgr.cpp:310-345); a random needs a made child (:98-108);</item>
/// <item>prob is split on ", " with no empty token and atoi per token (:86-87, placeholder/xmlattr.h);</item>
/// <item>CRandomEffect::vfunc_3 (:143-178): a random index other than 0 takes child index mod count; else a sorted map of
/// running total - 1 (weight 0 skipped, 0 past prob's end) and lower_bound on Unk_Random(tick)'s first number mod the
/// total (Build @0x1416f5250, Pick @0x1416f55b0, questmasktable.cpp:400-461); a total of 0 makes nothing.</item>
/// </list>
/// Ours is <see cref="EffectRandom.PlayList"/> over <see cref="EffectTable"/>'s parts with <see cref="EffectSoundParam.Made"/>.
/// </summary>
[NonParallelizable]
public class EffectRandomSweepTests
{
    static readonly HashSet<string> NeedsLoopOrDuration = new(StringComparer.OrdinalIgnoreCase)
        { "monochrome", "swoosh", "status", "light", "fog", "vibrate", "textureOverride" };

    static readonly string[] KnownTypes =
    {
        "nif", "soundEvent", "soundMusic", "monochrome", "radialBlur", "weather", "swoosh", "status", "light", "fog", "random",
        "vibrate", "ribbon", "chaoslight", "textureoverride", "lightning", "cameraMove", "screenMasking", "gaussianBlur",
        "postShimmer", "beam", "afterImage",
    };

    static string Data(string env)
    {
        var d = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore(env + " is not set");
        if (!File.Exists(Path.Combine(d, "Sound", "MS2Sound.fev"))) Assert.Ignore("no Sound/MS2Sound.fev under " + env);
        return d;
    }

    static bool TiBool(XAttribute a)
    {
        string v = (string)a;
        return !string.IsNullOrEmpty(v) && (v.StartsWith("true", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("yes", StringComparison.OrdinalIgnoreCase) || v[0] == '1');
    }

    static float Float(XAttribute a) =>
        float.TryParse((string)a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : 0;

    static readonly Regex LeadingInt = new(@"^\s*([+-]?)(\d+)", RegexOptions.Compiled);

    static int RawAtoi(string token)
    {
        var m = LeadingInt.Match(token);
        if (!m.Success) return 0;
        long v = 0;
        foreach (char c in m.Groups[2].Value) { v = v * 10 + (c - '0'); if (v > int.MaxValue) { v = (long)int.MaxValue + 1; break; } }
        if (m.Groups[1].Value == "-") v = -v;
        return (int)Math.Clamp(v, int.MinValue, int.MaxValue);
    }

    static List<int> RawWeights(XElement random) =>
        ((string)random.Attribute("prob") ?? "").Split(new[] { ',', ' ' }).Where(t => t.Length > 0).Select(RawAtoi).ToList();

    sealed class Counts
    {
        public int Dropped, DroppedType, DroppedDuration, DroppedSound, DroppedRandom;
    }

    /// <summary>The raw element's param made, with the reason counted when it is not.</summary>
    static bool RawMade(XElement e, FevProject fev, Counts n)
    {
        string type = (string)e.Attribute("type");
        int id = Array.FindIndex(KnownTypes, k => string.Equals(k, type, StringComparison.OrdinalIgnoreCase));
        if (id < 0) { if (n != null) n.DroppedType++; return false; }
        if (NeedsLoopOrDuration.Contains(type) && !TiBool(e.Attribute("loop")) && MathF.Abs(Float(e.Attribute("duration"))) <= 1e-4f)
        { if (n != null) n.DroppedDuration++; return false; }
        if (string.Equals(type, "soundEvent", StringComparison.OrdinalIgnoreCase))
        {
            string group = (string)e.Attribute("group");
            if (string.IsNullOrEmpty(group)) return true;
            var events = fev.EventsInGroup(group);
            var names = new List<string>();
            if (TiBool(e.Attribute("byJob")))
            {
                // m_mapJobEvent: one event per job code, the last one written wins
                var byJob = new Dictionary<string, string>();
                foreach (var item in ((string)e.Attribute("jobEvent") ?? "").Split(';'))
                {
                    var pair = item.Split(':');
                    if (pair.Length == 2) byJob[int.TryParse(pair[0], out int j) ? j.ToString() : "0"] = pair[1];
                }
                names = byJob.Values.ToList();
            }
            else names.Add((string)e.Attribute("event"));
            bool ok = events.Count > 0 && names.All(nm => !string.IsNullOrEmpty(nm) && events.Any(ev => string.Equals(ev.Name, nm, StringComparison.OrdinalIgnoreCase)));
            if (!ok && n != null) n.DroppedSound++;
            return ok;
        }
        if (string.Equals(type, "random", StringComparison.OrdinalIgnoreCase))
        {
            bool any = e.Elements("effect").Any(c => RawMade(c, fev, null));
            if (!any && n != null) n.DroppedRandom++;
            return any;
        }
        return true;
    }

    static int ReferencePick(IReadOnlyList<int> weights, int count, uint tick)
    {
        int seed = unchecked((int)tick);
        uint a = unchecked((uint)seed); seed = unchecked(seed * 0x343fd + 0x269ec3);
        uint b = unchecked((uint)seed); seed = unchecked(seed * 0x343fd + 0x269ec3);
        uint c = unchecked((uint)seed);
        a |= 0x100000; b |= 0x1000; c |= 0x10;
        a = (a & 0xfffffffe) << 12 ^ (a << 13 ^ a) >> 19;
        b = (b & 0xfffffff8) << 4 ^ (b << 2 ^ b) >> 25;
        c = (c & 0xfffffff0) << 17 ^ (c << 3 ^ c) >> 11;
        var map = new SortedDictionary<int, int>();
        int total = 0;
        for (int i = 0; i < count; i++)
        {
            int w = i < weights.Count ? weights[i] : 0;
            if (w == 0) continue;
            total += w;
            map[total - 1] = i;
        }
        if (total <= 0) return -1;
        int key = (int)((a ^ b ^ c) % (uint)total);
        foreach (var kv in map) if (kv.Key >= key) return kv.Value;
        return -1;
    }

    const int Ticks = 4000;

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryRandomPartPicksExactlyOneChildWeightedAsTheXmlSays(string env)
    {
        var data = Data(env);
        using var packs = new PackSet(data);
        var fev = FevProject.Load(Path.Combine(data, "Sound", "MS2Sound.fev"));
        var table = new EffectTable(packs);
        var saved = EffectRandom.SoundEventFound;
        EffectRandom.SoundEventFound = p => EffectSoundParam.Made(fev, p.SoundGroup, p.SoundEvent, p.JobEvents?.Values);
        var n = new Counts();
        int xmls = 0, unreadable = 0, randoms = 0, children = 0, made = 0, topRandoms = 0, nested = 0, notMade = 0, allZero = 0,
            weightMismatch = 0, offSkip = 0, picks = 0, indexPicks = 0, freqChecked = 0;
        long plays = 0;
        var bad = new List<string>();
        var list = new List<EffectPart>();
        try
        {
            foreach (var entry in packs.List("Xml").Where(e => e.StartsWith("effect/", StringComparison.Ordinal) && e.EndsWith(".xml", StringComparison.Ordinal))
                         .OrderBy(e => e, StringComparer.Ordinal))
            {
                XDocument raw;
                try { raw = PackXml.Parse(packs.Read("Xml", entry)); } catch { unreadable++; continue; }
                xmls++;
                var all = raw.Descendants("effect").ToList();
                if (!all.Any(e => string.Equals((string)e.Attribute("type"), "random", StringComparison.OrdinalIgnoreCase))) continue;
                var parts = table.Get(entry["effect/".Length..]);
                if (parts == null || parts.Count != all.Count) { bad.Add($"{entry}: parts {parts?.Count} raw {all.Count}"); continue; }
                var tops = new List<int>();
                for (int i = 0; i < all.Count; i++)
                {
                    var r = all[i];
                    if (!string.Equals((string)r.Attribute("type"), "random", StringComparison.OrdinalIgnoreCase)) continue;
                    randoms++;
                    if (r.Parent == raw.Root) { topRandoms++; tops.Add(i); }
                    else nested++;
                    if (TiBool(r.Attribute("offSkip"))) offSkip++;
                    var kids = r.Elements("effect").ToList();
                    children += kids.Count;
                    var madeKids = new List<int>();
                    foreach (var k in kids)
                        if (RawMade(k, fev, n)) madeKids.Add(all.IndexOf(k));
                        else n.Dropped++;
                    made += madeKids.Count;
                    var weights = RawWeights(r);
                    if (weights.Count != kids.Count) weightMismatch++;
                    if (!parts[i].RandomWeights.Select(w => (int)w).SequenceEqual(weights)) bad.Add($"{entry} #{i}: weights {string.Join(",", parts[i].RandomWeights)} raw {string.Join(",", weights)}");
                    if (!parts[i].RandomChildren.SequenceEqual(kids.Select(k => all.IndexOf(k)))) bad.Add($"{entry} #{i}: children differ");
                    if (madeKids.Count == 0) { notMade++; if (EffectRandom.Pick(parts, parts[i], 0, 1) != -1) bad.Add($"{entry} #{i}: no made child but a pick"); continue; }
                    int total = 0;
                    for (int c = 0; c < madeKids.Count; c++) total += c < weights.Count ? weights[c] : 0;
                    if (total == 0) allZero++;
                    // the seeded pick over many ticks (sequential, as skill serials are, and spread)
                    var hits = new int[madeKids.Count];
                    for (uint t = 0; t < Ticks; t++)
                    {
                        foreach (uint tick in new[] { t, unchecked(t * 2654435761u) })
                        {
                            int want = ReferencePick(weights, madeKids.Count, tick);
                            int got = EffectRandom.Pick(parts, parts[i], 0, tick);
                            int wantPart = want < 0 ? -1 : madeKids[want];
                            picks++;
                            if (got != wantPart) { bad.Add($"{entry} #{i} tick {tick}: ours {got}, client {wantPart}"); break; }
                            if (want >= 0 && tick != t) hits[want]++;
                        }
                        if (bad.Count > 50) break;
                    }
                    // the weights as the XML says: zero never, the rest at their share (spread ticks)
                    if (total > 0)
                    {
                        freqChecked++;
                        for (int c = 0; c < madeKids.Count; c++)
                        {
                            int w = c < weights.Count ? weights[c] : 0;
                            double p = (double)w / total, f = (double)hits[c] / Ticks;
                            double tol = 4 * Math.Sqrt(p * (1 - p) / Ticks) + 0.005;
                            if (w == 0 ? hits[c] != 0 : Math.Abs(f - p) > tol)
                                bad.Add($"{entry} #{i} child {c}: weight {w}/{total} picked {f:F4}");
                        }
                    }
                    // a random index: index mod the made count
                    for (uint ri = 1; ri <= 2 * madeKids.Count + 1; ri++)
                    {
                        indexPicks++;
                        int got = EffectRandom.Pick(parts, parts[i], ri, 0);
                        if (got != madeKids[(int)(ri % (uint)madeKids.Count)]) bad.Add($"{entry} #{i} index {ri}: ours {got}");
                    }
                }
                // one play: every top-level random part adds exactly its one pick (or nothing), and no other of its children
                for (uint t = 0; t < 64; t++)
                {
                    uint tick = unchecked(t * 2654435761u);
                    EffectRandom.PlayList(parts, 0, tick, list);
                    plays++;
                    foreach (int i in tops)
                    {
                        int from = list.Count(p => Under(parts, p, i));
                        int pick = EffectRandom.Pick(parts, parts[i], 0, tick);
                        int want = pick < 0 ? 0 : 1;
                        if (from != want) bad.Add($"{entry} #{i} tick {tick}: {from} parts from one random part, want {want}");
                    }
                    if (list.Count(p => !p.TopLevel && p.RandomParent < 0) != 0) bad.Add($"{entry}: a nested non-random child plays");
                }
                if (bad.Count > 50) break;
            }
        }
        finally { EffectRandom.SoundEventFound = saved; }
        TestContext.Out.WriteLine($"{env}: {xmls} xmls ({unreadable} unreadable), {randoms} random parts ({topRandoms} top level, {nested} nested, {offSkip} offSkip), " +
            $"{children} children: {made} made, {n.Dropped} dropped ({n.DroppedType} type, {n.DroppedDuration} no loop/duration, {n.DroppedSound} sound not in FMOD, {n.DroppedRandom} empty random); " +
            $"{notMade} random parts with no made child, {allZero} with all weights 0, {weightMismatch} with a weight count not the child count; " +
            $"{picks} seeded picks, {indexPicks} index picks, {freqChecked} weight shares checked, {plays} plays, {bad.Count} disagree");
        foreach (var b in bad.Take(20)) TestContext.Out.WriteLine("  " + b);
        Assert.That(randoms, Is.GreaterThan(0));
        Assert.That(bad, Is.Empty);
    }

    static bool Under(IReadOnlyList<EffectPart> parts, EffectPart p, int random)
    {
        for (int up = p.RandomParent; up >= 0; up = parts[up].RandomParent) if (up == random) return true;
        return false;
    }
}
