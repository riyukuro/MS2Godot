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

    public LoadingImageTable(PackSet packs)
    {
        const string entry = "table/loadingimage.xml";
        if (!packs.Exists("Xml", entry)) return;
        var x = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('﻿'));
        foreach (var e in x.Descendants("image"))
        {
            var loc = (string)e.Attribute("locale");
            if (!string.IsNullOrEmpty(loc) && loc != "KR") continue;
            var bg = (string)e.Attribute("bgLoading");
            if (string.IsNullOrEmpty(bg)) continue;
            if (!bg.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) bg += ".dds";
            int.TryParse((string)e.Attribute("code"), out int code);
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

    /// <summary>The candidate pictures for a map (entries in <see cref="ImageArchive"/>), at the given time.</summary>
    public List<string> Candidates(int mapCode, DateTime now)
    {
        var o = new List<string>();
        foreach (var r in rows)
        {
            if (!InPeriod(r, now)) continue;
            if (r.Code == mapCode) o.Add(r.File);
            else if (r.Code == 0 && !r.Except.Any(x => mapCode >= x.from && mapCode <= x.to)) o.Add(r.File);
        }
        return o;
    }

    /// <summary>A random picture for the map, or null when the data has none.</summary>
    public string Pick(int mapCode, Random rng)
    {
        var c = Candidates(mapCode, DateTime.Now);
        return c.Count == 0 ? null : c[rng.Next(c.Count)];
    }
}
