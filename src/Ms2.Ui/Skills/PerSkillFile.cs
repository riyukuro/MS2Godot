using System.Globalization;
namespace Ms2.Ui.Skills;

/// <summary>The recorded GMS2 client's per-skill data files.</summary>
public static class PerSkillFile
{
    /// <summary>The skill id of a per-skill file name (skill/&lt;nn&gt;/&lt;id&gt;.xml); null for another entry.</summary>
    public static string Id(string entry)
    {
        if (!entry.StartsWith("skill/", StringComparison.OrdinalIgnoreCase) || !entry.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) return null;
        int slash = entry.IndexOf('/', 6);
        if (slash < 0 || entry.IndexOf('/', slash + 1) >= 0) return null;
        string id = entry.Substring(slash + 1, entry.Length - slash - 5);
        return id.Length > 0 && id.All(char.IsAsciiDigit) ? id : null;
    }

    /// <summary>A per-skill file's text as the skilldata files hold a skill: its root &lt;ms2&gt; (basic, level...) becomes
    /// &lt;skill id="..."&gt; inside a &lt;ms2&gt; of its own; a skilldata file is returned as it is.</summary>
    public static string Wrap(string entry, string xml)
    {
        string id = Id(entry);
        if (id == null) return xml;
        if (!RootStart(xml, out int open, out int close) || xml[close - 1] == '/') return Unwrapped(xml);
        int end = xml.LastIndexOf("</ms2", StringComparison.Ordinal);
        if (end < close) return Unwrapped(xml);
        // the row takes its id from the file name; a root id attribute of its own would repeat the attribute
        string attrs = RootId.Replace(xml.Substring(open + 4, close - open - 4), "");
        return "<ms2><skill id=\"" + long.Parse(id, CultureInfo.InvariantCulture) + "\"" + attrs + ">"
            + xml.Substring(close + 1, end - close - 1) + "</skill></ms2>";
    }

    static readonly System.Text.RegularExpressions.Regex RootId = new(@"\sid\s*=\s*(""[^""]*""|'[^']*')");
    static int unwrapped;
    /// <summary>How many per-skill files Wrap could not wrap (they are left out of the tables); a test reads it.</summary>
    public static int UnwrappedCount => unwrapped;
    static string Unwrapped(string xml) { System.Threading.Interlocked.Increment(ref unwrapped); return xml; }

    /// <summary>The root start tag: skips the byte order mark, whitespace, comments, processing instructions and a doctype before it; the name must be exactly ms2.</summary>
    static bool RootStart(string x, out int open, out int close)
    {
        open = close = -1;
        int i = 0;
        while (i < x.Length)
        {
            while (i < x.Length && (char.IsWhiteSpace(x[i]) || x[i] == '\uFEFF')) i++;
            if (string.CompareOrdinal(x, i, "<!--", 0, 4) == 0) { int e = x.IndexOf("-->", i + 4, StringComparison.Ordinal); if (e < 0) return false; i = e + 3; }
            else if (string.CompareOrdinal(x, i, "<?", 0, 2) == 0) { int e = x.IndexOf("?>", i + 2, StringComparison.Ordinal); if (e < 0) return false; i = e + 2; }
            else if (string.CompareOrdinal(x, i, "<!", 0, 2) == 0) { int e = x.IndexOf('>', i + 2); if (e < 0) return false; i = e + 1; }
            else break;
        }
        if (string.CompareOrdinal(x, i, "<ms2", 0, 4) != 0 || i + 4 >= x.Length || !(char.IsWhiteSpace(x[i + 4]) || x[i + 4] is '>' or '/')) return false;
        char q = '\0';
        for (int j = i + 4; j < x.Length; j++)
        {
            char c = x[j];
            if (q != '\0') { if (c == q) q = '\0'; }
            else if (c is '"' or '\'') q = c;
            else if (c == '>') { open = i; close = j; return true; }
        }
        return false;
    }
}
