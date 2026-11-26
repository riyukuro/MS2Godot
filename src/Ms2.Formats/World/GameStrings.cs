using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;

namespace Ms2.Formats.World;

/// <summary>
/// The client's string tables (Xml.m2d : string/&lt;lang&gt;/*.xml). Tables are read in the preferred language first and filled
/// in from the others (en, then kr), as the player's data may hold only some of them translated.
/// </summary>
public sealed class GameStrings
{
    readonly PackSet packs;
    public readonly string[] Languages;

    public GameStrings(PackSet packs, params string[] languages)
    {
        this.packs = packs;
        Languages = (languages.Length > 0 ? languages : new[] { "en" }).Concat(new[] { "en", "kr" }).Distinct().ToArray();
    }

    /// <summary>The &lt;key&gt; rows (or other elements) of every table whose name starts with <paramref name="prefix"/>, preferred language first.</summary>
    public IEnumerable<XElement> Rows(string prefix, string element = "key")
    {
        foreach (var lang in Languages)
            foreach (var entry in packs.List("Xml").Where(n => n.StartsWith($"string/{lang}/{prefix}", StringComparison.OrdinalIgnoreCase)
                                                                 && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n))
            {
                XDocument x;
                try { x = Parse(packs.Read("Xml", entry)); } catch { continue; }
                foreach (var e in x.Descendants(element)) yield return e;
            }
    }

    /// <summary>
    /// One table as id -> attribute (the first language's text wins, but a localisation marker left where a language has
    /// no translation is filled in from the next language that has a real text). In each language the table is
    /// string/&lt;lang&gt;/&lt;name&gt;.xml and then its parts &lt;name&gt;_&lt;n&gt;.xml in number order: the GMS2 data
    /// keeps most skill names in skillname_&lt;job&gt;.xml next to a skillname.xml without them (no id in both). In the
    /// KMS data no table read here has such parts (only the kor*description tables, which are not), so its tables read
    /// as before. STILL-OPEN: the GMS2 exe is packed, so how its string table load finds the parts is not read; the
    /// order here only matters for an id in two files, which the data does not have.
    /// </summary>
    public Dictionary<string, string> Table(string name, string valueAttr, string idAttr = "id")
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var lang in Languages)
        {
            string entry = $"string/{lang}/{name}.xml";
            if (packs.Exists("Xml", entry)) AddRows(d, entry, valueAttr, idAttr);
            foreach (var part in Parts(lang, name)) AddRows(d, part, valueAttr, idAttr);
        }
        return d;
    }

    void AddRows(Dictionary<string, string> d, string entry, string valueAttr, string idAttr)
    {
        foreach (var r in Text.XmlRows.Elements(packs.Read("Xml", entry), "key"))    // Descendants("key"), streamed
        {
            var id = r.GetAttribute(idAttr);
            if (id == null) continue;
            string value = r.GetAttribute(valueAttr);
            if (!d.TryGetValue(id, out string had) || (IsUntranslated(had) && !IsUntranslated(value))) d[id] = value;
        }
    }

    Dictionary<string, List<(long N, string Entry)>> parts;   // "lang/name" (lower case) -> its parts, built on first use

    /// <summary>The entries string/&lt;lang&gt;/&lt;name&gt;_&lt;n&gt;.xml (n decimal digits) in the order of n; one scan of
    /// the archive's string entries finds the parts of every table.</summary>
    internal IReadOnlyList<string> Parts(string lang, string name)
    {
        if (parts == null)
        {
            var index = new Dictionary<string, List<(long N, string Entry)>>(StringComparer.Ordinal);
            foreach (var e in packs.List("Xml"))
            {
                if (!e.StartsWith("string/", StringComparison.OrdinalIgnoreCase) || !e.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
                int us = e.LastIndexOf('_'), dot = e.Length - 4;
                if (us < 0 || dot - us - 1 is < 1 or > 9 || e.IndexOf('/', 7) is var slash && (slash < 0 || slash > us)) continue;
                long n = 0;
                bool digits = true;
                for (int i = us + 1; i < dot && digits; i++)
                    if (e[i] is >= '0' and <= '9') n = n * 10 + (e[i] - '0'); else digits = false;
                if (!digits) continue;
                string key = e.Substring(7, us - 7).ToLowerInvariant();
                if (!index.TryGetValue(key, out var list)) index[key] = list = new();
                list.Add((n, e));
            }
            foreach (var list in index.Values) list.Sort((x, y) => x.N != y.N ? x.N.CompareTo(y.N) : string.CompareOrdinal(x.Entry, y.Entry));
            parts = index;
        }
        return parts.TryGetValue($"{lang}/{name}".ToLowerInvariant(), out var found) ? found.ConvertAll(p => p.Entry) : Array.Empty<string>();
    }

    /// <summary>Common UI strings (stringcommon.xml: key id -> value) with the client's $key:N$ action key names replaced.</summary>
    public Func<string, string> Common()
    {
        var (common, keys) = CommonTables();
        return Common(common, keys);
    }

    /// <summary>The tables <see cref="Common()"/> reads: stringcommon.xml (value) and keyname.xml (name).</summary>
    public (Dictionary<string, string> Common, Dictionary<string, string> Keys) CommonTables() =>
        (Table("stringcommon", "value"), Table("keyname", "name"));

    /// <summary><see cref="Common()"/> over its tables.</summary>
    public static Func<string, string> Common(Dictionary<string, string> common, Dictionary<string, string> keys) => id =>
    {
        if (!common.TryGetValue(id, out var s) || s == null) return null;
        return ReplaceKeys(s, keys).Replace("\\n", "\n");
    };

    /// <summary>"$key:57$" -> the key's name (keyname.xml, e.g. Space).</summary>
    public static string ReplaceKeys(string s, IReadOnlyDictionary<string, string> keys)
    {
        int i;
        while ((i = s.IndexOf("$key:", StringComparison.Ordinal)) >= 0)
        {
            int j = s.IndexOf('$', i + 5);
            if (j < 0) break;
            var id = s[(i + 5)..j];
            s = s[..i] + (keys.TryGetValue(id, out var n) ? n : id) + s[(j + 1)..];
        }
        return s;
    }

    /// <summary>
    /// The Flash translator's whole map, as NiGFxLocalizeManager::LoadStringTables (@0x1422eb940, nigfxlocalizemanager.cpp:105-134)
    /// fills it: korFlash.xml by "value" (loader 1, FUN_141628610), usmSubtitleText.xml by "name" (loader 2, LoadUsmSubtitleText),
    /// stringMovie.xml by "name" (loader 3, LoadStringMovie), each from the string folder (FUN_14071ae20, ms2memfile.cpp:543-566;
    /// set at ms2memfile.cpp:324-327). A later row replaces an earlier one. Loader 4 (flashDebug.xml, GetXmlDebugPath) is the
    /// developer layout's file and is not in the packed data. A file the data lacks is skipped (its loader returns false).
    /// </summary>
    public static Dictionary<string, string> FlashTable(PackSet packs, string language, string locale)
    {
        var map = new Dictionary<string, string>();
        foreach (var (file, attr) in FlashFiles)
            if (FindXml(packs, $"string/{language}/{file}") is { } entry)
                FlashKeys(packs.Read("Xml", entry), locale, attr, map);
        if (language != "kr" && FindXml(packs, "string/kr/korflash.xml") is { } kr)
            FillUntranslated(map, FlashKeys(packs.Read("Xml", kr), locale));
        return map;
    }

    /// <summary>
    /// A text the data's own localisation step left as its marker: "KORFLASH_&lt;KEY&gt;_VALUE" in the Flash tables (the other
    /// languages' korFlash.xml of the KMS2 data hold 122 of them, e.g. the limit break button, with the feature name after
    /// a ":[F]"), and "STRINGCOMMON_&lt;KEY&gt;_VALUE:[F]&lt;Feature&gt;" in stringcommon.xml (the en table's limit break label is
    /// one; the kr table's is real). The client never reads these as text: the game runs in the kr folder, where every row
    /// is real. <see cref="Table"/> takes the next language's text for a marker, as <see cref="FillUntranslated"/> does for
    /// the Flash tables. STILL-OPEN: what the retail client shows for them when its language is not KR is not in the decompile.
    /// </summary>
    public static bool IsUntranslated(string text) =>
        text != null && (text.StartsWith("KORFLASH_", StringComparison.Ordinal)
                         || (text.StartsWith("STRING", StringComparison.Ordinal) && text.Contains("_VALUE:[F]", StringComparison.Ordinal)));

    /// <summary>A row that is only the localisation marker takes the same key's text from <paramref name="fallback"/> (the kr folder's
    /// table: the data's own text, or the patch package's English), so a language without a translation shows words instead of the
    /// marker. A key the fallback lacks, or holds as a marker too, keeps what the language's table says.</summary>
    public static void FillUntranslated(Dictionary<string, string> map, IReadOnlyDictionary<string, string> fallback)
    {
        foreach (var key in map.Where(kv => IsUntranslated(kv.Value)).Select(kv => kv.Key).ToList())
            if (fallback.TryGetValue(key, out var text) && !IsUntranslated(text)) map[key] = text;
    }

    static readonly (string File, string Attr)[] FlashFiles =
        [("korflash.xml", "value"), ("usmsubtitletext.xml", "name"), ("stringmovie.xml", "name")];

    /// <summary>The Xml entry of a path, its case as the data has it (the client's file system ignores case).</summary>
    static string FindXml(PackSet packs, string path)
    {
        if (packs.Exists("Xml", path)) return path;
        foreach (var e in packs.List("Xml"))
            if (string.Equals(e, path, StringComparison.OrdinalIgnoreCase)) return e;
        return null;
    }

    /// <summary>
    /// korFlash.xml rows as NiGFxLocalizeManager::ParseStringXml (nigfxlocalizemanager.cpp:171) reads them. A row whose locale
    /// attribute is set and differs (case-insensitive, whole name) from the client's locale is skipped; the feature attribute is
    /// not looked at. A row without the value attribute gets its own key as text (XmlGetAttr returns 1 for a missing attribute and
    /// the client then sets text = key); a row without an id keeps the previous row's key (the key variable is outside the loop);
    /// an empty id is added as an empty key. The "\\n" and "\\r" escapes of a present value become line breaks; the later row
    /// replaces an earlier one (SetAt). A key that is not in the map is the caller's: the C++ GetString gives "" for it, while the
    /// Flash translator shows the key itself. The text attribute is "value" for korFlash.xml and "name" for usmSubtitleText.xml
    /// and stringMovie.xml (NiGFxLocalizeManager::LoadStringTables @0x1422eb940, nigfxlocalizemanager.cpp:112-134).
    /// </summary>
    public static Dictionary<string, string> FlashKeys(byte[] xml, string locale, string textAttribute = "value") =>
        FlashKeys(xml, locale, textAttribute, new());

    /// <summary>The same rows read with <paramref name="attr"/> as the text attribute, added to <paramref name="map"/> (AddString's
    /// SetAt: a later row replaces an earlier one).</summary>
    public static Dictionary<string, string> FlashKeys(byte[] xml, string locale, string attr, Dictionary<string, string> map)
    {
        using var xr = System.Xml.XmlReader.Create(new MemoryStream(xml));
        string key = "";
        while (xr.Read())
        {
            if (xr.NodeType != System.Xml.XmlNodeType.Element || xr.Name != "key") continue;
            var rowLocale = xr.GetAttribute("locale");
            if (!string.IsNullOrEmpty(rowLocale) && !string.Equals(rowLocale, locale, StringComparison.OrdinalIgnoreCase)) continue;
            if (xr.GetAttribute("id") is { } id) key = id;
            var v = xr.GetAttribute(attr);
            map[key] = v == null ? key : v.Replace("\\n", "\n").Replace("\\r", "\r");
        }
        return map;
    }

    public static XDocument Parse(byte[] data) => PackXml.Parse(data);
}
