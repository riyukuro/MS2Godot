using Ms2.Audio;
using Ms2.Formats.Options;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>
/// The sound options' categories. Every event of MS2Sound.fev on MS2_DATA (KMS2_DEV or KMS2) and MS2_GMS2_DATA (GMS2) is
/// resolved to the client's category slot (s_apszCategory, soundeventmgr.cpp:884; getCategory by the bare name,
/// :917-927) and to the authored volume of its sub-categories; SoundMix holds each slot's volume and mute as
/// FUN_14125fe20 (gameoptionstruct.cpp:2727-2752), SetAllMute (soundeventmgr.cpp:1711) and the window activation
/// (windowservice.cpp:801-831, soundservice.cpp:370-391) set them.
/// </summary>
[NonParallelizable]
public class SoundCategorySweepTests
{
    static FevProject Fev(string variable)
    {
        var d = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore($"{variable} is not set");
        var f = Path.Combine(d, "Sound", "MS2Sound.fev");
        if (!File.Exists(f)) Assert.Ignore($"{variable} has no Sound/MS2Sound.fev");
        return FevProject.Load(f);
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryEventHasAnOptionCategory(string variable)
    {
        var fev = Fev(variable);
        var perSlot = new int[SoundCategories.Count];
        var unresolved = new List<string>();
        int total = 0;
        foreach (var ev in fev.AllEvents)
        {
            total++;
            if (!SoundCategories.TryResolve(fev, ev.Category, out int slot, out float below)) { unresolved.Add($"{ev.FullPath} '{ev.Category}'"); continue; }
            perSlot[slot]++;
            Assert.That(below, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f), ev.FullPath);
        }
        TestContext.Progress.WriteLine($"{variable}: {total} events; " +
            string.Join(", ", Enumerable.Range(0, SoundCategories.Count).Select(i => $"{i} {SoundCategories.Names[i]} {perSlot[i]}")) +
            $"; unresolved {unresolved.Count}");
        Assert.That(total, Is.EqualTo(fev.EventCount).And.GreaterThan(10000));
        Assert.That(unresolved, Is.Empty, string.Join("\n", unresolved.Take(20)));
        // the categories the sliders reach are the ones the events use
        foreach (int s in new[] { SoundCategories.Effect, SoundCategories.Environment, SoundCategories.UiSound, SoundCategories.NpcVoice,
                     SoundCategories.SystemVoice, SoundCategories.MobVoice, SoundCategories.EmotionVoice })
            Assert.That(perSlot[s], Is.GreaterThan(0), SoundCategories.Names[s]);
    }

    /// <summary>The top-level categories of the project against the client's eleven names: getCategory by a bare name
    /// finds a top-level category; effectSpecialSkill sits under other categories and PCVoice is absent, so those two slots
    /// hold none (and no event plays in them).</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void TheClientsSlotsAgainstTheProjectsCategories(string variable)
    {
        var fev = Fev(variable);
        var top = fev.RootCategory.Children.Select(c => c.Name).ToList();
        TestContext.Progress.WriteLine($"{variable}: top-level categories {string.Join(", ", top)}");
        for (int i = 0; i < SoundCategories.Count; i++)
        {
            bool expected = i != SoundCategories.EffectSpecialSkill && i != SoundCategories.PcVoice;
            Assert.That(fev.FindCategory(SoundCategories.Names[i]) != null, Is.EqualTo(expected), SoundCategories.Names[i]);
        }
        Assert.That(top.Where(n => SoundCategories.IndexOf(n) < 0), Is.EqualTo(new[] { "music" }), "music is the music system's, no option");
        Assert.That(fev.AllEvents.Count(e => e.Category.StartsWith("music", StringComparison.OrdinalIgnoreCase)), Is.Zero);
    }

    /// <summary>A sub-category's authored volume stays under the option volume: the hit sounds of effect/Hit_JustFail
    /// play at that category's volume times the effect slider.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void SubCategoryVolumeStays(string variable)
    {
        var fev = Fev(variable);
        var sub = fev.FindCategory("effect/Hit_JustFail");
        Assert.That(sub, Is.Not.Null);
        Assert.That(SoundCategories.TryResolve(fev, "effect/Hit_JustFail", out int slot, out float below), Is.True);
        Assert.That((slot, below), Is.EqualTo((SoundCategories.Effect, sub.Volume)));
        Assert.That(SoundCategories.TryResolve(fev, "effect", out slot, out below), Is.True);
        Assert.That((slot, below), Is.EqualTo((SoundCategories.Effect, 1f)), "the top-level volume is the option's");
        Assert.That(SoundCategories.TryResolve(fev, "nothing/here", out slot, out _), Is.False);
        Assert.That(slot, Is.EqualTo(-1));
    }

    /// <summary>A resolved sound carries its slot, so its player takes the slot's bus.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void ResolvedClipsCarryTheirCategory(string variable)
    {
        var d = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore($"{variable} is not set");
        // the GMS2 data has the English voice banks only (voice 2), the KMS data the Korean ones (voice 0); pick by the banks the
        // data holds, not by the variable's name (the GMS2 gate runs the MS2_DATA case on GMS2 data)
        string sound = Path.Combine(d, "Sound");
        var lib = new SoundLibrary(sound, File.Exists(Path.Combine(sound, "MS2Sound_Voice_Kor_def.fsb")) ? 0 : 2);
        int checkedCount = 0;
        // per top-level category, the first event whose sound is in this data's banks
        foreach (var g in lib.Fev.AllEvents.Where(e => e.Defs.Count > 0).GroupBy(e => e.Category.Split('/')[0]))
            foreach (var ev in g.Take(200))
            {
                SoundClip clip;
                try { clip = lib.ResolveEvent(ev.FullPath, new Random(1)); } catch (FileNotFoundException) { continue; }
                if (clip == null) continue;
                SoundCategories.TryResolve(lib.Fev, ev.Category, out int slot, out _);
                Assert.That(clip.Category, Is.EqualTo(slot), ev.FullPath);
                checkedCount++;
                break;
            }
        TestContext.Progress.WriteLine($"{variable}: {checkedCount} top-level categories checked through ResolveEvent");
        Assert.That(checkedCount, Is.EqualTo(7), "effect, environment, UISound, npcVoice, SystemVoice, mobVoice, EmotionVoice");
        var voice = lib.Fev.FindEvent(SoundLibrary.TriggerVoiceEvent, SoundLibrary.TriggerVoiceGroup);
        Assert.That(voice?.Category, Does.StartWith("EmotionVoice"), "the trigger voices play through Voice/ko/1");
    }

    // ---- SoundVolumes -> per-category gain -------------------------------------------------------------------

    static GameOption.Sound Options() => new()
    {
        MainSoundStep = 8, EffectSoundStep = 5, EnviromentSoundStep = 4, BgmSoundStep = 10, UiSoundStep = 3, NpcSoundStep = 6,
        BossSoundStep = 7, SystemSoundStep = 2, CharSoundStep = 9, MovieSoundStep = 1,
    };

    [Test]
    public void EachSliderReachesItsCategories()
    {
        var mix = new SoundMix();
        mix.Apply(SoundVolumes.From(Options(), false, true));
        // GetCategoryVolume: step * main / 100 (gameoptionstruct.cpp:2757-2783)
        var expected = new[] { 0.40f, 0.32f, 0.80f, 0.24f, 0.48f, 0.16f, 0.56f, 0.72f, 0.16f, 0.40f, 0.48f };
        for (int i = 0; i < SoundMix.CategoryCount; i++)
            Assert.That(mix.Gain(i), Is.EqualTo(expected[i]).Within(1e-6f), SoundCategories.Names[i]);
        Assert.That(mix.MusicVolume, Is.EqualTo(0.8f).Within(1e-6f));
        Assert.That(mix.Gain(-1), Is.EqualTo(1f), "a sound in no category is under no option");
        Assert.That((mix.MusicMuted, mix.MovieMuted), Is.EqualTo((false, false)));
    }

    [Test]
    public void EffectsOffMusicOnThenTheOtherWay()
    {
        var mix = new SoundMix();
        var o = Options();
        o.EffectSoundStep = 0;
        mix.Apply(SoundVolumes.From(o, false, true));
        Assert.That((mix.Gain(SoundCategories.Effect), mix.Gain(SoundCategories.EffectSpecialSkill), mix.MusicVolume), Is.EqualTo((0f, 0f, 0.8f)));
        Assert.That(mix.Gain(SoundCategories.UiSound), Is.EqualTo(0.24f).Within(1e-6f), "the ui slider is its own");
        o.EffectSoundStep = 5;
        o.BgmSoundStep = 0;
        mix.Apply(SoundVolumes.From(o, false, true));
        Assert.That(mix.MusicVolume, Is.Zero);
        Assert.That(mix.Gain(SoundCategories.Effect), Is.EqualTo(0.4f).Within(1e-6f));
        o.MainSoundStep = 0;
        mix.Apply(SoundVolumes.From(o, false, true));
        for (int i = 0; i < SoundMix.CategoryCount; i++) Assert.That(mix.Gain(i), Is.Zero, "the main slider is in every category");
    }

    [Test]
    public void MuteAllMutesEveryCategoryTheMusicAndTheMovie()
    {
        var mix = new SoundMix();
        var o = Options();
        o.MuteAll = true;
        mix.Apply(SoundVolumes.From(o, false, true));
        for (int i = 0; i < SoundMix.CategoryCount; i++) Assert.That((mix.Muted(i), mix.Gain(i)), Is.EqualTo((true, 0f)), SoundCategories.Names[i]);
        Assert.That((mix.MusicMuted, mix.MovieMuted), Is.EqualTo((true, true)));
        Assert.That(mix.Volume(SoundCategories.Effect), Is.EqualTo(0.4f).Within(1e-6f), "the volume stays under the mute");
        o.MuteAll = false;
        mix.Apply(SoundVolumes.From(o, false, true));
        Assert.That((mix.Muted(0), mix.MusicMuted, mix.MovieMuted), Is.EqualTo((false, false, false)));
    }

    [Test]
    public void TheWindowFocusMutesUnlessPlayAll()
    {
        var mix = new SoundMix();
        var o = Options();
        mix.Apply(SoundVolumes.From(o, false, true));
        int v = mix.Version;
        mix.AppDeactivated(o);
        Assert.That(mix.AppActive, Is.False);
        Assert.That(mix.Version, Is.Not.EqualTo(v));
        Assert.That((mix.Gain(0), mix.MusicMuted, mix.MovieMuted), Is.EqualTo((0f, true, true)));
        mix.AppActivated(o);
        Assert.That((mix.Gain(0) > 0, mix.MusicMuted), Is.EqualTo((true, false)));
        Assert.That(mix.MovieMuted, Is.True, "FUN_1417ae840(0) unmutes the categories only, not the video (soundservice.cpp:378-388)");
        // with mute-all on, coming back keeps everything muted
        o.MuteAll = true;
        mix.AppDeactivated(o);
        mix.AppActivated(o);
        Assert.That((mix.Muted(4), mix.MusicMuted), Is.EqualTo((true, true)));
        // play in the background: the focus changes nothing
        o.MuteAll = false;
        o.PlayAll = true;
        mix.Apply(SoundVolumes.From(o, false, true));
        mix.AppDeactivated(o);
        Assert.That((mix.Muted(0), mix.MusicMuted), Is.EqualTo((false, false)));
        Assert.That(SoundVolumes.From(o, false, mix.AppActive).AllMute, Is.False);
        o.PlayAll = false;
        Assert.That(SoundVolumes.From(o, false, mix.AppActive).AllMute, Is.True, "a save while inactive keeps the sound muted");
    }

    [Test]
    public void GainReadsDoNotAllocate()
    {
        var mix = new SoundMix();
        mix.Apply(SoundVolumes.From(Options(), false, true));
        float sum = 0;
        for (int i = 0; i < 100; i++) sum += mix.Gain(i % 12 - 1);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) sum += mix.Gain(i % 12 - 1) + (mix.Muted(i % 11) ? 1 : 0);
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
        Assert.That(sum, Is.GreaterThan(0));
    }

    /// <summary>The game's bus layout (game/default_bus_layout.tres, which the engine loads at startup) holds Master and then
    /// SoundCategories.BusNames in order, each sending to Master: SoundBuses sets them by index from bus 1.</summary>
    [Test]
    public void TheBusLayoutMatchesTheBusNames()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MS2Godot.sln"))) dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "the repository root");
        var file = Path.Combine(dir.FullName, "game", "default_bus_layout.tres");
        var props = new Dictionary<string, string>();
        foreach (var line in File.ReadLines(file))
        {
            int eq = line.IndexOf(" = ", StringComparison.Ordinal);
            if (line.StartsWith("bus/", StringComparison.Ordinal) && eq > 0) props[line[..eq]] = line[(eq + 3)..];
        }
        static string Name(string v) => v.StartsWith("&\"", StringComparison.Ordinal) ? v[2..^1] : v;
        Assert.That(Name(props["bus/0/name"]), Is.EqualTo("Master"));
        int count = props.Keys.Count(k => k.EndsWith("/name", StringComparison.Ordinal));
        Assert.That(count, Is.EqualTo(SoundCategories.BusNames.Length + 1), "Master and one bus per name, no more");
        for (int i = 0; i < SoundCategories.BusNames.Length; i++)
        {
            Assert.That(Name(props[$"bus/{i + 1}/name"]), Is.EqualTo(SoundCategories.BusNames[i]), $"bus {i + 1}");
            Assert.That(Name(props[$"bus/{i + 1}/send"]), Is.EqualTo("Master"), $"bus {i + 1} send");
            Assert.That(props[$"bus/{i + 1}/volume_db"], Is.EqualTo("0.0"));
            Assert.That(props[$"bus/{i + 1}/mute"], Is.EqualTo("false"));
        }
        Assert.That(SoundCategories.BusNames.Take(SoundCategories.Count), Is.EqualTo(SoundCategories.Names), "a slot's bus is its category's");
        Assert.That(SoundCategories.BusNames.Skip(SoundCategories.Count), Is.EqualTo(new[] { "Music", "Movie" }));
    }

    /// <summary>The options save applies the page state before storing it (CGameOptionSoundDefault::vfunc_10,
    /// gameoptionstruct.cpp:645-648), so the movie volume must come with the applied state, not from the store.</summary>
    [Test]
    public void TheMovieVolumeComesWithTheAppliedState()
    {
        var stored = Options();
        var mix = new SoundMix();
        mix.Apply(SoundVolumes.From(stored, false, true));
        Assert.That(mix.MovieVolume, Is.EqualTo(0.08f).Within(1e-6f), "movie 1 x main 8 / 100");
        var page = stored.Clone();
        page.MovieSoundStep = 10;
        page.MainSoundStep = 10;
        int v = mix.Version;
        mix.Apply(SoundVolumes.From(page, false, true));   // the save, while the store still holds the old state
        Assert.That(mix.MovieVolume, Is.EqualTo(1f).Within(1e-6f));
        Assert.That(mix.Version, Is.Not.EqualTo(v), "the buses see the change");
        Assert.That(stored.MovieVolume, Is.EqualTo(0.08f).Within(1e-6f), "the store is not what was applied");
    }
}
