using System.Xml.Linq;

namespace Ms2.Audio;

/// <summary>
/// table/systemSound.xml as SoundPropertyMgr::LoadSystemSound (@0x141792360, soundproperty.cpp:417) reads it: every element
/// from the first &lt;systemSound&gt; on (the loop takes each next sibling element, whatever its name; the file spells one
/// "systemSOund") is a <see cref="Property"/> stored under its "key" in a case-insensitive map, a later row replacing an
/// earlier one. A document whose root is not &lt;ms2&gt; gives no rows.
/// </summary>
public sealed class SystemSoundTable
{
    /// <summary>
    /// SystemSoundProperty (soundproperty.h:50), loaded by SystemSoundProperty::Load (@0x141790ae0, soundproperty.cpp:135):
    /// "soundID" split at ',' (XmlTokenList, the empty tokens skipped), "group", the bools "byGender" and "byJob"
    /// (QueryBoolAttribute), the unsigned "cooltimeID", and only when both bools are set, "jobSoundID": ';'-separated
    /// "job:male,female" entries, an entry kept only with exactly two parts and two names (SetAt, a later one replacing).
    /// </summary>
    public sealed class Property
    {
        public string Group = "";
        public bool ByGender, ByJob;
        public uint CooltimeId;
        public string[] Sounds = Array.Empty<string>();
        public readonly Dictionary<int, string[]> JobSounds = new();

        /// <summary>SystemSoundProperty::GetCategory @0x141790e00 (soundproperty.cpp:169): the group, with "/Male" for gender 0
        /// and "/Female" for gender 1 on a byGender row.</summary>
        public string Category(int gender)
        {
            if (ByGender)
            {
                if (gender == 0) return Group + "/Male";
                if (gender == 1) return Group + "/Female";
            }
            return Group;
        }

        /// <summary>
        /// SystemSoundProperty::GetSoundName @0x141790ed0 (soundproperty.cpp:182). The job sounds count only on a row that is
        /// byGender and byJob while the job sound is on (IsJobSoundDisabled @0x141260470: the KR region and the character
        /// option's disableJobSound turn it off): no job sounds at all gives no name; the job's pair gives its name of the
        /// gender (none for a gender of 2 or more); a job without a pair falls to the soundIDs. Then: no soundIDs gives no
        /// name; a byGender row gives the gender's soundID (none for a gender of 2 or more), another row the first.
        /// STILL-OPEN: a byGender row with one soundID and gender 1 indexes past the array, which AtlThrow(E_INVALIDARG)s in
        /// the exe; here it gives no name. No row of the KMS2 or GMS2 tables has that shape.
        /// </summary>
        public string SoundName(uint gender, int job, bool jobSoundDisabled)
        {
            bool byJob = ByJob && !jobSoundDisabled;
            if (ByGender && byJob)
            {
                if (JobSounds.Count == 0) return null;
                if (JobSounds.TryGetValue(job, out var names))
                    return gender >= 2 ? null : names[gender];
            }
            if (Sounds.Length == 0) return null;
            if (ByGender) return gender >= 2 || gender >= Sounds.Length ? null : Sounds[gender];
            return Sounds[0];
        }
    }

    readonly Dictionary<string, Property> map = new(StringComparer.OrdinalIgnoreCase);

    public int Count => map.Count;

    /// <summary>Every row by key.</summary>
    public IReadOnlyDictionary<string, Property> Rows => map;

    public static SystemSoundTable Parse(string xml)
    {
        var t = new SystemSoundTable();
        var root = XDocument.Parse(xml.TrimStart('\uFEFF')).Root;
        if (root == null || root.Name.LocalName != "ms2") return t;
        bool started = false;
        foreach (var e in root.Elements())
        {
            if (!started && e.Name.LocalName != "systemSound") continue;
            started = true;
            var p = new Property
            {
                Sounds = Split((string)e.Attribute("soundID"), ','),
                Group = (string)e.Attribute("group") ?? "",
                ByGender = Flag(e, "byGender"),
                ByJob = Flag(e, "byJob"),
                CooltimeId = uint.TryParse((string)e.Attribute("cooltimeID"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out uint c) ? c : 0,
            };
            if (p.ByJob && p.ByGender)
                foreach (var entry in Split((string)e.Attribute("jobSoundID"), ';'))
                {
                    var parts = Split(entry, ':');
                    if (parts.Length != 2) continue;
                    var names = Split(parts[1], ',');
                    if (names.Length != 2) continue;
                    p.JobSounds[Atoi(parts[0])] = names;
                }
            t.map[(string)e.Attribute("key") ?? ""] = p;
        }
        return t;
    }

    /// <summary>Unk_SplitA (achievedata.cpp, @0x14015f0b0): CString::Tokenize, the empty tokens skipped, nothing trimmed.</summary>
    static string[] Split(string s, char sep) => string.IsNullOrEmpty(s) ? Array.Empty<string>() : s.Split(sep, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>XmlGetAttr of a bool (tinyxmlhelper.cpp:141, QueryBoolAttribute, tinyxml.cpp:620): a value starting with
    /// "true", "yes" or "1" in any case is set; otherwise the property's false stays.</summary>
    static bool Flag(XElement e, string name) =>
        (string)e.Attribute(name) is { Length: > 0 } v && (v.StartsWith("true", StringComparison.OrdinalIgnoreCase) || v.StartsWith("yes", StringComparison.OrdinalIgnoreCase) || v.StartsWith('1'));

    /// <summary>atoi: the leading signed digits after white space, 0 without them.</summary>
    static int Atoi(string s)
    {
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) neg = s[i++] == '-';
        long v = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9' && v <= int.MaxValue) v = v * 10 + (s[i++] - '0');
        return (int)(neg ? -v : v);
    }

    /// <summary>
    /// SoundPropertyMgr::FindSystemSound @0x141792ee0 (soundproperty.cpp:648): the row of "arg_name", or of the name with no
    /// <paramref name="arg"/>. A missing "arg_name" gives no row even when the plain name has one (the exe tests the plain
    /// name and returns its still-empty pointer).
    /// </summary>
    public Property Find(string name, string arg = "")
    {
        if (map.Count == 0) return null;
        string key = string.IsNullOrEmpty(arg) ? name ?? "" : arg + "_" + name;
        if (key.Length == 0) return null;
        return map.TryGetValue(key, out var p) ? p : null;
    }

    /// <summary>
    /// CEffectService::PlaySystemSound @0x1411a5a60 (effectservice.cpp:1288) up to its play: the row's sound name for the
    /// local player's gender and job group (0 and 1 without a local player), none for an empty name or "0"; the event path
    /// is GetCategory(gender) + "/" + the name (FUN_1411a5110 lower-cases the category; the FEV lookup ignores case). The
    /// row's cooltime (CooltimeManager::Use) is the caller's, through <see cref="Find"/>'s CooltimeId.
    /// </summary>
    public string Resolve(string key, int gender = 0, int jobGroup = 1, bool jobSoundDisabled = false, string arg = "")
    {
        var p = Find(key, arg);
        var name = p?.SoundName(unchecked((uint)gender), jobGroup, jobSoundDisabled);
        if (string.IsNullOrEmpty(name) || name == "0") return null;
        return p.Category(gender) + "/" + name;
    }
}

/// <summary>
/// table/hitSound.xml (SoundPropertyMgr::LoadHitSound, soundproperty.cpp:513): each element from the first &lt;hitSound&gt; on
/// (the loop takes every next sibling element, whatever its name) adds its "soundID" to the list of its ("AttackMaterial",
/// "DefenceMeterial") pair; a missing attribute reads 0.
/// </summary>
public sealed class HitSoundTable
{
    readonly Dictionary<(int attack, int defence), List<int>> map = new();

    public static HitSoundTable Parse(string xml)
    {
        var t = new HitSoundTable();
        var root = XDocument.Parse(xml.TrimStart('\uFEFF')).Root;
        if (root == null || root.Name.LocalName != "ms2") return t;
        bool started = false;
        foreach (var e in root.Elements())
        {
            if (!started && e.Name.LocalName != "hitSound") continue;
            started = true;
            var key = (Int(e, "AttackMaterial"), Int(e, "DefenceMeterial"));
            if (!t.map.TryGetValue(key, out var ids)) t.map[key] = ids = new List<int>();
            ids.Add(Int(e, "soundID"));
        }
        return t;
    }

    static int Int(XElement e, string name) => int.TryParse((string)e.Attribute(name), out int v) ? v : 0;

    /// <summary>SoundPropertyMgr::GetHitSound @0x141792d70 (soundproperty.cpp:652): one of the pair's ids by
    /// <paramref name="rand"/> modulo their count, both unsigned; 0 for a pair without ids. The client's number is
    /// FUN_140767ff0, s_random.FUN_1407684c0: the three-register Tausworthe generator (Ms2.Quest.QuestRandom.Next), not the CRT rand().</summary>
    public int Get(int attack, int defence, Func<uint> rand)
    {
        if (!map.TryGetValue((attack, defence), out var ids) || ids.Count == 0) return 0;
        return ids[(int)(rand() % (uint)ids.Count)];
    }

    /// <summary>CEffectService::PlayHitSound @0x1411a5580 (effectservice.cpp:1355): the sound event of a hit sound id, in the
    /// category "ms2sound/sound/attack/hit" under its 8-digit name.</summary>
    public static string EventPath(int id) => "ms2sound/sound/attack/hit/" + id.ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// table/eventsound.xml (SoundPropertyMgr::LoadEventSound @0x141792740, soundproperty.cpp:591): each element from the first
/// &lt;eventSound&gt; on (the loop takes every next sibling element, whatever its name) with a non-empty "event" and a
/// "soundID" or "restoreSoundID" is stored as m_events[type][gender][job][onlyCasterMyPC], a later row replacing an earlier
/// one. The type is FUN_141793500's case-sensitive name match (<see cref="TypeOf"/>); an unknown name is skipped.
/// "onlyCasterMyPC" is set only by the exact text "true"; "group" is lower-cased; "job" and "gender" are XmlGetAttr ints
/// (QueryIntAttribute, 0 without a leading number).
/// <para>STILL-OPEN: the row's onlyCasterMyPC without the attribute is Unk_EventVoice's implicit ctor's (not recovered;
/// taken as false). Every row of the KMS2 and GMS2 tables sets it.</para>
/// </summary>
public sealed class EventSoundTable
{
    /// <summary>FUN_141793500's event types.</summary>
    public const int BossKill = 1, BossEndKill = 2, OtherPcKill = 3, MyPcResurrection = 4, MyPcDead = 5, SkillAction = 6;

    /// <summary>Unk_EventVoice (soundproperty.h:93).</summary>
    public readonly record struct Voice(bool OnlyCasterMyPC, string Group, string SoundId, string RestoreSoundId);

    readonly Dictionary<(int Type, int Gender, int Job, bool Caster), Voice> map = new();

    public int Count => map.Count;

    /// <summary>FUN_141793500: the CString == compares, 0 for another name.</summary>
    public static int TypeOf(string name) => name switch
    {
        "Boss_Kill" => BossKill,
        "Boss_EndKill" => BossEndKill,
        "otherPC_Kill" => OtherPcKill,
        "myPC_Resurrection" => MyPcResurrection,
        "myPC_Dead" => MyPcDead,
        "skill_action" => SkillAction,
        _ => 0,
    };

    /// <summary>
    /// StateSkill::TextKeyEvent's voice (@0x1417e7660, stateskill.cpp:834-841): a skill motion's text key containing
    /// "EventSound" (no case) names the event after its first 11 characters (CString::Mid(11), "EventSound/" being 11 long;
    /// empty for a shorter key), then <see cref="TypeOf"/>. 0 for no such key or no known event.
    /// </summary>
    public static int TextKeyType(string key)
    {
        if (key == null || key.IndexOf("EventSound", StringComparison.OrdinalIgnoreCase) < 0) return 0;
        return TypeOf(key.Length > 11 ? key[11..] : "");
    }

    public static EventSoundTable Parse(string xml)
    {
        var t = new EventSoundTable();
        var root = XDocument.Parse(xml.TrimStart('\uFEFF')).Root;
        if (root == null || root.Name.LocalName != "ms2") return t;
        bool started = false;
        foreach (var e in root.Elements())
        {
            if (!started && e.Name.LocalName != "eventSound") continue;
            started = true;
            string ev = (string)e.Attribute("event") ?? "", sound = (string)e.Attribute("soundID") ?? "", restore = (string)e.Attribute("restoreSoundID") ?? "";
            if (ev.Length == 0 || (sound.Length == 0 && restore.Length == 0)) continue;
            int type = TypeOf(ev);
            if (type == 0) continue;
            bool caster = (string)e.Attribute("onlyCasterMyPC") == "true";
            string group = ((string)e.Attribute("group") ?? "").ToLowerInvariant();
            t.map[(type, Query(e, "gender"), Query(e, "job"), caster)] = new Voice(caster, group, sound, restore);
        }
        return t;
    }

    /// <summary>QueryIntAttribute (sscanf "%d"): the leading signed digits after white space; 0 (the default) without them.</summary>
    static int Query(XElement e, string name)
    {
        if ((string)e.Attribute(name) is not { } s) return 0;
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) neg = s[i++] == '-';
        int start = i;
        long v = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') { v = v * 10 + (s[i++] - '0'); if (v > int.MaxValue + 1L) return 0; }
        if (i == start) return 0;
        return (int)(neg ? -v : v);
    }

    /// <summary>SoundPropertyMgr::GetEventVoice @0x141792e00 (soundproperty.cpp:662): m_events[type][the PC's GetGender][its
    /// GetJobGroupId][1 for the local player, else 0]; a missing entry is the default voice (empty names).</summary>
    public Voice Get(int type, int gender, int jobGroup, bool localPlayer) =>
        map.TryGetValue((type, gender, jobGroup, localPlayer), out var v) ? v : default;

    /// <summary>
    /// CEffectService::PlayEventVoice @0x1411a5770 (effectservice.cpp:1371) up to its FUN_1411a5110 call: the voice's
    /// restoreSoundID when the job sound is off (IsJobSoundDisabled @0x141260470: the KR region and the character option's
    /// disableJobSound), else its soundID; nothing for an empty name, or for an onlyCasterMyPC voice on an actor whose object
    /// kind (GetObjectKind) is not 1, the own PC. Returns the event path, "group/name", or null.
    /// </summary>
    public string VoicePath(int type, int gender, int jobGroup, bool localPlayer, int objectKind, bool jobSoundDisabled)
    {
        if (type == 0) return null;
        var v = Get(type, gender, jobGroup, localPlayer);
        string name = jobSoundDisabled ? v.RestoreSoundId : v.SoundId;
        if (string.IsNullOrEmpty(name)) return null;
        if (v.OnlyCasterMyPC && objectKind != 1) return null;
        return (v.Group ?? "") + "/" + name;
    }
}

/// <summary>
/// Footsteps as the client plays them (CGroundEffectDataManager::PlayEffects): the ground's material name (the entity property
/// MapAttribute: grass, rock, ...) selects groundeffect/&lt;ground&gt;.xml, the action (walk, run0, run1, land) selects an
/// &lt;action&gt;, and every &lt;effects&gt; block of it picks one effect file by percent; the effect file's soundEvent effects are
/// the sounds.
/// </summary>
public sealed class GroundEffectTable
{
    sealed class Block { public List<string> Files = new(); public List<int> Percents = new(); }

    readonly Func<string, string> readText;
    readonly Dictionary<string, Dictionary<string, List<Block>>> grounds = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, List<string>> effectSounds = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="readXmlPack">reads an Xml.m2d entry by its lower-case path (e.g. "groundeffect/grass.xml"); null when missing</param>
    public GroundEffectTable(Func<string, string> readXmlPack) { readText = readXmlPack; }

    Dictionary<string, List<Block>> Ground(string name)
    {
        if (grounds.TryGetValue(name, out var g)) return g;
        g = null;
        var text = readText("groundeffect/" + name.ToLowerInvariant() + ".xml");
        if (text != null)
        {
            g = new(StringComparer.OrdinalIgnoreCase);
            foreach (var a in XDocument.Parse(text.TrimStart('\uFEFF')).Root.Elements("action"))
            {
                var type = (string)a.Attribute("type");
                if (type == null) continue;
                var blocks = new List<Block>();
                foreach (var eff in a.Elements("effects"))
                {
                    var b = new Block();
                    foreach (var e in eff.Elements("effect"))
                    {
                        b.Files.Add((string)e.Attribute("filePath") ?? "");
                        b.Percents.Add((int?)e.Attribute("percent") ?? 0);
                    }
                    blocks.Add(b);
                }
                g[type] = blocks;
            }
        }
        grounds[name] = g;
        return g;
    }

    /// <summary>The sound event paths (group/event) of an effect file's soundEvent parts.</summary>
    public List<string> SoundsOf(string file)
    {
        if (string.IsNullOrEmpty(file)) return new();
        if (effectSounds.TryGetValue(file, out var l)) return l;
        l = new();
        var text = readText("effect/" + file.ToLowerInvariant().Replace('\\', '/'));
        if (text != null)
            foreach (var e in XDocument.Parse(text.TrimStart('\uFEFF')).Descendants("effect"))
                if (string.Equals((string)e.Attribute("type"), "soundEvent", StringComparison.OrdinalIgnoreCase) && e.Attribute("event") != null)
                    l.Add(((string)e.Attribute("group") ?? "") + "/" + (string)e.Attribute("event"));
        return effectSounds[file] = l;
    }

    /// <summary>The sound event paths a step of the given action plays on the given ground (falls back to "default" for an unknown ground).</summary>
    public List<string> Pick(string ground, string action, Random rng = null) => PickFiles(ground, action, rng).SelectMany(SoundsOf).ToList();

    /// <summary>
    /// CGroundEffectDataManager::GetEffectFiles: one effect file per &lt;effects&gt; block of the action, picked by percent (the
    /// visual one and the sound one, e.g. default_e_land.xml and default_s_land.xml), "default" for an unknown ground.
    /// </summary>
    public List<string> PickFiles(string ground, string action, Random rng = null)
    {
        rng ??= Random.Shared;
        var g = Ground(string.IsNullOrEmpty(ground) ? "default" : ground) ?? Ground("default");
        var result = new List<string>();
        if (g == null || !g.TryGetValue(action, out var blocks)) return result;
        foreach (var b in blocks)
        {
            if (b.Files.Count == 0) continue;
            int pick = 0;
            if (b.Files.Count > 1)
            {
                int total = b.Percents.Sum();
                if (total <= 0) continue;
                int r = rng.Next(total);
                for (; pick < b.Percents.Count - 1 && r >= b.Percents[pick]; pick++) r -= b.Percents[pick];
            }
            if (!string.IsNullOrEmpty(b.Files[pick])) result.Add(b.Files[pick]);
        }
        return result;
    }
}

/// <summary>One row of table/loginbgbgm.xml (LoginBgBgmData, commonfiles/loginbgbgmdata.cpp:63-72).</summary>
public sealed class LoginBgBgmEntry
{
    public string BgDay { get; }
    public uint Priority { get; }
    public IReadOnlyList<(int CueId, int ParamId)> Bgms { get; }

    public LoginBgBgmEntry(string bgDay, uint priority, IReadOnlyList<(int CueId, int ParamId)> bgms)
    {
        BgDay = bgDay;
        Priority = priority;
        Bgms = bgms;
    }
}

/// <summary>
/// Port of LoginBgBgmTable (commonfiles/loginbgbgmdata.cpp:75-225; Load @0x140a72170, SelectCurrent @0x140a729e0,
/// GetBgDay @0x140a72cc0, GetBgm @0x140a72d70): selects the login screen's monthly background movie and BGM cue/param
/// pair by highest priority with random tie-break. Called by WorldService::LoadMap(1) (worldservice_1.cpp:3309-3313)
/// and read by CUIService::LoadBackgroundMovie (uiservice.cpp:509-518) and WorldService::FUN_141bc7b80
/// (worldservice_1.cpp:4856-4865).
/// </summary>
public sealed class LoginBgBgmTable
{
    readonly List<LoginBgBgmEntry>[] byMonth;

    public LoginBgBgmEntry Current { get; private set; }

    public LoginBgBgmTable()
    {
        // LoginBgBgmTable ctor (@0x140a71f90, loginbgbgmdata.cpp:78-81): buckets 0..12
        byMonth = new List<LoginBgBgmEntry>[13];
        for (int i = 0; i <= 12; i++) byMonth[i] = new List<LoginBgBgmEntry>();
    }

    public IReadOnlyList<LoginBgBgmEntry> RowsForMonth(int month) =>
        month >= 0 && month <= 12 ? byMonth[month] : Array.Empty<LoginBgBgmEntry>();

    public static LoginBgBgmTable Parse(string xml) =>
        string.IsNullOrEmpty(xml) ? new LoginBgBgmTable() : Parse(XDocument.Parse(xml.TrimStart('\uFEFF')));

    /// <summary>LoginBgBgmTable::Load (@0x140a72170, loginbgbgmdata.cpp:91-157).</summary>
    public static LoginBgBgmTable Parse(XDocument doc)
    {
        var table = new LoginBgBgmTable();
        if (doc?.Root == null) return table;
        foreach (var e in doc.Root.Elements("login"))
        {
            string strMonth = ((string)e.Attribute("month") ?? "").Trim();
            if (strMonth.Length == 0) continue;
            string strBgDay = ((string)e.Attribute("clientPropertyBgDay") ?? "").Trim();
            if (strBgDay.Length == 0) continue;
            string strBgmId = ((string)e.Attribute("bgmID") ?? "").Trim();
            if (strBgmId.Length == 0) continue;

            var months = SplitUInt(strMonth);
            var bgmIds = SplitUInt(strBgmId);
            if (bgmIds.Count == 0) continue;
            var bgmParams = SplitUInt((string)e.Attribute("bgmParam") ?? "");
            // loginbgbgmdata.cpp:128-135: pad missing bgmParam entries with 5 (MusicIntermission)
            while (bgmParams.Count < bgmIds.Count) bgmParams.Add(MusicController.IntermissionParamId);

            uint.TryParse((string)e.Attribute("priority"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out uint priority);
            var pairs = new (int CueId, int ParamId)[bgmIds.Count];
            for (int k = 0; k < bgmIds.Count; k++)
                pairs[k] = ((int)bgmIds[k], (int)bgmParams[k]);

            var entry = new LoginBgBgmEntry(strBgDay, priority, pairs);
            for (int i = 0; i < months.Count; i++)
            {
                uint m = months[i];
                if (m > 12) continue;   // loginbgbgmdata.cpp:146
                table.byMonth[m].Add(entry);
            }
        }
        return table;
    }

    static List<uint> SplitUInt(string text)
    {
        var list = new List<uint>();
        if (string.IsNullOrEmpty(text)) return list;
        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < parts.Length; i++)
            if (uint.TryParse(parts[i], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out uint v))
                list.Add(v);
        return list;
    }

    /// <summary>
    /// Computes the calendar month (1..12) from server time and timezone, matching LoginBgBgmTable::SelectCurrent
    /// (@0x140a729e0, commonfiles/loginbgbgmdata.cpp:164-177): when CMsTime::IsTimeZoneSet() is true, uses
    /// CMsTime::GetLocalTm(GetServerTime()) (unixSeconds - calcZoneOffset in UTC); otherwise uses local time of
    /// GetServerTime().
    /// </summary>
    public static int MonthForServerTime(long serverUnixSeconds, bool timeZoneSet, long calcZoneOffset) =>
        timeZoneSet
            ? DateTimeOffset.FromUnixTimeSeconds(serverUnixSeconds - calcZoneOffset).UtcDateTime.Month
            : DateTimeOffset.FromUnixTimeSeconds(serverUnixSeconds).ToLocalTime().Month;

    /// <summary>LoginBgBgmTable::SelectCurrent (@0x140a729e0, loginbgbgmdata.cpp:161-199).</summary>
    public bool SelectCurrent(int month, Func<int, int> pick = null)
    {
        Current = null;
        if (month <= 0 || month > 12) return false;
        var list = byMonth[month];
        if (list.Count == 0) return false;
        uint top = 0;
        for (int i = 0; i < list.Count; i++)
            if (list[i].Priority > top) top = list[i].Priority;
        var candidates = new List<LoginBgBgmEntry>();
        for (int i = 0; i < list.Count; i++)
            if (list[i].Priority == top) candidates.Add(list[i]);
        if (candidates.Count == 0) return false;
        int idx = pick != null ? Math.Clamp(pick(candidates.Count), 0, candidates.Count - 1) : Random.Shared.Next(candidates.Count);
        Current = candidates[idx];
        return true;
    }

    /// <summary>LoginBgBgmTable::GetBgDay (@0x140a72cc0, loginbgbgmdata.cpp:203-209).</summary>
    public string GetBgDay() => Current?.BgDay ?? "";

    /// <summary>LoginBgBgmTable::GetBgm (@0x140a72d70, loginbgbgmdata.cpp:213-224).</summary>
    public (int CueId, int ParamId) GetBgm(Func<int, int> pick = null)
    {
        if (Current == null || Current.Bgms.Count == 0) return (0, 0);
        int idx = pick != null ? Math.Clamp(pick(Current.Bgms.Count), 0, Current.Bgms.Count - 1) : Random.Shared.Next(Current.Bgms.Count);
        return Current.Bgms[idx];
    }

    /// <summary>
    /// Resolves the (cueId, paramId, paramValue) for the login field (fieldId == 1) per WorldService::FUN_141bc7b80
    /// (@0x141bc7b80, worldservice_1.cpp:4856-4872): uses LoginBgBgmTable::GetBgm() with map 1's BgmValue when CueId != 0,
    /// otherwise falls back to map 1's own FieldBgmSpec.
    /// </summary>
    public (int CueId, int ParamId, float ParamValue) ResolveLoginBgm(FieldBgmSpec map1Fallback, Func<int, int> pick = null)
    {
        var bgm = GetBgm(pick);
        if (bgm.CueId != 0) return (bgm.CueId, bgm.ParamId, map1Fallback.BgmValue);
        return (map1Fallback.BgmId, map1Fallback.BgmParam, map1Fallback.BgmValue);
    }
}
