using System.Globalization;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

/// <summary>
/// The loading screen tips (CLoadingDescriptionTable: string/&lt;lang&gt;/loadingdescription.xml, &lt;tip id locale startTime endTime
/// text&gt;). GetRandomTip picks one of the tips whose period holds now ("0-0-0-0-0-0" = always), at random. LoadXml
/// (0x14144eab0) reads each text, turns "\n" into a line break and resolves its object words
/// (CReservedWordTable::ReplaceObjectWords) once, as it loads.
/// </summary>
public sealed class LoadingTipTable
{
    readonly List<(DateTime? start, DateTime? end, string text)> tips = new();
    public int Count => tips.Count;

    /// <param name="words">The reserved word table the texts are resolved with; null leaves them as written.</param>
    public LoadingTipTable(PackSet packs, GameStrings text, ReservedWords words = null)
    {
        var seen = new HashSet<string>();
        foreach (var t in text.Rows("loadingdescription", "tip"))
        {
            var loc = (string)t.Attribute("locale");
            if (!string.IsNullOrEmpty(loc) && loc != "KR") continue;
            var id = (string)t.Attribute("id");
            if (id == null || !seen.Add(id)) continue;      // the preferred language's row of an id wins
            var s = (string)t.Attribute("text");
            if (string.IsNullOrEmpty(s)) continue;
            tips.Add((Time((string)t.Attribute("startTime")), Time((string)t.Attribute("endTime")), Resolve(s.Replace("\\n", "\n"), words)));
        }
    }

    static string Resolve(string s, ReservedWords words) => words == null ? s : words.ReplaceObjectWords(s);

    static DateTime? Time(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        var p = s.Split('-').Select(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0).ToArray();
        if (p.Length < 3 || p[0] == 0) return null;
        try { return new DateTime(p[0], p[1], p[2], p.Length > 3 ? p[3] : 0, p.Length > 4 ? p[4] : 0, p.Length > 5 ? p[5] : 0); }
        catch { return null; }
    }

    public IEnumerable<string> Candidates(DateTime now) =>
        tips.Where(t => (t.start == null || now >= t.start) && (t.end == null || now <= t.end)).Select(t => t.text);

    public string Pick(Random rng)
    {
        var c = Candidates(DateTime.Now).ToList();
        return c.Count == 0 ? "" : c[rng.Next(c.Count)];
    }
}
