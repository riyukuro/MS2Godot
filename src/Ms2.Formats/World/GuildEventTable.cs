using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

/// <summary>
/// CGuildXmlData's event table: LoadGuildEvent (@0x1404d5fa0, guilddef_2.cpp:519-529) reads table/GuildEvent.xml, one <c>guildEvent</c> row under
/// &lt;ms2&gt; into a GuildEvent (FUN_1404d4f10 @0x1404d4f10, guilddef_2.cpp:389-396: IsElementAvailable, then id, mapCode, imagePath), filed under its
/// id (a later row of the same id replaces the earlier); GetEvent (@0x1404d7830) finds one, GetEventIds (@0x1404d7900) lists the ids ascending.
/// </summary>
public sealed class GuildEventTable
{
    public const string File = "guildevent.xml";

    /// <summary>GuildEvent: id (f18), mapCode (f1c), imagePath (f20).</summary>
    public sealed record Event(int Id, int MapCode, string ImagePath);

    readonly SortedDictionary<int, Event> events = new();

    /// <param name="available">IsElementAvailable of a row (its locale and feature); every row when null.</param>
    public GuildEventTable(PackSet packs, Func<XElement, bool> available = null)
        : this(HousingTable.EntryOf(packs, File) is { } e ? Ms2.Formats.Text.PackXml.Parse(packs.Read("Xml", e)) : null, available) { }

    public GuildEventTable(XDocument doc, Func<XElement, bool> available = null)
    {
        if (doc?.Root is not { Name.LocalName: "ms2" } root) return;
        foreach (var n in root.Elements("guildEvent"))
        {
            if (available != null && !available(n)) continue;
            var e = new Event(Int(n, "id"), Int(n, "mapCode"), (string)n.Attribute("imagePath") ?? "");
            events[e.Id] = e;
        }
    }

    /// <summary>GetEvent(id): the row, else null.</summary>
    public Event Get(int id) => events.TryGetValue(id, out var e) ? e : null;

    /// <summary>GetEventIds: ids ascending (the client's std::map).</summary>
    public IEnumerable<int> Ids() => events.Keys;

    public int Count => events.Count;

    static int Int(XElement n, string a) =>
        int.TryParse((string)n.Attribute(a), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
