namespace Ms2.Audio;

/// <summary>
/// Undo/redo history list with a cursor, ported from TToolHistory&lt;T&gt; (soundeventmgr.h:165-179,
/// soundeventmgr.cpp:808-853; Add @0x141788c40, Get @0x141789570). Used by CSoundEventMgr for music cues and
/// music parameters.
/// </summary>
public sealed class ToolHistory<T>
{
    readonly List<T> items = new();
    int cursor;

    public int Count => items.Count;
    public int Pos => cursor - 1;

    public void Add(T data)
    {
        if (items.Count > cursor) items.RemoveRange(cursor, items.Count - cursor);
        items.Add(data);
        cursor = items.Count;
    }

    public bool Get(int offset, out T value)
    {
        if (items.Count == 0) { value = default; return false; }
        int index = cursor - 1 + offset;
        if (index < 0 || index >= items.Count) { value = default; return false; }
        cursor = index + 1;
        value = items[index];
        return true;
    }

    public void Clear()
    {
        items.Clear();
        cursor = 0;
    }
}

/// <summary>
/// The &lt;bgm&gt; element of a field's entry in table/fielddata.xml, as parsed by CFieldDataCli::vfunc_1
/// (@0x140a55500, fielddatacli.cpp:240-249; defaults in ctor :69).
/// </summary>
public readonly record struct FieldBgmSpec(
    int BgmId,
    int BgmParam = 0,
    float BgmValue = 0f,
    int BossBgmId = 0,
    int BossBgmParam = 0,
    float BossBgmValue = 0f,
    string EnterSystemSound = "")
{
    /// <summary>A &lt;bgm&gt; element read as CFieldDataCli::vfunc_1 reads it (fielddatacli.cpp:242-248, XmlGetAttr: 0 when absent or
    /// unparsed); default for no element.</summary>
    public static FieldBgmSpec Parse(System.Xml.Linq.XElement bgm)
    {
        if (bgm == null) return default;
        return new FieldBgmSpec(
            BgmId: Int(bgm, "id"),
            BgmParam: Int(bgm, "param"),
            BgmValue: Float(bgm, "value"),
            BossBgmId: Int(bgm, "boss"),
            BossBgmParam: Int(bgm, "bossParam"),
            BossBgmValue: Float(bgm, "bossValue"),
            EnterSystemSound: (string)bgm.Attribute("enterSystemSound") ?? "");
    }

    static int Int(System.Xml.Linq.XElement e, string attr) =>
        (string)e.Attribute(attr) is { } s && int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int v) ? v : 0;

    static float Float(System.Xml.Linq.XElement e, string attr) =>
        (string)e.Attribute(attr) is { } s && float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0f;
}

/// <summary>
/// Godot-free port of CSoundEventMgr's music state machine and FMOD Ex interactive music segment transitions:
/// - PlayMusic (@0x14177c100, soundeventmgr.cpp:1151-1203): priority gate, cue history pop on cueId == 0, sets param 5.
/// - SetMusicParameter (@0x14177c420, soundeventmgr.cpp:1207-1262): param 0 no-op, priority gate, history pop on -1.0f.
/// - GetMusicParameter (@0x14177c710, soundeventmgr.cpp:1266-1284).
/// - ResetMusicState (@0x14177c8a0, soundeventmgr.cpp:1307-1323).
/// - OnMusicCallback (@0x14177caf0, soundeventmgr.cpp:1369-1386): on FMOD_MUSIC_CALLBACKTYPE_SEGMENT_START (0),
///   toggles parameter 5 (MusicIntermission) between 0.0f and 10.0f at priority 0x80.
/// - WorldService field / combat (boss) BGM triggers (worldservice_1.cpp:4852-4930, 9717-9731).
/// </summary>
public sealed class MusicController
{
    public const uint DefaultPriority = 0x80;
    public const uint EmptyPriority = 0x100;
    public const int IntermissionParamId = 5;   // MS2Sound.fev comp/prms: 5 = "MusicIntermission"

    readonly FevProject fev;
    readonly HashSet<int> knownParams = new();
    readonly ToolHistory<(int CueId, uint Priority)> musicHistory = new();
    readonly Dictionary<int, ToolHistory<(float Value, uint Priority)>> paramHistories = new();
    readonly Dictionary<int, float> paramValues = new();
    int paramHistoryOffset = -1;   // s_nParamHistoryOffset @0x1439f86a8 (soundeventmgr.cpp:886)

    public MusicCue CurrentCue { get; private set; }
    public MusicSegment CurrentSegment { get; private set; }
    public bool IsPlaying { get; private set; }

    /// <summary>CSoundEventMgr::GetCurrentMusicId (@0x14177caa0, soundeventmgr.cpp:1356-1365).</summary>
    public int CurrentCueId => musicHistory.Get(0, out var cur) ? cur.CueId : 0;

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FevProject, MusicController> sharedByFev = new();

    /// <summary>
    /// Session-scoped MusicController per FEV project, matching the client's singleton CSoundEventMgr lifetime
    /// (reset only at shutdown: WorldService::OnShutdown worldservice_1.cpp:10583, CSoundEventMgr::Shutdown
    /// soundeventmgr.cpp:1036), not per field.
    /// </summary>
    public static MusicController Shared(FevProject fev)
    {
        if (fev == null) throw new ArgumentNullException(nameof(fev));
        return sharedByFev.GetValue(fev, static f => new MusicController(f));
    }

    /// <summary>Resets all session music controllers (WorldService::OnShutdown worldservice_1.cpp:10583).</summary>
    public static void ResetShared()
    {
        foreach (var (_, controller) in sharedByFev)
            controller.ResetMusicState();
    }

    public MusicController(FevProject fev)
    {
        this.fev = fev ?? throw new ArgumentNullException(nameof(fev));
        foreach (var p in fev.MusicParameters) knownParams.Add(p.Id);
    }

    static bool IsNearlyEqual(float a, float b) => MathF.Abs(a - b) <= 1e-4f;

    ToolHistory<(float Value, uint Priority)> ParamHistory(int paramId) =>
        paramHistories.TryGetValue(paramId, out var h) ? h : paramHistories[paramId] = new();

    /// <summary>CSoundEventMgr::PlayMusic (@0x14177c100, soundeventmgr.cpp:1151-1203).</summary>
    public bool PlayMusic(int cueId, float intermissionParam = 0f, uint priority = DefaultPriority, bool force = false)
    {
        var current = musicHistory.Get(0, out var cur) ? cur : (CueId: 0, Priority: EmptyPriority);
        if (cueId == 0)
        {
            if (!force && priority > current.Priority) return true;
            if (musicHistory.Get(-1, out var previous))
            {
                musicHistory.Add(previous);
                if (PromptCue(previous.CueId))
                {
                    SetMusicParameter(IntermissionParamId, intermissionParam, priority, false);
                    IsPlaying = true;
                    return true;
                }
            }
            return false;
        }
        if ((current.Priority >= priority || force) && (current.CueId != cueId || force))
        {
            musicHistory.Add((cueId, priority));
            if (PromptCue(cueId))
            {
                SetMusicParameter(IntermissionParamId, intermissionParam, priority, false);
                IsPlaying = true;
                return true;
            }
        }
        return false;
    }

    bool PromptCue(int cueId)
    {
        var cue = fev.GetMusicCue(cueId);
        if (cue == null) return false;
        var seg = fev.GetMusicSegment(cue.StartSegmentId) ?? (cue.Segments.Count > 0 ? cue.Segments[0] : null);
        if (seg == null) return false;
        CurrentCue = cue;
        CurrentSegment = seg;
        return true;
    }

    /// <summary>CSoundEventMgr::SetMusicParameter (@0x14177c420, soundeventmgr.cpp:1207-1262).</summary>
    public bool SetMusicParameter(int paramId, float value, uint priority = DefaultPriority, bool force = false)
    {
        if (paramId == 0) return true;
        var hist = ParamHistory(paramId);
        var current = hist.Get(0, out var cur) ? cur : (Value: 0f, Priority: EmptyPriority);
        if (value == -1f)
        {
            if (priority > current.Priority)
            {
                paramHistoryOffset--;
                return true;
            }
            if (hist.Get(paramHistoryOffset, out var previous))
            {
                if (SetFmodParameter(paramId, previous.Value))
                {
                    paramHistoryOffset = -1;
                    return true;
                }
            }
            paramHistoryOffset = -1;
            return false;
        }
        if (current.Priority >= priority || force)
        {
            hist.Add((value, priority));
            return SetFmodParameter(paramId, value);
        }
        return false;
    }

    bool SetFmodParameter(int paramId, float value)
    {
        if (knownParams.Count > 0 && !knownParams.Contains(paramId)) return false;
        paramValues[paramId] = value;
        return true;
    }

    /// <summary>CSoundEventMgr::GetMusicParameter (@0x14177c710, soundeventmgr.cpp:1266-1284).</summary>
    public bool TryGetMusicParameter(int paramId, out float value)
    {
        if (paramValues.TryGetValue(paramId, out value)) return true;
        if (knownParams.Contains(paramId)) { value = 0f; return true; }
        value = 0f;
        return false;
    }

    /// <summary>
    /// CSoundEventMgr::ResetMusicState (@0x14177c8a0, soundeventmgr.cpp:1307-1323): clears cue/parameter histories
    /// and playback state, while preserving static s_nParamHistoryOffset (@0x1439f86a8, :886) which the client does
    /// not reset here.
    /// </summary>
    public void ResetMusicState()
    {
        CurrentCue = null;
        CurrentSegment = null;
        IsPlaying = false;
        musicHistory.Clear();
        paramHistories.Clear();
        paramValues.Clear();
    }

    /// <summary>
    /// OnMusicCallback (@0x14177caf0, soundeventmgr.cpp:1369-1386) for FMOD_MUSIC_CALLBACKTYPE_SEGMENT_START (0):
    /// toggles parameter 5 (MusicIntermission) between 0.0f and 10.0f at priority 0x80. Call when the current segment
    /// starts playing (after PlayMusic + any caller SetMusicParameter on the same frame).
    /// </summary>
    public void OnSegmentStarted()
    {
        if (!TryGetMusicParameter(IntermissionParamId, out float value)) return;
        if (IsNearlyEqual(value, 10f))
            SetMusicParameter(IntermissionParamId, 0f, DefaultPriority, false);
        else if (IsNearlyEqual(value, 0f))
            SetMusicParameter(IntermissionParamId, 10f, DefaultPriority, false);
    }

    /// <summary>
    /// Evaluates the current segment's outgoing transition links (FEV comp/lnks/lfsd + cprm) against the live music
    /// parameters without mutating state. Zero allocations.
    /// </summary>
    public MusicSegment PeekNextSegment() => NextSegmentOf(CurrentSegment);

    /// <summary>
    /// The segment <paramref name="seg"/> goes to at its end under the live music parameters (its first matching outgoing
    /// link), or null when none matches. Any segment, not only the current cue's: FMOD's segment player follows an
    /// outgoing cue's links too while it fades out. Measured with the client's fmod_event64 4.44.64 and the client's
    /// segment-start callback: cue 196's 3.0 s intro (segment 329, link to 330 when parameter 5 is 10) prompted out by
    /// cue 43 or 197 at 2.0 s with a 2.0 s crossfade goes on into 330 at 3.0 s, on a new channel at the fading gain
    /// (0.74 -> 0.49 -> 0 with the ramp); with parameter 5 at 0 it stops at 3.0 s. Zero allocations.
    /// </summary>
    public MusicSegment NextSegmentOf(MusicSegment seg)
    {
        if (seg == null) return null;
        var links = seg.OutgoingLinks;
        for (int i = 0; i < links.Count; i++)
        {
            if (LinkMatches(links[i]))
                return fev.GetMusicSegment(links[i].TargetSegmentId);
        }
        return null;
    }

    /// <summary>
    /// Evaluates the current segment's outgoing transition links (FEV comp/lnks/lfsd + cprm) against the live music
    /// parameters when the segment reaches its end. Returns the next segment, or null when no link matches (the music
    /// stops, as on the 6 one-shot cues or when a condition does not match).
    /// </summary>
    public MusicSegment AdvanceSegment()
    {
        var next = PeekNextSegment();
        CurrentSegment = next;
        IsPlaying = next != null;
        return next;
    }

    bool LinkMatches(MusicLink link)
    {
        if (link.Condition == null) return true;
        return TryGetMusicParameter(link.Condition.ParamId, out float v) && link.Condition.Matches(v);
    }

    /// <summary>True when <paramref name="seg"/> loops directly back to itself unconditionally (single-segment loop).</summary>
    public static bool IsUnconditionalSelfLoop(MusicSegment seg) =>
        seg != null && seg.OutgoingLinks.Count > 0 && seg.OutgoingLinks[0].Condition == null && seg.OutgoingLinks[0].TargetSegmentId == seg.Id;

    /// <summary>
    /// WorldService field BGM start (@0x141bc7b80, worldservice_1.cpp:4866-4872): PlayMusic(m_bgmId, 0, 0x80, force)
    /// then SetMusicParameter(m_bgmParam, m_bgmValue, 0x80, force).
    /// </summary>
    public bool PlayFieldBgm(FieldBgmSpec spec, bool force = false)
    {
        if (spec.BgmId == 0) return false;
        if (!PlayMusic(spec.BgmId, 0f, DefaultPriority, force)) return false;
        SetMusicParameter(spec.BgmParam, spec.BgmValue, DefaultPriority, force);
        return true;
    }

    /// <summary>
    /// WorldService::FUN_141bc7b80 (@0x141bc7b80, worldservice_1.cpp:4856-4872) for map 1 (login screen):
    /// queries LoginBgBgmTable::GetBgm(); if CueId != 0 plays it with map 1's BgmValue and returns without falling
    /// through; when CueId == 0 falls back to map 1's own FieldBgmSpec.
    /// </summary>
    public bool PlayLoginBgm(LoginBgBgmTable table, FieldBgmSpec map1Fallback, Func<int, int> pick = null, bool force = false)
    {
        if (table != null)
        {
            var bgm = table.GetBgm(pick);
            if (bgm.CueId != 0)
            {
                if (!PlayMusic(bgm.CueId, 0f, DefaultPriority, force)) return false;
                SetMusicParameter(bgm.ParamId, map1Fallback.BgmValue, DefaultPriority, force);
                return true;
            }
        }
        return PlayFieldBgm(map1Fallback, force);
    }

    /// <summary>
    /// WorldService::StartCombatBgm (@0x141bc7d40, worldservice_1.cpp:4887-4894): PlayMusic(m_combatBgmId, 0, 0x80, 0)
    /// then SetMusicParameter(m_combatBgmParam, m_combatBgmValue, 0x80, 0).
    /// </summary>
    public bool PlayBossBgm(FieldBgmSpec spec)
    {
        if (spec.BossBgmId == 0) return false;
        if (!PlayMusic(spec.BossBgmId, 0f, DefaultPriority, false)) return false;
        SetMusicParameter(spec.BossBgmParam, spec.BossBgmValue, DefaultPriority, false);
        return true;
    }
}

/// <summary>
/// Tracks boss NPCs in range for field vs. combat BGM switching, ported from WorldService m_900 / m_910
/// (AddCombatBgmTracker @0x141bc7ec0, StartCombatBgm @0x141bc7d40, RemoveCombatBgmTracker @0x141bc7f10,
/// clear-in-range @0x141bc7fe0, tick restore worldservice_1.cpp:9717-9731; driven by CNpc unit_14108e1e0_npcactorandvibrate.cpp:534,613,2150,2354-2359).
/// </summary>
public sealed class BossBgmTracker
{
    readonly MusicController music;
    readonly FieldBgmSpec fieldBgm;
    readonly Dictionary<ulong, bool> activeByBoss = new();
    bool playingBoss;

    public bool IsBossBgmActive => playingBoss;

    /// <summary>
    /// The point a boss npc's music distance (GameObject m_unk_c4, read by CNpc::vfunc_53 :2355) is measured from. WorldService's
    /// update (worldservice_1.cpp:9672-9700) sets m_unk_c4 from its camera point: the camera target's place (CameraMgr m_1c0,
    /// FUN_140a38d60: the own PC, an entity camera's npc or the observed PC) when there is a camera (FUN_140a18070), else the
    /// camera's eye (FUN_140a17e10); the origin with no camera. The player's own focus (it, or its mount) is the other
    /// distance, m_unk_bc. <paramref name="cameraEye"/> is null for no camera.
    /// </summary>
    public static System.Numerics.Vector3 MusicPoint(System.Numerics.Vector3? cameraTarget, System.Numerics.Vector3? cameraEye) =>
        cameraEye is not { } eye ? System.Numerics.Vector3.Zero : cameraTarget ?? eye;

    public BossBgmTracker(MusicController music, FieldBgmSpec fieldBgm)
    {
        this.music = music;
        this.fieldBgm = fieldBgm;
    }

    /// <summary>WorldService::AddCombatBgmTracker (@0x141bc7ec0, worldservice_1.cpp:4899).</summary>
    public void AddBoss(ulong entityId) => activeByBoss.TryAdd(entityId, false);

    /// <summary>WorldService::StartCombatBgm (@0x141bc7d40, worldservice_1.cpp:4878): boss within bossSoundDistance.</summary>
    public bool EnterBossRange(ulong entityId)
    {
        if (playingBoss || !activeByBoss.ContainsKey(entityId)) return false;
        activeByBoss[entityId] = true;
        if (music.PlayBossBgm(fieldBgm)) { playingBoss = true; return true; }
        return false;
    }

    /// <summary>WorldService @0x141bc7fe0 (worldservice_1.cpp:4921): boss beyond bossSoundEndDistance.</summary>
    public void LeaveBossRange(ulong entityId)
    {
        if (playingBoss && activeByBoss.ContainsKey(entityId)) activeByBoss[entityId] = false;
    }

    /// <summary>
    /// CNpc::vfunc_53's boss check (@0x1410952f0, unit_14108e1e0_npcactorandvibrate.cpp:2354-2359), each frame for a live boss:
    /// at <paramref name="distance"/> (the boss's distance from the player, GameObject m_unk_c4) up to
    /// <paramref name="start"/> (bossSoundDistance) the boss music starts, at <paramref name="end"/> (bossSoundEndDistance) or farther the
    /// boss counts as out of range; between the two nothing changes. True when the boss music started.
    /// </summary>
    public bool Update(ulong entityId, float distance, float start, float end)
    {
        if (distance <= start) return EnterBossRange(entityId);
        if (distance >= end) LeaveBossRange(entityId);
        return false;
    }

    /// <summary>Whether a boss of this id is tracked (spawned and not yet dead or removed).</summary>
    public bool Tracks(ulong entityId) => activeByBoss.ContainsKey(entityId);

    /// <summary>WorldService::RemoveCombatBgmTracker (@0x141bc7f10, worldservice_1.cpp:4907): boss died or despawned.</summary>
    public bool RemoveBoss(ulong entityId)
    {
        activeByBoss.Remove(entityId);
        if (activeByBoss.Count == 0 && playingBoss)
        {
            music.PlayFieldBgm(fieldBgm, force: false);
            playingBoss = false;
            return true;
        }
        return false;
    }

    /// <summary>WorldService tick check (worldservice_1.cpp:9717-9731): restores field BGM once every tracked boss is out of range.</summary>
    public bool Tick()
    {
        if (!playingBoss) return false;
        foreach (var inRange in activeByBoss.Values)
            if (inRange) return false;
        music.PlayFieldBgm(fieldBgm, force: false);
        playingBoss = false;
        return true;
    }
}

/// <summary>
/// The voice gains of FMOD Ex interactive music across cue changes:
/// - The client calls FMOD::MusicSystem::promptCue (CSoundEventMgr::PlayMusic @0x14177c100, soundeventmgr.cpp:1165,1186).
/// - MS2Sound.fev comp/thms/thm /thmd stores each theme's crossfadeMs (offset 11, parsed into MusicTheme.CrossfadeMs);
///   all 311 in-theme links in comp/lnks/lnk /lnkd have TransitionType = 4 (EndOfSegment), so segments inside a theme
///   play back-to-back with 0 ms crossfade.
/// - Measured on the client's own fmod_event64.dll (4.44.64) through MusicSystem::promptCue, reading each music channel's
///   segment-player group volume every 21 ms:
///   - the incoming cue starts at once from 0 and both sides ramp linearly in amplitude over the incoming theme's
///     crossfadeMs (2000 to 500 ms took 500 ms, 500 to 2000 ms took 2000 ms); in + out stayed 1.025 across the
///     ramp, so it is not an equal-power curve;
///   - the first cue with nothing playing starts at full gain;
///   - a cue prompted mid-fade keeps every voice: each one still audible ramps from its gain at the prompt down to 0
///     over the new cue's crossfadeMs while the new cue ramps 0 to 1 (three voices at 0.50 / 0.50 / 0 at the prompt,
///     all reaching 0 / 0 / 1 together 2000 ms later);
///   - the segment player holds at most 10 voices: past that a prompted cue gets no voice, the newest voice keeps its
///     own 0 to 1 ramp and the older ones ramp to 0 over the new cue's crossfadeMs;
///   - an EndOfSegment chain of the incoming cue mid-fade goes on with the same ramp (a cue whose 1 s first segment
///     ends 1 s into a 2 s fade reached full gain 2 s after its prompt);
///   - a voice whose segment ends with no link to follow is freed, and the other ramps go on;
///   - a cue prompted when the incoming voice has ended starts at full gain at once, and the voices still fading stop
///     ramping and hold their gain (0.26 for 65 s), until a later cue with a playing voice ramps them out.
/// Zero per-frame allocations.
/// </summary>
public sealed class MusicCrossfade
{
    public const int SlotCount = 10;
    const float Silent = 1e-4f;

    readonly float[] gains = new float[SlotCount];
    readonly float[] rampFrom = new float[SlotCount];
    readonly float[] rampTo = new float[SlotCount];
    readonly float[] rampElapsed = new float[SlotCount];
    readonly float[] rampDuration = new float[SlotCount];
    readonly bool[] ramping = new bool[SlotCount];
    readonly bool[] busy = new bool[SlotCount];
    int activeSlot;
    bool fading;

    public int ActiveSlot => activeSlot;
    /// <summary>A ramp is running on some voice.</summary>
    public bool IsFading => fading;
    public float FadeElapsed => rampElapsed[activeSlot];
    public float FadeDuration => rampDuration[activeSlot];
    public float Gain(int slot) => gains[slot];
    public bool Busy(int slot) => busy[slot];
    public float ActiveGain => gains[activeSlot];

    /// <summary>The loudest voice other than the active one (0 when none).</summary>
    public float OutgoingGain
    {
        get
        {
            float g = 0f;
            for (int i = 0; i < SlotCount; i++)
                if (i != activeSlot && gains[i] > g) g = gains[i];
            return g;
        }
    }

    /// <summary>A voice other than the active one is still held (fading or holding its gain).</summary>
    public bool HasOutgoing
    {
        get
        {
            for (int i = 0; i < SlotCount; i++)
                if (i != activeSlot && busy[i]) return true;
            return false;
        }
    }

    /// <summary>
    /// Begins a transition into a new cue or segment over <paramref name="crossfadeMs"/>; returns the slot that plays it
    /// (-1: no voice, nothing plays) and a bit mask of the slots whose players stop now. <paramref name="hasActiveAudio"/>:
    /// the active voice still plays (or its next segment is on the way).
    /// - crossfadeMs &gt; 0 with the active voice playing (a cue change): voices already silent stop, every other one ramps
    ///   from its gain now to 0 over crossfadeMs, and the incoming one takes a free slot and ramps 0 to 1. With all
    ///   <see cref="SlotCount"/> slots taken the cue gets no voice and the active one keeps its own ramp.
    /// - crossfadeMs &lt;= 0 with the active voice playing (an EndOfSegment chain): the chained segment takes the active
    ///   slot with its gain and ramp, and the other ramps go on.
    /// - Otherwise (the first cue, or a cue after the active voice ended): the new cue plays at full gain; voices still
    ///   fading stop ramping and hold their gain, silent ones stop.
    /// </summary>
    public (int IncomingSlot, int StopMask, bool Crossfading) BeginTransition(int crossfadeMs, bool hasActiveAudio)
    {
        bool live = hasActiveAudio && busy[activeSlot];   // read before a silent incoming voice is freed below
        if (live && crossfadeMs <= 0) return (activeSlot, 0, HasOutgoing);
        int stop = 0, free = -1;
        for (int i = 0; i < SlotCount; i++)
        {
            if (busy[i] && gains[i] <= Silent) Free(i, ref stop);
            if (!busy[i] && free < 0) free = i;
        }

        if (live)
        {
            float seconds = crossfadeMs * 0.001f;
            for (int i = 0; i < SlotCount; i++)
                if (busy[i] && (free >= 0 || i != activeSlot)) Ramp(i, 0f, seconds);
            if (free < 0) return (-1, stop, true);
            activeSlot = free;
            busy[free] = true;
            gains[free] = 0f;
            Ramp(free, 1f, seconds);
            return (free, stop, true);
        }

        for (int i = 0; i < SlotCount; i++) ramping[i] = false;
        fading = false;
        if (busy[activeSlot]) Free(activeSlot, ref stop);
        busy[activeSlot] = true;
        gains[activeSlot] = 1f;
        rampElapsed[activeSlot] = 0f;
        rampDuration[activeSlot] = 0f;
        return (activeSlot, stop, HasOutgoing);
    }

    void Ramp(int slot, float to, float seconds)
    {
        rampFrom[slot] = gains[slot];
        rampTo[slot] = to;
        rampElapsed[slot] = 0f;
        rampDuration[slot] = seconds;
        ramping[slot] = true;
        fading = true;
    }

    void Free(int slot, ref int stop)
    {
        busy[slot] = false;
        ramping[slot] = false;
        gains[slot] = 0f;
        stop |= 1 << slot;
    }

    /// <summary>
    /// A voice's segment ended with nothing to follow: its slot is free again and the other ramps go on (the active slot
    /// keeps its index, with no voice in it).
    /// </summary>
    public void Release(int slot)
    {
        int stop = 0;
        if (busy[slot]) Free(slot, ref stop);
        fading = false;
        for (int i = 0; i < SlotCount; i++) fading |= ramping[i];
    }

    /// <summary>
    /// Advances the ramps by <paramref name="deltaSeconds"/>. Zero allocations. Returns the bit mask of the slots whose
    /// ramp to 0 completed on this tick (their players stop; 0 otherwise).
    /// </summary>
    public int Step(float deltaSeconds)
    {
        if (!fading) return 0;
        int stop = 0;
        bool any = false;
        for (int i = 0; i < SlotCount; i++)
        {
            if (!ramping[i]) continue;
            if (deltaSeconds > 0f) rampElapsed[i] += deltaSeconds;
            if (rampElapsed[i] >= rampDuration[i] || rampDuration[i] <= 0f)
            {
                rampElapsed[i] = rampDuration[i];
                ramping[i] = false;
                gains[i] = rampTo[i];
                if (rampTo[i] <= 0f) Free(i, ref stop);
                continue;
            }
            gains[i] = rampFrom[i] + (rampTo[i] - rampFrom[i]) * (rampElapsed[i] / rampDuration[i]);
            any = true;
        }
        fading = any;
        return stop;
    }

    /// <summary>
    /// Crossfade duration (ms) for an in-theme segment link (comp/lnks/lnk /lnkd). All 311 links in MS2Sound.fev have
    /// TransitionType == 4 (EndOfSegment), which chains segments sequentially back-to-back with 0 ms overlap; theme
    /// CrossfadeMs applies on cue/theme changes (promptCue), not EndOfSegment links. Zero allocations.
    /// </summary>
    public static int LinkCrossfadeMs(MusicLink link, int themeCrossfadeMs) =>
        link == null || link.TransitionType == MusicLink.TransitionEndOfSegment ? 0 : themeCrossfadeMs;

    public void StopAll()
    {
        activeSlot = 0;
        fading = false;
        for (int i = 0; i < SlotCount; i++)
        {
            gains[i] = 0f;
            busy[i] = false;
            ramping[i] = false;
            rampElapsed[i] = 0f;
            rampDuration[i] = 0f;
        }
    }
}

/// <summary>
/// Tracks async decode generation, segment-end chaining, and the last-2 s predecode window for FieldAudio.
/// On a cue change (<see cref="BeginCueChange"/>), immediately unchains and disarms the outgoing segment before the
/// incoming cue finishes worker-thread decode, so neither the old segment's Finished callback nor the per-frame
/// predecode check can mutate or advance the new cue's MusicController state. Zero per-frame allocations.
/// </summary>
public sealed class MusicPlaybackGuard
{
    public const double PredecodeLeadSeconds = 2.0;

    int generation;
    bool segmentActive;
    bool selfLoop;
    bool chained;
    bool transitionRequested;
    bool predecodeRequested;
    double segmentSeconds;

    public int Generation => generation;
    public bool IsSegmentActive => segmentActive;
    public bool IsChained => chained;
    public bool PredecodeRequested => predecodeRequested;

    /// <summary>Bumps generation and unchains/disarms the outgoing segment before decoding a new cue.</summary>
    public int BeginCueChange()
    {
        generation++;
        segmentActive = false;
        selfLoop = false;
        chained = false;
        transitionRequested = true;
        predecodeRequested = true;
        segmentSeconds = 0.0;
        return generation;
    }

    /// <summary>True when a worker-thread decode callback for <paramref name="gen"/> still matches the current cue.</summary>
    public bool AcceptDecode(int gen) => gen == generation;

    /// <summary>Arms chaining and predecode tracking once the decoded segment starts playing.</summary>
    public void OnSegmentPlaying(double seconds, bool isSelfLoop)
    {
        segmentActive = true;
        segmentSeconds = seconds;
        selfLoop = isSelfLoop;
        chained = !isSelfLoop;
        transitionRequested = false;
        predecodeRequested = false;
    }

    /// <summary>
    /// Per-frame predecode trigger (zero allocations): true once when a live non-looping segment enters its last
    /// <see cref="PredecodeLeadSeconds"/> seconds; always false during a pending cue-change decode.
    /// </summary>
    public bool TryBeginPredecode(double playbackPositionSeconds)
    {
        if (!segmentActive || selfLoop || transitionRequested || predecodeRequested || segmentSeconds <= 0.0)
            return false;
        if (playbackPositionSeconds < segmentSeconds - PredecodeLeadSeconds)
            return false;
        predecodeRequested = true;
        return true;
    }

    /// <summary>
    /// The perf gate's music hold (FieldAudio.GateHoldMusic): true while a live non-looping segment is in its last
    /// <see cref="PredecodeLeadSeconds"/> seconds, where <see cref="TryBeginPredecode"/> would start the next segment's
    /// decode. Zero allocations; it changes no state.
    /// </summary>
    public bool InEndLead(double playbackPositionSeconds) =>
        segmentActive && !selfLoop && !transitionRequested && segmentSeconds > 0.0
        && playbackPositionSeconds >= segmentSeconds - PredecodeLeadSeconds;

    /// <summary>
    /// Guards the chained player's Finished callback: true only while that segment is still active and chained for
    /// the current cue (never while a cue change is decoding).
    /// </summary>
    public bool TryAdvanceOnFinished()
    {
        if (!chained || transitionRequested) return false;
        chained = false;
        transitionRequested = true;
        return true;
    }

    public void Stop()
    {
        generation++;
        segmentActive = false;
        selfLoop = false;
        chained = false;
        transitionRequested = false;
        predecodeRequested = false;
        segmentSeconds = 0.0;
    }
}

/// <summary>
/// The rules for a music segment that plays from its decoded head while its rest decodes on a pool thread
/// (FieldAudio.DecodeSegment): what a voice's player finishing means, what the rest's arrival does to a voice on it, and
/// whether the rest is still worth decoding once the main thread has seen the head.
/// </summary>
public static class MusicSegmentHead
{
    public enum SlotEnd
    {
        /// <summary>The head's end, not the segment's: the rest's arrival goes on from there (<see cref="RestArrival.ResumeFromHead"/>).</summary>
        WaitForRest,
        /// <summary>An outgoing voice's segment ended: it chains into its own next segment, or its slot is freed.</summary>
        ChainOutgoing,
        /// <summary>The current cue's segment ended: the cue advances (if the playback guard still has it chained).</summary>
        AdvanceCue,
    }

    /// <summary>A music voice's player finished. <paramref name="hasSegment"/>: the slot has a decoded segment;
    /// <paramref name="complete"/>: its stream has the whole PCM; <paramref name="currentCue"/>: the voice is the current cue's.</summary>
    public static SlotEnd OnSlotFinished(bool hasSegment, bool complete, bool currentCue) =>
        hasSegment && !complete ? SlotEnd.WaitForRest : currentCue ? SlotEnd.AdvanceCue : SlotEnd.ChainOutgoing;

    public enum RestArrival
    {
        /// <summary>The voice is still on the head and goes on into the whole PCM at its own position.</summary>
        Nothing,
        /// <summary>The head ran out before the rest came: the voice plays on from the head's end.</summary>
        ResumeFromHead,
        /// <summary>The rest failed and this is the current cue's active voice: it ends as one with nothing to follow.</summary>
        EndActiveVoice,
        /// <summary>The rest failed on another voice (outgoing, or the cue's but not active): its slot is freed.</summary>
        ReleaseSlot,
    }

    /// <summary>A segment's rest decoded (<paramref name="decoded"/>) or failed, for a voice playing that segment.</summary>
    public static RestArrival OnRestArrived(bool decoded, bool activeVoiceOfCue, bool playing) =>
        !decoded ? activeVoiceOfCue ? RestArrival.EndActiveVoice : RestArrival.ReleaseSlot
        : playing ? RestArrival.Nothing : RestArrival.ResumeFromHead;
}

/// <summary>
/// Whether a segment's rest is still wanted, shared between the pool thread that decodes it and the main thread that
/// takes or rejects the head. The pool thread starts on the rest at once and asks <see cref="KeepDecoding"/> between
/// steps; when the main thread rejects the head (a newer cue came first, or the outgoing voice it was for moved on),
/// <see cref="Resolve"/> stops the rest at the next step.
/// </summary>
public sealed class SegmentRest
{
    volatile int state;   // 0 pending, 1 taken, 2 rejected

    /// <summary>False once the head was rejected; true while it is pending or taken.</summary>
    public bool KeepDecoding => state != 2;
    public bool Rejected => state == 2;

    /// <summary>The main thread took the head (<paramref name="taken"/>) or rejected it. Called once.</summary>
    public void Resolve(bool taken) => state = taken ? 1 : 2;
}

/// <summary>
/// Parameters of a SoundMusic effect (kEffectSoundMusic = 3, CSoundMusicEffectParam @0x14178fd20,
/// vfunc_1 @0x14178fdb0, soundmusiceffectparam.cpp:69-84).
/// </summary>
public readonly record struct SoundMusicEffectParam(
    int CueId = -1,
    int ParamId = -1,
    float ParamValue = 0f,
    int Priority = 0x80,
    float Volume = -1f,
    float Duration = 0f,
    float StartDelay = 0f,
    string Reverb = "");

/// <summary>
/// Godot-free runtime of CSoundMusicEffect (effect type 3, soundmusiceffectparam.cpp:118-176):
/// - vfunc_3 (@0x1417900e0, :133-156): rejects caster kind &gt; 1 (m_unk_18 &gt; 1), invokes PlayMusic(cueID, 0, priority, 0)
///   when cueID != -1, SetMusicParameter(paramID, paramValue, priority, 0) when paramID != -1, ignores reverb (the client's
///   CSoundEventMgr::FUN_14177ddd0 at soundeventmgr.cpp:1738 is an empty stub returning 1), and plans a linear volume fade
///   from GetMusicVolume() to GetCategoryVolume(soundOption, 2) * volume when volume &gt; 0 and duration != 0.
/// - vfunc_9 (@0x1417902b0, :161-176): linearly interpolates music volume while now &lt;= startTime + duration.
/// - ~CSoundMusicEffect (@0x141790060, :125-128): restores music volume to GetCategoryVolume(soundOption, 2).
/// Zero per-frame allocations.
/// </summary>
public sealed class SoundMusicEffectState
{
    /// <summary>
    /// Unk_EffectTarget::m_nKind values (effectservice.cpp:255,304,360,2169; soundmusiceffectparam.cpp:139-140;
    /// WorldService::GetObjectKind @0x141bd1a10, worldservice_1.cpp:6304-6309: MyPC::vfunc_58 @0x141522cc0,
    /// mypc.cpp:1247 returns 1; PCBase::vfunc_58, pcbase.cpp:1092 returns 3 for other PCs; CNpc::vfunc_58,
    /// unit_14108e1e0_npcactorandvibrate.cpp:654 returns 6 for a boss, else 5).
    /// </summary>
    public const int CasterSystem = 0;
    public const int CasterMyPc = 1;
    public const int CasterOther = 2;
    public const int CasterOtherPc = 3;
    public const int CasterNpc = 5;
    public const int CasterBoss = 6;
    /// <summary>A world object's effect: "background" in effectOption.xml (EffectSkipMgr::ParseCaster @0x1411b9180), set by the
    /// effect requests of an entity vibrator (entityvibrator.cpp:190, :213), the cube map (cubemapinformationservice_3.cpp:2547)
    /// and a bgEffect_ text key (animationcallbackmanager.cpp:406).</summary>
    public const int CasterBackground = 7;

    /// <summary>
    /// Maps a combat actor to Unk_EffectTarget::m_nKind per WorldService::GetObjectKind (worldservice_1.cpp:6304-6309):
    /// 1 for the local player (MyPC::vfunc_58, mypc.cpp:1247), 3 for remote PCs (PCBase::vfunc_58, pcbase.cpp:1092); an npc is
    /// 5 or 6 (CNpc::vfunc_58), not mapped here.
    /// </summary>
    public static int CasterKindForActor(bool isMyPc) => isMyPc ? CasterMyPc : CasterOtherPc;

    bool active;
    double startTime;
    float duration;
    float startVolume;
    float targetVolume;
    float deltaVolume;

    public bool IsFading => active;
    public double StartTime => startTime;
    public float Duration => duration;
    public float StartVolume => startVolume;
    public float TargetVolume => targetVolume;
    public float DeltaVolume => deltaVolume;

    /// <summary>
    /// CSoundMusicEffect::vfunc_3 (@0x1417900e0, soundmusiceffectparam.cpp:133-156).
    /// Defaults <paramref name="casterKind"/> to <see cref="CasterOther"/> (rejected, m_unk_18 &gt; 1) so callers
    /// that do not know the caster cannot hijack local music state; pass <see cref="CasterSystem"/> (0) or
    /// <see cref="CasterMyPc"/> (1) when the effect originates from the system or local player.
    /// </summary>
    public (bool CueChanged, bool ParamApplied, bool FadeStarted) Apply(
        MusicController music,
        in SoundMusicEffectParam param,
        double nowSeconds,
        float currentMusicVolume,
        float categoryMusicVolume,
        int casterKind = CasterOther)
    {
        // soundmusiceffectparam.cpp:139-140: m_unk_18 > 1 returns false (only system=0 / local player=1 apply).
        if (casterKind > 1) return (false, false, false);

        bool cueChanged = false;
        bool paramApplied = false;
        if (music != null && param.CueId != -1)
        {
            int prevCue = music.CurrentCueId;
            if (music.PlayMusic(param.CueId, 0f, (uint)param.Priority, false))
                cueChanged = music.CurrentCueId != prevCue;
        }
        if (music != null && param.ParamId != -1)
            paramApplied = music.SetMusicParameter(param.ParamId, param.ParamValue, (uint)param.Priority, false);

        // soundmusiceffectparam.cpp:145-146: non-empty reverb calls CSoundEventMgr::FUN_14177ddd0 (stub returning 1).
        // soundmusiceffectparam.cpp:147-148: each CSoundMusicEffect fades independently; a non-fading effect returns
        // false without cancelling another effect's running fade.
        if (param.Volume <= 0f || param.Duration == 0f)
            return (cueChanged, paramApplied, false);

        // CEffect::vfunc_3 (@0x14119f0b0, effectservice.cpp:365): m_dStartTime = dStart + m_fStartDelay.
        // CSoundMusicEffect::vfunc_3 (:141-144) already prompted PlayMusic / SetMusicParameter above at creation;
        // StartDelay only shifts the fade window [startTime, startTime + duration].
        startTime = nowSeconds + param.StartDelay;
        duration = param.Duration;
        startVolume = currentMusicVolume;
        targetVolume = categoryMusicVolume * param.Volume;
        deltaVolume = targetVolume - startVolume;
        active = true;
        return (cueChanged, paramApplied, true);
    }

    /// <summary>
    /// CSoundMusicEffect::vfunc_9 (@0x1417902b0, soundmusiceffectparam.cpp:161-176) and destructor restore
    /// (@0x141790060, :125-128). Zero allocations.
    /// </summary>
    public bool Tick(double nowSeconds, float categoryMusicVolume, out float volume)
    {
        if (!active)
        {
            volume = categoryMusicVolume;
            return false;
        }
        if (nowSeconds <= startTime + duration)
        {
            float t = nowSeconds <= startTime ? 0f : (float)(nowSeconds - startTime) / duration;
            volume = startVolume + t * deltaVolume;
            return true;
        }
        active = false;
        volume = categoryMusicVolume;
        return false;
    }

    /// <summary>CSoundMusicEffect::~CSoundMusicEffect (@0x141790060, soundmusiceffectparam.cpp:125-128).</summary>
    public float Stop(float categoryMusicVolume)
    {
        active = false;
        return categoryMusicVolume;
    }
}
