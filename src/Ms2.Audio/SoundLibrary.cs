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
    /// <summary>The event's sound loops until it is stopped (<see cref="FevEvent.DefLoops"/>); a music segment's loop is the cue's.</summary>
    public bool Loops;
    public float MinDistance, MaxDistance;   // 3D events: attenuation range in world units (0 = not 3D)
    /// <summary>The client's category slot the event plays in (<see cref="SoundCategories"/>); -1 for none (a music
    /// segment, which the music system plays).</summary>
    public int Category = -1;
}

/// <summary>
/// Summary of a music cue resolved from the FEV segment graph:
/// - OneShot = true: Loop holds the single segment (plays once, no outgoing links).
/// - Intro != null: Intro plays first, then Loop repeats unconditionally.
/// - Bridge != null: Loop and Bridge alternate via parameter 5 (MusicIntermission 0 &lt;-&gt; 10).
/// - Otherwise: Loop repeats unconditionally.
/// </summary>
public sealed class MusicPlan
{
    public int Cue;
    public SoundClip Intro;
    public SoundClip Loop;
    public SoundClip Bridge;
    public bool OneShot;
    public int CrossfadeMs;
}

/// <summary>
/// The client's Data/Sound folder: MS2Sound.fev plus its FSB banks, for a chosen voice language (GetVoice():
/// 0 KR, 1 JP, 2 EN, 3 CN; CSoundService @0x1417aea70, soundservice.cpp:418-433). Resolves music cues/segments,
/// event paths (with full event group disambiguation) and voice programmer-sound tags to mp3 payloads.
/// </summary>
public sealed class SoundLibrary
{
    public FevProject Fev { get; }
    /// <summary>Client voice language id (0 KR, 1 JP, 2 EN, 3 CN; localepath.cpp:400).</summary>
    public int VoiceLanguage { get; }
    /// <summary>FMOD project language name passed to setLanguage ("Korea", "jp", "en", "cn").</summary>
    public string LanguageName => FevProject.VoiceLanguageName(VoiceLanguage);

    readonly string dir;
    Dictionary<string, string> files;
    readonly Dictionary<string, Fsb5> banks = new(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, (Fsb5 Bank, FsbSample Sample)> voiceTags;

    public SoundLibrary(string soundDir, int voiceLanguage = 0)
        : this(soundDir, LoadSharedFev(soundDir), voiceLanguage) { }

    SoundLibrary(string soundDir, FevProject fev, int voiceLanguage)
    {
        dir = soundDir;
        Fev = fev;
        VoiceLanguage = voiceLanguage;
    }

    static readonly Dictionary<string, FevProject> fevsByDir = new(StringComparer.Ordinal);
    static readonly Dictionary<(string Dir, int Voice), SoundLibrary> shared = new();

    static FevProject LoadSharedFev(string soundDir) =>
        fevsByDir.TryGetValue(soundDir, out var f) ? f : fevsByDir[soundDir] = FevProject.Load(Path.Combine(soundDir, "MS2Sound.fev"));

    /// <summary>
    /// The one library of a (Data/Sound folder, voice language) pair for the process. The client loads MS2Sound.fev once,
    /// at CSoundService init (@0x1417aee20, soundservice.cpp:515-561) with EventSystem::setLanguage(szLanguage) (:550),
    /// and keeps it for every field; banks opened through it stay with it. Main thread only, as every caller is.
    /// </summary>
    public static SoundLibrary Shared(string soundDir, int voiceLanguage = 0)
    {
        var key = (soundDir, voiceLanguage);
        if (shared.TryGetValue(key, out var l)) return l;
        return shared[key] = new SoundLibrary(soundDir, LoadSharedFev(soundDir), voiceLanguage);
    }

    Dictionary<string, string> Files() =>
        files ??= Directory.GetFiles(dir, "*.fsb").ToDictionary(f => Path.GetFileName(f), f => f, StringComparer.OrdinalIgnoreCase);   // the data is Windows-cased

    /// <summary>True when the FSB file for <paramref name="name"/> in <paramref name="voiceLanguage"/> exists on disk.</summary>
    public bool HasBankForVoice(string name, int voiceLanguage) =>
        Files().ContainsKey(Fev.BankFileNameForVoice(name, voiceLanguage));

    /// <summary>
    /// Opens the bank file for <paramref name="name"/> using the FEV LANG + bank suffix table for
    /// <see cref="VoiceLanguage"/>. When <paramref name="allowFallback"/> is true and the requested language's bank is not
    /// installed on disk, falls back to the FEV's default language suffix, then the unsuffixed bank name.
    /// </summary>
    public Fsb5 Bank(string name, bool allowFallback = true)
    {
        if (banks.TryGetValue(name, out var b)) return b;
        var map = Files();
        string primary = Fev.BankFileNameForVoice(name, VoiceLanguage);
        if (map.TryGetValue(primary, out var p)) return banks[name] = new Fsb5(p);
        if (allowFallback)
        {
            string defName = Fev.BankFileName(name, null);
            if (!defName.Equals(primary, StringComparison.OrdinalIgnoreCase) && map.TryGetValue(defName, out p))
                return banks[name] = new Fsb5(p);
            string bare = name + ".fsb";
            if (map.TryGetValue(bare, out p)) return banks[name] = new Fsb5(p);
        }
        throw new FileNotFoundException($"bank not found: {primary} (for {name}, language {LanguageName})");
    }

    static SoundClip Clip(Fsb5 bank, FsbSample s, float vol) => new()
    {
        Name = s.Name, Mp3 = bank.ReadPayload(s), Frequency = s.Frequency, Channels = s.Channels, Seconds = s.Seconds,
        LoopStartSample = s.LoopStart, LoopEndSample = s.LoopEnd, Volume = vol,
    };

    /// <summary>Resolves one FEV music segment to its bank sample clip, or null if the bank is missing or index is out of range.</summary>
    public SoundClip ResolveSegment(MusicSegment seg)
    {
        if (seg == null || string.IsNullOrEmpty(seg.Bank)) return null;
        Fsb5 bank;
        try { bank = Bank(seg.Bank); } catch (FileNotFoundException) { return null; }
        if (seg.Index < 0 || seg.Index >= bank.Samples.Count) return null;
        return Clip(bank, bank.Samples[seg.Index], 1f);
    }

    /// <summary>
    /// Resolves a music cue (the bgm id of table/fielddata.xml) using the FEV's real segment graph (theme start segment,
    /// outgoing links and parameter 5 conditions) rather than sample-name heuristics.
    /// </summary>
    public MusicPlan ResolveMusic(int cueId)
    {
        var cue = Fev.GetMusicCue(cueId);
        if (cue == null || cue.Segments.Count == 0) return null;
        var startSeg = Fev.GetMusicSegment(cue.StartSegmentId) ?? cue.Segments[0];
        var startClip = ResolveSegment(startSeg);
        if (startClip == null) return null;

        var plan = new MusicPlan { Cue = cueId, Loop = startClip, CrossfadeMs = cue.CrossfadeMs };
        if (startSeg.OutgoingLinks.Count == 0)
        {
            plan.OneShot = true;
            return plan;
        }
        if (MusicController.IsUnconditionalSelfLoop(startSeg))
            return plan;

        // Follow the cue's first outgoing transition (e.g. start -> loop or start -> bridge).
        var nextSeg = Fev.GetMusicSegment(startSeg.OutgoingLinks[0].TargetSegmentId);
        var nextClip = ResolveSegment(nextSeg);
        if (nextSeg == null || nextClip == null || nextSeg.Id == startSeg.Id)
            return plan;

        if (MusicController.IsUnconditionalSelfLoop(nextSeg))
        {
            // Intro (or silence lead-in) -> unconditionally looping main segment.
            plan.Intro = startClip;
            plan.Loop = nextClip;
        }
        else if (nextSeg.OutgoingLinks.Any(l => l.TargetSegmentId == startSeg.Id))
        {
            // Main segment <-> bridge/intermission alternating loop (param 5 toggled 0 <-> 10).
            plan.Loop = startClip;
            plan.Bridge = nextClip;
        }
        return plan;
    }

    /// <summary>
    /// An event to the sound it plays: the first sound definition's waves (the client picks among a definition's waves at
    /// random) at the event's volume. <paramref name="path"/> is the event name, or a full "MS2Sound/Sound/group/name" path.
    /// </summary>
    public SoundClip ResolveEvent(string path, Random rng = null)
    {
        if (string.IsNullOrEmpty(path)) return null;
        path = path.Replace('\\', '/').Trim('/');
        int cut = path.LastIndexOf('/');
        string name = cut < 0 ? path : path[(cut + 1)..];
        string group = cut < 0 ? null : path[..cut];
        var ev = Fev.FindEvent(name, group);
        if (ev == null || ev.Defs.Count == 0) return null;
        rng ??= Random.Shared;
        var def = Fev.Defs[ev.Defs[0]];
        var waves = def.Entries.Where(e => e.Kind == 0).ToList();
        if (waves.Count == 0) return null;
        var clip = FromWave(waves[rng.Next(waves.Count)]);
        if (clip == null) return null;
        clip.Volume = ev.Volume * ev.DefVolumes[0];
        clip.Loops = ev.DefLoops[0];
        TakeCategory(ev, clip);
        clip.MinDistance = ev.MinDistance;
        clip.MaxDistance = ev.MaxDistance;
        return clip;
    }

    /// <summary>
    /// A wave of a sound definition: resolved directly through its FEV bank index (DefEntry.Bank -> Fev.Banks) in the
    /// configured <see cref="VoiceLanguage"/>. Handles language-prefixed sample names (e.g. "en_...") and falls back to
    /// name lookup within that bank when a language FSB's sample order differs from the default bank (as MS2Sound_Voice8_en does).
    /// </summary>
    public SoundClip FromWave(DefEntry w)
    {
        string stem = Path.GetFileNameWithoutExtension(w.Wave);
        string prefix = FevProject.VoiceSamplePrefix(VoiceLanguage);
        if (w.Bank >= 0 && w.Bank < Fev.Banks.Count)
        {
            Fsb5 bank;
            try { bank = Bank(Fev.Banks[w.Bank]); } catch (FileNotFoundException) { bank = null; }
            if (bank != null)
            {
                if (w.BankIndex >= 0 && w.BankIndex < bank.Samples.Count)
                {
                    var s = bank.Samples[w.BankIndex];
                    if (MatchesWaveName(s.Name, stem, prefix))
                        return Clip(bank, s, 1f);
                }
                var byName = (prefix.Length > 0 ? bank.Find(prefix + stem) : null) ?? bank.Find(stem);
                if (byName != null) return Clip(bank, byName, 1f);
            }
        }
        return null;
    }

    public static bool MatchesWaveName(string sampleName, string stem, string prefix)
    {
        if (string.IsNullOrEmpty(sampleName) || string.IsNullOrEmpty(stem)) return false;
        if (string.Equals(sampleName, stem, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrEmpty(prefix) && sampleName.Length == prefix.Length + stem.Length
            && sampleName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && sampleName.EndsWith(stem, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    /// <summary>
    /// Programmer-sound voice tag lookup over MS2Sound_Voice8 and MS2Sound_Voice9 for <see cref="VoiceLanguage"/>, ported
    /// from CSoundService::OpenVoiceStreams (@0x1417aff20, soundservice.cpp:804-848), BuildTagTable (@0x1417afb50, :733-800)
    /// and GetSubSound (@0x1417b0560, :880-903). Note: voice language 1 (JP) returns no streams in the client (:811-812, :755-757).
    /// </summary>
    public bool HasVoiceTag(string tag) => !string.IsNullOrEmpty(tag) && VoiceTags().ContainsKey(tag);

    /// <summary>Resolves a trigger/programmer voice tag (e.g. from CEffectService::PlayTriggerVoice) in the current voice language.</summary>
    /// <summary>The sound plays through the event "MS2Sound/Sound/Voice/ko" "1" (CEffectService::PlayTriggerVoice,
    /// effectservice.cpp:1438), so it takes that event's category.</summary>
    public SoundClip ResolveVoiceTag(string tag)
    {
        if (string.IsNullOrEmpty(tag) || !VoiceTags().TryGetValue(tag, out var hit)) return null;
        var clip = Clip(hit.Bank, hit.Sample, 1f);
        triggerVoice ??= Fev.FindEvent(TriggerVoiceEvent, TriggerVoiceGroup);
        if (triggerVoice != null) TakeCategory(triggerVoice, clip);
        return clip;
    }

    FevEvent triggerVoice;

    /// <summary>The event's category on its clip: the options set the top-level category's volume (the clip's bus), the
    /// authored sub-category volumes stay in the clip's own volume. Resolved once per event.</summary>
    void TakeCategory(FevEvent ev, SoundClip clip)
    {
        int slot = System.Threading.Volatile.Read(ref ev.CategorySlot);
        if (slot == -2)
        {
            slot = SoundCategories.TryResolve(Fev, ev.Category, out int s, out float belowTop) ? s : -1;
            ev.CategoryBelowTop = belowTop;
            System.Threading.Volatile.Write(ref ev.CategorySlot, slot);
        }
        clip.Category = slot;
        if (slot >= 0) clip.Volume *= ev.CategoryBelowTop;
    }

    /// <summary>The programmer-sound event PlayTriggerVoice plays a voice tag through (effectservice.cpp:1438).</summary>
    public const string TriggerVoiceGroup = "MS2Sound/Sound/Voice/ko", TriggerVoiceEvent = "1";

    Dictionary<string, (Fsb5 Bank, FsbSample Sample)> VoiceTags()
    {
        if (voiceTags != null) return voiceTags;
        voiceTags = new Dictionary<string, (Fsb5, FsbSample)>(StringComparer.OrdinalIgnoreCase);
        // CSoundService::OpenVoiceStreams (soundservice.cpp:809-833) & BuildTagTable (:753-764): case 1 (jp) returns early.
        if (VoiceLanguage != 0 && VoiceLanguage != 2 && VoiceLanguage != 3) return voiceTags;
        string prefix = FevProject.VoiceSamplePrefix(VoiceLanguage);
        foreach (var bankName in new[] { "MS2Sound_Voice8", "MS2Sound_Voice9" })
        {
            if (!HasBankForVoice(bankName, VoiceLanguage)) continue;
            var bank = Bank(bankName, allowFallback: false);
            foreach (var s in bank.Samples)
            {
                if (string.IsNullOrEmpty(s.Name)) continue;
                string key = prefix.Length > 0 && s.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    ? s.Name[prefix.Length..]
                    : s.Name;
                voiceTags[key] = (bank, s);
            }
        }
        return voiceTags;
    }

    /// <summary>A bank sample by its name in the bank, for sounds the client plays without an event.</summary>
    public SoundClip FindSample(string bankName, string sampleName)
    {
        var bank = Bank(bankName);
        var s = bank.Find(sampleName);
        return s == null ? null : Clip(bank, s, 1f);
    }
}
