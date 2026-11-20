using System.Xml.Linq;

namespace Ms2.Audio;

/// <summary>
/// table/systemsound.xml: a UI sound key (button_click, dialog_open, dialog_close, ...) to the sound event the client plays
/// (SystemSoundProperty: group + soundID).
/// </summary>
public sealed class SystemSoundTable
{
    readonly Dictionary<string, (string group, string[] ids)> map = new(StringComparer.OrdinalIgnoreCase);

    public static SystemSoundTable Parse(string xml)
    {
        var t = new SystemSoundTable();
        foreach (var e in XDocument.Parse(xml).Root.Elements())
        {
            if (!e.Name.LocalName.Equals("systemSound", StringComparison.OrdinalIgnoreCase)) continue;   // the file spells one "systemSOund"
            var key = (string)e.Attribute("key");
            if (key == null) continue;
            var ids = ((string)e.Attribute("soundID") ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            t.map[key] = ((string)e.Attribute("group") ?? "", ids);
        }
        return t;
    }

    public int Count => map.Count;

    /// <summary>The event path ("MS2Sound/Sound/system/02000002") of a key, or null when the key has no sound.</summary>
    public string Resolve(string key)
    {
        if (!map.TryGetValue(key, out var v) || v.ids.Length == 0) return null;
        return v.group + "/" + v.ids[0];
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
