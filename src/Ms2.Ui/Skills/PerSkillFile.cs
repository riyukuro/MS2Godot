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
        int open = xml.IndexOf("<ms2", StringComparison.Ordinal);
        int end = xml.LastIndexOf("</ms2>", StringComparison.Ordinal);
        int close = open < 0 ? -1 : xml.IndexOf('>', open);
        if (close < 0 || end < close || xml[close - 1] == '/') return xml;
        return "<ms2><skill id=\"" + long.Parse(id, CultureInfo.InvariantCulture) + "\"" + xml.Substring(open + 4, close - open - 4) + ">"
            + xml.Substring(close + 1, end - close - 1) + "</skill></ms2>";
    }
}
