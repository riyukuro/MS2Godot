using System.Text;
using System.Text.RegularExpressions;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>
/// Every looping event of MS2Sound.fev (an event with a looping sound, <see cref="FevEvent.Loops"/>) on MS2_DATA (KMS2) and
/// MS2_GMS2_DATA (GMS2) by the owners that play it, each with the client's stop. A looping sound never ends by itself
/// (CollectEventSounds @0x141825f60 reads FMOD_EVENT_INFO.lengthms, -1 for it), so its owner must stop it:
/// <list type="bullet">
/// <item>by name: a map sound entity (a flat or xblock MS2Sound soundEventID, <c>AmbientSounds</c>) or a system sound row
/// (table/systemsound.xml, played by handle);</item>
/// <item>through an effect: every effect/*.xml with a soundEvent part of the event, owned by what names the effect file
/// (a buff's keepEffect, a skill's motionEffect, a ride's loopEffect, a trigger's path, ...).</item>
/// </list>
/// An owner kind not in <see cref="Owners"/> fails the test, so a new kind needs its stop rule first. The counts per owner,
/// the effects released with a looping sound and no looping part of their own, and the events nothing plays are printed;
/// the totals are pinned per region.
/// </summary>
[NonParallelizable]
public class LoopingEventOwnerSweepTests
{
    /// <summary>An owner kind: whether this client plays it, and where its sound stops.</summary>
    sealed record Owner(bool Played, string Stop);

    static readonly Dictionary<string, Owner> Owners = new()
    {
        ["map ambience"] = new(true, "AmbientSounds is a child of the field, freed with it (the client's map leave, "
            + "CEffectService::vfunc_3 @0x1411a8210, removes the point sounds, FUN_1411a6490 effectservice.cpp:1595-1621); "
            + "a trigger object's sound only between its Trigger packet on and off (FUN_14177ce30)"),
        ["system sound"] = new(true, "PlaySystemSound by handle (FieldAudio.PlayUiHandle): the dialog's state or unload stops it"),
        ["buff keep"] = new(true, "AdditionalCli::PlayKeepEffect: the keep effect lives while the buff does and stops at its end"),
        ["buff start"] = new(true, "released at once (FUN_1411a69e0 effectservice.cpp:1510-1528): a looping part stops it; else "
            + "the sound plays on, following the actor until its node leaves, or until the cap takes the oldest looping voice"),
        ["skill motion"] = new(true, "StateSkill::vfunc_5 (stateskill.cpp:515-567): released or stopped at the state's end"),
        ["skill object"] = new(true, "the skill's effect, released or stopped with the skill state (stateskill.cpp:515-567)"),
        ["skill loop keep"] = new(true, "the loop keep key starts it and the loop end stops it (FieldAudio.PlayEventVoice keys)"),
        ["skill region"] = new(true, "the region skill's end (RegionSkills FadeOut stops its kept sounds)"),
        ["fly item"] = new(true, "the glide's end stops the run and idle effects (PlayerRide keeps them)"),
        ["ride"] = new(true, "~Ridee @0x141704240 (ridee.cpp:390-407) stops the loop and run effects"),
        ["trigger effect"] = new(true, "the trigger's effect off (FieldTriggers FadeOut)"),
        ["interact object"] = new(true, "the object's state change or leave (FieldInteractDraws and ReactorLayer FadeOut)"),
        ["npc keep"] = new(false, "not played here: the npc data's keepEffect is not read"),
        ["function cube"] = new(false, "not played here: a function cube's FirstEffect"),
        ["emotion"] = new(false, "not played here: the emotion files' effects"),
        ["map entity effect"] = new(false, "not played here: a map entity's effect draws its model only (FieldEffects)"),
        ["common resource"] = new(false, "not played here: the fishing and tutorial effects of table/commonresource.xml"),
        ["pet taming"] = new(false, "not played here: the taming effect of table/constants.xml"),
    };

    /// <summary>Per region: the looping events, those nothing plays (named nowhere, or only in an effect nothing names), and
    /// those only owners not played here play.</summary>
    static readonly Dictionary<string, (int Events, int NoOwner, int Unplayed)> Pins = new()
    {
        ["MS2_DATA"] = (289, 36, 36),
        ["MS2_GMS2_DATA"] = (289, 36, 36),
    };

    static readonly Regex FileRef = new("([A-Za-z_][\\w.]*)\\s*=\\s*\"([^\"<>]{1,300}\\.xml)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex SoundRef = new("\"(MS2Sound/[^\"<>]{1,200})\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static FevEvent Find(FevProject fev, string path)
    {
        path = path.Replace('\\', '/').Trim('/');
        int cut = path.LastIndexOf('/');
        return cut < 0 ? fev.FindEvent(path) : fev.FindEvent(path[(cut + 1)..], path[..cut]);
    }

    /// <summary>The owner kind of an effect named by <paramref name="attr"/> in <paramref name="entry"/>; null for a list of
    /// effects that plays none (table/effect_benchmark.xml).</summary>
    static string Kind(string entry, string attr)
    {
        int c = entry.IndexOf('/');
        string dir = c > 0 ? entry[..c] : entry;
        bool buff = dir is "additional" or "additionaleffect";
        bool skill = dir is "skilldata" or "skill";
        return (dir, attr) switch
        {
            (_, "p") when entry == "table/effect_benchmark.xml" => null,
            (_, "keepEffect") when buff => "buff keep",
            (_, "keepEffect") when dir is "npcdata" or "npc" => "npc keep",
            (_, "effect") when buff => "buff start",
            (_, "effect") when skill => "skill object",
            (_, "motionEffect") => "skill motion",
            (_, "loopKeepEffect") => "skill loop keep",
            (_, "splashInvokeEffect" or "effectRemain") => "skill region",
            (_, "effectRun" or "effectIdle") => "fly item",
            ("riding", "loopEffect") => "ride",
            ("trigger", "path") => "trigger effect",
            ("object", "FirstEffect") => "function cube",
            ("emotion", "file") => "emotion",
            ("xblock", "value") => "map entity effect",
            _ when entry == "table/interactobject.xml" && attr is "reactable" or "interaction" or "normal" => "interact object",
            _ when entry == "table/commonresource.xml" && attr == "value" => "common resource",
            _ when entry == "table/constants.xml" && attr == "value" => "pet taming",
            _ => "?" + entry + "@" + attr,
        };
    }

    static IEnumerable<string> EffectKeys(string v)
    {
        v = v.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
        yield return v;
        int i = v.IndexOf("effect/", StringComparison.Ordinal);
        if (i >= 0) yield return v[(i + 7)..];
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryLoopingEventHasAnOwnerThatStopsIt(string variable)
    {
        var d = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore($"{variable} is not set");
        var soundDir = Path.Combine(d, "Sound");
        if (!File.Exists(Path.Combine(soundDir, "MS2Sound.fev"))) Assert.Ignore($"no Sound/MS2Sound.fev under {variable}");
        var packs = AudioData.Packs(d);
        var fev = new SoundLibrary(soundDir, AudioData.IsKr(packs) ? 0 : 2).Fev;

        var owners = new Dictionary<FevEvent, SortedSet<string>>(ReferenceEqualityComparer.Instance);
        foreach (var ev in fev.AllEvents)
            if (ev.Defs.Count > 0 && ev.Loops) owners[ev] = new SortedSet<string>(StringComparer.Ordinal);
        void Own(FevEvent ev, string kind) { if (ev != null && owners.TryGetValue(ev, out var s)) s.Add(kind); }

        // the effects with a soundEvent part of a looping event
        var fx = new EffectTable(packs);
        var loopFx = new Dictionary<string, List<FevEvent>>(StringComparer.OrdinalIgnoreCase);
        var releasedLoops = new List<string>();   // effects whose only looping thing is the sound
        foreach (var entry in packs.List("Xml"))
        {
            if (!entry.StartsWith("effect/", StringComparison.Ordinal) || !entry.EndsWith(".xml", StringComparison.Ordinal)) continue;
            var rel = entry["effect/".Length..];
            var parts = fx.Get(rel);
            if (parts == null) continue;
            List<FevEvent> evs = null;
            foreach (var p in parts)
                if (p.Type == "soundEvent" && !string.IsNullOrEmpty(p.SoundEvent)
                    && Find(fev, p.SoundGroup == null ? p.SoundEvent : p.SoundGroup + "/" + p.SoundEvent) is { } ev && owners.ContainsKey(ev))
                    (evs ??= new()).Add(ev);
            if (evs == null) continue;
            loopFx[rel] = evs;
            if (!parts.Any(p => p.Loop)) releasedLoops.Add(rel);
        }

        // what names the effects, and the events named directly
        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        var effectKinds = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var examples = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);   // a few referrers per kind, for the log
        void Scan(string archive, string entry, bool sounds)
        {
            string text;
            try { text = Encoding.UTF8.GetString(packs.Read(archive, entry)); } catch (Exception) { return; }
            if (sounds)
                foreach (Match m in SoundRef.Matches(text)) Own(Find(fev, m.Groups[1].Value), "map ambience");
            foreach (Match m in FileRef.Matches(text))
                foreach (var key in EffectKeys(m.Groups[2].Value))
                {
                    if (!loopFx.ContainsKey(key)) continue;
                    var kind = Kind(entry, m.Groups[1].Value);
                    if (kind == null) continue;
                    if (kind[0] == '?') { unknown.Add(kind); continue; }
                    if (!effectKinds.TryGetValue(key, out var s)) effectKinds[key] = s = new(StringComparer.Ordinal);
                    s.Add(kind);
                    if (!examples.TryGetValue(kind, out var x)) examples[kind] = x = new(StringComparer.Ordinal);
                    if (x.Count < 4) x.Add($"{entry} -> {key}");
                }
        }
        foreach (var entry in packs.List("Xml"))
            if (!entry.StartsWith("string/", StringComparison.Ordinal) && entry.EndsWith(".xml", StringComparison.Ordinal))
                Scan("Xml", entry, false);
        foreach (var entry in packs.List("Resource/Exported"))
            if (entry.EndsWith(".xblock", StringComparison.Ordinal) || entry.EndsWith(".flat", StringComparison.Ordinal))
                Scan("Resource/Exported", entry, true);
        var system = SystemSoundTable.Parse(Encoding.UTF8.GetString(packs.Read("Xml", "table/systemsound.xml")));
        foreach (var row in system.Rows.Values)
            foreach (var name in row.Sounds.Concat(row.JobSounds.Values.SelectMany(n => n)))
                for (int gender = 0; gender < 2; gender++) Own(Find(fev, row.Category(gender) + "/" + name), "system sound");
        foreach (var (rel, evs) in loopFx)
            if (effectKinds.TryGetValue(rel, out var kinds))
                foreach (var ev in evs)
                    foreach (var k in kinds) Own(ev, k);

        var byKind = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var noOwner = new List<string>();
        var unplayed = new List<string>();
        foreach (var (ev, kinds) in owners)
        {
            foreach (var k in kinds) byKind[k] = byKind.GetValueOrDefault(k) + 1;
            if (kinds.Count == 0) noOwner.Add(ev.FullPath);
            else if (kinds.All(k => !Owners[k].Played)) unplayed.Add(ev.FullPath);
        }
        var log = TestContext.Progress;
        log.WriteLine($"{variable}: {owners.Count} looping events, {loopFx.Count} effects play one ({releasedLoops.Count} with no "
            + $"looping part of their own), {noOwner.Count} events nothing plays, {unplayed.Count} only by owners not played here");
        foreach (var (k, n) in byKind)
        {
            log.WriteLine($"  {k}: {n} events; {Owners[k].Stop}");
            if (examples.TryGetValue(k, out var x)) foreach (var e in x) log.WriteLine($"    e.g. {e}");
        }
        foreach (var e in noOwner.OrderBy(x => x, StringComparer.Ordinal)) log.WriteLine($"  no owner: {e}");
        foreach (var e in unplayed.OrderBy(x => x, StringComparer.Ordinal))
            log.WriteLine($"  not played here: {e} ({string.Join(", ", owners.First(o => o.Key.FullPath == e).Value)})");

        Assert.That(unknown, Is.Empty, "an effect owner with no stop rule");
        Assert.That(owners.Count, Is.EqualTo(Pins[variable].Events), "the looping events");
        Assert.That(noOwner.Count, Is.EqualTo(Pins[variable].NoOwner), "the looping events nothing plays");
        Assert.That(unplayed.Count, Is.EqualTo(Pins[variable].Unplayed), "the looping events only owners not played here play");
    }
}
