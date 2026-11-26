using System.Text;

namespace Ms2.Audio;

/// <summary>One wave entry of a sound definition: the wave's file name in the FEV, its bank and sample index, and its weight.</summary>
public sealed class DefEntry
{
    public int Kind;         // 0 wave, 2 silence, 3 programmer sound
    public int Weight = 100; // percent
    public string Wave;
    public int Type;         // bank index in FevProject.Banks (0..Banks.Count-1)
    public int Bank => Type;
    public int BankIndex;    // index of the sample in its FSB bank
    public int Length;       // length in ms
}

public sealed class SoundDef
{
    public int NameIndex;
    public int Mode;
    public List<DefEntry> Entries = new();
}

/// <summary>One bank entry in the FEV project with its per-language filename suffixes (from the LANG + LGCY bank table).</summary>
public sealed class FevBank
{
    public string Name;
    public uint Flags;
    public uint MaxStreams;
    /// <summary>Filename suffix per language in <see cref="FevProject.Languages"/> order (e.g. "", "_Kor_def", "_jp", "_en", "_cn").</summary>
    public string[] LanguageSuffixes = Array.Empty<string>();
}

/// <summary>A music parameter declared in the FEV interactive music section (comp/prms).</summary>
public sealed class MusicParameterDef
{
    public int Id;
    public string Name;
}

/// <summary>
/// A parameter condition on a music link (comp/lnks/lnk /cond/cprm).
/// FMOD Designer 4 condition ops: 0 equal, 1 not equal, 2 less, 3 less or equal, 4 greater, 5 greater or equal, 6 in range.
/// </summary>
public sealed class MusicCondition
{
    public const int OpEqual = 0;
    public const int OpNotEqual = 1;
    public const int OpLess = 2;
    public const int OpLessOrEqual = 3;
    public const int OpGreater = 4;
    public const int OpGreaterOrEqual = 5;
    public const int OpInRange = 6;

    public int Op;
    public int ParamId;
    public float Value1;
    public float Value2;

    /// <summary>Evaluates this condition against the current parameter value.</summary>
    public bool Matches(float value) => Op switch
    {
        OpEqual => MathF.Abs(value - Value1) <= 1e-4f,
        OpNotEqual => MathF.Abs(value - Value1) > 1e-4f,
        OpLess => value < Value1,
        OpLessOrEqual => value <= Value1,
        OpGreater => value > Value1,
        OpGreaterOrEqual => value >= Value1,
        OpInRange => value >= Value1 && value <= Value2,
        _ => false,
    };
}

/// <summary>An outgoing transition link from a music segment (comp/lnks/lnk  + lfsd).</summary>
public sealed class MusicLink
{
    public const int TransitionEndOfSegment = 4;

    public int Id;
    public int TargetSegmentId;
    public int TransitionType;   // 4 = end of segment
    public MusicCondition Condition;   // null = unconditional transition
}

/// <summary>A music segment: one sample of a bank, with its theme id and outgoing transition links.</summary>
public sealed class MusicSegment
{
    public int Id;
    public int ThemeId;
    public string Bank;      // e.g. "MS2Sound_BGM"
    public int Index;
    public List<MusicLink> OutgoingLinks = new();
}

/// <summary>A music theme (comp/thms/thm ): its start segment, crossfade time and member segments.</summary>
public sealed class MusicTheme
{
    public int Id;
    public int CrossfadeMs;
    public int StartSegmentId;
    public List<MusicSegment> Segments = new();
}

/// <summary>A music cue (CSoundEventMgr::PlayMusic id) with its theme, start segment and segments ordered by segment id.</summary>
public sealed class MusicCue
{
    public int Id;
    public string Name;
    public int Theme;
    public int StartSegmentId;
    public int CrossfadeMs;
    public MusicTheme ThemeInfo;
    public List<MusicSegment> Segments = new();
}

/// <summary>An event category of the FEV project (FMOD::EventCategory): its name, authored volume and pitch, and its
/// sub-categories. A sub-category's volume multiplies with its parents' (FMOD Ex category volumes are hierarchical).</summary>
public sealed class FevCategory
{
    public string Name;
    public float Volume = 1f, Pitch;
    public FevCategory Parent;
    public List<FevCategory> Children = new();
}

/// <summary>A sound event: its group path, category, volume, 3D distances and sound definitions.</summary>
public sealed class FevEvent
{
    public string Name;
    public string Group;      // full FMOD event group path, e.g. "MS2Sound/Sound/Skill/Berserker"
    public string FullPath => string.IsNullOrEmpty(Group) ? Name : Group + "/" + Name;
    public string Category;   // the event's own category: "effect", "environment", "UISound", "npcVoice/..."
    public bool IsSimple;     // true for simple events (type 0x10), false for complex events (type 0x08)
    public float Volume = 1f;
    public float MinDistance, MaxDistance;   // 3D attenuation (0 when the record has none)
    public List<int> Defs = new();
    public List<float> DefVolumes = new();   // the sound's own volume (parallel to Defs)
    /// <summary>Whether the sound loops until the event is stopped (parallel to <see cref="Defs"/>): a sound instance's loop
    /// mode (<see cref="FevProject.LoopModeOneShot"/> plays once, the other two loop).</summary>
    public List<bool> DefLoops = new();
    /// <summary>The event has a looping sound, so it plays until stopped (FMOD_EVENT_INFO.lengthms is -1 for it).</summary>
    public bool Loops => DefLoops.Contains(true);
    /// <summary>The client's category slot of <see cref="Category"/> (<see cref="SoundCategories.TryResolve"/>), resolved
    /// at the event's first play: -2 until then, -1 for none.</summary>
    internal int CategorySlot = -2;
    internal float CategoryBelowTop = 1f;
    /// <summary>The event's playback limits from its record (laid out against the FMOD Designer 4.44 example project's
    /// .fdp and .fev): +0x28 priority (0 the most important), +0x2c max playbacks, +0x78 max playbacks behavior
    /// (FMOD_EVENTPROPERTY_PRIORITY, _MAX_PLAYBACKS, _MAX_PLAYBACKS_BEHAVIOR, fmod_event.h:185-187).</summary>
    public int Priority, MaxPlaybacks, MaxPlaybacksBehavior;
}

/// <summary>
/// Reader of MS2Sound.fev (FMOD Designer 4.x project, version 0x45): language table, bank names and per-language suffixes,
/// event group hierarchy, sound definitions (index = the value an event's sound refers to), and the interactive music
/// system (parameters, cues, themes, segments and conditional transition links).
/// </summary>
public sealed class FevProject
{
    byte[] d;
    int lgcy, lgcyEnd;
    string projName = "";
    string[] strr = Array.Empty<string>();
    readonly List<string> languages = new();
    int defaultLanguageIndex;
    readonly List<string> banks = new();
    readonly List<FevBank> bankTable = new();
    readonly Dictionary<string, FevBank> banksByName = new(StringComparer.OrdinalIgnoreCase);
    readonly List<SoundDef> defs = new();
    readonly List<MusicParameterDef> musicParameters = new();
    readonly Dictionary<int, string> cueNames = new();
    readonly Dictionary<int, int> cueToTheme = new();
    readonly Dictionary<int, MusicSegment> segmentsById = new();
    readonly Dictionary<int, MusicTheme> themesById = new();
    readonly Dictionary<int, List<MusicSegment>> themeSegments = new();
    sealed class PairIgnoreCase : IEqualityComparer<(string Group, string Name)>
    {
        public static readonly PairIgnoreCase Instance = new();
        public bool Equals((string Group, string Name) x, (string Group, string Name) y) =>
            string.Equals(x.Group, y.Group, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string Group, string Name) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Group ?? ""),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name ?? ""));
    }

    FevCategory rootCategory;
    readonly List<string> groups = new();
    readonly Dictionary<string, List<FevEvent>> eventsByGroup = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<(string Group, string Name), FevEvent> eventsByGroupAndName = new(PairIgnoreCase.Instance);
    readonly Dictionary<string, List<FevEvent>> events = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Languages => languages;
    public int DefaultLanguageIndex => defaultLanguageIndex;
    public IReadOnlyList<string> Banks => banks;
    public IReadOnlyList<FevBank> BankTable => bankTable;
    public IReadOnlyList<SoundDef> Defs => defs;
    public IReadOnlyList<MusicParameterDef> MusicParameters => musicParameters;
    public IReadOnlyCollection<int> CueIds => cueToTheme.Keys;
    public IReadOnlyList<string> Groups => groups;
    public int EventCount => eventsByGroupAndName.Count;
    /// <summary>The project's event category tree (the root is FMOD's "master").</summary>
    public FevCategory RootCategory => rootCategory;
    /// <summary>The category at <paramref name="path"/> ("npcVoice/npcMaidVoiceMonologue/Arita"), from the root's
    /// children down; null when a step names no category. Names compare as FMOD does, case-insensitively.</summary>
    public FevCategory FindCategory(string path)
    {
        if (rootCategory == null || string.IsNullOrEmpty(path)) return null;
        var c = rootCategory;
        int start = 0;
        while (start <= path.Length)
        {
            int end = path.IndexOf('/', start);
            if (end < 0) end = path.Length;
            var step = path.AsSpan(start, end - start);
            FevCategory next = null;
            foreach (var k in c.Children)
                if (step.Equals(k.Name, StringComparison.OrdinalIgnoreCase)) { next = k; break; }
            if (next == null) return null;
            c = next;
            start = end + 1;
        }
        return c;
    }

    /// <summary>Every event of the project, in group order.</summary>
    public IEnumerable<FevEvent> AllEvents => eventsByGroupAndName.Values;
    public IEnumerable<string> EventNames => events.Keys;
    public IReadOnlyList<FevEvent> EventsNamed(string name) => events.TryGetValue(name, out var l) ? l : Array.Empty<FevEvent>();

    public static FevProject Load(string path)
    {
        var p = new FevProject { d = File.ReadAllBytes(path) };
        p.Parse();
        p.d = null;
        return p;
    }

    /// <summary>
    /// Maps the client's voice language id (GetVoice(), localepath.cpp:400; 0 KR, 1 JP, 2 EN, 3 CN) to the FMOD project
    /// language name passed to EventSystem::setLanguage (CSoundService init @0x1417aea70, soundservice.cpp:418-433).
    /// </summary>
    public static string VoiceLanguageName(int voiceIndex) => voiceIndex switch
    {
        1 => "jp",
        2 => "en",
        3 => "cn",
        _ => "Korea",
    };

    /// <summary>
    /// Sample name prefix stripped by CSoundService::BuildTagTable (@0x1417afb50, soundservice.cpp:752-764) and carried by
    /// non-Korean voice FSB sub-sounds (empty for Korean).
    /// </summary>
    public static string VoiceSamplePrefix(int voiceIndex) => voiceIndex switch
    {
        1 => "jp_",
        2 => "en_",
        3 => "cn_",
        _ => "",
    };

    /// <summary>
    /// Bank filename suffix from the FEV LANG + bank table for <paramref name="languageName"/> (e.g. "Korea" -> "_Kor_def",
    /// "en" -> "_en", "jp" -> "_jp", "cn" -> "_cn" for voice banks; "" for non-voice banks). Falls back to the project's
    /// default language index when the language name is absent.
    /// </summary>
    public string BankSuffix(string bankName, string languageName)
    {
        if (!banksByName.TryGetValue(bankName, out var bank) || bank.LanguageSuffixes.Length == 0) return "";
        int li = FindLanguageIndex(languageName);
        if (li < 0 || li >= bank.LanguageSuffixes.Length)
            li = defaultLanguageIndex >= 0 && defaultLanguageIndex < bank.LanguageSuffixes.Length ? defaultLanguageIndex : 0;
        return bank.LanguageSuffixes[li] ?? "";
    }

    /// <summary>The FSB file name for a bank in a project language (e.g. "MS2Sound_Voice_en.fsb").</summary>
    public string BankFileName(string bankName, string languageName) => bankName + BankSuffix(bankName, languageName) + ".fsb";

    /// <summary>The FSB file name for a bank given the client's voice index (0 KR, 1 JP, 2 EN, 3 CN).</summary>
    public string BankFileNameForVoice(string bankName, int voiceIndex) => BankFileName(bankName, VoiceLanguageName(voiceIndex));

    int FindLanguageIndex(string languageName)
    {
        if (string.IsNullOrEmpty(languageName)) return -1;
        for (int i = 0; i < languages.Count; i++)
            if (string.Equals(languages[i], languageName, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    int U32(int o) => BitConverter.ToInt32(d, o);
    uint U32U(int o) => BitConverter.ToUInt32(d, o);
    ushort U16(int o) => BitConverter.ToUInt16(d, o);
    float F32(int o) => BitConverter.ToSingle(d, o);

    void Parse()
    {
        if (Encoding.ASCII.GetString(d, 0, 4) != "RIFF" || Encoding.ASCII.GetString(d, 8, 4) != "FEV ")
            throw new InvalidDataException("not an FEV file");
        int strrOff = -1, obct = -1, langOff = -1;
        int o = 36;   // inside LIST PROJ (RIFF 12, FMT 12, LIST header 12)
        int end = d.Length;
        while (o + 8 <= end)
        {
            string tag = Encoding.ASCII.GetString(d, o, 4);
            int size = U32(o + 4);
            if (tag == "LGCY") { lgcy = o + 8; lgcyEnd = o + 8 + size; }
            else if (tag == "STRR") strrOff = o + 8;
            else if (tag == "OBCT") obct = o + 8;
            else if (tag == "LANG") langOff = o + 8;
            o += 8 + size + (size & 1);
        }
        if (lgcy == 0 || strrOff < 0) throw new InvalidDataException("FEV without LGCY / STRR");
        ReadStrings(strrOff);
        if (langOff >= 0) ReadLanguages(langOff);
        int afterBanks = ReadBanks();
        int defCount = ObjectCount(obct, 17);
        ReadDefs(defCount, out int defsEnd);
        ReadMusic(defsEnd);
        int groupsStart = ReadCategoryTree(afterBanks, null);
        ReadEventGroups(groupsStart);
    }

    void ReadStrings(int o)
    {
        int n = U32(o);
        strr = new string[n];
        int bas = o + 4 + 4 * n;
        for (int i = 0; i < n; i++)
        {
            int s = bas + U32(o + 4 + 4 * i);
            int e = Array.IndexOf(d, (byte)0, s);
            strr[i] = Encoding.UTF8.GetString(d, s, e - s);
        }
    }

    // LANG: u32 count, count x (u32 len, ascii+NUL), u32 defaultLanguageIndex.
    void ReadLanguages(int o)
    {
        int n = U32(o);
        int p = o + 4;
        for (int i = 0; i < n; i++)
        {
            int len = U32(p);
            languages.Add(len > 1 ? Encoding.ASCII.GetString(d, p + 4, len - 1) : "");
            p += 4 + len;
        }
        defaultLanguageIndex = U32(p);
    }

    // OBCT: object count, then (kind, count) pairs; kind 17 counts the sound definitions.
    int ObjectCount(int obct, int kind)
    {
        int n = U32(obct);
        for (int i = 0; i < n; i++)
            if (U32(obct + 4 + i * 8) == kind) return U32(obct + 8 + i * 8);
        throw new InvalidDataException("FEV: no sound definition count");
    }

    // LGCY header: 8 bytes, project name (u32 len + chars), u32 bankCount, u32 langCount, then per bank:
    // u32 flags, u32 maxStreams, langCount x (u64 hash, u32 suffixStrIndex), u32 nameLen + chars.
    int ReadBanks()
    {
        int o = lgcy + 8;
        int len = U32(o);
        projName = Encoding.ASCII.GetString(d, o + 4, len - 1);
        o += 4 + len;
        int count = U32(o);
        int langCount = U32(o + 4);
        int p = o + 8;
        for (int i = 0; i < count; i++)
        {
            uint flags = U32U(p);
            uint maxStreams = U32U(p + 4);
            p += 8;
            var suffixes = new string[langCount];
            for (int l = 0; l < langCount; l++)
            {
                int suffixIdx = U32(p + 8);
                suffixes[l] = suffixIdx >= 0 && suffixIdx < strr.Length ? strr[suffixIdx] : "";
                p += 12;
            }
            int nlen = U32(p);
            string name = Encoding.ASCII.GetString(d, p + 4, nlen - 1);
            p += 4 + nlen;
            var fb = new FevBank { Name = name, Flags = flags, MaxStreams = maxStreams, LanguageSuffixes = suffixes };
            banks.Add(name);
            bankTable.Add(fb);
            banksByName[name] = fb;
        }
        return p;
    }

    // Category tree immediately after the bank table: len-prefixed name, f32 volume, f32 pitch, u32 maxPlay, u32 steal, u32 nSub, sub...
    int ReadCategoryTree(int p, FevCategory parent)
    {
        int len = U32(p);
        var c = new FevCategory
        {
            Name = len > 1 ? Encoding.UTF8.GetString(d, p + 4, len - 1) : "",
            Volume = F32(p + 4 + len),
            Pitch = F32(p + 8 + len),
            Parent = parent,
        };
        if (parent == null) rootCategory = c; else parent.Children.Add(c);
        p += 4 + len + 16;
        int nSub = U32(p);
        p += 4;
        for (int i = 0; i < nSub; i++) p = ReadCategoryTree(p, c);
        return p;
    }

    int SkipUserProps(int p)
    {
        int n = U32(p);
        p += 4;
        for (int i = 0; i < n; i++)
        {
            int len = U32(p);
            p += 4 + len;
            int type = U32(p);
            p += 4;
            if (type == 0 || type == 1) p += 4;
            else if (type == 2) { int slen = U32(p); p += 4 + slen; }
            else throw new InvalidDataException($"FEV: unknown user property type {type}");
        }
        return p;
    }

    // EventGroup hierarchy: u32 numTopGroups, then recursively:
    // u32 nameIdx, userProps, u32 numSubGroups, u32 numEvents, subGroups[], events[].
    void ReadEventGroups(int p)
    {
        int topCount = U32(p);
        p += 4;
        for (int i = 0; i < topCount; i++)
            p = ParseGroup(p, projName);
    }

    int ParseGroup(int p, string prefix)
    {
        int nameIdx = U32(p);
        p += 4;
        string gname = strr[nameIdx];
        string gpath = string.IsNullOrEmpty(prefix) ? gname : prefix + "/" + gname;
        p = SkipUserProps(p);
        int nSub = U32(p);
        int nEv = U32(p + 4);
        p += 8;
        groups.Add(gpath);
        var groupList = new List<FevEvent>(nEv);
        eventsByGroup[gpath] = groupList;
        for (int i = 0; i < nSub; i++)
            p = ParseGroup(p, gpath);
        for (int i = 0; i < nEv; i++)
            p = ParseEvent(p, gpath, groupList);
        return p;
    }

    int ParseEvent(int p, string groupPath, List<FevEvent> groupList)
    {
        int start = p;
        int etype = U32(p);
        int nameIdx = U32(p + 4);
        string name = strr[nameIdx];
        float vol = F32(p + 24);
        if (!(vol >= 0 && vol <= 8)) vol = 1f;
        var ev = new FevEvent
        {
            Name = name,
            Group = groupPath,
            IsSimple = etype == 0x10,
            Volume = vol,
            Priority = U32(p + 0x28),
            MaxPlaybacks = U32(p + 0x2c),
            MaxPlaybacksBehavior = U32(p + 0x78),
        };
        if (etype == 8)
        {
            float a = F32(p + 24 + 36), b = F32(p + 24 + 44);
            if (a > 0 && b > 0 && a < 1e6f && b < 1e6f) { ev.MinDistance = Math.Min(a, b); ev.MaxDistance = Math.Max(a, b); }
        }
        p = start + 0xa8;
        if (etype == 0x10)
        {
            // Simple event: u32 flags, then one 54-byte sound entry (u32 defIdx, u16 flags, f32 volume, u32 at +14 the loop mode, ...).
            p += 4;
            AddEventDef(ev, U32(p), F32(p + 6), U32(p + 14));
            p += 54;
        }
        else if (etype == 8)
        {
            int nLayers = U32(p);
            p += 4;
            for (int l = 0; l < nLayers; l++)
            {
                int nSnd = U16(p + 6);
                int nEnv = U16(p + 8);
                p += 10;
                for (int s = 0; s < nSnd; s++)
                {
                    AddEventDef(ev, U32(p), F32(p + 6), U32(p + 14));   // the 58-byte sound instance, laid out as the simple one
                    p += 58;
                }
                for (int e = 0; e < nEnv; e++)
                {
                    int nPts = U32(p + 20);
                    p += 24 + 4 * nPts + 8;
                }
            }
            int nParams = U32(p);
            p += 4 + 32 * nParams;
        }
        else
        {
            throw new InvalidDataException($"FEV: unknown event type 0x{etype:x} in {groupPath}");
        }
        p = SkipUserProps(p);
        int nCats = U32(p);
        p += 4;
        string cat = "";
        for (int c = 0; c < nCats; c++)
        {
            int clen = U32(p);
            if (c == 0 && clen > 1) cat = Encoding.UTF8.GetString(d, p + 4, clen - 1);
            p += 4 + clen;
        }
        ev.Category = cat;
        groupList.Add(ev);
        eventsByGroupAndName[(groupPath, name)] = ev;
        if (!events.TryGetValue(ev.Name, out var list)) events[ev.Name] = list = new();
        list.Add(ev);
        return p;
    }

    /// <summary>A sound instance's loop mode (its u32 at +14): FMOD Designer's three, one-shot and the two loops. Every voice
    /// and hit sound of the KMS2 and GMS2 projects is a one-shot; the keep, remain, idle and loop sounds are 0 or 2 (2 only in
    /// layered events). A loop plays until the event stops, which is what FMOD's own lengthms of -1 reports.</summary>
    public const int LoopModeOneShot = 1;

    /// <summary>The instances read by loop mode: [0], [1], [2] for the three modes, [3] for any other value (counted as a loop).</summary>
    public readonly int[] LoopModeCounts = new int[4];

    void AddEventDef(FevEvent ev, int ord, float vol, int loopMode)
    {
        LoopModeCounts[loopMode is >= 0 and <= 2 ? loopMode : 3]++;
        if (ord < 0 || ord >= defs.Count || defs[ord].Entries.Count == 0) return;
        if (!(vol > 0 && vol <= 4)) vol = 1f;
        bool loops = loopMode != LoopModeOneShot;
        int at = ev.Defs.IndexOf(ord);
        if (at < 0) { ev.Defs.Add(ord); ev.DefVolumes.Add(vol); ev.DefLoops.Add(loops); }
        else if (loops) ev.DefLoops[at] = true;
    }

    bool PlainName(int o, out int len)
    {
        len = U32(o + 8);
        if (len < 2 || len > 400 || o + 12 + len > d.Length || d[o + 11 + len] != 0) return false;
        for (int i = 0; i < len - 1; i++) if (d[o + 12 + i] < 0x20 || d[o + 12 + i] > 0x7e) return false;
        return true;
    }

    // Sound definition table: count, then per definition (name index, mode, entry count) and its entries.
    // Entry: kind 0 = wave: (0, weight, length, name, bankIndex, sampleIndex, lengthMs); kind 2 = silence (2, ms);
    // kind 3 = programmer sound (3, weight).
    void ReadDefs(int count, out int end)
    {
        for (int start = lgcy; start + 8 < lgcyEnd; start++)
        {
            if (U32(start) != count) continue;
            if (TryDefs(start + 4, count, out end)) return;
            defs.Clear();
        }
        throw new InvalidDataException("FEV: sound definition table not found");
    }

    bool TryDefs(int p, int count, out int end)
    {
        end = 0;
        try
        {
            for (int i = 0; i < count; i++)
            {
                if (p + 12 > lgcyEnd) return false;
                var df = new SoundDef { NameIndex = U32(p), Mode = U32(p + 4) };
                int cnt = U32(p + 8); p += 12;
                if (df.NameIndex < 0 || df.NameIndex >= strr.Length || cnt < 0 || cnt > 20000) return false;
                for (int k = 0; k < cnt; k++)
                {
                    int kind = U32(p);
                    var e = new DefEntry { Kind = kind };
                    if (kind == 0)
                    {
                        e.Weight = U32(p + 4);
                        if (!PlainName(p, out int l)) return false;
                        e.Wave = Encoding.ASCII.GetString(d, p + 12, l - 1);
                        int q = p + 12 + l;
                        e.Type = U32(q); e.BankIndex = U32(q + 4); e.Length = U32(q + 8);
                        p = q + 12;
                    }
                    else if (kind == 2) { e.Length = U32(p + 4); p += 8; }
                    else if (kind == 3) { e.Weight = U32(p + 4); p += 8; }
                    else return false;
                    df.Entries.Add(e);
                }
                defs.Add(df);
            }
        }
        catch (ArgumentException) { return false; }
        end = p;
        return true;
    }

    // Interactive music container ('comp' chunk ending at lgcyEnd):
    // subchunks: 'sett', 'cues' ('entl'), 'prms' ('prmd' + 'entl'), 'lnks' ('lnk ' -> 'lnkd'+'cond'/'cprm', 'lfsd'),
    // 'tlns', 'sgms' ('sgmd' -> 'smp '), 'thms' ('thm ' -> 'thmd'), 'scns' ('scnd': cue id -> theme id).
    void ReadMusic(int from)
    {
        int comp = FindCompChunk(from);
        if (comp < 0) return;
        int compEnd = comp + U32(comp);
        var linksById = new Dictionary<int, MusicLink>();
        var lfsBySeg = new Dictionary<int, List<int>>();

        int p = comp + 8;
        while (p + 8 <= compEnd)
        {
            int sz = U32(p);
            if (sz < 8 || p + sz > compEnd) break;
            string tag = Encoding.ASCII.GetString(d, p + 4, 4);
            int subEnd = p + sz;
            if (tag == "cues") ReadMusicEntityListSection(p + 8, subEnd, (id, name) => cueNames[id] = name);
            else if (tag == "prms") ReadMusicEntityListSection(p + 8, subEnd, (id, name) => musicParameters.Add(new MusicParameterDef { Id = id, Name = name }));
            else if (tag == "lnks") ReadMusicLinks(p + 8, subEnd, linksById, lfsBySeg);
            else if (tag == "sgms") ReadMusicSegments(p + 8, subEnd);
            else if (tag == "thms") ReadMusicThemes(p + 8, subEnd);
            else if (tag == "scns") ReadMusicScenes(p + 8, subEnd);
            p += sz;
        }

        // Attach outgoing links to segments, and segments to themes.
        foreach (var (segId, linkIds) in lfsBySeg)
        {
            if (!segmentsById.TryGetValue(segId, out var seg)) continue;
            foreach (var lid in linkIds)
                if (linksById.TryGetValue(lid, out var link)) seg.OutgoingLinks.Add(link);
        }
        foreach (var l in themeSegments.Values) l.Sort((a, b) => a.Id.CompareTo(b.Id));
        foreach (var (tid, theme) in themesById)
            if (themeSegments.TryGetValue(tid, out var segs)) theme.Segments.AddRange(segs);
    }

    int FindCompChunk(int from)
    {
        int p = from;
        while (true)
        {
            int i = IndexOf("comp", p);
            if (i < 4) return -1;
            int sz = U32(i - 4);
            if (sz > 8 && i - 4 + sz == lgcyEnd) return i - 4;
            p = i + 4;
        }
    }

    void ReadMusicEntityListSection(int p, int end, Action<int, string> onEntry)
    {
        while (p + 8 <= end)
        {
            int sz = U32(p);
            if (sz < 8 || p + sz > end) break;
            if (Encoding.ASCII.GetString(d, p + 4, 4) == "entl")
            {
                int q = p + 8;
                int count = U16(q);
                int strOff = q + 4 + 4 * count;
                for (int i = 0; i < count; i++)
                {
                    int id = U32(q + 4 + 4 * i);
                    int nul = Array.IndexOf(d, (byte)0, strOff);
                    if (nul < 0 || nul > p + sz) break;
                    string name = Encoding.UTF8.GetString(d, strOff, nul - strOff);
                    strOff = nul + 1;
                    onEntry(id, name);
                }
            }
            p += sz;
        }
    }

    void ReadMusicLinks(int p, int end, Dictionary<int, MusicLink> linksById, Dictionary<int, List<int>> lfsBySeg)
    {
        while (p + 8 <= end)
        {
            int sz = U32(p);
            if (sz < 8 || p + sz > end) break;
            string tag = Encoding.ASCII.GetString(d, p + 4, 4);
            if (tag == "lnk ")
            {
                MusicLink link = null;
                int q = p + 8, qEnd = p + sz;
                while (q + 8 <= qEnd)
                {
                    int csz = U32(q);
                    if (csz < 8 || q + csz > qEnd) break;
                    string ctag = Encoding.ASCII.GetString(d, q + 4, 4);
                    if (ctag == "lnkd" && csz >= 20)
                    {
                        link = new MusicLink { Id = U32(q + 8), TargetSegmentId = U32(q + 12), TransitionType = U32(q + 16) };
                    }
                    else if (ctag == "cond" && link != null)
                    {
                        int r = q + 8, rEnd = q + csz;
                        while (r + 8 <= rEnd)
                        {
                            int rsz = U32(r);
                            if (rsz < 8 || r + rsz > rEnd) break;
                            if (Encoding.ASCII.GetString(d, r + 4, 4) == "cprm" && rsz >= 22)
                            {
                                link.Condition = new MusicCondition
                                {
                                    Op = U16(r + 8),
                                    ParamId = U32(r + 10),
                                    Value1 = F32(r + 14),
                                    Value2 = F32(r + 18),
                                };
                            }
                            r += rsz;
                        }
                    }
                    q += csz;
                }
                if (link != null) linksById[link.Id] = link;
            }
            else if (tag == "lfsd" && sz >= 14)
            {
                int segId = U32(p + 8);
                int count = U16(p + 12);
                var list = new List<int>(count);
                for (int i = 0; i < count && p + 14 + 4 * i + 4 <= p + sz; i++)
                    list.Add(U32(p + 14 + 4 * i));
                lfsBySeg[segId] = list;
            }
            p += sz;
        }
    }

    void ReadMusicSegments(int p, int end)
    {
        while (p + 8 <= end)
        {
            int sz = U32(p);
            if (sz < 8 || p + sz > end) break;
            if (Encoding.ASCII.GetString(d, p + 4, 4) == "sgmd" && sz >= 16)
            {
                int segId = U32(p + 8);
                int themeId = U32(p + 12);
                int k = IndexOf("smp ", p + 8, p + sz);
                if (k > 0)
                {
                    int l = U32(k + 4);
                    var seg = new MusicSegment
                    {
                        Id = segId,
                        ThemeId = themeId,
                        Bank = Encoding.ASCII.GetString(d, k + 8, l - 1),
                        Index = U32(k + 8 + l),
                    };
                    segmentsById[segId] = seg;
                    if (!themeSegments.TryGetValue(themeId, out var lst)) themeSegments[themeId] = lst = new();
                    lst.Add(seg);
                }
            }
            p += sz;
        }
    }

    void ReadMusicThemes(int p, int end)
    {
        while (p + 8 <= end)
        {
            int sz = U32(p);
            if (sz < 8 || p + sz > end) break;
            if (Encoding.ASCII.GetString(d, p + 4, 4) == "thm ")
            {
                int q = p + 8, qEnd = p + sz;
                while (q + 8 <= qEnd)
                {
                    int csz = U32(q);
                    if (csz < 8 || q + csz > qEnd) break;
                    if (Encoding.ASCII.GetString(d, q + 4, 4) == "thmd" && csz >= 27)
                    {
                        int tid = U32(q + 8);
                        int crossfadeMs = U32(q + 8 + 11);
                        int nStart = U16(q + 8 + 17);
                        int startSeg = nStart > 0 && q + 8 + 19 + 4 <= q + csz ? U32(q + 8 + 19) : 0;
                        themesById[tid] = new MusicTheme { Id = tid, CrossfadeMs = crossfadeMs, StartSegmentId = startSeg };
                    }
                    q += csz;
                }
            }
            p += sz;
        }
    }

    void ReadMusicScenes(int p, int end)
    {
        while (p + 8 <= end)
        {
            int sz = U32(p);
            if (sz < 8 || p + sz > end) break;
            if (Encoding.ASCII.GetString(d, p + 4, 4) == "scnd" && sz >= 14)
            {
                int n = U16(p + 12);
                for (int i = 0; i < n && p + 14 + 8 * i + 8 <= p + sz; i++)
                    cueToTheme[U32(p + 14 + 8 * i)] = U32(p + 18 + 8 * i);
            }
            p += sz;
        }
    }

    int IndexOf(string tag, int from, int limit = -1)
    {
        if (limit < 0) limit = lgcyEnd;
        var t = Encoding.ASCII.GetBytes(tag);
        return d.AsSpan(from, limit - from).IndexOf(t) is var r && r >= 0 ? from + r : -1;
    }

    public MusicTheme GetMusicTheme(int themeId) => themesById.TryGetValue(themeId, out var t) ? t : null;
    public MusicSegment GetMusicSegment(int segmentId) => segmentsById.TryGetValue(segmentId, out var s) ? s : null;

    public MusicCue GetMusicCue(int cueId)
    {
        if (!cueToTheme.TryGetValue(cueId, out var themeId)) return null;
        themesById.TryGetValue(themeId, out var theme);
        var c = new MusicCue
        {
            Id = cueId,
            Name = cueNames.TryGetValue(cueId, out var nm) ? nm : "",
            Theme = themeId,
            ThemeInfo = theme,
            StartSegmentId = theme?.StartSegmentId ?? 0,
            CrossfadeMs = theme?.CrossfadeMs ?? 0,
        };
        if (themeSegments.TryGetValue(themeId, out var l)) c.Segments.AddRange(l);
        if (c.StartSegmentId == 0 && c.Segments.Count > 0) c.StartSegmentId = c.Segments[0].Id;
        return c;
    }

    /// <summary>
    /// Events directly in the given FMOD event group (exact match or '/'-bounded suffix match, case-insensitive;
    /// matches CSoundEventMgr::GetEventGroup @0x14177d6e0, soundeventmgr.cpp:1540).
    /// </summary>
    public IReadOnlyList<FevEvent> EventsInGroup(string groupPath)
    {
        var g = NormalizePath(groupPath);
        if (string.IsNullOrEmpty(g)) return Array.Empty<FevEvent>();
        if (eventsByGroup.TryGetValue(g, out var exact)) return exact;
        string suffix = "/" + g;
        foreach (var (path, list) in eventsByGroup)
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return list;
        return Array.Empty<FevEvent>();
    }

    /// <summary>
    /// Resolves an event by name and optional group path. Matches the client's full event group path first
    /// (CSoundEventMgr::GetEventGroup + EventGroup::getEvent / EventSystem::getEvent, soundeventmgr.cpp:940-1001), then a
    /// '/'-bounded group suffix (e.g. "ko/Npc" -> "MS2Sound/Sound/Voice/ko/Npc", "Skill/Berserker" ->
    /// "MS2Sound/Sound/Skill/Berserker"), and falls back to category only when no group path matches.
    /// </summary>
    public FevEvent FindEvent(string name, string group = null)
    {
        if (string.IsNullOrEmpty(name)) return null;
        name = name.Replace('\\', '/').Trim('/');
        if (group == null)
        {
            int cut = name.LastIndexOf('/');
            if (cut >= 0) { group = name[..cut]; name = name[(cut + 1)..]; }
        }
        group = NormalizePath(group);
        if (!string.IsNullOrEmpty(group))
        {
            if (eventsByGroupAndName.TryGetValue((group, name), out var exact)) return exact;
            if (!events.TryGetValue(name, out var candidates)) return null;
            string suffix = "/" + group;
            foreach (var ev in candidates)
                if (ev.Group.Equals(group, StringComparison.OrdinalIgnoreCase) || ev.Group.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return ev;
            // Fallback for callers that pass an informal category hint rather than an FEV group path.
            string want = CategoryHint(group);
            return candidates.FirstOrDefault(e => e.Category.StartsWith(want, StringComparison.OrdinalIgnoreCase)) ?? candidates[0];
        }
        if (!events.TryGetValue(name, out var l)) return null;
        if (l.Count == 1) return l[0];
        return l.FirstOrDefault(e => e.Category.StartsWith("effect", StringComparison.OrdinalIgnoreCase)) ?? l[0];
    }

    static string NormalizePath(string path) => string.IsNullOrEmpty(path) ? "" : path.Replace('\\', '/').Trim('/');

    static string CategoryHint(string group)
    {
        var g = group.ToLowerInvariant();
        if (g.EndsWith("/system") || g == "system") return "UISound";
        if (g.Contains("/voice") || g == "voice") return "npcVoice";
        if (g.EndsWith("/amb") || g.Contains("/amb/") || g == "amb") return "environment";
        return "effect";
    }
}
