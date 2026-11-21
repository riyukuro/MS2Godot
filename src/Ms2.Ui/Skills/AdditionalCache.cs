using System.Security.Cryptography;
using System.Text;
using Ms2.Formats.Pack;

namespace Ms2.Ui.Skills;

/// <summary>
/// The per-install cache of the additional effect tables: every additional/*.xml effect (<see cref="AdditionalData"/>, feature
/// selected) and every language's stringAdditionalDescription.xml rows, parsed once from the player's Xml.m2d and written
/// next to the UI code (the game's user://uigen/KEY/). The file name carries the Xml.m2d hash and <see cref="Version"/>, so
/// other client data or another parse gets another file. It is read whole on the first lookup and each id is decoded when it
/// is first asked for; a file that fails its checks is not used (the tables then parse the XML on first use) and the next
/// setup step writes it again. Nothing of it is in the repo.
/// </summary>
public sealed class AdditionalCache
{
    /// <summary>The format and parse version: bump it with any change to what the build writes or how the XML is read.</summary>
    public const int Version = 4;     // 2: the levels' EventBuffType; 3: PvpOnOff; 4: ForceApplyTarget, Hide
    const uint Magic = 0x4441324D;     // "M2AD"
    const uint EndMagic = 0x444E4541;  // "AEND"

    readonly string path;
    byte[] bytes;
    bool opened, bad;
    Index effects;
    readonly Dictionary<string, Index> descs = new(StringComparer.Ordinal);

    readonly struct Index
    {
        public readonly uint[] Ids;
        public readonly int[] Offsets;
        public Index(uint[] ids, int[] offsets) { Ids = ids; Offsets = offsets; }
        public int Find(uint id) => Ids == null ? -1 : Array.BinarySearch(Ids, id);
    }

    AdditionalCache(string path) => this.path = path;

    /// <summary>The cache at <paramref name="path"/>, null without the file. The bytes are read on first use.</summary>
    public static AdditionalCache Open(string path) => path != null && File.Exists(path) ? new AdditionalCache(path) : null;

    /// <summary>The cache file name for an Xml.m2d key (<see cref="XmlKey"/>).</summary>
    public static string FileName(string xmlKey) => $"additional-{xmlKey}-v{Version}.bin";

    /// <summary>
    /// The Xml.m2d key: the first 16 hex digits of its SHA-1. <paramref name="stampFile"/> keeps the hash with the file's
    /// size and write time; with a stale or missing stamp the file is hashed again when <paramref name="hash"/> is set (the
    /// setup step), else the key is null (the game: no cache until the next setup).
    /// </summary>
    public static string XmlKey(string dataDir, string stampFile, bool hash)
    {
        var xml = Path.Combine(dataDir, "Xml.m2d");
        var fi = new FileInfo(xml);
        if (!fi.Exists) return null;
        string stamp = $"{fi.Length} {fi.LastWriteTimeUtc.Ticks}";
        if (File.Exists(stampFile) && File.ReadAllText(stampFile).Split('\n') is { Length: >= 2 } l && l[0] == stamp) return l[1].Trim()[..16];
        if (!hash) return null;
        string h;
        using (var s = File.OpenRead(xml)) h = Convert.ToHexString(SHA1.HashData(s)).ToLowerInvariant();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(stampFile)));
        File.WriteAllText(stampFile, stamp + "\n" + h + "\n");
        return h[..16];
    }

    /// <summary>
    /// Parses the tables of <paramref name="packs"/> and writes <paramref name="path"/> (through a temporary file, so a
    /// stopped build leaves no partial cache). The effects are read as the client does, every file in name order (a later
    /// row with the same id replaces the earlier one).
    /// </summary>
    public static void Build(PackSet packs, string path)
    {
        var table = GameTables.NewAdditionalTable(packs);
        var all = table.LoadAll().OrderBy(d => d.Id).ToList();
        var langs = packs.List("Xml").Select(n => n.Split('/'))
            .Where(p => p.Length == 3 && p[0].Equals("string", StringComparison.OrdinalIgnoreCase)
                        && p[2].Equals("stringadditionaldescription.xml", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[1].ToLowerInvariant()).Distinct().OrderBy(l => l, StringComparer.Ordinal).ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        string tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
        using (var w = new BinaryWriter(fs, Encoding.UTF8))
        {
            w.Write(Magic);
            w.Write(Version);
            WriteSection(w, all.Select(d => (d.Id, (Action<BinaryWriter>)(bw => WriteEffect(bw, d)))).ToList());
            w.Write(langs.Count);
            foreach (var lang in langs)
            {
                w.Write(lang);
                var file = GameTables.LevelDescFile(packs, lang) ?? new();
                WriteSection(w, file.OrderBy(kv => kv.Key).Select(kv => (kv.Key, (Action<BinaryWriter>)(bw => WriteDesc(bw, kv.Value)))).ToList());
            }
            w.Write(EndMagic);
        }
        File.Move(tmp, path, true);
    }

    // a section: the count, the sorted ids, each entry's offset from the section's data start, the data length, then the data
    static void WriteSection(BinaryWriter w, List<(uint Id, Action<BinaryWriter> Write)> rows)
    {
        var data = new MemoryStream();
        var dw = new BinaryWriter(data, Encoding.UTF8);
        var offsets = new int[rows.Count];
        for (int i = 0; i < rows.Count; i++) { offsets[i] = (int)data.Position; rows[i].Write(dw); }
        dw.Flush();
        w.Write(rows.Count);
        foreach (var r in rows) w.Write(r.Id);
        foreach (var o in offsets) w.Write(o);
        w.Write((int)data.Length);
        data.WriteTo(w.BaseStream);
    }

    static void WriteEffect(BinaryWriter w, AdditionalData d)
    {
        w.Write(d.MainType);
        w.Write(d.IsDebuff);
        w.Write(d.Levels.Count);
        foreach (var l in d.Levels)
        {
            w.Write(l.Level);
            w.Write(l.StringParam);
            w.Write(l.DurationTick);
            w.Write(l.KeepCondition);
            w.Write(l.IconOnOff);
            w.Write(l.Icon);
            w.Write(l.Priority);
            w.Write(l.IsHideCooltime);
            w.Write(l.EventBuffType);
            w.Write(l.PvpOnOff);
            w.Write(l.ForceApplyTarget);
            w.Write(l.Hide);
        }
    }

    static void WriteDesc(BinaryWriter w, SortedDictionary<ushort, Dictionary<string, string>> levels)
    {
        w.Write(levels.Count);
        foreach (var (lv, tags) in levels)
        {
            w.Write(lv);
            w.Write(tags.Count);
            foreach (var (t, v) in tags) { w.Write(t); w.Write(v); }
        }
    }

    /// <summary>The file is read and its checks passed (false: the caller parses the XML).</summary>
    public bool Valid
    {
        get
        {
            if (!opened)
            {
                opened = true;
                try { Load(); }
                catch (Exception) { bad = true; bytes = null; }
            }
            return !bad;
        }
    }

    void Load()
    {
        bytes = File.ReadAllBytes(path);
        var r = new BinaryReader(new MemoryStream(bytes, false), Encoding.UTF8);
        if (r.ReadUInt32() != Magic || r.ReadInt32() != Version) { bad = true; return; }
        effects = ReadIndex(r);
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            string lang = r.ReadString();
            descs[lang] = ReadIndex(r);
        }
        if (r.ReadUInt32() != EndMagic || r.BaseStream.Position != bytes.Length) bad = true;
    }

    static Index ReadIndex(BinaryReader r)
    {
        int n = r.ReadInt32();
        var ids = new uint[n];
        var offsets = new int[n];
        for (int i = 0; i < n; i++) ids[i] = r.ReadUInt32();
        for (int i = 0; i < n; i++) offsets[i] = r.ReadInt32();
        int len = r.ReadInt32();
        int start = (int)r.BaseStream.Position;
        if (len < 0 || start + len > r.BaseStream.Length) throw new InvalidDataException("additional cache: short section");
        for (int i = 0; i < n; i++)
        {
            if (offsets[i] < 0 || offsets[i] >= len) throw new InvalidDataException("additional cache: bad offset");
            offsets[i] += start;
        }
        r.BaseStream.Position = start + len;
        return new Index(ids, offsets);
    }

    BinaryReader At(int offset) => new(new MemoryStream(bytes, offset, bytes.Length - offset, false), Encoding.UTF8);

    /// <summary>The effect <paramref name="id"/> (null when the client data has none). Call only when <see cref="Valid"/>.</summary>
    public AdditionalData ReadEffect(uint id)
    {
        int i = effects.Find(id);
        if (i < 0) return null;
        var r = At(effects.Offsets[i]);
        var d = new AdditionalData { Id = id, MainType = r.ReadInt32(), IsDebuff = r.ReadBoolean() };
        int n = r.ReadInt32();
        for (int k = 0; k < n; k++)
            d.Levels.Add(new AdditionalLevelData
            {
                Level = r.ReadUInt16(), StringParam = r.ReadString(), DurationTick = r.ReadUInt32(), KeepCondition = r.ReadInt32(),
                IconOnOff = r.ReadInt32(), Icon = r.ReadString(), Priority = r.ReadInt32(), IsHideCooltime = r.ReadBoolean(),
                EventBuffType = r.ReadUInt32(), PvpOnOff = r.ReadBoolean(),
                ForceApplyTarget = r.ReadInt32(), Hide = r.ReadInt32(), Parent = d,
            });
        return d;
    }

    /// <summary>The <paramref name="lang"/> description rows of <paramref name="id"/> by level, null when that language's file
    /// has none. Call only when <see cref="Valid"/>.</summary>
    public SortedDictionary<ushort, Dictionary<string, string>> ReadDesc(string lang, uint id)
    {
        if (!descs.TryGetValue(lang, out var index)) return null;
        int i = index.Find(id);
        if (i < 0) return null;
        var r = At(index.Offsets[i]);
        var levels = new SortedDictionary<ushort, Dictionary<string, string>>();
        int n = r.ReadInt32();
        for (int k = 0; k < n; k++)
        {
            ushort lv = r.ReadUInt16();
            int tags = r.ReadInt32();
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int t = 0; t < tags; t++) d[r.ReadString()] = r.ReadString();
            levels[lv] = d;
        }
        return levels;
    }
}
