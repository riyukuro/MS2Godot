using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

/// <summary>
/// One ranking category of CSeasonDataTable (seasondatatable.cpp): Load (@0x140791c30) reads Table/SeasonData_&lt;type&gt;.xml with ReadData
/// (@0x1407927d0, :510-535: the &lt;Season&gt; rows under &lt;ms2&gt;, attributes seasonID, seasonName, eventStart, eventEnd, grade1..grade7, filed
/// by season id ascending, a later row of an id replacing the earlier) and ReadNames (@0x1407931f0, :585-): the &lt;season&gt; rows of the string
/// folder's seasonName table, a row of this type and id with no contentsid giving the season its display name.
/// The guild PvP category is type "GuildPvP" (SeasonRankType 2), the one CGuildInfo::UpdateSeason (@0x1404d31b0) copies into the guild's season.
/// <para>A date is the client's "Y-M-D-h-m-s" string (FUN_140718950, stringconversionhelpers.cpp:84-110): a field out of range gives time 0, else
/// CMsTime(y,m,d,h,mi,s) = the fields read as UTC (_mkgmtime64) and marked local, so the instant is those seconds + CalcZoneOffset
/// (mstime.cpp:46-60, GetTime). <see cref="Season.LocalStart"/> and <see cref="Season.LocalEnd"/> are the first of those (0 stays 0, no offset).</para>
/// STILL-OPEN: CSeasonDataTable::Refresh (@0x140792b90) walks from the current season's position to the next season holding now; <see cref="Current"/>
/// takes the first season by id that holds now, the same unless the seasons overlap or are not in time order.
/// </summary>
public sealed class SeasonDataTable
{
    /// <summary>CSeasonData: seasonID (+0x1c), display name (+0x20), seasonName (+0x28), eventStart (+0x30), eventEnd (+0x40), grade1..7 (+0x50).</summary>
    public sealed class Season
    {
        public int Id;
        public string DisplayName = "";
        public string SeasonName = "";
        public long LocalStart, LocalEnd;
        public int[] Grades = new int[7];

        /// <summary>CMsTime::GetTime of the start: 0, else the local seconds + the zone offset.</summary>
        public long Start(long zoneOffset) => LocalStart == 0 ? 0 : LocalStart + zoneOffset;
        public long End(long zoneOffset) => LocalEnd == 0 ? 0 : LocalEnd + zoneOffset;
    }

    readonly SortedDictionary<int, Season> seasons = new();

    /// <param name="type">The category's name in the file name and the names table ("GuildPvP").</param>
    /// <param name="text">The string tables for the names (the seasonName table); no names when null.</param>
    public SeasonDataTable(PackSet packs, string type, GameStrings text = null)
        : this(Doc(packs, "SeasonData_" + type + ".xml"), type, text?.Rows("seasonname", "season")) { }

    /// <summary>The table from the xml itself (tests); <paramref name="names"/> are the seasonName rows.</summary>
    public SeasonDataTable(XDocument doc, string type, IEnumerable<XElement> names = null)
    {
        if (doc?.Root is { Name.LocalName: "ms2" } root)
            foreach (var n in root.Elements("Season"))
            {
                var s = new Season
                {
                    Id = Int(n, "seasonID"), SeasonName = (string)n.Attribute("seasonName") ?? "",
                    LocalStart = Time((string)n.Attribute("eventStart")), LocalEnd = Time((string)n.Attribute("eventEnd")),
                };
                for (int i = 0; i < 7; i++) s.Grades[i] = Int(n, "grade" + (i + 1));
                seasons[s.Id] = s;
            }
        if (names == null) return;
        foreach (var n in names)
        {
            if (!string.Equals((string)n.Attribute("type"), type, StringComparison.OrdinalIgnoreCase)) continue;
            if (Int(n, "contentsid") != 0) continue;     // a contents name; not the display name
            if (seasons.TryGetValue(Int(n, "id"), out var s)) s.DisplayName = (string)n.Attribute("name") ?? "";
        }
    }

    static XDocument Doc(PackSet packs, string file) =>
        HousingTable.EntryOf(packs, file) is { } e ? Ms2.Formats.Text.PackXml.Parse(packs.Read("Xml", e)) : null;

    public int Count => seasons.Count;
    public Season Get(int id) => seasons.TryGetValue(id, out var s) ? s : null;
    public IEnumerable<Season> All => seasons.Values;

    /// <summary>GetCurrent: the first season (by id) with start &lt;= now &lt; end, else null.</summary>
    public Season Current(long now, long zoneOffset) =>
        seasons.Values.FirstOrDefault(s => now >= s.Start(zoneOffset) && now < s.End(zoneOffset));

    /// <summary>FUN_140718950 + the CMsTime(y,m,d,h,mi,s) ctor: the fields as UTC seconds; 0 for a missing, short or out-of-range date.</summary>
    public static long Time(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var v = new int[6];
        var parts = s.Split('-');
        for (int i = 0; i < 6 && i < parts.Length; i++) v[i] = AtoI(parts[i]);
        if (v[0] < 1900 || v[1] < 1 || v[1] > 12 || v[2] < 1 || v[2] > 31 || v[3] < 0 || v[3] > 23 || v[4] < 0 || v[4] > 59 || v[5] < 0 || v[5] > 59) return 0;
        // _mkgmtime64 normalizes a day past the month's end into the next month
        var d = new DateTime(v[0], v[1], 1, 0, 0, 0, DateTimeKind.Utc).AddDays(v[2] - 1).AddHours(v[3]).AddMinutes(v[4]).AddSeconds(v[5]);
        return new DateTimeOffset(d).ToUnixTimeSeconds();
    }

    /// <summary>atoi: the leading integer, 0 when none.</summary>
    static int AtoI(string s)
    {
        s = s.TrimStart();
        int i = 0, sign = 1; long r = 0;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) { if (s[i] == '-') sign = -1; i++; }
        for (; i < s.Length && s[i] >= '0' && s[i] <= '9'; i++) r = Math.Min(r * 10 + (s[i] - '0'), int.MaxValue);
        return (int)(sign * r);
    }

    static int Int(XElement n, string a) =>
        int.TryParse((string)n.Attribute(a), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
