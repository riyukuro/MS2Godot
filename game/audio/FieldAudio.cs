using System.Globalization;
using Godot;
using Ms2.Audio;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2Godot;

/// <summary>
/// Sound of a loaded field, after CSoundEventMgr::PlayMusic / SetMusicParameter / OnMusicCallback and the fielddata
/// &lt;bgm&gt; entry: the field's table/fielddata.xml entry names a music cue and parameter (&lt;bgm id="35" param="5"/&gt;);
/// MusicController drives the cue's segment graph (intro, loop, bridge/intermission transitions via parameter 5, one-shots)
/// and boss combat BGM switching. Voice banks follow GameOptions.Voice (CSoundService @0x1417aea70, soundservice.cpp:418-433).
/// </summary>
public partial class FieldAudio : Node
{
    public const string UiClickWave = "System_Button_click_01";   // MS2Sound_System.fsb, the client's button click

    /// <summary>The audio of the field in the tree; null from the moment that field leaves it (a field change, the exit)
    /// until the next field's audio opens, so a sound asked for during the load (a party sound, an effect, an option save)
    /// plays nowhere instead of reaching the freed players.</summary>
    public static FieldAudio Instance { get; private set; }

    public override void _ExitTree()
    {
        Unchain();
        if (Instance == this) Instance = null;
    }

    SoundLibrary library;
    SystemSoundTable systemSounds;
    public SoundLibrary Library => library;
    public MusicController MusicState { get; private set; }
    public BossBgmTracker BossBgm { get; private set; }
    public AmbientSounds Ambient { get; set; }
    public FootstepSounds Footsteps { get; private set; }
    AudioStreamPlayer music;
    AudioStreamPlayer ui;
    int playGeneration;
    MusicSegment activeSegment;
    float activeSegmentVolume = 1f;
    string bgmName;
    readonly Dictionary<int, (SoundClip Clip, Pcm16 Pcm)> cueSegments = new();

    /// <summary>Music category volume, the client's CSoundEventMgr::m_fNowMusicVolume, multiplied onto the FEV-authored cue
    /// volume. The client sets it from the sound options (FUN_14125fe20, gameoptionstruct.cpp:2727: SetCategoryVolume(2,
    /// GetCategoryVolume(sound, 2)), gameoptionstruct.cpp:2757-2783), bgmSoundStep * mainSoundStep / 100 from the option
    /// store (<see cref="GameOptions"/>). Open sets it.</summary>
    public float MusicVolume { get; set; } = 1f;

    /// <summary>The options dialog's sound save (CGameOptionSoundDefault::vfunc_10 -> FUN_14125fe20,
    /// gameoptionstruct.cpp:2727-2752): CSoundEventMgr::SetCategoryVolume(2, ...) becomes the music volume, applied to the
    /// playing cue at once. SetAllMute and the sound service's main volume are not carried here yet.</summary>
    public void ApplyVolumes(Ms2.Formats.Options.SoundVolumes v)
    {
        MusicVolume = v.Category[2];
        if (music != null && activeSegment != null)
            music.VolumeDb = Mathf.LinearToDb(activeSegmentVolume * MusicVolume);
        GD.Print($"audio: music volume {MusicVolume:F2}");
    }

    public int BgmCue { get; private set; }
    public string BgmName => bgmName;
    public bool MusicPlaying => music != null && music.Playing;
    public double MusicPosition => music?.GetPlaybackPosition() ?? 0;

    /// <summary>The Data/Sound folder under the client's Data folder; null when there is none.</summary>
    public bool Open(string dataDir, PackSet packs = null)
    {
        Instance = this;
        var dir = System.IO.Path.Combine(dataDir, "Sound");
        if (!System.IO.File.Exists(System.IO.Path.Combine(dir, "MS2Sound.fev")))
        {
            GD.PushWarning("audio: no Sound/MS2Sound.fev under " + dataDir);
            return false;
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        MusicVolume = GameOptions.Current.SoundOption.CategoryVolume(2);
        library = SoundLibrary.Shared(dir, GameOptions.Voice);
        MusicState = new MusicController(library.Fev);
        GD.Print($"audio: MS2Sound.fev ready in {sw.ElapsedMilliseconds} ms: {library.Fev.Banks.Count} banks (voice {library.LanguageName}), {library.Fev.Defs.Count} sound definitions, {library.Fev.EventCount} events in {library.Fev.Groups.Count} groups, {library.Fev.CueIds.Count()} music cues");
        GD.Print($"audio: music volume {MusicVolume:F2}");
        if (packs != null)
            systemSounds = SystemSounds(packs);
        music = new AudioStreamPlayer { Name = "Music" };
        ui = new AudioStreamPlayer { Name = "Ui" };
        AddChild(music);
        AddChild(ui);
        return true;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, SystemSoundTable> systemSoundTables = new();

    /// <summary>table/systemsound.xml, parsed once per pack set: the client reads it once, in SoundPropertyMgr::Load
    /// (soundproperty.cpp:477, called at startup from ms2.cpp:1872), not per field. Null when the table cannot be read
    /// (warned once).</summary>
    static SystemSoundTable SystemSounds(PackSet packs)
    {
        if (systemSoundTables.TryGetValue(packs, out var t)) return t;
        try { t = SystemSoundTable.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/systemsound.xml"))); }
        catch (Exception ex) { GD.PushWarning("audio: systemsound.xml: " + ex.Message); }
        systemSoundTables.AddOrUpdate(packs, t);
        return t;
    }

    /// <summary>The full &lt;bgm&gt; spec of a field's fielddata entry (default environment; CFieldDataCli::vfunc_1 @0x140a55500, fielddatacli.cpp:240-249).</summary>
    public static FieldBgmSpec FieldBgm(PackSet packs, string xblockName)
    {
        var fd = FieldData.EntryByXBlock(packs, xblockName);
        var bgm = FieldData.DefaultEnvironment(fd)?.Element("bgm");
        if (bgm == null) return default;
        return new FieldBgmSpec(
            BgmId: ParseInt(bgm, "id"),
            BgmParam: ParseInt(bgm, "param"),
            BgmValue: ParseFloat(bgm, "value"),
            BossBgmId: ParseInt(bgm, "boss"),
            BossBgmParam: ParseInt(bgm, "bossParam"),
            BossBgmValue: ParseFloat(bgm, "bossValue"),
            EnterSystemSound: (string)bgm.Attribute("enterSystemSound") ?? "");
    }

    /// <summary>The bgm cue of a field's fielddata entry (default environment), 0 when the field has none.</summary>
    public static int FieldBgmCue(PackSet packs, string xblockName) => FieldBgm(packs, xblockName).BgmId;

    static int ParseInt(System.Xml.Linq.XElement e, string attr) =>
        (string)e.Attribute(attr) is { } s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    static float ParseFloat(System.Xml.Linq.XElement e, string attr) =>
        (string)e.Attribute(attr) is { } s && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

    /// <summary>
    /// PlayMusic(cue) with optional field parameter, following WorldService (@0x141bc7b80, worldservice_1.cpp:4866-4872)
    /// and CSoundEventMgr::PlayMusic (@0x14177c100, soundeventmgr.cpp:1151-1203). Decodes on a worker thread and plays the
    /// cue's segment graph (including bridge/intermission and intro-&gt;loop transitions). Returns false when the cue is unknown.
    /// </summary>
    public bool PlayBgm(int cue, int paramId = 0, float paramValue = 0f, uint priority = MusicController.DefaultPriority, bool force = false)
    {
        BgmCue = cue;
        if (library == null || MusicState == null || cue == 0) return false;
        if (!MusicState.PlayMusic(cue, 0f, priority, force))
        {
            if (library.Fev.GetMusicCue(cue) == null)
                GD.PushWarning($"audio: music cue {cue} not found in MS2Sound.fev");
            return false;
        }
        if (paramId != 0)
            MusicState.SetMusicParameter(paramId, paramValue, priority, force);
        return StartCurrentCue();
    }

    /// <summary>Starts a field's BGM spec and initializes its boss combat BGM tracker.</summary>
    public bool PlayFieldBgm(FieldBgmSpec spec, bool force = false)
    {
        if (MusicState != null)
            BossBgm = new BossBgmTracker(MusicState, spec);
        if (!string.IsNullOrEmpty(spec.EnterSystemSound))
            PlayUi(spec.EnterSystemSound);
        return PlayBgm(spec.BgmId, spec.BgmParam, spec.BgmValue, MusicController.DefaultPriority, force);
    }

    /// <summary>Sets a live FMOD music parameter (CSoundEventMgr::SetMusicParameter @0x14177c420).</summary>
    public bool SetMusicParameter(int paramId, float value, uint priority = MusicController.DefaultPriority, bool force = false) =>
        MusicState != null && MusicState.SetMusicParameter(paramId, value, priority, force);

    /// <summary>Reads a live FMOD music parameter (CSoundEventMgr::GetMusicParameter @0x14177c710).</summary>
    public bool TryGetMusicParameter(int paramId, out float value)
    {
        if (MusicState != null) return MusicState.TryGetMusicParameter(paramId, out value);
        value = 0f;
        return false;
    }

    /// <summary>Syncs Godot audio playback to whatever cue/segment <see cref="MusicState"/> currently holds (e.g. after BossBgm switches cues).</summary>
    public bool SyncMusicToState() => StartCurrentCue();

    bool StartCurrentCue()
    {
        var cue = MusicState?.CurrentCue;
        var startSeg = MusicState?.CurrentSegment;
        if (cue == null || startSeg == null) return false;
        var startClip = library.ResolveSegment(startSeg);
        if (startClip == null) return false;

        BgmCue = cue.Id;
        bgmName = startClip.Name;

        int gen = ++playGeneration;
        activeSegment = null;
        cueSegments.Clear();
        music.Stop();
        Unchain();

        DecodeAndPlaySegment(gen, cue, startSeg, startClip);
        return true;
    }

    void DecodeAndPlaySegment(int gen, MusicCue cue, MusicSegment seg, SoundClip clip)
    {
        System.Threading.Tasks.Task.Run(() =>
        {
            var pcm = SafeDecode.Decode(clip);
            if (pcm == null) return;   // undecodable segment stays silent
            Callable.From(() =>
            {
                if (!IsInstanceValid(this) || !IsInsideTree() || gen != playGeneration) return;
                cueSegments[seg.Id] = (clip, pcm);
                PlaySegment(cue, seg);
            }).CallDeferred();
        });
    }

    void PlaySegment(MusicCue cue, MusicSegment seg)
    {
        if (seg == null || !cueSegments.TryGetValue(seg.Id, out var entry)) return;
        Unchain();
        activeSegment = seg;
        activeSegmentVolume = entry.Clip.Volume;
        bgmName = entry.Clip.Name;
        // CSoundEventMgr OnMusicCallback (@0x14177caf0, soundeventmgr.cpp:1369): fires on segment start, toggling param 5 (0 <-> 10).
        MusicState.OnSegmentStarted();
        bool selfLoop = MusicController.IsUnconditionalSelfLoop(seg);
        music.Stream = Stream(entry.Pcm, selfLoop);
        music.VolumeDb = Mathf.LinearToDb(activeSegmentVolume * MusicVolume);
        if (!selfLoop)
        {
            music.Finished += OnSegmentFinished;
            chained = true;
        }
        music.Play();
        GD.Print($"audio: bgm cue {cue.Id} seg {seg.Id} -> {entry.Clip.Name} ({entry.Pcm.Seconds:F1} s, {entry.Pcm.Rate} Hz, {entry.Pcm.Channels} ch, loop={selfLoop}), playing={music.Playing}");
    }

    bool chained;   // current segment is non-looping and OnSegmentFinished advances the FEV segment graph

    void Unchain()
    {
        if (!chained) return;
        if (music != null && IsInstanceValid(music))
            music.Finished -= OnSegmentFinished;
        chained = false;
    }

    void OnSegmentFinished()
    {
        Unchain();
        if (MusicState == null) return;
        var next = MusicState.AdvanceSegment();
        if (next == null) return;   // one-shot cue or no matching link condition: stop cleanly
        if (cueSegments.ContainsKey(next.Id))
        {
            PlaySegment(MusicState.CurrentCue, next);
            return;
        }
        var clip = library.ResolveSegment(next);
        if (clip == null) return;
        DecodeAndPlaySegment(playGeneration, MusicState.CurrentCue, next, clip);
    }

    /// <summary>Play a sound event by name (event volume from the FEV).</summary>
    public bool PlayEvent(string eventName)
    {
        var clip = library?.ResolveEvent(eventName);
        if (clip == null) return false;
        PlayClip(clip);
        GD.Print($"audio: event {eventName} -> {clip.Name} volume {clip.Volume:F2}");
        return true;
    }

    /// <summary>Play a trigger/programmer voice tag (MS2Sound_Voice8/9 in the configured voice language; CEffectService::PlayTriggerVoice).</summary>
    public bool PlayVoiceTag(string tag)
    {
        var clip = library?.ResolveVoiceTag(tag);
        if (clip == null) return false;
        PlayClip(clip);
        GD.Print($"audio: voice tag {tag} -> {clip.Name} ({library.LanguageName})");
        return true;
    }

    /// <summary>
    /// A UI sound by the client's key (table/systemsound.xml: button_click, dialog_open, dialog_close, ...), played as the
    /// event the table names. Returns false when the key has no sound.
    /// </summary>
    public bool PlayUi(string key)
    {
        var path = systemSounds?.Resolve(key);
        return path != null && PlayEvent(path);
    }

    public bool PlayUiClick() => PlayUi("button_click");

    /// <summary>The field's sound: bgm cue and parameters, ambient sound entities and, when a player is given, footsteps. Never throws.</summary>
    public static FieldAudio Start(Node parent, string dataDir, PackSet packs, string fieldName, FieldMap map, CharacterBody3D player = null, AnimationPlayer anim = null)
    {
        FieldAudio a = null;
        try
        {
            a = new FieldAudio { Name = "FieldAudio" };
            parent.AddChild(a);
            if (player != null && anim != null)
            {
                // the ground effects of a step or a landing play whether or not there is a sound bank (CGroundEffectDataManager::
                // PlayEffects inserts the effect files through CEffectService, groundeffectdata.cpp:275-305); the bank adds the sounds
                var ground = new GroundEffectTable(p => { try { return System.Text.Encoding.UTF8.GetString(packs.Read("Xml", p)); } catch { return null; } });
                a.Footsteps = new FootstepSounds(null, ground, player, anim);
                a.AddChild(a.Footsteps);
            }
            if (!a.Open(dataDir, packs)) return a;
            if (a.Footsteps != null) a.Footsteps.Library = a.library;
            a.PlayFieldBgm(FieldBgm(packs, fieldName));
            a.Ambient = new AmbientSounds(a.library);
            a.AddChild(a.Ambient);
            a.Ambient.Build(map);
            GD.Print($"audio: {a.Ambient.Count} ambient sound entities");
            return a;
        }
        catch (Exception ex) { GD.PushWarning("audio: " + ex.Message); return a; }   // the footsteps' ground effects stay
    }

    readonly AudioStreamPlayer3D[] voices3d = new AudioStreamPlayer3D[16];
    int nextVoice3d;
    readonly Dictionary<string, AudioStreamWav> decoded = new();
    public int PositionalPlayed;

    /// <summary>
    /// Play a sound event at a world position (effect soundEvent parts, npc sounds). <paramref name="path"/> is the event name or
    /// "group/name"; <paramref name="delay"/> seconds later. Attenuation follows the event's 3D distances.
    /// </summary>
    public bool PlayEventAt(string path, Vector3 position, float delay = 0)
    {
        var clip = library?.ResolveEvent(path);
        if (clip == null) return false;
        if (!decoded.TryGetValue(clip.Name, out var wav))
        {
            var pcm = SafeDecode.Decode(clip);
            decoded[clip.Name] = wav = pcm == null ? null : new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, Stereo = pcm.Channels == 2, MixRate = pcm.Rate, Data = pcm.ToBytes() };
        }
        if (wav == null) return false;
        var v = voices3d[nextVoice3d++ % voices3d.Length];
        if (v == null) { v = voices3d[(nextVoice3d - 1) % voices3d.Length] = new AudioStreamPlayer3D { MaxDistance = 3000, UnitSize = 300 }; AddChild(v); }
        v.Stream = wav;
        v.GlobalPosition = position;
        v.VolumeDb = Mathf.LinearToDb(clip.Volume);
        v.MaxDistance = clip.MaxDistance > 0 ? clip.MaxDistance : 3000;
        v.UnitSize = clip.MinDistance > 0 ? clip.MinDistance : 300;
        // the tree's timer outlives the field: a voice freed with it before the delay ends plays nothing
        if (delay > 0) GetTree().CreateTimer(delay).Timeout += () => { if (IsInstanceValid(v)) v.Play(); }; else v.Play();
        PositionalPlayed++;
        return true;
    }

    void PlayClip(SoundClip clip)
    {
        var pcm = SafeDecode.Decode(clip);
        if (pcm == null) return;
        ui.Stream = Stream(pcm, false);
        ui.VolumeDb = Mathf.LinearToDb(clip.Volume);
        ui.Play();
    }

    // The banks hold MPEG layer II, which Godot's mp3 decoder rejects: play the decoded PCM.
    static AudioStreamWav Stream(Pcm16 pcm, bool loop) => new()
    {
        Format = AudioStreamWav.FormatEnum.Format16Bits,
        Stereo = pcm.Channels == 2,
        MixRate = pcm.Rate,
        Data = pcm.ToBytes(),
        LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
        LoopBegin = 0,
        LoopEnd = pcm.Frames,
    };
}
