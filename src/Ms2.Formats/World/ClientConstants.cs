using System.Globalization;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

/// <summary>
/// The client's tunable constants (Common::Constants): Xml.m2d : table/constants.xml and table/colorconstants.xml
/// (&lt;v key value/&gt;), which override the defaults compiled into the client (passed in by the caller).
/// </summary>
public sealed class ClientConstants
{
    readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

    public ClientConstants(PackSet packs)
    {
        foreach (var entry in new[] { "table/constants.xml", "table/colorconstants.xml" })
        {
            if (!packs.Exists("Xml", entry)) continue;
            foreach (var v in GameStrings.Parse(packs.Read("Xml", entry)).Descendants("v"))
            {
                var loc = (string)v.Attribute("locale");
                if (!string.IsNullOrEmpty(loc) && loc != "KR") continue;
                var k = (string)v.Attribute("key");
                if (k != null) values[k] = (string)v.Attribute("value");
            }
        }
    }

    /// <summary>A string constant (Value&lt;CString&gt;); <paramref name="def"/> when the tables do not set it.</summary>
    public string String(string key, string def) => values.TryGetValue(key, out var s) && s != null ? s : def;
    /// <summary>A constant's text; null when it is not in the tables.</summary>
    public string String(string key) => values.TryGetValue(key, out var s) ? s : null;

    public float Float(string key, float def) =>
        values.TryGetValue(key, out var s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : def;

    /// <summary>A colour constant ("0xa2ff8c" or "8cafff") as r, g, b in 0..1.</summary>
    public (float r, float g, float b) Color(string key, (float, float, float) def)
    {
        if (!values.TryGetValue(key, out var s) || s == null) return def;
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint c)) return def;
        return (((c >> 16) & 0xff) / 255f, ((c >> 8) & 0xff) / 255f, (c & 0xff) / 255f);
    }
}
