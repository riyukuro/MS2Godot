namespace Ms2.Audio;

/// <summary>A decoded-ready sound: the mp3 frame stream of a bank sample plus the bank's loop info and the volume to play at.</summary>
public sealed class SoundClip
{
    public string Name;
    public byte[] Mp3;
    public int Frequency;
    public int Channels;
    public double Seconds;
    public int LoopStartSample = -1;
    public int LoopEndSample = -1;
    public float Volume = 1f;
    public float MinDistance, MaxDistance;   // 3D events: attenuation range in world units (0 = not 3D)
}

/// <summary>What PlayMusic(cue) resolves to: the sounds in play order. Loop is the last one; intro (if any) plays once before it.</summary>
public sealed class MusicPlan
{
    public int Cue;
    public SoundClip Intro;
    public SoundClip Loop;
}

/// <summary>
/// The client's Data/Sound folder: MS2Sound.fev plus its FSB banks. Resolves the music cue and event names the client
/// uses (CSoundEventMgr::PlayMusic, getEvent) to mp3 payloads.
/// </summary>
public sealed class SoundLibrary
{
    public FevProject Fev { get; }
    readonly string dir;
    Dictionary<string, string> files;
    readonly Dictionary<string, Fsb5> banks = new(StringComparer.OrdinalIgnoreCase);

    public SoundLibrary(string soundDir)
    {
        dir = soundDir;
        Fev = FevProject.Load(Path.Combine(soundDir, "MS2Sound.fev"));
    }

    static readonly Dictionary<string, SoundLibrary> shared = new(StringComparer.Ordinal);

    /// <summary>
    /// The one library of a Data/Sound folder for the process. The client loads MS2Sound.fev once, at CSoundService init
    /// (FUN_1417aee20, soundservice.cpp:515: m_bLoaded returns at once on a second call; the load at :561), and keeps it for
    /// every field; the field teardown (WorldService FUN_141bbc580, worldservice_1.cpp:9963) only fades the environment
    /// sounds out. The banks opened through it stay with it. Main thread only, as every caller of the library is.
    /// </summary>
    public static SoundLibrary Shared(string soundDir) =>
        shared.TryGetValue(soundDir, out var l) ? l : shared[soundDir] = new SoundLibrary(soundDir);

    /// <summary>The bank file of a bank name from the FEV: MS2Sound_X.fsb, or the language variant of the voice banks.</summary>
    public Fsb5 Bank(string name)
    {
        if (banks.TryGetValue(name, out var b)) return b;
        files ??= Directory.GetFiles(dir, "*.fsb").ToDictionary(f => Path.GetFileName(f), f => f, StringComparer.OrdinalIgnoreCase);   // the data is Windows-cased
        foreach (var suffix in new[] { "", "_Kor_def", "_en" })
            if (files.TryGetValue(name + suffix + ".fsb", out var p)) return banks[name] = new Fsb5(p);
        throw new FileNotFoundException("bank not found: " + name);
    }

    static SoundClip Clip(Fsb5 bank, FsbSample s, float vol) => new()
    {
        Name = s.Name, Mp3 = bank.ReadPayload(s), Frequency = s.Frequency, Channels = s.Channels, Seconds = s.Seconds,
        LoopStartSample = s.LoopStart, LoopEndSample = s.LoopEnd, Volume = vol,
    };

    /// <summary>Music cue (the bgm id of table/fielddata.xml) to its sounds. Segments named bridge* are transitions and skipped.</summary>
    public MusicPlan ResolveMusic(int cueId)
    {
        var cue = Fev.GetMusicCue(cueId);
        if (cue == null || cue.Segments.Count == 0) return null;
        var clips = new List<SoundClip>();
        foreach (var seg in cue.Segments)
        {
            var bank = Bank(seg.Bank);
            if (seg.Index < 0 || seg.Index >= bank.Samples.Count) continue;
            var s = bank.Samples[seg.Index];
            if (s.Name != null && s.Name.StartsWith("bridge", StringComparison.OrdinalIgnoreCase)) continue;
            clips.Add(Clip(bank, s, 1f));
        }
        if (clips.Count == 0) return null;
        var plan = new MusicPlan { Cue = cueId, Loop = clips[0] };
        if (clips.Count > 1 && clips[0].Name.Contains("intro", StringComparison.OrdinalIgnoreCase)) { plan.Intro = clips[0]; plan.Loop = clips[1]; }
        return plan;
    }

    /// <summary>
    /// An event to the sound it plays: the first sound definition's waves (the client picks among a definition's waves at
    /// random) at the event's volume. <paramref name="path"/> is the event name, or a full "MS2Sound/Sound/group/name" path.
    /// </summary>
    public SoundClip ResolveEvent(string path, Random rng = null)
    {
        int cut = path.LastIndexOf('/');
        string name = cut < 0 ? path : path[(cut + 1)..];
        string group = cut < 0 ? null : path[..cut];
        var ev = Fev.FindEvent(name, group);
        if (ev == null) return null;
        rng ??= Random.Shared;
        var def = Fev.Defs[ev.Defs[0]];
        var waves = def.Entries.Where(e => e.Kind == 0).ToList();
        if (waves.Count == 0) return null;
        var clip = FromWave(waves[rng.Next(waves.Count)]);
        if (clip == null) return null;
        clip.Volume = ev.Volume * ev.DefVolumes[0];
        clip.MinDistance = ev.MinDistance;
        clip.MaxDistance = ev.MaxDistance;
        return clip;
    }

    /// <summary>A wave of a sound definition: found by its index and file name in the banks.</summary>
    public SoundClip FromWave(DefEntry w)
    {
        string stem = Path.GetFileNameWithoutExtension(w.Wave);
        foreach (var name in Fev.Banks)
        {
            if (name.StartsWith("MS2Sound_Voice", StringComparison.OrdinalIgnoreCase)) continue;
            Fsb5 bank;
            try { bank = Bank(name); } catch (FileNotFoundException) { continue; }
            if (w.BankIndex >= 0 && w.BankIndex < bank.Samples.Count && string.Equals(bank.Samples[w.BankIndex].Name, stem, StringComparison.OrdinalIgnoreCase))
                return Clip(bank, bank.Samples[w.BankIndex], 1f);
        }
        foreach (var name in Fev.Banks)
        {
            if (!name.StartsWith("MS2Sound_Voice", StringComparison.OrdinalIgnoreCase)) continue;
            Fsb5 bank;
            try { bank = Bank(name); } catch (FileNotFoundException) { continue; }
            if (w.BankIndex >= 0 && w.BankIndex < bank.Samples.Count && string.Equals(bank.Samples[w.BankIndex].Name, stem, StringComparison.OrdinalIgnoreCase))
                return Clip(bank, bank.Samples[w.BankIndex], 1f);
        }
        return null;
    }

    /// <summary>A bank sample by its name in the bank, for sounds the client plays without an event.</summary>
    public SoundClip FindSample(string bankName, string sampleName)
    {
        var bank = Bank(bankName);
        var s = bank.Find(sampleName);
        return s == null ? null : Clip(bank, s, 1f);
    }
}
