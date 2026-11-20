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
    string EnterSystemSound = "");

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

    /// <summary>CSoundEventMgr::ResetMusicState (@0x14177c8a0, soundeventmgr.cpp:1307-1323).</summary>
    public void ResetMusicState()
    {
        CurrentCue = null;
        CurrentSegment = null;
        IsPlaying = false;
        musicHistory.Clear();
        paramHistories.Clear();
        paramValues.Clear();
        paramHistoryOffset = -1;
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
    /// parameters when the segment reaches its end. Returns the next segment, or null when no link matches (the music
    /// stops, as on the 6 one-shot cues or when a condition does not match).
    /// </summary>
    public MusicSegment AdvanceSegment()
    {
        if (CurrentSegment == null) { IsPlaying = false; return null; }
        foreach (var link in CurrentSegment.OutgoingLinks)
        {
            if (LinkMatches(link))
            {
                CurrentSegment = fev.GetMusicSegment(link.TargetSegmentId);
                IsPlaying = CurrentSegment != null;
                return CurrentSegment;
            }
        }
        CurrentSegment = null;
        IsPlaying = false;
        return null;
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
