using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

/// <summary>
/// The field loading pictures (Xml.m2d : table/loadingimage.xml, CLoadingImageTable). A row names a map code (0 = shown for
/// every map), an optional period and, for the common rows, the map codes it skips. The picture for a map is a random one of
/// its own rows plus the common rows in their period; the file is Resource/Image.m2d : loadingimage/&lt;bgLoading&gt;.dds.
/// </summary>
public sealed class LoadingImageTable
{
    public const string ImageArchive = "Resource/Image";

    sealed record Row(int Code, DateTime? Start, DateTime? End, string File, List<(int from, int to)> Except);

    readonly List<Row> rows = new();

    // movieFileName: per map code, the transition movie by index ("index:file", or a bare file for index 0) and playOneTime
    readonly Dictionary<int, (Dictionary<int, string> Files, bool OneTime)> movies = new();

    public LoadingImageTable(PackSet packs)
    {
        const string entry = "table/loadingimage.xml";
        if (!packs.Exists("Xml", entry)) return;
        Load(XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('\uFEFF')));
    }

    /// <summary>The table from the xml itself (tests).</summary>
    public LoadingImageTable(XDocument x) => Load(x);

    void Load(XDocument x)
    {
        foreach (var e in x.Descendants("image"))
        {
            var loc = (string)e.Attribute("locale");
            if (!string.IsNullOrEmpty(loc) && loc != "KR") continue;
            int.TryParse((string)e.Attribute("code"), out int code);
            var movie = (string)e.Attribute("movieFileName");
            if (!string.IsNullOrEmpty(movie))
            {
                // CLoadingImageTable::Load: the last row of a code with a movie wins (CAtlMap::SetAt)
                var files = new Dictionary<int, string>();
                foreach (var t in movie.Split(',', StringSplitOptions.TrimEntries))
                {
                    var parts = t.Split(':');
                    if (parts.Length == 2) { uint.TryParse(parts[0], out uint i); files.TryAdd((int)i, parts[1].Trim()); }
                    else if (parts.Length == 1) files.TryAdd(0, parts[0].Trim());
                }
                int.TryParse((string)e.Attribute("playOneTime"), out int once);
                movies[code] = (files, once != 0);
            }
            var bg = (string)e.Attribute("bgLoading");
            if (string.IsNullOrEmpty(bg)) continue;
            if (!bg.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) bg += ".dds";
            var except = new List<(int, int)>();
            foreach (var part in ((string)e.Attribute("exceptMap") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var ab = part.Split('-');
                if (int.TryParse(ab[0], out int a)) except.Add((a, ab.Length > 1 && int.TryParse(ab[1], out int b) ? b : a));
            }
            rows.Add(new Row(code, Time((string)e.Attribute("startTime")), Time((string)e.Attribute("endTime")), "loadingimage/" + bg.ToLowerInvariant(), except));
        }
    }

    /// <summary>"yyyy-M-d-H-m-s"; all zero = no limit.</summary>
    static DateTime? Time(string s)
    {
        var p = (s ?? "").Split('-');
        if (p.Length != 6 || p.All(v => v == "0")) return null;
        var n = p.Select(v => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : 0).ToArray();
        try { return new DateTime(n[0], Math.Max(n[1], 1), Math.Max(n[2], 1), n[3], n[4], n[5]); } catch { return null; }
    }

    static bool InPeriod(Row r, DateTime now) => (r.Start == null || now >= r.Start) && (r.End == null || now < r.End);

    /// <summary>CLoadingImageTable::CollectImages: the map's own rows in their period and, with <paramref name="common"/>, the
    /// common rows (code 0) in their period that do not skip the map.</summary>
    List<Row> Collect(int mapCode, bool common, DateTime now)
    {
        var o = new List<Row>();
        if (common)
            foreach (var r in rows)
                if (r.Code == 0 && !r.Except.Any(x => mapCode >= x.from && mapCode <= x.to) && InPeriod(r, now)) o.Add(r);
        foreach (var r in rows)
            if (r.Code != 0 && r.Code == mapCode && InPeriod(r, now)) o.Add(r);
        return o;
    }

    /// <summary>The candidate pictures for a map (entries in <see cref="ImageArchive"/>), at the given time.</summary>
    public List<string> Candidates(int mapCode, DateTime now) => Collect(mapCode, true, now).Select(r => r.File).ToList();

    /// <summary>A random picture for the map, or null when the data has none (GetImageFile(map, 1)).</summary>
    public string Pick(int mapCode, Random rng)
    {
        var c = Candidates(mapCode, DateTime.Now);
        return c.Count == 0 ? null : c[rng.Next(c.Count)];
    }

    /// <summary>CLoadingImageTable::IsSameImages: the two maps' own rows (no common ones) pair up with the same period and file.</summary>
    public bool SameImages(int mapCode, int otherCode, DateTime now)
    {
        var a = Collect(mapCode, false, now);
        var b = Collect(otherCode, false, now);
        if (a.Count != b.Count) return false;
        return a.All(x => b.Any(y => x.Start == y.Start && x.End == y.End && x.File == y.File));
    }

    /// <summary>CLoadingImageTable::GetMovieFile != "": the map has a transition movie for the index (or for index 0).
    /// <paramref name="played"/> holds the maps whose playOneTime movie has played.</summary>
    public bool HasMovie(int mapCode, int index, ICollection<int> played = null)
    {
        if (!movies.TryGetValue(mapCode, out var m)) return false;
        if (m.OneTime && played != null && played.Contains(mapCode)) return false;
        return (m.Files.TryGetValue(index, out var f) || m.Files.TryGetValue(0, out f)) && !string.IsNullOrEmpty(f);
    }

    /// <summary>
    /// The loading picture for a field move from <paramref name="fromMap"/> to <paramref name="toMap"/>, or null for a screenshot
    /// of the field being left. UILoadingProgress::GetLoadingImagePath (0x1419e0710) asks FUN_1419dfe70: no picture for the
    /// target, or a transition movie (UICutSceneMovie::HasSceneVideo), gives the screenshot; so does a target whose own pictures
    /// are those of the map being left (IsSameImages), e.g. one town to another. Otherwise a random picture of the target.
    /// </summary>
    public string PickForMove(int toMap, int fromMap, Random rng)
    {
        var now = DateTime.Now;
        if (Collect(toMap, true, now).Count == 0) return null;
        if (HasMovie(fromMap, fromMap)) return null;
        if (SameImages(toMap, fromMap, now)) return null;
        return Pick(toMap, rng);
    }
}
