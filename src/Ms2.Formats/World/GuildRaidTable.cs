using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

/// <summary>
/// CGuildXmlData's raid table: LoadGuildRaid (@0x1404d67e0, guilddef_2.cpp:562-575) reads table/GuildRaid.xml, the <c>guildRaid</c> rows under
/// &lt;ms2&gt; (collected by FUN_14028faa0, commonutil.cpp:1748, keyed by "dungeonRoomID") into a GuildRaid each (FUN_1404d53d0 @0x1404d53d0,
/// guilddef_2.cpp:435-443: IsElementAvailable, then dungeonRoomID, clearRaidPoint, clearGuildFund, clearGuildExp), filed under its dungeon room id
/// (a later row of the same id replaces the earlier); GetRaid (@0x1404d7cb0) finds one, GetRaidIds (@0x1404d7d80) lists the ids ascending.
/// STILL-OPEN: when two available rows share a dungeonRoomID the collector keeps the one CFeatureManager::SelectFeatureElement picks; here the
/// later row wins, as in the other guild tables.
/// </summary>
public sealed class GuildRaidTable
{
    public const string File = "guildraid.xml";

    /// <summary>GuildRaid: dungeonRoomID (f18), clearRaidPoint (f1c), clearGuildFund (f20, default 1), clearGuildExp (f24, default 1).</summary>
    public sealed record Raid(uint DungeonRoomId, int ClearRaidPoint, float ClearGuildFund, float ClearGuildExp);

    readonly SortedDictionary<uint, Raid> raids = new();

    /// <param name="available">IsElementAvailable of a row (its locale and feature); every row when null.</param>
    public GuildRaidTable(PackSet packs, Func<XElement, bool> available = null)
        : this(HousingTable.EntryOf(packs, File) is { } e ? Ms2.Formats.Text.PackXml.Parse(packs.Read("Xml", e)) : null, available) { }

    public GuildRaidTable(XDocument doc, Func<XElement, bool> available = null)
    {
        if (doc?.Root is not { Name.LocalName: "ms2" } root) return;
        foreach (var n in root.Elements("guildRaid"))
        {
            if (available != null && !available(n)) continue;
            var r = new Raid(UInt(n, "dungeonRoomID"), Int(n, "clearRaidPoint"), Float(n, "clearGuildFund"), Float(n, "clearGuildExp"));
            raids[r.DungeonRoomId] = r;
        }
    }

    /// <summary>GetRaid(id): the row, else null.</summary>
    public Raid Get(uint id) => raids.TryGetValue(id, out var r) ? r : null;

    /// <summary>GetRaidIds: ids ascending (the client's std::map).</summary>
    public IEnumerable<uint> Ids() => raids.Keys;

    public int Count => raids.Count;

    static int Int(XElement n, string a) =>
        int.TryParse((string)n.Attribute(a), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    static uint UInt(XElement n, string a) =>
        uint.TryParse((string)n.Attribute(a), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    static float Float(XElement n, string a) =>
        float.TryParse((string)n.Attribute(a), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 1f;
}
