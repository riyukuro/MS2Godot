using System.Text;

namespace Ms2.Audio;

/// <summary>One wave entry of a sound definition: the wave's file name in the FEV, its index in the FSB bank and its weight.</summary>
public sealed class DefEntry
{
    public int Kind;         // 0 wave, 2 silence, 3 programmer sound
    public int Weight = 100; // percent
    public string Wave;
    public int Type;
    public int BankIndex;    // index of the sound in its FSB bank
    public int Length;
}

public sealed class SoundDef
{
    public int NameIndex;
    public int Mode;
    public List<DefEntry> Entries = new();
}

/// <summary>A music segment: one sample of a bank.</summary>
public sealed class MusicSegment
{
    public int Id;
    public string Bank;      // e.g. "MS2Sound_BGM"
    public int Index;
}

/// <summary>A music cue (CSoundEventMgr::PlayMusic id) with the segments of its theme, ordered by segment id.</summary>
public sealed class MusicCue
{
    public int Id;
    public int Theme;
    public List<MusicSegment> Segments = new();
}

/// <summary>A sound event: its volume and the sound definitions it plays.</summary>
public sealed class FevEvent
{
    public string Name;
    public string Category;   // event category the record sits in: "effect", "environment", "UISound", "npcVoice/..."
    public float Volume = 1f;
    public float MinDistance, MaxDistance;   // 3D attenuation (0 when the record has none)
    public List<int> Defs = new();
    public List<float> DefVolumes = new();   // the sound's own volume (parallel to Defs)
}

/// <summary>
/// Reader of MS2Sound.fev (FMOD Designer 4.x project, version 0x45), the parts the client needs: bank names, sound
/// definitions (index = the value an event's sound refers to), events by name, and the music system's cue table.
/// The layout was worked out from the file; fields that are not read are skipped by anchors, see the comments.
/// </summary>
public sealed class FevProject
{
    byte[] d;
    int lgcy, lgcyEnd;
    string[] strr = Array.Empty<string>();
    readonly List<string> banks = new();
    readonly List<SoundDef> defs = new();
    readonly Dictionary<int, int> cueToTheme = new();
    readonly Dictionary<int, List<MusicSegment>> themeSegments = new();
    readonly Dictionary<string, List<FevEvent>> events = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Banks => banks;
    public IReadOnlyList<SoundDef> Defs => defs;
    public IReadOnlyCollection<int> CueIds => cueToTheme.Keys;
    public int EventCount => events.Values.Sum(l => l.Count);
    public IEnumerable<string> EventNames => events.Keys;
    public IReadOnlyList<FevEvent> EventsNamed(string name) => events.TryGetValue(name, out var l) ? l : Array.Empty<FevEvent>();

    public static FevProject Load(string path)
    {
        var p = new FevProject { d = File.ReadAllBytes(path) };
        p.Parse();
        p.d = null;
        return p;
    }

    int U32(int o) => BitConverter.ToInt32(d, o);

    void Parse()
    {
        if (Encoding.ASCII.GetString(d, 0, 4) != "RIFF" || Encoding.ASCII.GetString(d, 8, 4) != "FEV ")
            throw new InvalidDataException("not an FEV file");
        int strrOff = -1, obct = -1;
        int o = 36;   // inside LIST PROJ (RIFF 12, FMT 12, LIST header 12)
        int end = d.Length;
        while (o + 8 <= end)
        {
            string tag = Encoding.ASCII.GetString(d, o, 4);
            int size = U32(o + 4);
            if (tag == "LGCY") { lgcy = o + 8; lgcyEnd = o + 8 + size; }
            else if (tag == "STRR") strrOff = o + 8;
            else if (tag == "OBCT") obct = o + 8;
            o += 8 + size + (size & 1);
        }
        if (lgcy == 0 || strrOff < 0) throw new InvalidDataException("FEV without LGCY / STRR");
        ReadStrings(strrOff);
        ReadBanks();
        int defCount = ObjectCount(obct, 17);
        ReadDefs(defCount, out int defsEnd);
        ReadMusic(defsEnd);
        ReadEvents();
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

    // OBCT: object count, then (kind, count) pairs; kind 17 counts the sound definitions.
    int ObjectCount(int obct, int kind)
    {
        int n = U32(obct);
        for (int i = 0; i < n; i++)
            if (U32(obct + 4 + i * 8) == kind) return U32(obct + 8 + i * 8);
        throw new InvalidDataException("FEV: no sound definition count");
    }

    // LGCY: project name (length + chars), bank count, then per bank a length-prefixed name and its language table.
    void ReadBanks()
    {
        int o = lgcy + 8;
        int len = U32(o); o += 4 + len;
        // after the name: bank count; each bank: flags, 5 x (hash, ...) records and then the next name. Banks are found by
        // their names, which all start with the project name.
        string proj = Encoding.ASCII.GetString(d, lgcy + 12, len - 1);
        int count = U32(o);
        int p = o + 4;
        int limit = Math.Min(lgcyEnd, p + 4096);
        for (int i = p; i + 8 < limit; i++)
        {
            int l = U32(i);
            if (l < proj.Length + 1 || l > 64 || i + 4 + l > limit) continue;
            if (d[i + 3 + l] != 0) continue;
            var s = Encoding.ASCII.GetString(d, i + 4, l - 1);
            if (s.StartsWith(proj + "_", StringComparison.Ordinal) && IsPlain(s)) { banks.Add(s); i += 3 + l; }
        }
    }

    static bool IsPlain(string s) { foreach (var c in s) if (c < 0x20 || c > 0x7e) return false; return true; }

    bool PlainName(int o, out int len)
    {
        len = U32(o + 8);
        if (len < 2 || len > 400 || o + 12 + len > d.Length || d[o + 11 + len] != 0) return false;
        for (int i = 0; i < len - 1; i++) if (d[o + 12 + i] < 0x20 || d[o + 12 + i] > 0x7e) return false;
        return true;
    }

    // Sound definition table: count, then per definition (name index, mode, entry count) and its entries.
    // Entry: kind 0 = wave: (0, weight, length, name, type, bank index, length); kind 2 = silence (2, ms);
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

    int defsStart;

    bool TryDefs(int p, int count, out int end)
    {
        end = 0;
        defsStart = p - 4;
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

    // Music: after the definitions come tagged records [size][tag][...]. 'sgmd' = segment (id, theme, ..., 'smp ' = bank name +
    // sample index), 'scnd' = cue table (u32 1, u16 count, then count x (cue id, theme)).
    void ReadMusic(int from)
    {
        int sgms = IndexOf("sgms", from);
        int scnd = LastIndexOf("scnd");
        int thms = IndexOf("thms", sgms < 0 ? from : sgms);
        if (sgms < 0 || scnd < 0) return;
        int o = sgms;
        int stop = thms > 0 ? thms : scnd;
        while (true)
        {
            int i = IndexOf("sgmd", o);
            if (i < 0 || i > stop) break;
            int size = U32(i - 4);
            int segId = U32(i + 4), theme = U32(i + 8);
            int k = IndexOf("smp ", i);
            if (k > 0 && k < i + size)
            {
                int l = U32(k + 4);
                var seg = new MusicSegment { Id = segId, Bank = Encoding.ASCII.GetString(d, k + 8, l - 1), Index = U32(k + 8 + l) };
                if (!themeSegments.TryGetValue(theme, out var lst)) themeSegments[theme] = lst = new();
                lst.Add(seg);
            }
            o = i + 4;
        }
        foreach (var l in themeSegments.Values) l.Sort((a, b) => a.Id.CompareTo(b.Id));
        int a0 = scnd + 4;
        int n = BitConverter.ToUInt16(d, a0 + 4);
        for (int i = 0; i < n; i++) cueToTheme[U32(a0 + 6 + 8 * i)] = U32(a0 + 10 + 8 * i);
    }

    int IndexOf(string tag, int from)
    {
        var t = Encoding.ASCII.GetBytes(tag);
        return d.AsSpan(from, lgcyEnd - from).IndexOf(t) is var r && r >= 0 ? from + r : -1;
    }

    int LastIndexOf(string tag)
    {
        var t = Encoding.ASCII.GetBytes(tag);
        var r = d.AsSpan(lgcy, lgcyEnd - lgcy).LastIndexOf(t);
        return r >= 0 ? lgcy + r : -1;
    }

    // Events: a record starts with (flag 8 or 0x10, name index, guid[16], volume) in the area before the definitions; the flag
    // follows the event's category string (length-prefixed). The sound definitions an event plays are the u32 definition
    // indices that are preceded by (u16 4) or (u32 1) and followed by (u16 0, sound volume float), later in the record, up to the next event record. Only
    // events named by a decimal id are read (every event of the client is). The same name can occur in several groups.
    void ReadEvents()
    {
        var starts = new List<(int pos, string name)>();
        for (int i = lgcy + 8; i + 24 < defsStart; i++)
        {
            if ((d[i] != 8 && d[i] != 0x10) || d[i + 1] != 0 || d[i + 2] != 0 || d[i + 3] != 0 || d[i + 6] != 0 || d[i + 7] != 0) continue;
            int n = d[i + 4] | d[i + 5] << 8;
            if (n == 0 || n >= strr.Length) continue;
            var s = strr[n];
            if (s.Length < 6 || s.Length > 12) continue;
            bool digits = true;
            foreach (var c in s) if (c < '0' || c > '9') { digits = false; break; }
            if (digits) starts.Add((i, s));
        }
        for (int e = 0; e < starts.Count; e++)
        {
            int pos = starts[e].pos;
            int from = pos + 24 + 100;   // the volume block before the sound list holds 1.0 floats too
            int to = e + 1 < starts.Count ? starts[e + 1].pos : Math.Min(defsStart, pos + 4000);
            var ev = new FevEvent { Name = starts[e].name, Category = CategoryBefore(pos), Volume = BitConverter.ToSingle(d, pos + 24) };
            if (!(ev.Volume >= 0 && ev.Volume <= 8)) ev.Volume = 1f;
            if (d[pos] == 8)
            {
                float a = BitConverter.ToSingle(d, pos + 24 + 36), b = BitConverter.ToSingle(d, pos + 24 + 44);
                if (a > 0 && b > 0 && a < 1e6 && b < 1e6) { ev.MinDistance = Math.Min(a, b); ev.MaxDistance = Math.Max(a, b); }
            }
            for (int i = from; i + 10 <= to; i++)
            {
                if (d[i + 4] != 0 || d[i + 5] != 0) continue;
                if (!((d[i - 2] == 4 && d[i - 1] == 0) || U32(i - 4) == 1)) continue;   // layer sound (u16 4, index) or simple sound (1, index)
                float v = BitConverter.ToSingle(d, i + 6);
                if (!(v > 0 && v <= 4)) continue;
                int ord = U32(i);
                if (ord >= 0 && ord < defs.Count && defs[ord].Entries.Count > 0 && !ev.Defs.Contains(ord)) { ev.Defs.Add(ord); ev.DefVolumes.Add(v); }
            }
            if (ev.Defs.Count == 0) continue;
            if (!events.TryGetValue(ev.Name, out var l)) events[ev.Name] = l = new();
            l.Add(ev);
        }
    }

    string CategoryBefore(int flagPos)
    {
        if (flagPos < 4 || d[flagPos - 1] != 0) return "";
        for (int len = 2; len < 48; len++)
        {
            int o = flagPos - len - 4;
            if (o < lgcy || U32(o) != len) continue;
            bool ok = true;
            for (int k = 0; k < len - 1; k++) if (d[o + 4 + k] < 0x20 || d[o + 4 + k] > 0x7e) { ok = false; break; }
            if (ok) return Encoding.ASCII.GetString(d, o + 4, len - 1);
        }
        return "";
    }

    public MusicCue GetMusicCue(int cueId)
    {
        if (!cueToTheme.TryGetValue(cueId, out var theme)) return null;
        var c = new MusicCue { Id = cueId, Theme = theme };
        if (themeSegments.TryGetValue(theme, out var l)) c.Segments.AddRange(l);
        return c;
    }

    /// <summary>
    /// An event by name. Names repeat across groups, so a group path (e.g. "MS2Sound/Sound/system") picks the category the client
    /// puts that group's events in: system -> UISound, voice -> npcVoice, AMB -> environment, anything else -> the first
    /// "effect" event.
    /// </summary>
    public FevEvent FindEvent(string name, string group = null)
    {
        if (!events.TryGetValue(name, out var l)) return null;
        if (l.Count == 1) return l[0];
        string want = "effect";
        if (group != null)
        {
            var g = group.ToLowerInvariant();
            if (g.EndsWith("/system")) want = "UISound";
            else if (g.Contains("/voice")) want = "npcVoice";
            else if (g.EndsWith("/amb") || g.Contains("/amb/")) want = "environment";
        }
        return l.FirstOrDefault(e => e.Category.StartsWith(want, StringComparison.OrdinalIgnoreCase)) ?? l[0];
    }
}
