using Ms2.Audio;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>
/// Every event of MS2Sound.fev on MS2_DATA (KMS2) and MS2_GMS2_DATA (GMS2) by its sounds' loop mode (<see cref="FevEvent.DefLoops"/>):
/// the client plays an event and FMOD keeps it playing while it has a looping sound (CollectEventSounds @0x141825f60 reads
/// FMOD_EVENT_INFO.lengthms, -1 for such an event), so a looping event is stopped by its caller. The counts are printed per
/// group; every voice and hit sound plays once, and the table/systemSound.xml rows whose event loops are listed.
/// </summary>
[NonParallelizable]
public class EventLoopModeSweepTests
{
    static SoundLibrary Library(string variable, out PackSet packs)
    {
        packs = null;
        var d = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore($"{variable} is not set");
        var soundDir = Path.Combine(d, "Sound");
        if (!File.Exists(Path.Combine(soundDir, "MS2Sound.fev"))) Assert.Ignore($"no Sound/MS2Sound.fev under {variable}");
        packs = AudioData.Packs(d);
        return new SoundLibrary(soundDir, AudioData.IsKr(packs) ? 0 : 2);
    }

    static string Top(FevEvent ev)
    {
        var parts = ev.Group.Split('/');
        return parts.Length >= 3 ? parts[2] : parts[^1];
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryEventByLoopMode(string variable)
    {
        var fev = Library(variable, out _).Fev;
        var byGroup = new SortedDictionary<string, int[]>(StringComparer.Ordinal);   // looping, one-shot, mixed
        int loops = 0, once = 0, mixed = 0;
        var loopingVoices = new List<string>();
        foreach (var ev in fev.AllEvents)
        {
            if (ev.Defs.Count == 0) continue;
            string top = ev.Group.StartsWith("MS2Sound/Trigger", StringComparison.Ordinal) ? "Trigger" : Top(ev);
            if (!byGroup.TryGetValue(top, out var c)) byGroup[top] = c = new int[3];
            bool all = ev.DefLoops.TrueForAll(l => l);
            if (!ev.Loops) { once++; c[1]++; }
            else if (all) { loops++; c[0]++; }
            else { mixed++; c[2]++; }
            if (ev.Loops && (top == "Voice" || top == "Attack")) loopingVoices.Add(ev.FullPath);
        }
        var m = fev.LoopModeCounts;
        TestContext.Progress.WriteLine($"{variable}: sound instances by loop mode 0 {m[0]}, one-shot {m[1]}, 2 {m[2]}, other {m[3]}; "
            + $"events looping {loops}, one-shot {once}, both {mixed}");
        foreach (var (g, c) in byGroup) TestContext.Progress.WriteLine($"  {g}: looping {c[0]}, one-shot {c[1]}, both {c[2]}");
        Assert.That(m[3], Is.Zero, "a sound instance with a loop mode other than the three");
        Assert.That(loopingVoices, Is.Empty, "a voice or hit sound that loops");
        Assert.That(loops + mixed, Is.GreaterThan(100), "the looping events (keep, remain, idle and ambient sounds)");
    }

    /// <summary>The gacha machine's rolling sound loops until UIItemGachaBoxDialog stops it; its click and result sounds play once.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void TheGachaRollingSoundLoopsAndItsClickAndResultsPlayOnce(string variable)
    {
        var lib = Library(variable, out var packs);
        var table = SystemSoundTable.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/systemsound.xml")));
        bool Loops(string key) => lib.ResolveEvent(table.Resolve(key, 0, 1)) is { } clip ? clip.Loops : throw new AssertionException($"{key} plays nothing");
        Assert.That(Loops("System_Gotcha_Rolling_01"), Is.True);
        foreach (var key in new[] { "System_Gotcha_Click_01", "System_Gotcha_Normal_01", "System_Gotcha_Special_01" })
            Assert.That(Loops(key), Is.False, key);
    }

    /// <summary>Every table/systemSound.xml row whose event loops (PlaySystemSound's sound plays until its handle is stopped).</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void TheSystemSoundRowsThatLoop(string variable)
    {
        var lib = Library(variable, out var packs);
        var table = SystemSoundTable.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/systemsound.xml")));
        var looping = new List<string>();
        int rows = 0, once = 0;
        foreach (var (key, _) in table.Rows)
        {
            var path = table.Resolve(key, 0, 1);
            if (path == null) continue;
            int cut = path.LastIndexOf('/');
            var ev = lib.Fev.FindEvent(path[(cut + 1)..], path[..cut]);
            if (ev == null || ev.Defs.Count == 0) continue;
            rows++;
            if (ev.Loops) looping.Add($"{key} ({path[(cut + 1)..]}{(ev.DefLoops[0] ? "" : ", its first sound plays once")})");
            else once++;
        }
        TestContext.Progress.WriteLine($"{variable}: {rows} rows with an event, {once} play once, {looping.Count} loop: {string.Join(", ", looping)}");
        Assert.That(looping, Has.Some.StartsWith("System_Gotcha_Rolling_01 "));
    }
}
