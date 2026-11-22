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

    /// <summary>One table as id -> attribute (first language that has the id wins).</summary>
    public Dictionary<string, string> Table(string name, string valueAttr, string idAttr = "id")
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var lang in Languages)
        {
            string entry = $"string/{lang}/{name}.xml";
            if (!packs.Exists("Xml", entry)) continue;
            foreach (var r in Text.XmlRows.Elements(packs.Read("Xml", entry), "key"))    // Descendants("key"), streamed
            {
                var id = r.GetAttribute(idAttr);
                if (id != null && !d.ContainsKey(id)) d[id] = r.GetAttribute(valueAttr);
            }
        }
        return d;
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
