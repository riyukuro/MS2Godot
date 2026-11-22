using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

/// <summary>
/// CGuildXmlData's npc table: LoadGuildNpc (@0x1404d64c0, guilddef_2.cpp:545-559) reads table/GuildNpc.xml, the <c>npc</c> rows under
/// &lt;ms2&gt; into a GuildNpc each (FUN_1404d5270 @0x1404d5270, guilddef_2.cpp:421-430: IsElementAvailable, then type by name
/// (Parse_140219f50 @0x140219f50, commonenum.cpp:676: equip 1, goods 2, gemstone 3, itemMerge 4, music 5, quest 6, else 0, the compare
/// case-insensitive), level, upgradeCost (64 bit), requireGuildLevel, requireHouseLevel, stringKey), filed under (kind, level) (a later row of
/// the same key replaces the earlier); GetNpc (@0x1404d7b40) finds one, GetNpcKinds (@0x1404d7c20) lists the kinds.
/// STILL-OPEN: the row collector FUN_14028fd90 (it takes the "type" and "level" attribute names): every <c>npc</c> row is read here.
/// </summary>
public sealed class GuildNpcTable
{
    public const string File = "guildnpc.xml";

    /// <summary>GuildNpc: kind (f18), level (f1c), upgradeCost (f20), requireGuildLevel (f28), requireHouseLevel (f2c), stringKey (f30).</summary>
    public sealed record Npc(int Kind, int Level, long UpgradeCost, int RequireGuildLevel, int RequireHouseLevel, int StringKey);

    readonly SortedDictionary<(int Kind, int Level), Npc> npcs = new();

    /// <param name="available">IsElementAvailable of a row (its locale and feature); every row when null.</param>
    public GuildNpcTable(PackSet packs, Func<XElement, bool> available = null)
        : this(HousingTable.EntryOf(packs, File) is { } e ? Ms2.Formats.Text.PackXml.Parse(packs.Read("Xml", e)) : null, available) { }

    public GuildNpcTable(XDocument doc, Func<XElement, bool> available = null)
    {
        if (doc?.Root is not { Name.LocalName: "ms2" } root) return;
        foreach (var n in root.Descendants("npc"))
        {
            if (available != null && !available(n)) continue;
            var row = new Npc(KindOf((string)n.Attribute("type")), Int(n, "level"),
                long.TryParse((string)n.Attribute("upgradeCost"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) ? c : 0,
                Int(n, "requireGuildLevel"), Int(n, "requireHouseLevel"), Int(n, "stringKey"));
            npcs[(row.Kind, row.Level)] = row;
        }
    }

    /// <summary>Parse_140219f50: the kind of its name, 0 for any other.</summary>
    public static int KindOf(string name) => (name ?? "").ToLowerInvariant() switch
    {
        "equip" => 1, "goods" => 2, "gemstone" => 3, "itemmerge" => 4, "music" => 5, "quest" => 6, _ => 0,
    };

    /// <summary>GetNpc(kind, level): the row, else null.</summary>
    public Npc Get(int kind, int level) => npcs.TryGetValue((kind, level), out var n) ? n : null;

    /// <summary>GetNpcKinds: the kinds of the rows, ascending without repeats (the client's std::set).</summary>
    public IEnumerable<int> Kinds() => npcs.Keys.Select(k => k.Kind).Distinct();

    public int Count => npcs.Count;

    static int Int(XElement n, string a) =>
        int.TryParse((string)n.Attribute(a), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
