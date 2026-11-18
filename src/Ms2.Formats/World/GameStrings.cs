using System.Xml.Linq;
using Ms2.Formats.Pack;

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
        var common = Table("stringcommon", "value");
        var keys = Table("keyname", "name");
        return id =>
        {
            if (!common.TryGetValue(id, out var s) || s == null) return null;
            return ReplaceKeys(s, keys).Replace("\\n", "\n");
        };
    }

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

    public static XDocument Parse(byte[] data) => XDocument.Parse(System.Text.Encoding.UTF8.GetString(data).TrimStart('﻿'));
}
