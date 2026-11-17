using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

/// <summary>
/// The region spawn data the client holds: table/mapspawntag.xml (per field, per spawn point id: tags, difficulty range,
/// population) and table/npcregionspawndata.xml (per npc: main and sub tags, rare degree, difficulty). The client's
/// CNpcDataTable::FindSpawnResult picks the npcs a tag list names and weights them (CalcNpcSpawnWeight).
/// </summary>
public sealed class RegionSpawnTable
{
    public sealed record Region(int SpawnPointId, int MinDifficulty, int MaxDifficulty, int Population, string[] Tags);
    sealed record NpcSpawn(int NpcId, string[] Main, string[] Sub, int RareDegree, int Difficulty);

    readonly Dictionary<int, Dictionary<int, Region>> regions = new();
    readonly List<NpcSpawn> npcs = new();

    public RegionSpawnTable(PackSet packs)
    {
        foreach (var r in Rows(packs, "table/mapspawntag.xml", "region"))
        {
            if (!int.TryParse((string)r.Attribute("mapCode"), out int map) || !int.TryParse((string)r.Attribute("spawnPointID"), out int sp)) continue;
            if (!regions.TryGetValue(map, out var d)) regions[map] = d = new();
            d[sp] = new Region(sp, I(r, "difficultyMin"), I(r, "difficulty"), I(r, "population"), List((string)r.Attribute("tag")));
        }
        foreach (var r in Rows(packs, "table/npcregionspawndata.xml", "NpcRegionSpawnData"))
            if (int.TryParse((string)r.Attribute("npcID"), out int id))
                npcs.Add(new NpcSpawn(id, List((string)r.Attribute("mainTags")), List((string)r.Attribute("subTags")), I(r, "rareDegree"), I(r, "difficulty")));
    }

    static IEnumerable<XElement> Rows(PackSet packs, string entry, string name)
    {
        if (!packs.Exists("Xml", entry)) return Array.Empty<XElement>();
        return XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('﻿')).Descendants(name);
    }

    static int I(XElement e, string a) => int.TryParse((string)e.Attribute(a), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
    static string[] List(string s) => (s ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The field's region spawn row for a spawn point id, or null (towns have none).</summary>
    public Region Find(int fieldId, int spawnPointId) =>
        regions.TryGetValue(fieldId, out var d) && d.TryGetValue(spawnPointId, out var r) ? r : null;

    public bool HasRegions(int fieldId) => regions.ContainsKey(fieldId);

    /// <summary>CLua::CalcNpcSpawnWeight (the script, cut to an int as the client does).</summary>
    static int Weight(int main, int sub, int rareDegree, int difficultyDiff)
    {
        float mainWeight = main == 0 ? 1 : (main + 1) * 20;
        float subWeight = sub == 0 ? 1 : (sub + 1);
        float diffWeight = difficultyDiff > 10 ? 1 : 12 - difficultyDiff;
        return (int)(mainWeight + subWeight + rareDegree * 0.01f + diffWeight);
    }

    /// <summary>FindSpawnResult: the npcs the row's tags pick, with their weights. An npc counts only when the row names all its main tags.</summary>
    public List<(int NpcId, int Weight)> Candidates(Region r)
    {
        var result = new List<(int, int)>();
        foreach (var n in npcs)
        {
            if (n.Difficulty > r.MaxDifficulty || n.Difficulty < r.MinDifficulty) continue;
            int main = 0, sub = 0;
            foreach (var t in r.Tags)
            {
                main += n.Main.Count(x => x == t);
                sub += n.Sub.Count(x => x == t);
            }
            if (main == 0 && sub == 0) continue;
            if (main != n.Main.Length) continue;
            result.Add((n.NpcId, Weight(main, sub, n.RareDegree, r.MaxDifficulty - n.Difficulty)));
        }
        return result;
    }
}
