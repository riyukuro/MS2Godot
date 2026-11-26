using System.Xml.Linq;

namespace Ms2.Ui;

/// <summary>
/// CFameTable (famegradedata.cpp): fameDef.xml (the alliances' officer npc kinds and symbols, which the overhead emoticon
/// reads, GetNpcMovieName, uiemoticon.cpp:125-126, and the seasons), fame.xml (the grades) and fameLog.xml (the log
/// ids), which the fame point board and the fame contents notice read. fameLimit.xml and famePickMethod.xml are not read
/// here.
/// </summary>
public sealed class FameTable
{
    /// <summary>CFameDef (@0x140359240): m_nAlliance, m_nOfficerNpcKind, m_strSymbol (lower case), m_nIsExped, m_nSeason.</summary>
    public sealed record Def(int Alliance, int OfficerNpcKind, string Symbol, int IsExped, int Season);

    // m_mapDef: CAtlMap by alliance, 17 bins; the alliance ids (0-9) sit in the bins by value, so its walk is in id order
    readonly SortedDictionary<int, Def> defs = new();
    readonly HashSet<int> officerNpcs = new();     // m_setOfficerNpc

    /// <summary>CFameSeasonDef (@0x140359460): m_nSeason, m_nRequireQuest, m_nRequireLv.</summary>
    public sealed record SeasonDef(int Season, int RequireQuest, int RequireLv);

    /// <summary>CFameGradeData (@0x140358970): m_nAlliance, m_nGrade, m_nRequirePoint.</summary>
    public sealed record Grade(int Alliance, int Number, int RequirePoint);

    /// <summary>CFameLogData (@0x140358ad0): m_nId, m_nGrade, m_nAlliance.</summary>
    public sealed record Log(long Id, int Grade, int Alliance);

    readonly Dictionary<int, SeasonDef> seasons = new();                  // m_mapSeason
    readonly SortedDictionary<int, Grade>[] grades = new SortedDictionary<int, Grade>[10];   // m_aGrades: CRBMap grade -> data
    readonly List<int> allianceIds = new();                               // m_aAllianceId
    readonly Dictionary<long, Log> logs = new();                          // m_mapLog

    public static readonly FameTable Empty = new(null, _ => true);

    /// <summary>
    /// CFameTable::Load (@0x140359840): LoadFame (fame.xml), LoadFameLog (fameLog.xml), then LoadFameDef (fameDef.xml), each
    /// element tested by IsElementAvailable (<paramref name="available"/>).
    /// </summary>
    public FameTable(XDocument def, XDocument fame, XDocument log, Func<XElement, bool> available) : this(def, available)
    {
        // LoadFame (@0x1403598c0): each available <fame> that reads (alliance 1-9) goes into its alliance's grade map by
        // grade (SetAt: a later one replaces it); the alliance joins m_aAllianceId on its first row, in file order
        if (fame?.Root is { Name.LocalName: "ms2" } fr)
            foreach (var e in fr.Elements("fame"))
            {
                if (!available(e)) continue;
                int alliance = Alliance((string)e.Attribute("alliance"));
                if (alliance <= 0 || alliance >= 10) continue;
                var g = new Grade(alliance, Int(e, "grade"), Int(e, "requirePoint"));
                (grades[alliance] ??= new())[g.Number] = g;
                if (!allianceIds.Contains(alliance)) allianceIds.Add(alliance);
            }
        // LoadFameLog (@0x140359bd0): each available <fame> that reads (alliance 1-9) by its id (SetAt)
        if (log?.Root is { Name.LocalName: "ms2" } lr)
            foreach (var e in lr.Elements("fame"))
            {
                if (!available(e)) continue;
                int alliance = Alliance((string)e.Attribute("alliance"));
                if (alliance <= 0 || alliance >= 10) continue;
                var l = new Log((uint)Int(e, "id"), Int(e, "grade"), alliance);
                logs[l.Id] = l;
            }
    }

    static int Int(XElement e, string name) => e.Attribute(name) is { } a ? Items.ItemData.Atoi((string)a) : 0;

    /// <summary>
    /// CFameTable::LoadFameDef (@0x14035a460, famegradedata.cpp:606): under &lt;ms2&gt;, each available &lt;season&gt;
    /// (IsElementAvailable, <paramref name="available"/>) and each of its &lt;fame&gt; (not tested): the def by its
    /// alliance (a later one replaces it) and its officer npc kind kept in the set.
    /// </summary>
    public FameTable(XDocument doc, Func<XElement, bool> available)
    {
        if (doc?.Root is not { Name.LocalName: "ms2" } root) return;
        foreach (var season in root.Elements("season"))
        {
            if (!available(season)) continue;
            // CFameSeasonDef::Read (@0x140359460) always succeeds; its "name" is the season of the defs
            int seasonId = Season((string)season.Attribute("name"));
            seasons[seasonId] = new SeasonDef(seasonId, Int(season, "requireQuest"), Int(season, "requireLv"));
            foreach (var fame in season.Elements("fame"))
            {
                var def = Read(fame, seasonId);
                defs[def.Alliance] = def;
                officerNpcs.Add(def.OfficerNpcKind);
            }
        }
    }

    /// <summary>
    /// CFameDef::Read (@0x1403592f0): alliance, officerNpcKind, symbol (ToLower), isExped; a missing attribute keeps the
    /// constructor's value (alliance none 0, 0, ""). Its "m_nAlliance != 10" test passes for every def, as the alliance
    /// parse gives 0-9.
    /// </summary>
    static Def Read(XElement e, int season) => new(
        Alliance((string)e.Attribute("alliance")),
        e.Attribute("officerNpcKind") is { } k ? Items.ItemData.Atoi((string)k) : 0,
        ((string)e.Attribute("symbol") ?? "").ToLowerInvariant(),
        e.Attribute("isExped") is { } x ? Items.ItemData.Atoi((string)x) : 0,
        season);

    static readonly string[] AllianceNames =
    {
        "darkwind", "mapleunion", "lumiknight", "triaroyalguard", "greenhood", "mapleunion_kritiasexped",
        "lumiknight_kritiasexped", "greenhood_kritiasexped", "georg",
    };

    /// <summary>Name_14021a6b0 (commonenum.cpp:772): the alliance's name key part for 1-9, else "none".</summary>
    public static string AllianceName(int alliance) => alliance >= 1 && alliance <= AllianceNames.Length ? AllianceNames[alliance - 1] : "none";

    /// <summary>Parse_14021a330 (commonenum.cpp): the alliance name (any case) to 1-9, else 0.</summary>
    public static int Alliance(string name)
    {
        if (name == null) return 0;
        int i = Array.FindIndex(AllianceNames, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        return i + 1;
    }

    /// <summary>Parse_14021a480 (commonenum.cpp): skyfotress 1, kritiasexped 2, georg 3 (any case), else 0.</summary>
    public static int Season(string name) => name?.ToLowerInvariant() switch
    {
        "skyfotress" => 1,
        "kritiasexped" => 2,
        "georg" => 3,
        _ => 0,
    };

    public IReadOnlyCollection<Def> Defs => defs.Values;

    /// <summary>CFameTable::IsOfficerNpc (@0x14035b0a0): the npc kind is some def's officerNpcKind.</summary>
    public bool IsOfficerNpc(int kind) => officerNpcs.Contains(kind);

    /// <summary>CFameTable::GetSymbolByOfficerNpc (@0x14035b2a0): the symbol of the first def in m_mapDef with the kind, else "".</summary>
    public string GetSymbolByOfficerNpc(int kind)
    {
        foreach (var d in defs.Values)
            if (d.OfficerNpcKind == kind) return d.Symbol;
        return "";
    }

    /// <summary>CFameTable::GetAllianceIds (@0x14035aff0): the alliances of fame.xml in the order of their first row.</summary>
    public IReadOnlyList<int> AllianceIds => allianceIds;

    /// <summary>The grades of the alliance in grade order (m_aGrades[alliance]); empty outside 1-9.</summary>
    public IReadOnlyCollection<Grade> Grades(int alliance) =>
        alliance > 0 && alliance < 10 && grades[alliance] is { } m ? m.Values : Array.Empty<Grade>();

    /// <summary>
    /// CFameTable::GetGradeByPoint (@0x14035a870): 0 outside alliance 1-9; else, walking the grades up, one under the first
    /// grade the point is below, and the max grade when it is below none.
    /// </summary>
    public int GetGradeByPoint(int alliance, int point)
    {
        if (alliance <= 0 || alliance >= 10) return 0;
        if (grades[alliance] is { } m)
            foreach (var g in m.Values)
                if (point < g.RequirePoint) return g.Number - 1;
        return GetMaxGrade(alliance);
    }

    /// <summary>CFameTable::GetNextGradePoint (@0x14035a960): the require point of grade + 1, else 0x7fffffff.</summary>
    public int GetNextGradePoint(int alliance, int grade) => GetGradePoint(alliance, grade + 1);

    /// <summary>CFameTable::GetGradePoint (@0x14035ab90): the require point of the grade, else 0x7fffffff.</summary>
    public int GetGradePoint(int alliance, int grade) =>
        alliance > 0 && alliance < 10 && grades[alliance] is { } m && m.TryGetValue(grade, out var g) ? g.RequirePoint : int.MaxValue;

    /// <summary>CFameTable::GetMaxGrade (@0x14035aa20): the last grade of the alliance, else 0.</summary>
    public int GetMaxGrade(int alliance) =>
        alliance > 0 && alliance < 10 && grades[alliance] is { Count: > 0 } m ? m.Keys.Last() : 0;

    /// <summary>CFameTable::IsLogOfAlliance (@0x14035ae50): the log id is known and is the alliance's.</summary>
    public bool IsLogOfAlliance(long logId, int alliance) => logs.TryGetValue(logId, out var l) && l.Alliance == alliance;

    public IReadOnlyCollection<Log> Logs => logs.Values;

    /// <summary>CFameTable::GetSymbol (@0x14035b1d0): the def's symbol, else "".</summary>
    public string GetSymbol(int alliance) => defs.TryGetValue(alliance, out var d) ? d.Symbol : "";

    /// <summary>CFameTable::GetSeason (@0x14035b620): the season of the alliance's def, else null.</summary>
    public SeasonDef GetSeason(int alliance) =>
        defs.TryGetValue(alliance, out var d) && seasons.TryGetValue(d.Season, out var s) ? s : null;

    /// <summary>CFameTable::GetSeasonRequireQuest (@0x14035b510): the season's requireQuest, else 0.</summary>
    public int GetSeasonRequireQuest(int alliance) => GetSeason(alliance)?.RequireQuest ?? 0;
}
