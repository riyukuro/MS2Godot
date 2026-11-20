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
    readonly AudioStreamPlayer[] musicPlayers = new AudioStreamPlayer[MusicCrossfade.SlotCount];
    readonly MusicCrossfade crossfade = new();
    readonly MusicPlaybackGuard playbackGuard = new();
    readonly SoundMusicEffectState soundMusicFade = new();
    readonly float[] slotSegmentVolume = { 1f, 1f };
    AudioStreamPlayer ui;
    MusicSegment activeSegment;
    double audioClock;
    float categoryMusicVolume = 1f;
    int chainedSlot = -1;
    string bgmName;
    readonly Dictionary<int, (SoundClip Clip, Pcm16 Pcm, AudioStreamWav Wav)> cueSegments = new();

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
        categoryMusicVolume = v.Category[2];
        MusicVolume = categoryMusicVolume;
        ApplyPlayerGain(0);
        ApplyPlayerGain(1);
        GD.Print($"audio: music volume {MusicVolume:F2}");
    }

    public int BgmCue { get; private set; }
    public string BgmName => bgmName;
    public bool MusicPlaying => (musicPlayers[0]?.Playing ?? false) || (musicPlayers[1]?.Playing ?? false);
    public bool MusicCrossfading => crossfade.IsFading;
    public bool SoundMusicFading => soundMusicFade.IsFading;
    public double MusicPosition => musicPlayers[crossfade.ActiveSlot]?.GetPlaybackPosition() ?? 0;

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
        categoryMusicVolume = GameOptions.Current.SoundOption.CategoryVolume(2);
        MusicVolume = categoryMusicVolume;
        library = SoundLibrary.Shared(dir, GameOptions.Voice);
        MusicState = new MusicController(library.Fev);
        GD.Print($"audio: MS2Sound.fev ready in {sw.ElapsedMilliseconds} ms: {library.Fev.Banks.Count} banks (voice {library.LanguageName}), {library.Fev.Defs.Count} sound definitions, {library.Fev.EventCount} events in {library.Fev.Groups.Count} groups, {library.Fev.CueIds.Count()} music cues");
        GD.Print($"audio: music volume {MusicVolume:F2}");
        if (packs != null)
            systemSounds = SystemSounds(packs);
        musicPlayers[0] = new AudioStreamPlayer { Name = "MusicA" };
        musicPlayers[1] = new AudioStreamPlayer { Name = "MusicB" };
        ui = new AudioStreamPlayer { Name = "Ui" };
        AddChild(musicPlayers[0]);
        AddChild(musicPlayers[1]);
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
    public static FieldBgmSpec FieldBgm(PackSet packs, string xblockName) =>
        ParseBgmElement(FieldData.DefaultEnvironment(FieldData.EntryByXBlock(packs, xblockName))?.Element("bgm"));

    /// <summary>The full &lt;bgm&gt; spec of a field by numeric id (including map 1 which may lack an xblock).</summary>
    public static FieldBgmSpec FieldBgmById(PackSet packs, int fieldId)
    {
        var root = FieldData.Document(packs)?.Root;
        if (root == null) return default;
        System.Xml.Linq.XElement match = null;
        foreach (var fd in root.Elements("fieldData"))
            if ((int?)fd.Attribute("id") == fieldId) match = fd;
        return ParseBgmElement(FieldData.DefaultEnvironment(match)?.Element("bgm"));
    }

    static FieldBgmSpec ParseBgmElement(System.Xml.Linq.XElement bgm)
    {
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

    /// <summary>
    /// Starts the login screen BGM per WorldService::FUN_141bc7b80 (@0x141bc7b80, worldservice_1.cpp:4856-4872):
    /// uses LoginBgBgmTable::GetBgm() when CueId != 0, otherwise falls back to map 1's FieldBgmSpec.
    /// </summary>
    public bool PlayLoginBgm(LoginBgBgmTable table, FieldBgmSpec map1Fallback, Func<int, int> pick = null, bool force = false)
    {
        if (table != null)
        {
            var bgm = table.GetBgm(pick);
            if (bgm.CueId != 0)
                return PlayBgm(bgm.CueId, bgm.ParamId, map1Fallback.BgmValue, MusicController.DefaultPriority, force);
        }
        return PlayFieldBgm(map1Fallback, force);
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

    /// <summary>
    /// Executes a SoundMusic effect part (effect type 3, CSoundMusicEffect::vfunc_3 @0x1417900e0,
    /// soundmusiceffectparam.cpp:133-156): PlayMusic and SetMusicParameter prompt immediately at creation (not delayed
    /// by StartDelay; CEffect::vfunc_3 effectservice.cpp:365 only shifts m_dStartTime for the linear volume fade).
    /// Defaults <paramref name="casterKind"/> to <see cref="SoundMusicEffectState.CasterOther"/> (rejected at
    /// m_unk_18 &gt; 1) unless the caller passes <see cref="SoundMusicEffectState.CasterSystem"/> (0) or
    /// <see cref="SoundMusicEffectState.CasterMyPc"/> (1).
    /// </summary>
    public bool PlaySoundMusicEffect(in SoundMusicEffectParam param, int casterKind = SoundMusicEffectState.CasterOther)
    {
        if (MusicState == null) return false;
        return ApplySoundMusicEffect(param, casterKind);
    }

    /// <summary>Plays a parsed <see cref="EffectPart"/> of type <c>soundMusic</c>.</summary>
    public bool PlayEffectSoundMusic(EffectPart part, int casterKind = SoundMusicEffectState.CasterOther)
    {
        if (part == null) return false;
        var param = new SoundMusicEffectParam(
            CueId: part.MusicCueId,
            ParamId: part.MusicParamId,
            ParamValue: part.MusicParamValue,
            Priority: part.MusicPriority,
            Volume: part.MusicVolume,
            Duration: part.Duration,
            StartDelay: part.StartDelay,
            Reverb: part.MusicReverb);
        return PlaySoundMusicEffect(param, casterKind);
    }

    bool ApplySoundMusicEffect(in SoundMusicEffectParam param, int casterKind)
    {
        if (casterKind > 1 || MusicState == null) return false;
        var res = soundMusicFade.Apply(MusicState, param, audioClock, MusicVolume, categoryMusicVolume, casterKind);
        if (res.CueChanged) StartCurrentCue();
        return res.CueChanged || res.ParamApplied || res.FadeStarted;
    }

    /// <summary>Stops both music players and clears any in-progress crossfade or SoundMusic volume fade.</summary>
    public void StopMusic()
    {
        playbackGuard.Stop();
        Unchain();
        activeSegment = null;
        cueSegments.Clear();
        crossfade.StopAll();
        MusicVolume = soundMusicFade.Stop(categoryMusicVolume);
        if (musicPlayers[0] != null && IsInstanceValid(musicPlayers[0])) musicPlayers[0].Stop();
        if (musicPlayers[1] != null && IsInstanceValid(musicPlayers[1])) musicPlayers[1].Stop();
    }

    public override void _Process(double delta)
    {
        audioClock += delta;
        bool gainDirty = false;
        if (soundMusicFade.IsFading)
        {
            soundMusicFade.Tick(audioClock, categoryMusicVolume, out float fadedVolume);
            MusicVolume = fadedVolume;
            gainDirty = true;
        }

        if (crossfade.IsFading)
        {
            int stopSlot = crossfade.Step((float)delta);
            gainDirty = true;
            if (stopSlot >= 0)
            {
                var old = musicPlayers[stopSlot];
                if (old != null && IsInstanceValid(old)) old.Stop();
            }
        }

        if (gainDirty)
        {
            ApplyPlayerGain(0);
            ApplyPlayerGain(1);
        }

        if (playbackGuard.IsSegmentActive)
        {
            var activePlayer = musicPlayers[crossfade.ActiveSlot];
            if (activePlayer != null && activePlayer.Playing && playbackGuard.TryBeginPredecode(activePlayer.GetPlaybackPosition()))
                PredecodeNextSegment();
        }
    }

    void ApplyPlayerGain(int slot)
    {
        var p = musicPlayers[slot];
        if (p == null || !IsInstanceValid(p)) return;
        float linear = slotSegmentVolume[slot] * MusicVolume * crossfade.Gain(slot);
        p.VolumeDb = linear <= 1e-6f ? -80f : Mathf.LinearToDb(linear);
    }

    bool StartCurrentCue()
    {
        var cue = MusicState?.CurrentCue;
        var startSeg = MusicState?.CurrentSegment;
        if (cue == null || startSeg == null) return false;
        var startClip = library.ResolveSegment(startSeg);
        if (startClip == null) return false;

        BgmCue = cue.Id;
        bgmName = startClip.Name;

        // Unchain and disarm the outgoing segment immediately before async decode so neither its Finished signal
        // nor the per-frame predecode window can advance or populate against the new cue's MusicState.
        Unchain();
        activeSegment = null;
        int gen = playbackGuard.BeginCueChange();
        cueSegments.Clear();

        DecodeAndPlaySegment(gen, cue, startSeg, startClip, cue.CrossfadeMs);
        return true;
    }

    void DecodeAndPlaySegment(int gen, MusicCue cue, MusicSegment seg, SoundClip clip, int crossfadeMs)
    {
        bool selfLoop = MusicController.IsUnconditionalSelfLoop(seg);
        System.Threading.Tasks.Task.Run(() =>
        {
            var pcm = SafeDecode.Decode(clip);
            if (pcm == null) return;   // undecodable segment stays silent
            var wav = Stream(pcm, selfLoop);
            Callable.From(() =>
            {
                if (!IsInstanceValid(this) || !IsInsideTree() || !playbackGuard.AcceptDecode(gen)) return;
                cueSegments[seg.Id] = (clip, pcm, wav);
                PlaySegment(cue, seg, crossfadeMs);
            }).CallDeferred();
        });
    }

    void PredecodeNextSegment()
    {
        if (MusicState == null || library == null) return;
        var next = MusicState.PeekNextSegment();
        if (next == null || cueSegments.ContainsKey(next.Id)) return;
        var clip = library.ResolveSegment(next);
        if (clip == null) return;
        int gen = playbackGuard.Generation;
        bool selfLoop = MusicController.IsUnconditionalSelfLoop(next);
        System.Threading.Tasks.Task.Run(() =>
        {
            var pcm = SafeDecode.Decode(clip);
            if (pcm == null) return;
            var wav = Stream(pcm, selfLoop);
            Callable.From(() =>
            {
                if (!IsInstanceValid(this) || !IsInsideTree() || !playbackGuard.AcceptDecode(gen)) return;
                cueSegments[next.Id] = (clip, pcm, wav);
            }).CallDeferred();
        });
    }

    void PlaySegment(MusicCue cue, MusicSegment seg, int crossfadeMs)
    {
        if (seg == null || !cueSegments.TryGetValue(seg.Id, out var entry)) return;
        Unchain();
        bool hasActive = musicPlayers[crossfade.ActiveSlot] is { Playing: true };
        var (inSlot, stopSlot, isCrossfading) = crossfade.BeginTransition(crossfadeMs, hasActive);
        if (stopSlot >= 0 && musicPlayers[stopSlot] is { } oldPlayer && IsInstanceValid(oldPlayer))
            oldPlayer.Stop();

        activeSegment = seg;
        bool selfLoop = MusicController.IsUnconditionalSelfLoop(seg);
        playbackGuard.OnSegmentPlaying(entry.Pcm.Seconds, selfLoop);
        slotSegmentVolume[inSlot] = entry.Clip.Volume;
        bgmName = entry.Clip.Name;

        // CSoundEventMgr OnMusicCallback (@0x14177caf0, soundeventmgr.cpp:1369): fires on segment start, toggling param 5 (0 <-> 10).
        MusicState.OnSegmentStarted();
        var player = musicPlayers[inSlot];
        player.Stream = entry.Wav;
        ApplyPlayerGain(0);
        ApplyPlayerGain(1);
        if (!selfLoop)
        {
            player.Finished += OnSegmentFinished;
            chainedSlot = inSlot;
        }
        player.Play();
        GD.Print($"audio: bgm cue {cue.Id} seg {seg.Id} -> {entry.Clip.Name} ({entry.Pcm.Seconds:F1} s, {entry.Pcm.Rate} Hz, {entry.Pcm.Channels} ch, loop={selfLoop}, xfade={crossfadeMs} ms, fading={isCrossfading}), playing={player.Playing}");
    }

    void Unchain()
    {
        if (chainedSlot < 0) return;
        var p = musicPlayers[chainedSlot];
        if (p != null && IsInstanceValid(p))
            p.Finished -= OnSegmentFinished;
        chainedSlot = -1;
    }

    void OnSegmentFinished()
    {
        Unchain();
        if (!playbackGuard.TryAdvanceOnFinished()) return;
        AdvanceToNextSegment(0);
    }

    void AdvanceToNextSegment(int crossfadeMs)
    {
        if (MusicState == null) return;
        var next = MusicState.AdvanceSegment();
        if (next == null)
        {
            activeSegment = null;
            return;   // one-shot cue or no matching link condition: stop cleanly
        }
        if (cueSegments.ContainsKey(next.Id))
        {
            PlaySegment(MusicState.CurrentCue, next, crossfadeMs);
            return;
        }
        var clip = library.ResolveSegment(next);
        if (clip == null) return;
        DecodeAndPlaySegment(playbackGuard.Generation, MusicState.CurrentCue, next, clip, crossfadeMs);
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
