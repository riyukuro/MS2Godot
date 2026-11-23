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
    /// One table as id -> attribute (first language that has the id wins). In each language the table is
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
            if (id != null && !d.ContainsKey(id)) d[id] = r.GetAttribute(valueAttr);
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
    /// korFlash.xml rows as NiGFxLocalizeManager::ParseStringXml (nigfxlocalizemanager.cpp:171) reads them. A row whose locale
    /// attribute is set and differs (case-insensitive, whole name) from the client's locale is skipped; the feature attribute is
    /// not looked at. A row without the value attribute gets its own key as text (XmlGetAttr returns 1 for a missing attribute and
    /// the client then sets text = key); a row without an id keeps the previous row's key (the key variable is outside the loop);
    /// an empty id is added as an empty key. The "\\n" and "\\r" escapes of a present value become line breaks; the later row
    /// replaces an earlier one (SetAt). A key that is not in the map is the caller's: the C++ GetString gives "" for it, while the
    /// Flash translator shows the key itself.
    /// </summary>
    public static Dictionary<string, string> FlashKeys(byte[] xml, string locale)
    {
        var map = new Dictionary<string, string>();
        using var xr = System.Xml.XmlReader.Create(new MemoryStream(xml));
        string key = "";
        while (xr.Read())
        {
            if (xr.NodeType != System.Xml.XmlNodeType.Element || xr.Name != "key") continue;
            var rowLocale = xr.GetAttribute("locale");
            if (!string.IsNullOrEmpty(rowLocale) && !string.Equals(rowLocale, locale, StringComparison.OrdinalIgnoreCase)) continue;
            if (xr.GetAttribute("id") is { } id) key = id;
            var v = xr.GetAttribute("value");
            map[key] = v == null ? key : v.Replace("\\n", "\n").Replace("\\r", "\r");
        }
        return map;
    }

    public static XDocument Parse(byte[] data) => PackXml.Parse(data);
}
