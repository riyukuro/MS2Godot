using System.Xml.Linq;

namespace Ms2.Ui;

/// <summary>
/// CFameTable's fameDef.xml part (famegradedata.cpp): the alliances' officer npc kinds and symbols, which the overhead
/// emoticon reads (GetNpcMovieName, uiemoticon.cpp:125-126). The other fame tables (fame.xml, fameLog.xml, fameLimit.xml,
/// famePickMethod.xml) are not read here.
/// </summary>
public sealed class FameTable
{
    /// <summary>CFameDef (@0x140359240): m_nAlliance, m_nOfficerNpcKind, m_strSymbol (lower case), m_nIsExped, m_nSeason.</summary>
    public sealed record Def(int Alliance, int OfficerNpcKind, string Symbol, int IsExped, int Season);

    // m_mapDef: CAtlMap by alliance, 17 bins; the alliance ids (0-9) sit in the bins by value, so its walk is in id order
    readonly SortedDictionary<int, Def> defs = new();
    readonly HashSet<int> officerNpcs = new();     // m_setOfficerNpc

    public static readonly FameTable Empty = new(null, _ => true);

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
}
