using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

/// <summary>
/// The client's tunable constants (Common::Constants): Xml.m2d : table/constants.xml and table/colorconstants.xml
/// (&lt;v key value/&gt;), which override the defaults compiled into the client (passed in by the caller).
/// </summary>
public sealed class ClientConstants
{
    readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Constants from key/value pairs (tests, tools).</summary>
    public ClientConstants(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        foreach (var (k, v) in pairs) values[k] = v;
    }

    /// <summary>
    /// Common::Constants::LoadFiles (unit_1402ab440_constantsblocks.cpp:1354) reads each file's &lt;ms2&gt; through
    /// FUN_14028faa0(root, "v", "key"): one row per key, picked by locale and feature, and sets each picked row's value.
    /// <paramref name="rows"/> is that pick (Ms2.Ui FeatureElements.Constants passes it).
    /// </summary>
    public ClientConstants(PackSet packs, Func<XElement, IEnumerable<XElement>> rows)
    {
        foreach (var entry in new[] { "table/constants.xml", "table/colorconstants.xml" })
        {
            if (!packs.Exists("Xml", entry)) continue;
            var root = GameStrings.Parse(packs.Read("Xml", entry)).Root;
            if (root == null || root.Name != "ms2") continue;
            foreach (var v in rows(root))
            {
                var k = (string)v.Attribute("key");
                if (k != null) values[k] = (string)v.Attribute("value");
            }
        }
    }

    /// <summary>A string constant (Value&lt;CString&gt;); <paramref name="def"/> when the tables do not set it.</summary>
    public string String(string key, string def) => values.TryGetValue(key, out var s) && s != null ? s : def;
    /// <summary>A constant's text; null when it is not in the tables.</summary>
    public string String(string key) => values.TryGetValue(key, out var s) ? s : null;

    /// <summary>An int constant (Value&lt;int&gt;, GetOr); <paramref name="def"/> when the tables do not set it or it is not an int.</summary>
    public int Int(string key, int def) =>
        values.TryGetValue(key, out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : def;

    public float Float(string key, float def) =>
        values.TryGetValue(key, out var s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : def;

    /// <summary>A bool constant (Value&lt;bool&gt;, GetOr): FUN_140718390 (packetlogger.cpp:180), true when the text holds any of
    /// "1oOtT"; <paramref name="def"/> when the tables do not set it.</summary>
    public bool Bool(string key, bool def) =>
        values.TryGetValue(key, out var s) && s != null ? s.IndexOfAny(BoolChars) >= 0 : def;
    static readonly char[] BoolChars = { '1', 'o', 'O', 't', 'T' };

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
