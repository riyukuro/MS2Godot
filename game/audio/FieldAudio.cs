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
public partial class FieldAudio : Node, World.ITriggerSounds
{
    public const string UiClickWave = "System_Button_click_01";   // MS2Sound_System.fsb, the client's button click

    /// <summary>The audio of the field in the tree; null from the moment that field leaves it (a field change, the exit)
    /// until the next field's audio opens, so a sound asked for during the load (a party sound, an effect, an option save)
    /// plays nowhere instead of reaching the freed players.</summary>
    public static FieldAudio Instance { get; private set; }

    public override void _EnterTree()
    {
        lock (poolGate) poolClosed = false;
    }

    public override void _ExitTree()
    {
        // close the pool side first: a decode that finishes after this point makes no engine call (see poolGate)
        lock (poolGate) poolClosed = true;
        UnchainAll();
        if (Instance == this) Instance = null;
        // the field's voices free with it, looping ones too: none outlives its field
        int made = made3d;
        Live3dPlayers -= made3d;
        made3d = 0;
        if (Ms2Godot.Net.LiveSession.LiveCheck)
            GD.Print($"audio: field audio leaves: {lastPlaying3d} 3d playing on its last frame, {voiceSlots.Looping} looping, {made} 3d players made, "
                + $"{LoopsStoppedWithNode} loops stopped with their node, {LoopsTaken} loops taken, {Live3dPlayers} 3d players live after");
    }

    SoundLibrary library;
    SystemSoundTable systemSounds;
    HitSoundTable hitSounds;
    EventSoundTable eventSounds;
    Ms2.Formats.World.EffectSkipTable effectSkip;
    Ms2.Formats.World.CooltimeTable cooltimes;
    float volumeOtherPc = Ms2.Formats.World.EffectSkipTable.VolumeOtherPcDefine;
    public SoundLibrary Library => library;
    public MusicController MusicState { get; private set; }
    public BossBgmTracker BossBgm { get; private set; }
    public AmbientSounds Ambient { get; set; }
    public FootstepSounds Footsteps { get; private set; }
    readonly AudioStreamPlayer[] musicPlayers = new AudioStreamPlayer[MusicCrossfade.SlotCount];
    readonly MusicCrossfade crossfade = new();
    readonly MusicPlaybackGuard playbackGuard = new();
    /// <summary>The music segments decoded and built on the pool threads, as the main thread's steps (run first in
    /// <see cref="_Process"/>): the cue map and the play stay on the main thread.</summary>
    readonly System.Collections.Concurrent.ConcurrentQueue<Action> decodedSegments = new();
    /// <summary>Guards every engine call a pool thread makes (the stream build, the decode warning): it runs under this lock
    /// only while <see cref="poolClosed"/> is false, and <see cref="_ExitTree"/> sets the flag under the same lock. A decode
    /// still running at the quit then never reaches the engine: before this, one finished into the engine's teardown and
    /// crashed the exit (SIGSEGV, exit 139) about one run in three. Godot leaves the tree (SceneTree::finalize, from
    /// OS_LinuxBSD::run) before Main::cleanup frees the script languages, the servers and the message queue, so once
    /// the flag is set no pool call can follow them.</summary>
    readonly object poolGate = new();
    bool poolClosed;
    readonly SoundMusicEffectState soundMusicFade = new();
    /// <summary>Per music slot: the FEV volume of the segment it plays (one per <see cref="MusicCrossfade.SlotCount"/>).</summary>
    readonly float[] slotSegmentVolume = System.Linq.Enumerable.Repeat(1f, MusicCrossfade.SlotCount).ToArray();
    AudioStreamPlayer ui;
    MusicSegment activeSegment;
    double audioClock;
    /// <summary>The real seconds this node has processed (<see cref="GameClock.Real"/>): FMOD's channel runs on the mixer's
    /// time, which the game's time scale does not reach (CMainSystem::SetTimeScale scales the game clock alone,
    /// mainsystem.cpp:218), so a loop's event position is counted on it (<see cref="EventFadeIn.LoopPosition"/>).</summary>
    double loopClock;
    float categoryMusicVolume = 1f;
    string bgmName;
    Dictionary<int, (SoundClip Clip, Pcm16 Pcm, AudioStreamWav Wav)> cueSegments = new();
    /// <summary>The previous cue's decoded segments while its voices fade out: an outgoing voice whose segment ends in
    /// the fade goes on into its next segment (<see cref="ChainOutgoing"/>), as FMOD's segment player does.</summary>
    Dictionary<int, (SoundClip Clip, Pcm16 Pcm, AudioStreamWav Wav)> outgoingSegments = new();
    int outgoingGeneration = -1;
    /// <summary>Per music slot: the segment it plays, whether that is the current cue's, whether its Finished goes to
    /// <see cref="OnSlotFinished"/> (a segment that does not loop on itself), its handler (made once in Open), and a
    /// count that drops an outgoing chain's decode once the slot has moved on.</summary>
    readonly MusicSegment[] slotSegment = new MusicSegment[MusicCrossfade.SlotCount];
    readonly bool[] slotCurrentCue = new bool[MusicCrossfade.SlotCount];
    readonly bool[] slotChained = new bool[MusicCrossfade.SlotCount];
    readonly Action[] slotFinished = new Action[MusicCrossfade.SlotCount];
    readonly int[] slotGeneration = new int[MusicCrossfade.SlotCount];

    /// <summary>Music category volume, the client's CSoundEventMgr::m_fNowMusicVolume, multiplied onto the FEV-authored cue
    /// volume. The client sets it from the sound options (FUN_14125fe20, gameoptionstruct.cpp:2727: SetCategoryVolume(2,
    /// GetCategoryVolume(sound, 2)), gameoptionstruct.cpp:2757-2783), bgmSoundStep * mainSoundStep / 100 from the option
    /// store (<see cref="GameOptions"/>). Open sets it.</summary>
    public float MusicVolume { get; set; } = 1f;

    /// <summary>The sound options' volumes (FUN_14125fe20, gameoptionstruct.cpp:2727-2752, from the options dialog's save
    /// CGameOptionSoundDefault::vfunc_10 and at startup CGameOption::FUN_141243860, gameoption.cpp:1925): every category's
    /// volume and mute go to <see cref="SoundMix.Shared"/> and its buses (<see cref="SoundBuses"/>), which reach the playing
    /// sounds at once; SetCategoryVolume(2) also sets the music system's volume, applied to the playing cue here.</summary>
    public static void ApplyVolumes(Ms2.Formats.Options.SoundVolumes v)
    {
        Ms2.Formats.Options.SoundMix.Shared.Apply(v);
        SoundBuses.Sync();
        Instance?.ApplyMusicVolume();
        if (Ms2Godot.Net.LiveSession.LiveCheck) GD.Print($"audio: volumes {SoundBuses.Describe()} music volume {Ms2.Formats.Options.SoundMix.Shared.MusicVolume:F2}");
    }

    void ApplyMusicVolume(bool log = true)
    {
        categoryMusicVolume = Ms2.Formats.Options.SoundMix.Shared.MusicVolume;
        MusicVolume = categoryMusicVolume;
        ApplyPlayerGains();
        if (log) GD.Print($"audio: music volume {MusicVolume:F2}");
    }

    /// <summary>CSoundEventMgr's volume fades (m_mapFades), advanced each frame by <see cref="_Process"/> (CSoundEventMgr::Update
    /// -> UpdateFades, soundeventmgr.cpp:1055-1057).</summary>
    public static readonly Ms2.Formats.Options.SoundFades Fades = new(Ms2.Formats.Options.SoundMix.Shared, Ms2.Formats.Options.SoundFades.SystemClockMs);

    /// <summary>UICutSceneMovie::SetSoundFade's body (@0x14190f270, uicutscenemovie.cpp:197-213): the sound options are applied
    /// again (FUN_14125fe20 of the saved options), then categories 2 (bgm) and 1 (environment) fade out or in over 1000 ms.</summary>
    public static void CutSceneSoundFade(bool fade)
    {
        const int nFadeTime = 1000;
        var mix = Ms2.Formats.Options.SoundMix.Shared;
        ApplyVolumes(Ms2.Formats.Options.SoundVolumes.From(GameOptions.Current.SoundOption, false, mix.AppActive));
        if (fade) { Fades.StartFadeOut(2, nFadeTime); Fades.StartFadeOut(1, nFadeTime); }
        else { Fades.StartFadeIn(2, nFadeTime); Fades.StartFadeIn(1, nFadeTime); }
        if (Ms2Godot.Net.LiveSession.LiveCheck) GD.Print($"audio: cut scene sound fade {(fade ? "out" : "in")} started: {SoundBuses.Describe()}");
    }

    double fadeLogClock;

    bool gateHold;

    /// <summary>
    /// The perf gate's music hold (HudScene.PerfGate.cs, a gate session only): from here on the playing segment never
    /// reaches its end. Where its last <see cref="MusicPlaybackGuard.PredecodeLeadSeconds"/> seconds start (the point the
    /// next segment's decode would start), it goes back to its start, so no segment switch (the next segment's decode on
    /// the pool threads, its first play on the main thread) lands in a measured window. The switch follows the music's
    /// own clock, the audio driver's wall time, not the gate's frames. The per-frame music work runs as before. The seek
    /// to the current position here compiles the seek before any window. The hold covers every busy music voice: an
    /// outgoing one (fading, or holding its gain after the incoming voice ended) goes back to its start the same way, so
    /// it neither chains into its next segment nor frees its slot in a window.
    /// </summary>
    public void GateHoldMusic()
    {
        gateHold = true;
        for (int i = 0; i < musicPlayers.Length; i++)
            if (crossfade.Busy(i) && musicPlayers[i] is { } p && IsInstanceValid(p) && p.Playing) p.Seek((float)p.GetPlaybackPosition());
    }

    /// <summary>The gate hold for the voices the playback guard does not track: each outgoing voice, and the active one
    /// while the guard has no segment armed (a cue's decode, or a cue at the voice cap), goes back to its start where its
    /// last <see cref="MusicPlaybackGuard.PredecodeLeadSeconds"/> seconds start. A segment no longer than that lead is left
    /// to play: it would otherwise be sent back to its start every frame. Gate sessions only.</summary>
    void HoldUntrackedVoices()
    {
        bool holdActive = !playbackGuard.IsSegmentActive;
        for (int i = 0; i < musicPlayers.Length; i++)
        {
            if ((i == crossfade.ActiveSlot && !holdActive) || !crossfade.Busy(i)) continue;
            var p = musicPlayers[i];
            if (p == null || !p.Playing || p.Stream == null) continue;
            double length = p.Stream.GetLength();
            if (length > MusicPlaybackGuard.PredecodeLeadSeconds && p.GetPlaybackPosition() >= length - MusicPlaybackGuard.PredecodeLeadSeconds)
                p.Seek(0);
        }
    }

    public int BgmCue { get; private set; }
    public string BgmName => bgmName;
    public bool MusicPlaying
    {
        get
        {
            foreach (var p in musicPlayers)
                if (p?.Playing ?? false) return true;
            return false;
        }
    }
    public bool MusicCrossfading => crossfade.IsFading;
    public bool SoundMusicFading => soundMusicFade.IsFading;
    public double MusicPosition => musicPlayers[crossfade.ActiveSlot]?.GetPlaybackPosition() ?? 0;

    /// <summary>The scripted live run's music trace (MS2_LIVE_COMBAT_STEP music): while set, every frame of a crossfade, and
    /// the frame it ends on, prints the busy music slots' crossfade gains, segments and play state.</summary>
    public bool TraceMusicGains { get; set; }
    /// <summary>The sound clock the fades run on, in ms (the trace's time).</summary>
    public double AudioClockMs => audioClock * 1000;

    void LogMusicGains()
    {
        var line = new System.Text.StringBuilder($"audio: bgm gains {AudioClockMs:F0} ms active {crossfade.ActiveSlot}");
        for (int i = 0; i < musicPlayers.Length; i++)
            if (crossfade.Busy(i)) line.Append($" | {i} {crossfade.Gain(i):F3} seg {slotSegment[i]?.Id.ToString() ?? "-"} {(musicPlayers[i] is { Playing: true } ? "playing" : "stopped")}");
        GD.Print(line.ToString());
    }

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
        // the saved volumes, as the client applies them at startup (CGameOption::FUN_141243860, gameoption.cpp:1925)
        var mix = Ms2.Formats.Options.SoundMix.Shared;
        mix.Apply(Ms2.Formats.Options.SoundVolumes.From(GameOptions.Current.SoundOption, false, mix.AppActive));
        SoundBuses.Sync();
        categoryMusicVolume = mix.MusicVolume;
        MusicVolume = categoryMusicVolume;
        library = SoundLibrary.Shared(dir, GameOptions.Voice);
        // One MusicController for the session (CSoundEventMgr singleton; resets only at shutdown, worldservice_1.cpp:10583, soundeventmgr.cpp:1036).
        MusicState = MusicController.Shared(library.Fev);
        // a random part's soundEvent children are made only when FMOD has their event (CSoundEventEffectParam::vfunc_2)
        var fev = library.Fev;
        Ms2.Formats.World.EffectRandom.SoundEventFound = p => EffectSoundParam.Made(fev, p.SoundGroup, p.SoundEvent, p.JobEvents?.Values);
        GD.Print($"audio: MS2Sound.fev ready in {sw.ElapsedMilliseconds} ms: {library.Fev.Banks.Count} banks (voice {library.LanguageName}), {library.Fev.Defs.Count} sound definitions, {library.Fev.EventCount} events in {library.Fev.Groups.Count} groups, {library.Fev.CueIds.Count()} music cues");
        GD.Print($"audio: music volume {MusicVolume:F2}");
        if (packs != null)
        {
            systemSounds = SystemSounds(packs);
            hitSounds = HitSounds(packs);
            eventSounds = EventSounds(packs);
            effectSkip = EffectSkip(packs);
            cooltimes = Cooltimes(packs);
            try { volumeOtherPc = World.FieldContents.Constants(packs).Float("VolumeOtherPc", Ms2.Formats.World.EffectSkipTable.VolumeOtherPcDefine); }
            catch (Exception ex) { GD.PushWarning("audio: constants: " + ex.Message); }
        }
        for (int i = 0; i < musicPlayers.Length; i++)
        {
            musicPlayers[i] = new AudioStreamPlayer { Name = "Music" + (char)('A' + i), Bus = SoundBuses.Music };
            AddChild(musicPlayers[i]);
            int slot = i;
            slotFinished[i] = () => OnSlotFinished(slot);
        }
        ui = new AudioStreamPlayer { Name = "Ui" };
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


    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, HitSoundTable> hitSoundTables = new();

    /// <summary>table/hitsound.xml, parsed once per pack set (SoundPropertyMgr::LoadHitSound, soundproperty.cpp:513, from the
    /// same startup Load). Null when the table cannot be read (warned once).</summary>
    static HitSoundTable HitSounds(PackSet packs)
    {
        if (hitSoundTables.TryGetValue(packs, out var t)) return t;
        try { t = HitSoundTable.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/hitsound.xml"))); }
        catch (Exception ex) { GD.PushWarning("audio: hitsound.xml: " + ex.Message); }
        hitSoundTables.AddOrUpdate(packs, t);
        return t;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, EventSoundTable> eventSoundTables = new();

    /// <summary>table/eventsound.xml, parsed once per pack set (SoundPropertyMgr::LoadEventSound, soundproperty.cpp:591, from
    /// the same startup Load). Null when the table cannot be read (warned once).</summary>
    static EventSoundTable EventSounds(PackSet packs)
    {
        if (eventSoundTables.TryGetValue(packs, out var t)) return t;
        try { t = EventSoundTable.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/eventsound.xml"))); }
        catch (Exception ex) { GD.PushWarning("audio: eventsound.xml: " + ex.Message); }
        eventSoundTables.AddOrUpdate(packs, t);
        return t;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, Ms2.Formats.World.EffectSkipTable> effectSkipTables = new();

    /// <summary>Table/effectOption.xml for the graphic quality (EffectSkipMgr::LoadOption, effectskipmgr.cpp:181), read once per
    /// pack set as the client reads it once. Null when the table cannot be read (warned once): nothing is skipped by view.</summary>
    // one per data set for the session: a cooldown outlives a field change (CCooltimeManager::GetInstance)
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, Ms2.Formats.World.CooltimeTable> cooltimeTables = new();

    static Ms2.Formats.World.CooltimeTable Cooltimes(PackSet packs)
    {
        if (cooltimeTables.TryGetValue(packs, out var t)) return t;
        try
        {
            var root = Ms2.Formats.Text.PackXml.Parse(packs.Read("Xml", "table/cooltimetable.xml")).Root;
            t = Ms2.Formats.World.CooltimeTable.From(root == null ? [] : Ms2.Ui.FeatureElements.Collect(packs, root, "v", "cooltimeID"));
        }
        catch (Exception ex) { GD.PushWarning("audio: CooltimeTable.xml: " + ex.Message); }
        cooltimeTables.AddOrUpdate(packs, t);
        return t;
    }

    public static Ms2.Formats.World.EffectSkipTable EffectSkip(PackSet packs)
    {
        if (effectSkipTables.TryGetValue(packs, out var t)) return t;
        try { t = Ms2.Formats.World.EffectSkipTable.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/effectoption.xml")), GameOptions.Current.GraphicOption.Quality); }
        catch (Exception ex) { GD.PushWarning("audio: effectOption.xml: " + ex.Message); }
        effectSkipTables.AddOrUpdate(packs, t);
        return t;
    }

    /// <summary>
    /// CEffectService::PlayHitSound @0x1411a5580 (effectservice.cpp:1355): the hit sound table's pick for the attack and
    /// defence materials (<paramref name="rand"/> s_random's Tausworthe number), played as "ms2sound/sound/attack/hit/%08d"
    /// at <paramref name="position"/> through FUN_1411a5110's checks (<see cref="Ms2.Formats.World.EffectSkipTable.SoundEventVolume"/>:
    /// the pet options of the object relation <paramref name="kind"/>, the sound row's view distance from
    /// <paramref name="viewer"/>, the camera's actor, and VolumeOtherPc for relation 3) at <paramref name="volume"/>; no id
    /// plays nothing. Returns whether a sound played. CEffectService::IsEnabled is always true here (only the developer
    /// dialog turns it off, uidevelopdialog.cpp:378).
    /// </summary>
    public bool PlayHitSound(int attack, int defence, int kind, Vector3 position, float volume, Vector3? viewer, Func<uint> rand)
    {
        int id = hitSounds?.Get(attack, defence, rand) ?? 0;
        if (id == 0) return false;
        var status = GameOptions.Current?.StatusOption;
        float v = Ms2.Formats.World.EffectSkipTable.SoundEventVolume(effectSkip, kind, ToNumerics(position), viewer is { } e ? ToNumerics(e) : null,
            volume, volumeOtherPc, status?.DisplayShowOnlyMyPet ?? false, status?.DisplayHideMyPet ?? false);
        return v >= 0 && PlayEventAt(HitSoundTable.EventPath(id), position, 0, v);
    }

    static System.Numerics.Vector3 ToNumerics(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>
    /// CEffectService::FUN_1411a5110 @0x1411a5110 (effectservice.cpp:1321) at volume 1: the event <paramref name="path"/>
    /// ("group/name") at <paramref name="position"/> through the checks of the object kind <paramref name="kind"/>
    /// (<see cref="Ms2.Formats.World.EffectSkipTable.SoundEventVolume"/>). Returns whether a sound played.
    /// </summary>
    public bool PlaySoundEvent(string path, int kind, Vector3 position, Vector3? viewer)
    {
        var status = GameOptions.Current?.StatusOption;
        float v = Ms2.Formats.World.EffectSkipTable.SoundEventVolume(effectSkip, kind, ToNumerics(position), viewer is { } e ? ToNumerics(e) : null,
            1f, volumeOtherPc, status?.DisplayShowOnlyMyPet ?? false, status?.DisplayHideMyPet ?? false);
        return v >= 0 && PlayEventAt(path, position, 0, v);
    }

    /// <summary>
    /// CEffectService::PlayEventVoice @0x1411a5770 (effectservice.cpp:1371): the event voice of <paramref name="type"/> for a
    /// PC of <paramref name="gender"/> and <paramref name="jobGroup"/> (<see cref="EventSoundTable.VoicePath"/>; the job sound
    /// is off with the KR region and the character option's disableJobSound, IsJobSoundDisabled @0x141260470), played through
    /// FUN_1411a5110 at <paramref name="position"/> (the PC's vfunc_18) at volume 1 with the object kind's checks. Returns the
    /// event path asked for, null when the table gives none.
    /// STILL-OPEN: the PC's sound pitch (PCBase::GetSoundPitch, m_unk_118) passed as the last argument is not applied.
    /// </summary>
    public string PlayEventVoice(int type, int gender, int jobGroup, bool localPlayer, int objectKind, Vector3 position, Vector3? viewer)
    {
        bool off = (GameOptions.Current?.StatusOption?.RegionKr ?? false) && GameOptions.SoundFlag.DisableJobSound;
        var path = eventSounds?.VoicePath(type, gender, jobGroup, localPlayer, objectKind, off);
        if (path == null) return null;
        var status = GameOptions.Current?.StatusOption;
        float v = Ms2.Formats.World.EffectSkipTable.SoundEventVolume(effectSkip, objectKind, ToNumerics(position), viewer is { } e ? ToNumerics(e) : null,
            1f, volumeOtherPc, status?.DisplayShowOnlyMyPet ?? false, status?.DisplayHideMyPet ?? false);
        bool played = v >= 0 && PlayEventAt(path, position, 0, v);
        if (Ms2Godot.Net.LiveSession.LiveCheck) GD.Print($"audio: event voice {type} -> {path} played {played}");
        return path;
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
        if (library == null || MusicState == null || cue == 0) return false;
        if (!MusicState.PlayMusic(cue, 0f, priority, force))
        {
            if (library.Fev.GetMusicCue(cue) == null)
                GD.PushWarning($"audio: music cue {cue} not found in MS2Sound.fev");
            // When a new FieldAudio node attaches after a scene reload on the same cue, resume playback on this node
            // without pushing a duplicate history entry.
            if (!MusicPlaying && MusicState.IsPlaying && MusicState.CurrentCueId == cue)
            {
                if (paramId != 0)
                    MusicState.SetMusicParameter(paramId, paramValue, priority, force);
                return StartCurrentCue();
            }
            return false;
        }
        BgmCue = MusicState.CurrentCueId;
        if (paramId != 0)
            MusicState.SetMusicParameter(paramId, paramValue, priority, force);
        return StartCurrentCue();
    }

    /// <summary>
    /// A trigger sound's BGM event: CSoundEventMgr::PlayMusic(cue, 0, 0x80, 0) (@0x14177c100, soundeventmgr.cpp:1151-1203,
    /// from TriggerObjectSound::vfunc_2, triggeractioncli.cpp:568 and :577). A cue goes on the music history at the default
    /// priority; cue 0 goes back to the cue before the current one. Playback follows when the current cue changed.
    /// </summary>
    public bool PlayTriggerMusic(int cue)
    {
        if (MusicState == null) return false;
        int before = MusicState.CurrentCueId;
        if (!MusicState.PlayMusic(cue, 0f, MusicController.DefaultPriority, false)) return false;
        if (MusicState.CurrentCueId != before) StartCurrentCue();
        return true;
    }

    /// <summary>A trigger sound's AMB event (<see cref="AmbientSounds.SetTriggerSound"/>); nothing without the field's sounds.</summary>
    public void SetTriggerAmbient(FieldEntity e, bool on) => Ambient?.SetTriggerSound(e, on);

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
        UnchainAll();
        activeSegment = null;
        cueSegments.Clear();
        outgoingSegments.Clear();
        crossfade.StopAll();
        MusicVolume = soundMusicFade.Stop(categoryMusicVolume);
        StopMusicPlayers(-1);
    }

    public override void _Process(double delta)
    {
        while (decodedSegments.TryDequeue(out var decoded)) decoded();
        if (following3d > 0) FollowVoices();
        RollOff();
        if (Ms2Godot.Net.LiveSession.LiveCheck) lastPlaying3d = Playing3d;   // the leave's count: the voices stop as they leave first
        audioClock += delta;
        loopClock += GameClock.Real(delta);
        if (voiceSlots.Pending > 0)
            for (int due; (due = voiceSlots.NextDue(audioClock)) >= 0;) StopVoice(due);
        if (Fades.Running)
        {
            if (Fades.Update())
            {
                SoundBuses.Sync();
                ApplyMusicVolume(false);
            }
            if (Ms2Godot.Net.LiveSession.LiveCheck && (!Fades.Running || audioClock - fadeLogClock >= 0.25))
            {
                fadeLogClock = audioClock;
                GD.Print($"audio: sound fade {(Fades.Running ? "running" : "done")}: {SoundBuses.Describe()} music volume {Ms2.Formats.Options.SoundMix.Shared.MusicVolume:F2}");
            }
        }
        bool gainDirty = false;
        if (soundMusicFade.IsFading)
        {
            soundMusicFade.Tick(audioClock, categoryMusicVolume, out float fadedVolume);
            MusicVolume = fadedVolume;
            gainDirty = true;
        }

        if (crossfade.IsFading)
        {
            StopMusicPlayers(crossfade.Step((float)delta));
            if (!crossfade.IsFading) { outgoingSegments.Clear(); outgoingGeneration = -1; }
            gainDirty = true;
            if (TraceMusicGains) LogMusicGains();
        }

        if (gainDirty) ApplyPlayerGains();

        if (playbackGuard.IsSegmentActive)
        {
            var activePlayer = musicPlayers[crossfade.ActiveSlot];
            if (activePlayer != null && activePlayer.Playing)
            {
                double pos = activePlayer.GetPlaybackPosition();
                if (gateHold) { if (playbackGuard.InEndLead(pos)) activePlayer.Seek(0); }
                else if (playbackGuard.TryBeginPredecode(pos)) PredecodeNextSegment();
            }
        }
        if (gateHold && (crossfade.HasOutgoing || !playbackGuard.IsSegmentActive)) HoldUntrackedVoices();
    }

    /// <summary>The busy slots' volumes; a free slot's player is stopped and gets its volume when a segment takes it.</summary>
    void ApplyPlayerGains()
    {
        for (int i = 0; i < musicPlayers.Length; i++)
            if (crossfade.Busy(i)) ApplyPlayerGain(i);
    }

    /// <summary>A voice's segment ended with nothing to follow: FMOD frees its channel, the other ramps go on.</summary>
    void ReleaseSlot(int slot)
    {
        crossfade.Release(slot);
        StopMusicPlayers(1 << slot);
        if (TraceMusicGains) LogMusicGains();
    }

    /// <summary>Stops the music players whose bits are set in <paramref name="mask"/> (MusicCrossfade's stop masks).</summary>
    void StopMusicPlayers(int mask)
    {
        for (int i = 0; i < musicPlayers.Length; i++)
        {
            if ((mask & (1 << i)) == 0) continue;
            Unchain(i);
            slotSegment[i] = null;
            slotCurrentCue[i] = false;
            slotGeneration[i]++;
            if (musicPlayers[i] is { } p && IsInstanceValid(p)) p.Stop();
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
        if (startClip == null)
        {
            // MusicState has already moved to the new cue; stop any previous cue's playback so stale audio does not linger.
            StopMusic();
            BgmCue = cue.Id;
            bgmName = null;
            return false;
        }

        BgmCue = cue.Id;
        bgmName = startClip.Name;

        // Disarm the outgoing cue before the async decode so neither its Finished signal nor the per-frame predecode
        // window can advance or populate against the new cue's MusicState: its voices become outgoing ones, which follow
        // their own segment's links (ChainOutgoing), and its decoded segments move to the outgoing cache.
        for (int i = 0; i < slotCurrentCue.Length; i++) slotCurrentCue[i] = false;
        activeSegment = null;
        outgoingGeneration = playbackGuard.Generation;
        int gen = playbackGuard.BeginCueChange();
        (outgoingSegments, cueSegments) = (cueSegments, outgoingSegments);
        cueSegments.Clear();

        DecodeAndPlaySegment(gen, cue, startSeg, startClip, cue.CrossfadeMs);
        return true;
    }

    /// <param name="advance">The active voice's own next segment (an EndOfSegment advance): if it cannot be decoded, the
    /// voice ends there and frees its slot, as one with nothing to follow. A cue's first segment leaves the slots alone.</param>
    void DecodeAndPlaySegment(int gen, MusicCue cue, MusicSegment seg, SoundClip clip, int crossfadeMs, bool advance = false)
    {
        bool selfLoop = MusicController.IsUnconditionalSelfLoop(seg);
        System.Threading.Tasks.Task.Run(() =>
        {
            var pcm = SafeDecode.Decode(clip, out var failure);
            var data = pcm?.ToBytes();
            lock (poolGate)
            {
                if (poolClosed) return;
                if (failure != null) GD.PushWarning(failure);
                if (pcm == null)   // undecodable segment stays silent
                {
                    if (advance) decodedSegments.Enqueue(() => { if (playbackGuard.AcceptDecode(gen)) EndActiveVoice(); });
                    return;
                }
                var wav = Stream(pcm, data, selfLoop);
                decodedSegments.Enqueue(() =>
                {
                    if (!playbackGuard.AcceptDecode(gen)) return;
                    cueSegments[seg.Id] = (clip, pcm, wav);
                    PlaySegment(cue, seg, crossfadeMs);
                });
            }
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
            var pcm = SafeDecode.Decode(clip, out var failure);
            var data = pcm?.ToBytes();
            lock (poolGate)
            {
                if (poolClosed) return;
                if (failure != null) GD.PushWarning(failure);
                if (pcm == null) return;
                var wav = Stream(pcm, data, selfLoop);
                decodedSegments.Enqueue(() =>
                {
                    // a predecode that lands after the cue changed is the outgoing voice's next segment
                    if (playbackGuard.AcceptDecode(gen)) cueSegments[next.Id] = (clip, pcm, wav);
                    else if (gen == outgoingGeneration) outgoingSegments[next.Id] = (clip, pcm, wav);
                });
            }
        });
    }

    void PlaySegment(MusicCue cue, MusicSegment seg, int crossfadeMs)
    {
        if (seg == null || !cueSegments.TryGetValue(seg.Id, out var entry)) return;
        // the active voice is busy while it plays or its next segment is on the way; its end with nothing to follow
        // frees it (ReleaseSlot)
        bool hasActive = crossfade.Busy(crossfade.ActiveSlot);
        var (inSlot, stopMask, isCrossfading) = crossfade.BeginTransition(crossfadeMs, hasActive);
        StopMusicPlayers(stopMask);
        if (!isCrossfading) { outgoingSegments.Clear(); outgoingGeneration = -1; }
        if (inSlot < 0)
        {
            // FMOD's segment player is at its 10 voices: the cue gets none and the newest voice keeps its ramp
            activeSegment = null;
            GD.Print($"audio: bgm cue {cue.Id} seg {seg.Id}: no music voice free ({MusicCrossfade.SlotCount} fading), not played");
            return;
        }

        activeSegment = seg;
        bool selfLoop = MusicController.IsUnconditionalSelfLoop(seg);
        playbackGuard.OnSegmentPlaying(entry.Pcm.Seconds, selfLoop);
        slotSegmentVolume[inSlot] = entry.Clip.Volume;
        bgmName = entry.Clip.Name;

        // CSoundEventMgr OnMusicCallback (@0x14177caf0, soundeventmgr.cpp:1369): fires on segment start, toggling param 5 (0 <-> 10).
        MusicState.OnSegmentStarted();
        var player = musicPlayers[inSlot];
        Unchain(inSlot);
        slotGeneration[inSlot]++;
        slotSegment[inSlot] = seg;
        slotCurrentCue[inSlot] = true;
        player.Stream = entry.Wav;
        ApplyPlayerGains();
        if (!selfLoop) Chain(inSlot);
        player.Play();
        GD.Print($"audio: bgm cue {cue.Id} seg {seg.Id} -> {entry.Clip.Name} ({entry.Pcm.Seconds:F1} s, {entry.Pcm.Rate} Hz, {entry.Pcm.Channels} ch, loop={selfLoop}, xfade={crossfadeMs} ms, fading={isCrossfading}), playing={player.Playing}");
    }

    void Chain(int slot)
    {
        if (slotChained[slot] || musicPlayers[slot] is not { } p || !IsInstanceValid(p)) return;
        p.Finished += slotFinished[slot];
        slotChained[slot] = true;
    }

    void Unchain(int slot)
    {
        if (!slotChained[slot]) return;
        slotChained[slot] = false;
        if (musicPlayers[slot] is { } p && IsInstanceValid(p)) p.Finished -= slotFinished[slot];
    }

    void UnchainAll()
    {
        for (int i = 0; i < slotChained.Length; i++) Unchain(i);
    }

    void OnSlotFinished(int slot)
    {
        Unchain(slot);
        if (!slotCurrentCue[slot])
        {
            if (!ChainOutgoing(slot)) ReleaseSlot(slot);
            return;
        }
        if (!playbackGuard.TryAdvanceOnFinished()) return;
        AdvanceToNextSegment(0);
    }

    /// <summary>
    /// An outgoing voice's segment ended while it fades out: it goes on into the segment its own links pick under the
    /// live parameters, at the fading or held gain of its slot; false when none matches (MusicController.NextSegmentOf),
    /// and the caller frees the slot.
    /// The start fires the segment-start callback like any other (OnMusicCallback toggles parameter 5), which the
    /// incoming cue's links then see: in the client a cue whose intro links on parameter 5 that comes in over a chained
    /// outgoing voice stops after its intro (cue 196 -> 197 in the FMOD probe: silent from 8.9 s).
    /// </summary>
    bool ChainOutgoing(int slot)
    {
        if (MusicState == null || library == null || !crossfade.Busy(slot)) return false;
        var next = MusicState.NextSegmentOf(slotSegment[slot]);
        if (next == null) return false;
        if (outgoingSegments.TryGetValue(next.Id, out var entry) || cueSegments.TryGetValue(next.Id, out entry))
        {
            PlayOutgoing(slot, next, entry);
            return true;
        }
        var clip = library.ResolveSegment(next);
        if (clip == null) return false;
        int sg = ++slotGeneration[slot];
        bool selfLoop = MusicController.IsUnconditionalSelfLoop(next);
        System.Threading.Tasks.Task.Run(() =>
        {
            var pcm = SafeDecode.Decode(clip, out var failure);
            var data = pcm?.ToBytes();
            lock (poolGate)
            {
                if (poolClosed) return;
                if (failure != null) GD.PushWarning(failure);
                if (pcm == null)   // the voice ends where its segment did, as one with nothing to follow
                {
                    decodedSegments.Enqueue(() => { if (slotGeneration[slot] == sg && !slotCurrentCue[slot]) ReleaseSlot(slot); });
                    return;
                }
                var wav = Stream(pcm, data, selfLoop);
                decodedSegments.Enqueue(() =>
                {
                    if (slotGeneration[slot] != sg || slotCurrentCue[slot] || !crossfade.Busy(slot)) return;
                    var decoded = (clip, pcm, wav);
                    outgoingSegments[next.Id] = decoded;
                    PlayOutgoing(slot, next, decoded);
                });
            }
        });
        return true;
    }

    void PlayOutgoing(int slot, MusicSegment seg, (SoundClip Clip, Pcm16 Pcm, AudioStreamWav Wav) entry)
    {
        var player = musicPlayers[slot];
        if (player == null || !IsInstanceValid(player)) return;
        slotGeneration[slot]++;
        slotSegment[slot] = seg;
        slotSegmentVolume[slot] = entry.Clip.Volume;
        player.Stream = entry.Wav;
        ApplyPlayerGain(slot);
        MusicState.OnSegmentStarted();
        if (!MusicController.IsUnconditionalSelfLoop(seg)) Chain(slot);
        player.Play();
        GD.Print($"audio: bgm outgoing slot {slot} seg {seg.Id} -> {entry.Clip.Name} at gain {crossfade.Gain(slot):F3}");
    }

    void AdvanceToNextSegment(int crossfadeMs)
    {
        if (MusicState == null) return;
        var next = MusicState.AdvanceSegment();
        if (next == null)
        {
            EndActiveVoice();   // one-shot cue or no matching link condition: stop cleanly
            return;
        }
        if (cueSegments.ContainsKey(next.Id))
        {
            PlaySegment(MusicState.CurrentCue, next, crossfadeMs);
            return;
        }
        var clip = library.ResolveSegment(next);
        if (clip == null) { EndActiveVoice(); return; }
        DecodeAndPlaySegment(playbackGuard.Generation, MusicState.CurrentCue, next, clip, crossfadeMs, advance: true);
    }

    /// <summary>The active voice's segment ended with nothing to follow: no segment is active, and its slot is freed.</summary>
    void EndActiveVoice()
    {
        activeSegment = null;
        ReleaseSlot(crossfade.ActiveSlot);
    }

    /// <summary>Play a sound event by name (event volume from the FEV).</summary>
    public bool PlayEvent(string eventName)
    {
        var clip = library?.ResolveEvent(eventName);
        if (clip == null) return false;
        PlayClip(clip);
        if (Ms2Godot.Net.LiveSession.LiveCheck) GD.Print($"audio: event {eventName} -> {clip.Name} volume {clip.Volume:F2}{(clip.Loops ? " loops" : "")}");
        return true;
    }

    /// <summary>Play a trigger/programmer voice tag (MS2Sound_Voice8/9 in the configured voice language; CEffectService::PlayTriggerVoice).</summary>
    public bool PlayVoiceTag(string tag)
    {
        var clip = library?.ResolveVoiceTag(tag);
        if (clip == null) return false;
        PlayClip(clip);
        if (Ms2Godot.Net.LiveSession.LiveCheck) GD.Print($"audio: voice tag {tag} -> {clip.Name} ({library.LanguageName})");
        return true;
    }

    /// <summary>
    /// CEffectService::PlaySystemSound @0x1411a5a60 (effectservice.cpp:1288): a UI sound by the client's key (table/systemsound.xml:
    /// button_click, dialog_open, system_notice_error_warningBag, ...). The row's sound is the local player's gender and job
    /// group's (gender 0 and job 1 without one; SystemSoundTable.Resolve), with the job sounds off under the KR region's
    /// disableJobSound (IsJobSoundDisabled @0x141260470); a row with a cooltimeID plays only when CooltimeManager::Use lets it.
    /// The event's waves come from the banks of the voice language (SoundLibrary). Returns false when nothing is played.
    /// An event with a looping sound loops (FMOD keeps it until its handle is stopped); without a handle here it loops until
    /// the next UI sound takes the shared player. Of the table's looping rows only the gacha machine's rolling sound is
    /// played, by <see cref="PlayUiHandle"/>. STILL-OPEN: the client keeps a dropped handle's looping sound playing beside the next.
    /// </summary>
    public bool PlayUi(string key, string arg = "") => UiSoundPath(key, arg) is { } path && PlayEvent(path);

    /// <summary>PlaySystemSound's event path for a key (null when nothing plays), its cool time used.</summary>
    string UiSoundPath(string key, string arg)
    {
        var p = systemSounds?.Find(key, arg);
        if (p == null) return null;
        var me = Ms2Godot.Net.LiveSession.Instance?.Me.User;
        int gender = me?.Gender ?? 0, job = me?.JobCode ?? 1;
        bool off = (GameOptions.Current?.StatusOption?.RegionKr ?? false) && GameOptions.SoundFlag.DisableJobSound;
        var path = systemSounds.Resolve(key, gender, job, off, arg);
        if (path == null) return null;
        if (p.CooltimeId != 0 && cooltimes != null && !cooltimes.Use((int)p.CooltimeId, System.Environment.TickCount64)) return null;
        return path;
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
            GD.Print($"audio: {a.Ambient.Count} ambient sound entities, {AmbientSounds.Live} ambient players live, {Live3dPlayers} 3d players live");
            return a;
        }
        catch (Exception ex) { GD.PushWarning("audio: " + ex.Message); return a; }   // the footsteps' ground effects stay
    }

    // the client's 100 channels; a voice's player is made at its first sound
    readonly AudioStreamPlayer3D[] voices3d = new AudioStreamPlayer3D[EffectVoiceSlots.ClientChannels];
    // which sound each voice holds, the doNotStop stops pending, and the voices looping sounds hold (at most half)
    readonly EffectVoiceSlots voiceSlots = new(EffectVoiceSlots.ClientChannels, EffectVoiceSlots.ClientChannels / 2);
    readonly Node3D[] followed3d = new Node3D[EffectVoiceSlots.ClientChannels];    // the node a voice follows (attachToNode), null for none
    readonly Vector3[] followOffset3d = new Vector3[EffectVoiceSlots.ClientChannels];
    int following3d;
    /// <summary>Event sounds that failed for their event's max playbacks or for no channel (<see cref="EffectVoiceSlots.Start"/>).</summary>
    public int EventSoundsFailed;

    /// <summary>The 3D players of every field's audio now in the tree (made and not yet freed with their field).</summary>
    public static int Live3dPlayers { get; private set; }
    int made3d, lastPlaying3d;

    /// <summary>The 3D voices a looping sound holds now (<see cref="EffectVoiceSlots.Looping"/>).</summary>
    public int Looping3d => voiceSlots.Looping;

    /// <summary>The 3D voices playing now.</summary>
    public int Playing3d
    {
        get
        {
            int n = 0;
            foreach (var v in voices3d) if (v != null && IsInstanceValid(v) && v.Playing) n++;
            return n;
        }
    }

    /// <summary>Looping sounds stopped as the node they followed left the tree.</summary>
    public int LoopsStoppedWithNode;

    /// <summary>
    /// The followed voices to their nodes' places (CSoundEventEffect::vfunc_9's SetPosition, soundeventmgr.cpp:582).
    /// A node that left the tree ends the follow; a one-shot sound stays where it was and plays to its end. A looping sound
    /// stops there: it never ends by itself, and the client's owners stop their kept looping effects as they go (the mount's
    /// loop and run effects in ~Ridee @0x141704240, ridee.cpp:390-407; a skill state's on its end, StateSkill::vfunc_5,
    /// stateskill.cpp:515-567; a buff's keep effect with the buff).
    /// STILL-OPEN: a looping part that is not attached to a node and is played without a kept ticket (CombatLayer's
    /// effect sounds) is not stopped when its actor leaves; it plays until the field ends. The client keys an effect
    /// group by its play id, not by its owner (CEffectService::m_mapGroups), and stops one by that id from whoever kept it
    /// (CEffectService::FUN_1411a6060: keep effects, breakable objects, the avatar's timers); otherwise the group goes with
    /// a finished effect (FUN_1411a6160) or the field (OnShutdown). Whether the skill path keeps its ids and stops them as
    /// its actor leaves is not traced. MS2's looping unattached parts are mostly trigger effects and buff keeps, which are kept.
    /// </summary>
    void FollowVoices()
    {
        for (int i = 0; i < followed3d.Length; i++)
        {
            var n = followed3d[i];
            if (n == null) continue;
            if (IsInstanceValid(n) && n.IsInsideTree() && IsInstanceValid(voices3d[i]))
            {
                var at = n.GlobalPosition + followOffset3d[i];
                voices3d[i].GlobalPosition = at;
                gains3d.Move(i, ToNumerics(at));
                continue;
            }
            followed3d[i] = null;
            following3d--;
            if (!voiceSlots.Loops(i)) continue;
            voiceSlots.Free(i);
            StopVoice(i);
            LoopsStoppedWithNode++;
        }
    }
    /// <summary>The event voices' sounds by clip, fade-in and loop (a clip can be played by events with different fade-ins, and
    /// by a looping event and a one-shot: <see cref="EventStream"/> bakes both), with a stereo clip's channel correlation
    /// (<see cref="Player3DOutput.Correlation"/>, NaN for a mono one).</summary>
    readonly Dictionary<(string, int, bool), (AudioStreamWav Wav, StereoChannels Stereo)> decoded = new();
    /// <summary>Looping sounds stopped because <see cref="EffectVoiceSlots.MaxLooping"/> voices looped already.</summary>
    public int LoopsTaken;
    public int PositionalPlayed;
    /// <summary>The camera's actor, where the client measures an effect's view distance from (EffectSkipMgr::IsOutOfView,
    /// effectskipmgr.cpp:140-145, CameraMgr::FUN_140a38d60); null when there is none, and then nothing is out of view.</summary>
    public Func<Vector3?> Viewer;
    /// <summary>Effect sounds not played for the view distance (<see cref="PlayEffectSound"/>).</summary>
    public int EffectSoundsOutOfView;
    /// <summary>Effect sounds not made because their cooltimeID was cooling (<see cref="Ms2.Formats.World.CooltimeTable.Use"/>).</summary>
    public int EffectSoundsCooling;
    /// <summary>Effect sounds not made because the "mute all" sound option is on (<see cref="PlayEffectSound"/>).</summary>
    public int EffectSoundsMuted;
    /// <summary>The field is the music concert (<see cref="EffectPart.IsMusicConcert"/>), set by Field on its build.</summary>
    public bool InMusicConcert;

    /// <summary>
    /// An effect's soundEvent part (CSoundEventEffect::vfunc_3 @0x141776760, soundeventmgr.cpp:456-574) for
    /// <paramref name="owner"/> at <paramref name="at"/>: the event of the owner's job when the part is byJob
    /// (<see cref="EffectPart.SoundEventFor"/>; the character's disableJobSound option keeps the plain event), none when that
    /// is empty, after the job's start delay (<see cref="EffectPart.SoundStartDelayFor"/>). The caller has made the owner tests
    /// (<see cref="EffectPart.SkippedFor"/>); CreateEffect's view test is made here, from <see cref="Viewer"/>
    /// (<see cref="Ms2.Formats.World.EffectSkipTable.SoundEventOutOfView"/>). With attachToNode the sound's root hangs under the owner's node (:514-534) and
    /// vfunc_9 (:577-583) moves the sound to the root's world place each frame, so it follows <paramref name="actor"/> from
    /// where it started; without it, or without an actor, it stays at <paramref name="at"/>.
    /// STILL-OPEN: the voice pitch of SoundPropertyMgr::FindPitch (:483-485) is not applied; an attached sound follows the
    /// actor's root, not the named parentNode.
    /// </summary>
    public int PlayEffectSound(EffectPart part, EffectOwner owner, Vector3 at, Node3D actor = null)
    {
        // CreateEffect's vfunc_7 filter (effectservice.cpp:289) comes before the view test
        if (part.SkipMusicConcert && InMusicConcert) return 0;
        if (Ms2.Formats.World.EffectSkipTable.SoundEventOutOfView(effectSkip, part, owner.Kind, ToNumerics(at),
                Viewer?.Invoke() is { } eye ? ToNumerics(eye) : null)) { EffectSoundsOutOfView++; return 0; }
        // CSoundEventEffect::vfunc_3 (soundeventmgr.cpp:460): with the "mute all" option on (the sound options' first byte,
        // bMuteAll; not the mute while the window is in the background) no sound is made, so no voice is taken and the
        // cooltime below is not used
        if (GameOptions.Current.SoundOption.MuteAll) { EffectSoundsMuted++; return 0; }
        // CSoundEventEffect::vfunc_3 (soundeventmgr.cpp:463): a cooling cooltimeID makes no sound (GetTickCount ms)
        if (part.CooltimeId != 0 && cooltimes?.Use(part.CooltimeId, System.Environment.TickCount64) == false) { EffectSoundsCooling++; return 0; }
        string ev = part.SoundEventFor(owner.JobCode, GameOptions.SoundFlag.DisableJobSound);
        if (string.IsNullOrEmpty(ev)) return 0;
        return PlayEventVoice(part.SoundGroup == null ? ev : part.SoundGroup + "/" + ev, at, part.SoundStartDelayFor(owner.JobCode),
            1, part.AttachToNode ? actor : null);
    }

    /// <summary>
    /// The sound of <paramref name="voice"/> (<see cref="PlayEffectSound"/>'s return) stopped with its effect
    /// (CSoundEventEffect::vfunc_5 @0x141777520, soundeventmgr.cpp:612-626): at once, so one still waiting for its start
    /// delay never starts; with <paramref name="doNotStop"/> 10 s later (vfunc_9, :585-590). Nothing when the voice has
    /// gone to a newer sound.
    /// </summary>
    public void StopEffectSound(int voice, bool doNotStop)
    {
        int slot = voiceSlots.Stop(voice, doNotStop, audioClock);
        if (slot >= 0) StopVoice(slot);
    }

    void StopVoice(int slot)
    {
        if (followed3d[slot] != null) { followed3d[slot] = null; following3d--; }
        gains3d.Clear(slot);
        if (voices3d[slot] is { } v && IsInstanceValid(v)) v.Stop();
    }

    /// <summary>
    /// Play a sound event at a world position (effect soundEvent parts, npc sounds). <paramref name="path"/> is the event name or
    /// "group/name"; <paramref name="delay"/> seconds later, at <paramref name="volume"/> times the event's own volume
    /// (CreateSoundEventEffectParam's volume). Attenuation follows the event's 3D distances. With <paramref name="follow"/> the
    /// sound keeps its offset from that node until the node leaves the tree or the voice is reused.
    /// </summary>
    public bool PlayEventAt(string path, Vector3 position, float delay = 0, float volume = 1, Node3D follow = null) =>
        PlayEventVoice(path, position, delay, volume, follow) != 0;

    /// <summary><see cref="PlayEventAt"/>, returning the sound's voice for <see cref="StopEffectSound"/> (0 when nothing plays).</summary>
    public int PlayEventVoice(string path, Vector3 position, float delay, float volume, Node3D follow)
    {
        var clip = library?.ResolveEvent(path);
        if (clip == null) return 0;
        var key = (clip.Name, clip.Event?.FadeIn ?? 0, clip.Loops);
        if (!decoded.TryGetValue(key, out var sound))
        {
            var pcm = SafeDecode.Decode(clip);
            decoded[key] = sound = pcm == null ? default : (EventStream(pcm, clip), Player3DOutput.Channels(pcm));   // a looping event loops until stopped
        }
        var wav = sound.Wav;
        if (wav == null) return 0;
        // the event's instance is taken as the effect is made, so it is held through the start delay too
        var ev = clip.Event;
        float audibility = Audibility(clip, clip.Volume * volume, position);
        double length = clip.Seconds > 0 ? clip.Seconds : wav.GetLength();
        bool loopsFull = clip.Loops && voiceSlots.Looping >= voiceSlots.MaxLooping;
        int ticket = voiceSlots.Start(ev, ev?.MaxPlaybacks ?? 0, ev?.MaxPlaybacksBehavior ?? 0, ev?.Priority ?? 0, audibility,
            audioClock, audioClock + delay + length, out bool stolen, clip.Loops);
        if (Ms2Godot.Net.LiveSession.LiveCheck || LogVoices)
            GD.Print($"audio: voice {audioClock:F3} {ev?.FullPath ?? clip.Name} max {ev?.MaxPlaybacks} behavior {ev?.MaxPlaybacksBehavior} "
                     + (ticket == 0 ? "failed" : $"slot {voiceSlots.SlotOf(ticket)}{(stolen ? " stolen" : "")}") + $" sounding {voiceSlots.SoundingCount(audioClock)} of the event {voiceSlots.SoundingOf(ev, audioClock)}");
        if (ticket == 0) { EventSoundsFailed++; return 0; }
        int slot = voiceSlots.SlotOf(ticket);
        if (loopsFull && stolen) LoopsTaken++;   // the oldest looping sound gave its voice to a newer one
        var v = voices3d[slot];
        if (v == null)
        {
            v = voices3d[slot] = new AudioStreamPlayer3D { MaxDistance = 3000, UnitSize = 300 };
            int s = slot;
            v.Finished += () => gains3d.Clear(s);   // a one-shot played out: no more volume sets for it
            AddChild(v);
            made3d++;
            Live3dPlayers++;
        }
        if (followed3d[slot] != null) following3d--;
        followed3d[slot] = follow;
        if (follow != null) { followOffset3d[slot] = position - follow.GlobalPosition; following3d++; }
        v.Stream = wav;
        v.GlobalPosition = position;
        v.Bus = SoundBuses.Of(clip.Category);
        if (Ms2Godot.Net.LiveSession.LiveCheck) LogGain("3d", clip, clip.Volume * volume, v.Bus);
        SetAttenuation(v, slot, clip, clip.Volume * volume, position, sound.Stereo);
        if (Ms2Godot.Net.LiveSession.LiveCheck) LogRolloff(clip, clip.Volume * volume, position, v.VolumeDb);
        // the tree's timer outlives the field: a voice freed with it before the delay ends plays nothing
        // the voice's sound may be stopped, or the voice taken by a newer sound, before the delay ends
        if (delay > 0) GetTree().CreateTimer(delay).Timeout += () => { if (IsInstanceValid(v) && voiceSlots.SlotOf(ticket) == slot) Start3d(v, slot); }; else Start3d(v, slot);
        PositionalPlayed++;
        return ticket;
    }

    /// <summary>The sound of <paramref name="voice"/> (<see cref="PlayEventVoice"/>'s return) still holds its voice and plays.</summary>
    public bool VoicePlaying(int voice) =>
        voiceSlots.SlotOf(voice) is int slot and >= 0 && voices3d[slot] is { } v && IsInstanceValid(v) && v.Playing;

    /// <summary>Print each event sound's voice choice (the play log of the pickup sound check).</summary>
    public static bool LogVoices;

    /// <summary>The new sound's volume at the listener for the quietest choices, measured from the camera's actor where the
    /// listener stands (Field's listener, soundservice.cpp:975-999). That is its gain times its curve's audibility at that
    /// distance (FMOD_EVENT_INFO.audibility: the occlusion blended by the pan level and no pan level gain,
    /// <see cref="DistanceRolloff.AudibilityAt"/>; the curve starts at FMOD's 3D min of 0), a 2D event's gain alone, or
    /// without either times the inverse distance from the event's range (<see cref="SoundClip.AudibilityAt"/>). Without a
    /// viewer it is the gain alone.</summary>
    float Audibility(SoundClip clip, float gain, Vector3 position) =>
        Viewer?.Invoke() is { } at ? clip.AudibilityAt(gain, at.DistanceTo(position)) : gain;

    static void LogGain(string player, SoundClip clip, float own, StringName bus)
    {
        float busGain = SoundBuses.BusGain(bus);
        GD.Print($"audio: play {player} {clip.Name} category {clip.Category} bus {bus} own {own:F2} bus gain {busGain:F2} heard {own * busGain:F2}");
    }

    /// <summary>The gain of every player now (own volume, bus gain, playing), for the live check.</summary>
    public string DescribeGains()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < musicPlayers.Length; i++)
            if (musicPlayers[i] is { } m && IsInstanceValid(m))
                sb.Append($"music{i} own {Mathf.DbToLinear(m.VolumeDb):F2} bus {SoundBuses.BusGain(m.Bus):F2} playing {m.Playing}; ");
        if (ui != null && IsInstanceValid(ui)) sb.Append($"ui bus {ui.Bus} {SoundBuses.BusGain(ui.Bus):F2}; ");
        int playing = 0;
        foreach (var v in voices3d)
            if (v != null && IsInstanceValid(v) && v.Playing) { playing++; sb.Append($"3d {v.Bus} own {Mathf.DbToLinear(v.VolumeDb):F2} bus {SoundBuses.BusGain(v.Bus):F2}; "); }
        sb.Append($"{playing} 3d playing, {voiceSlots.Looping} looping, {Live3dPlayers} 3d players live, {AmbientSounds.Live} ambient players live; buses {SoundBuses.Describe()}");
        return sb.ToString();
    }

    void PlayClip(SoundClip clip)
    {
        var pcm = SafeDecode.Decode(clip);
        if (pcm == null) return;
        ui.Stream = EventStream(pcm, clip);   // a looping event plays until the next UI sound takes the player
        ui.VolumeDb = Mathf.LinearToDb(clip.Volume);
        ui.Bus = SoundBuses.Of(clip.Category);
        if (Ms2Godot.Net.LiveSession.LiveCheck) LogGain("ui", clip, clip.Volume, ui.Bus);
        ui.Play();
    }

    // The banks hold MPEG layer II, which Godot's mp3 decoder rejects: play the decoded PCM.
    static AudioStreamWav Stream(Pcm16 pcm, byte[] data, bool loop) => new()
    {
        Format = AudioStreamWav.FormatEnum.Format16Bits,
        Stereo = pcm.Channels == 2,
        MixRate = pcm.Rate,
        Data = data,
        LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
        LoopBegin = 0,
        LoopEnd = pcm.Frames,
    };

    /// <summary>The stream of an event's sound: looping as the event does, with the event's fade-in baked into its start
    /// (<see cref="EventFadeIn"/>).</summary>
    internal static AudioStreamWav EventStream(Pcm16 pcm, SoundClip clip)
    {
        var wav = Stream(pcm, EventFadeIn.Bytes(pcm, clip.Event?.FadeIn ?? 0, clip.Loops, out int begin, out int end), clip.Loops);
        wav.LoopBegin = begin;
        wav.LoopEnd = end;
        return wav;
    }
}
