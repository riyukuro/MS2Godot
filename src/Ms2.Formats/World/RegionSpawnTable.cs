using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Ui;

namespace Ms2.Formats.World;

/// <summary>
/// The region spawn data the client holds: table/mapspawntag.xml (per field, per spawn point id: tags, difficulty range,
/// population) and table/npcregionspawndata.xml (per npc: main and sub tags, rare degree, difficulty; GMS2: the npc files,
/// <see cref="NpcFileRows"/>). The client's CNpcDataTable::FindSpawnResult picks the npcs a tag list names and weights them (CalcNpcSpawnWeight).
/// </summary>
public sealed class RegionSpawnTable
{
    public sealed record Region(int SpawnPointId, int MinDifficulty, int MaxDifficulty, int Population, string[] Tags);
    sealed record NpcSpawn(int NpcId, string[] Main, string[] Sub, int RareDegree, int Difficulty);

    readonly Dictionary<int, Dictionary<int, Region>> regions = new();
    /// <summary>CNpcDataTable::m_mapSpawn: one row per npc id, the last read kept (SetAt); walked in id order, as FindSpawnResult
    /// walks its hits (a CRBMap by npc id).</summary>
    readonly SortedDictionary<int, NpcSpawn> npcs = new();

    public RegionSpawnTable(PackSet packs)
    {
        // LoadMapSpawnTag (fielddata.cpp:488): the available <region> of each mapCode and spawnPointID (FUN_14028fd90)
        if (Root(packs, "table/mapspawntag.xml") is { } tags)
            foreach (var r in FeatureElements.Collect(packs, tags, "region", "mapCode", "spawnPointID"))
            {
                if (!int.TryParse((string)r.Attribute("mapCode"), out int map) || !int.TryParse((string)r.Attribute("spawnPointID"), out int sp)) continue;
                if (!regions.TryGetValue(map, out var d)) regions[map] = d = new();
                d[sp] = new Region(sp, I(r, "difficultyMin"), I(r, "difficulty"), I(r, "population"), List((string)r.Attribute("tag")));
            }
        // LoadRegionSpawn (npcdata.cpp:1249): a row of another locale or a feature that is off is skipped
        var rows = PerFileTable.Choose<IEnumerable<XElement>>(packs.Exists("Xml", NpcSpawnData),
            () => (Root(packs, NpcSpawnData)?.Elements("NpcRegionSpawnData") ?? Enumerable.Empty<XElement>()).Where(e => FeatureElements.IsAvailable(packs, e)),
            () => NpcFileRows(packs));
        foreach (var r in rows)
            if (int.TryParse((string)r.Attribute("npcID"), out int id))
                npcs[id] = new NpcSpawn(id, List((string)r.Attribute("mainTags")), List((string)r.Attribute("subTags")), I(r, "rareDegree"), I(r, "difficulty"));
    }

    const string NpcSpawnData = "table/npcregionspawndata.xml";

    /// <summary>
    /// GMS2 has no table/npcregionspawndata.xml: an npc's mainTags, subTags, rareDegree and difficulty are on the
    /// &lt;basic&gt; of its npc/&lt;aa&gt;/&lt;bb&gt;/&lt;id&gt;.xml environment. Those of an npc with a main or sub tag, in id order, each
    /// with its npcID. INFERRED from the data, the GMS2 client's reader is not in the restored source.
    /// </summary>
    static IEnumerable<XElement> NpcFileRows(PackSet packs)
    {
        if (PerFileTable.Rows(packs, "npc/", "npc")?.Root is not { } root) yield break;
        foreach (var n in root.Elements("npc"))
        {
            var env = n.Element("environment");
            if ((env?.Element("basic") ?? env) is not { } basic) continue;
            if (string.IsNullOrWhiteSpace((string)basic.Attribute("mainTags")) && string.IsNullOrWhiteSpace((string)basic.Attribute("subTags"))) continue;
            yield return new XElement("NpcRegionSpawnData", new XAttribute("npcID", (string)n.Attribute("id") ?? ""),
                new XAttribute("mainTags", (string)basic.Attribute("mainTags") ?? ""), new XAttribute("subTags", (string)basic.Attribute("subTags") ?? ""),
                new XAttribute("rareDegree", (string)basic.Attribute("rareDegree") ?? ""), new XAttribute("difficulty", (string)basic.Attribute("difficulty") ?? ""));
        }
    }

    static XElement Root(PackSet packs, string entry) =>
        packs.Exists("Xml", entry) && PackXml.Parse(packs.Read("Xml", entry)).Root is { } r && r.Name.LocalName == "ms2" ? r : null;

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
        foreach (var n in npcs.Values)
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
