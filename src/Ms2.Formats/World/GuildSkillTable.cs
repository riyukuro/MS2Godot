using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

/// <summary>
/// CGuildXmlData's skill table: LoadGuildBuff (@0x1404d5d20, guilddef_2.cpp:505-518) reads table/guildBuff.xml, one
/// <c>guildBuff</c> row under &lt;ms2&gt; into a GuildSkill (@0x1404d4cb0) filed under its id, in document order; GetSkill (@0x1404d74e0) finds the row of a
/// level, GetGuildSkillIds (@0x1404d7630) lists the ids whose first row is a guild (not a personal) skill.
/// </summary>
public sealed class GuildSkillTable
{
    public const string File = "guildbuff.xml";

    /// <summary>GuildSkill: id (f18), level (f1c), requireLevel (f20), upgradeCost (f24), cost (f28), duration in seconds (f2c, the xml's
    /// minutes x 60), cooltime in seconds (f30), additionalEffectId (f34), additionalEffectLevel (f38), type (f3c), functionName.</summary>
    public sealed record Skill(int Id, int Level, int RequireLevel, int UpgradeCost, int Cost, int Duration, int Cooltime,
        int AdditionalEffectId, int AdditionalEffectLevel, int Type, string FunctionName)
    {
        /// <summary>FUN_1404d4e40: id above 10000, the personal skills.</summary>
        public bool IsPersonal => Id > 10000;
        /// <summary>FUN_1404d4e60: id up to 10000, the guild skills.</summary>
        public bool IsGuild => Id <= 10000;
        /// <summary>FUN_1404d4e80: id above 1000 and up to 10000, the event skills.</summary>
        public bool IsEvent => Id > 1000 && Id <= 10000;
    }

    readonly SortedDictionary<int, List<Skill>> skills = new();

    /// <param name="available">IsElementAvailable of a row (its locale and feature); every row when null.</param>
    public GuildSkillTable(PackSet packs, Func<XElement, bool> available = null)
        : this(HousingTable.EntryOf(packs, File) is { } e ? Ms2.Formats.Text.PackXml.Parse(packs.Read("Xml", e)) : null, available) { }

    public GuildSkillTable(XDocument doc, Func<XElement, bool> available = null)
    {
        if (doc?.Root is not { Name.LocalName: "ms2" } root) return;
        foreach (var n in root.Elements("guildBuff"))
        {
            if (available != null && !available(n)) continue;
            var s = new Skill(Int(n, "id"), Int(n, "level"), Int(n, "requireLevel"), Int(n, "upgradeCost"), Int(n, "cost"),
                Int(n, "duration") * 60, Int(n, "cooltime") * 60, Int(n, "additionalEffectId"), Int(n, "additionalEffectLevel"),
                Int(n, "type"), (string)n.Attribute("functionName") ?? "");
            if (!skills.TryGetValue(s.Id, out var list)) skills[s.Id] = list = new List<Skill>();
            list.Add(s);
        }
    }

    /// <summary>GetSkill(id, level): the first row of that id whose level matches, else null.</summary>
    public Skill Get(int id, int level) =>
        skills.TryGetValue(id, out var list) ? list.FirstOrDefault(s => s.Level == level) : null;

    /// <summary>GetGuildSkillIds: ids ascending (the client's std::map), whose first row is a guild skill.</summary>
    public IEnumerable<int> GuildSkillIds() => skills.Where(k => k.Value.Count > 0 && k.Value[0].IsGuild).Select(k => k.Key);

    public int Count => skills.Count;

    static int Int(XElement e, string name) =>
        int.TryParse((string)e.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
}
