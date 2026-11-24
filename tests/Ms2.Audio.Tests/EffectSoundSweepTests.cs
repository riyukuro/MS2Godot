using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>
/// Every soundEvent part of every effect XML (Xml.m2d effect/*.xml) against the client's rules: the attributes
/// CEffectParam::vfunc_1 @0x1411a0290 (effectservice.cpp:649-662) and CSoundEventEffectParam::vfunc_1 @0x141775af0
/// (soundeventmgr.cpp:283-307) read, the owner and job fields ours takes from them, and the event the client finds:
/// CSoundEventEffect::vfunc_3 (soundeventmgr.cpp:466-494) gets the part's group by its full path
/// (CSoundEventMgr::GetEventGroup @soundeventmgr.cpp:1540-1560, FMOD getGroup) and plays nothing when the group or the
/// event in it is missing. Ours resolves through SoundLibrary.ResolveEvent -> FevProject.FindEvent.
/// </summary>
public class EffectSoundSweepTests
{
    // read by the client and used by ours
    static readonly HashSet<string> Handled = new(StringComparer.Ordinal)
    {
        "type", "group", "event", "startDelay", "byJob", "jobEvent", "byJobStartDelay", "gender", "jobCode", "onlyCasterMyPC",
        "onlyTargetMyPC",
        "attachInfo.attachToNode",   // the sound follows its owner (vfunc_9 SetPosition, soundeventmgr.cpp:582)
    };
    // read by the client, not used by ours yet (STILL-OPEN, counted below)
    static readonly HashSet<string> Open = new(StringComparer.Ordinal)
    {
        "doNotStop", "forceHearDistance", "cooltimeID", "isSkipMusicConcert", "loop", "duration", "offSkip", "lodSkipLevel",
        "ignoreOptimize",
        // attachInfo (:300-306): ours follows the actor's root, not the named node (CSoundEventEffect::vfunc_3 :514-560)
        "attachInfo.applyNodeTransform", "attachInfo.parentNode",
    };
    // in the data but read by neither parser (TinyXML names are case-sensitive): no effect in the client
    static readonly HashSet<string> ClientIgnores = new(StringComparer.Ordinal)
    {
        "donotstop", "viewDistance", "onlyCastMyPC", "notForceStop", "startTime",
        "attachInfo.doNotStop", "attachInfo.useShadow", "attachInfo.onGround", "attachInfo.type", "attachInfo.group",
        "attachInfo.event", "attachInfo.startDelay", "attachInfo.translate", "attachInfo.rotation", "attachInfo.scale",
    };

    static string Data(string env)
    {
        var d = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore(env + " is not set");
        if (!File.Exists(Path.Combine(d, "Sound", "MS2Sound.fev"))) Assert.Ignore("no Sound/MS2Sound.fev under " + env);
        return d;
    }

    static IEnumerable<string> EffectEntries(PackSet packs) =>
        packs.List("Xml").Where(e => e.StartsWith("effect/", StringComparison.Ordinal) && e.EndsWith(".xml", StringComparison.Ordinal))
            .OrderBy(e => e, StringComparer.Ordinal);

    /// <summary>TiXmlElement::QueryBoolAttribute, which XmlGetAttr(bool) calls (tinyxmlhelper.cpp:211-214, tinyxml.cpp): a
    /// value starting with "true", "yes" or "1" in any case is true; anything else leaves these parts' default, false.</summary>
    static bool TiBool(XAttribute a)
    {
        string v = (string)a;
        return !string.IsNullOrEmpty(v) && (v.StartsWith("true", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("yes", StringComparison.OrdinalIgnoreCase) || v[0] == '1');
    }

    static IEnumerable<XElement> SoundEvents(XDocument x) =>
        x.Descendants("effect").Where(e => (string)e.Attribute("type") == "soundEvent");

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EverySoundEventAttributeIsHandledIgnoredByTheClientOrCountedOpen(string env)
    {
        using var packs = new PackSet(Data(env));
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var unknown = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int files = 0, parts = 0, unparsed = 0;
        foreach (var entry in EffectEntries(packs))
        {
            files++;
            XDocument x;
            try { x = PackXml.Parse(packs.Read("Xml", entry)); }
            catch (System.Xml.XmlException) { unparsed++; continue; }
            foreach (var e in SoundEvents(x))
            {
                parts++;
                var attach = e.Element("attachInfo")?.Attributes().Select(a => "attachInfo." + a.Name.LocalName) ?? Enumerable.Empty<string>();
                foreach (var n in e.Attributes().Select(a => a.Name.LocalName).Concat(attach))
                {
                    var into = Handled.Contains(n) || Open.Contains(n) || ClientIgnores.Contains(n) ? counts : unknown;
                    into[n] = into.GetValueOrDefault(n) + 1;
                }
            }
        }
        TestContext.Out.WriteLine($"{env}: {files} effect files ({unparsed} not XML), {parts} soundEvent parts");
        foreach (var (n, c) in counts)
            TestContext.Out.WriteLine($"  {(Handled.Contains(n) ? "handled" : Open.Contains(n) ? "OPEN" : "client ignores")} {n} {c}");
        Assert.That(parts, Is.GreaterThan(10000));
        Assert.That(unknown, Is.Empty, "attributes on soundEvent parts that neither list names");
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryPartsOwnerJobAndAttachFieldsAreTheXmls(string env)
    {
        using var packs = new PackSet(Data(env));
        var table = new EffectTable(packs);
        int parts = 0, byJob = 0, jobPairs = 0, filtered = 0, following = 0;
        var wrong = new List<string>();
        foreach (var entry in EffectEntries(packs))
        {
            XDocument x;
            try { x = PackXml.Parse(packs.Read("Xml", entry)); }
            catch (System.Xml.XmlException) { continue; }
            var raw = SoundEvents(x).ToList();
            if (raw.Count == 0) continue;
            var ours = table.Get(entry["effect/".Length..])?.Where(p => p.Type == "soundEvent").ToList();
            if (ours == null || ours.Count != raw.Count) { wrong.Add($"{entry}: {raw.Count} parts, ours {ours?.Count}"); continue; }
            for (int i = 0; i < raw.Count; i++)
            {
                parts++;
                var r = raw[i];
                var p = ours[i];
                bool rawByJob = TiBool(r.Attribute("byJob"));
                // "job:event;job:event", each pair kept only when it splits into exactly two (FUN_141776330 @soundeventmgr.cpp:408-424)
                var pairs = rawByJob ? ((string)r.Attribute("jobEvent") ?? "").Split(';').Select(s => s.Split(':'))
                    .Where(s => s.Length == 2).ToDictionary(s => int.Parse(s[0]), s => s[1]) : null;
                if (rawByJob) { byJob++; jobPairs += pairs.Count; }
                int gender = int.TryParse((string)r.Attribute("gender"), out int g) ? g : 2;
                int job = int.TryParse((string)r.Attribute("jobCode"), out int j) ? j : 0;
                bool caster = TiBool(r.Attribute("onlyCasterMyPC")), target = TiBool(r.Attribute("onlyTargetMyPC"));
                bool follows = TiBool(r.Element("attachInfo")?.Attribute("attachToNode"));
                if (gender != 2 || job != 0 || caster || target) filtered++;
                if (follows) following++;
                if (p.Gender != gender || p.JobCode != job || p.OnlyCasterMyPC != caster || p.OnlyTargetMyPC != target || p.AttachToNode != follows
                    || p.SoundGroup != (string)r.Attribute("group") || p.SoundEvent != (string)r.Attribute("event")
                    || (pairs == null ? p.JobEvents != null : p.JobEvents == null || !p.JobEvents.OrderBy(k => k.Key).SequenceEqual(pairs.OrderBy(k => k.Key))))
                    wrong.Add($"{entry} part {i}");
            }
        }
        TestContext.Out.WriteLine($"{env}: {parts} soundEvent parts, {filtered} with an owner filter, {byJob} byJob with {jobPairs} job events, {following} following their owner (attachToNode)");
        Assert.That(parts, Is.GreaterThan(10000));
        Assert.That(wrong, Is.Empty);
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryPartsEventResolvesAsTheClientFindsIt(string env)
    {
        var data = Data(env);
        using var packs = new PackSet(data);
        var fev = FevProject.Load(Path.Combine(data, "Sound", "MS2Sound.fev"));
        var table = new EffectTable(packs);
        int events = 0, found = 0, missing = 0;
        var otherGroup = new List<string>();
        foreach (var entry in EffectEntries(packs))
        {
            var parts = table.Get(entry["effect/".Length..]);
            if (parts == null) continue;
            foreach (var p in parts)
            {
                if (p.Type != "soundEvent") continue;
                var names = new List<string> { p.SoundEvent };
                if (p.JobEvents != null) names.AddRange(p.JobEvents.Values);
                foreach (var name in names)
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    events++;
                    // the client: the group by its full path, then the event in it (case-insensitive, as FMOD's lookups are)
                    bool client = !string.IsNullOrEmpty(p.SoundGroup)
                        ? fev.EventsNamed(name).Any(e => string.Equals(e.Group, p.SoundGroup.Trim('/'), StringComparison.OrdinalIgnoreCase))
                        : fev.EventsNamed(name).Count > 0;
                    var ours = fev.FindEvent(name, p.SoundGroup);
                    if (client) found++; else missing++;
                    if (client != (ours != null)) otherGroup.Add($"{entry} {p.SoundGroup}/{name} -> {ours?.FullPath}");
                }
            }
        }
        TestContext.Out.WriteLine($"{env}: {events} events, {found} found, {missing} not in the part's group (silent in the client)");
        foreach (var s in otherGroup.Take(20)) TestContext.Out.WriteLine("  " + s);
        Assert.That(events, Is.GreaterThan(10000));
        Assert.That(otherGroup, Is.Empty, "ours plays where the client finds nothing, or the reverse");
    }
}
