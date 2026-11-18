using System.Globalization;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui;

/// <summary>
/// The game tables the HUD controllers read: table/nextexp.xml (CExpTable), string/&lt;lang&gt;/jobname.xml (string table 6)
/// and stringcommon.xml (GetStringCommonByKey). Read from the client's own Xml.m2d; <see cref="Empty"/> without data.
/// </summary>
public sealed class GameTables
{
    readonly Dictionary<int, long> nextExp = new();
    readonly Dictionary<string, string> jobNames = new();
    readonly Dictionary<string, string> mapNames = new();
    readonly Func<string, string> common = _ => null;

    public static readonly GameTables Empty = new();

    GameTables() { }

    public GameTables(PackSet packs, params string[] languages)
    {
        if (packs.Exists("Xml", "table/nextexp.xml"))
            foreach (var e in GameStrings.Parse(packs.Read("Xml", "table/nextexp.xml")).Descendants("exp"))
                if (int.TryParse((string)e.Attribute("level"), out var lv) && long.TryParse((string)e.Attribute("value"), out var v))
                    nextExp.TryAdd(lv, v);
        var s = new GameStrings(packs, languages);
        jobNames = s.Table("jobname", "name");
        mapNames = s.Table("mapname", "name");
        common = s.Common();
    }

    /// <summary>Exp needed from <paramref name="level"/> to the next level; 0 = unknown level.</summary>
    public long NextExp(int level) => nextExp.TryGetValue(level, out var v) ? v : 0;
    public int MaxLevel => nextExp.Count == 0 ? 0 : nextExp.Keys.Max();
    /// <summary>The job's display name by sub job code (string table 6); the code as text when unknown.</summary>
    public string JobName(int subJob) => jobNames.TryGetValue(subJob.ToString(CultureInfo.InvariantCulture), out var n) && n != null ? n : subJob.ToString(CultureInfo.InvariantCulture);
    public string Common(string key) => common(key);
    /// <summary>The map's display name (string table mapname, id = 8-digit map id); null when unknown.</summary>
    public string MapName(int mapId) => mapNames.TryGetValue(mapId.ToString("D8", CultureInfo.InvariantCulture), out var n) ? n : null;
}
