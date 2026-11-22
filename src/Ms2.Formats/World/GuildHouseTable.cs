using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

/// <summary>
/// CGuildXmlData's house table: LoadGuildHouse (@0x1404d6210, guilddef_2.cpp:532-543) reads table/GuildHouse.xml, one <c>guildHouse</c> row
/// under &lt;ms2&gt; into a GuildHouse (FUN_1404d5070 @0x1404d5070, guilddef_2.cpp:402-417: IsElementAvailable, then level, theme, fieldID,
/// areaSize, facility (XmlTokenList: the attribute split at ',', empty tokens skipped), stringKey, upgradeCost, upgradeReqGuildLevel,
/// rethemeCost, imagePath, thumbnailPath), filed under (level, theme) (a later row of the same key replaces the earlier); GetHouse
/// (@0x1404d79b0) finds one, GetHouseKeys (@0x1404d7a90) lists the keys ascending.
/// STILL-OPEN: how XmlTokenList turns a token that is not a number into an int (here 0).
/// </summary>
public sealed class GuildHouseTable
{
    public const string File = "guildhouse.xml";

    /// <summary>GuildHouse: level (f18), theme (f1c), stringKey (f20), areaSize (f24), upgradeCost (f28), upgradeReqGuildLevel (f2c),
    /// rethemeCost (f30), facility ids (f38), fieldID (f50), thumbnailPath (f58), imagePath (f60).</summary>
    public sealed record House(int Level, int Theme, int StringKey, int AreaSize, int UpgradeCost, int UpgradeReqGuildLevel, int RethemeCost,
        IReadOnlyList<int> Facilities, uint FieldId, string ThumbnailPath, string ImagePath);

    readonly SortedDictionary<(int Level, int Theme), House> houses = new();

    /// <param name="available">IsElementAvailable of a row (its locale and feature); every row when null.</param>
    public GuildHouseTable(PackSet packs, Func<XElement, bool> available = null)
        : this(HousingTable.EntryOf(packs, File) is { } e ? Ms2.Formats.Text.PackXml.Parse(packs.Read("Xml", e)) : null, available) { }

    public GuildHouseTable(XDocument doc, Func<XElement, bool> available = null)
    {
        if (doc?.Root is not { Name.LocalName: "ms2" } root) return;
        foreach (var n in root.Elements("guildHouse"))
        {
            if (available != null && !available(n)) continue;
            var h = new House(Int(n, "level"), Int(n, "theme"), Int(n, "stringKey"), Int(n, "areaSize"), Int(n, "upgradeCost"),
                Int(n, "upgradeReqGuildLevel"), Int(n, "rethemeCost"), Tokens((string)n.Attribute("facility")),
                uint.TryParse((string)n.Attribute("fieldID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var f) ? f : 0,
                (string)n.Attribute("thumbnailPath") ?? "", (string)n.Attribute("imagePath") ?? "");
            houses[(h.Level, h.Theme)] = h;
        }
    }

    /// <summary>GetHouse(level, theme): the row, else null.</summary>
    public House Get(int level, int theme) => houses.TryGetValue((level, theme), out var h) ? h : null;

    /// <summary>GetHouseKeys: (level, theme) ascending (the client's std::map of a pair).</summary>
    public IEnumerable<(int Level, int Theme)> Keys() => houses.Keys;

    public int Count => houses.Count;

    static int Int(XElement n, string a) =>
        int.TryParse((string)n.Attribute(a), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    static int[] Tokens(string s) => (s ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(t => int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0).ToArray();
}
