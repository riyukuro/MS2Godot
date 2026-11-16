using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2Godot.Ui.Create;

/// <summary>
/// The tables character creation reads from the player's Xml.m2d, as the client does:
/// table/charactercreateselect.xml (UICharacterCreateSelectDialog::LoadCharacterCreateSelect: the job tabs, their order,
/// the disabled and preselected jobs), table/lobbyfield.xml (LobbyFieldTable: the create map per job),
/// table/fielddata.xml (map id to xblock) and table/defaultitems.xml (CItemDefaultData: the items each slot offers).
/// Strings: string/&lt;lang&gt;/jobgroupname.xml and stringcharactercreateselect.xml.
/// </summary>
public sealed class CreateTables
{
    public sealed record Job(int JobCode, string Name, string Desc, string Movie, bool Enabled, bool Event);

    public readonly List<Job> Jobs = new();          // tabs in display order
    public int SelectJob;                             // selectJobCode of the display row
    public readonly List<int> RandomJobs = new();     // randomselectJobCode
    readonly Dictionary<int, int> createMap = new(), selectMap = new();
    readonly List<int> selectMaps = new();
    readonly PackSet packs;
    readonly string lang;

    public CreateTables(PackSet packs, string lang = "en")
    {
        this.packs = packs; this.lang = lang;
        LoadJobs();
        LoadLobbyField();
    }

    XDocument Xml(string entry) => packs.Exists("Xml", entry)
        ? XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('﻿'))
        : null;

    Dictionary<int, string> Strings(string file)
    {
        var d = new Dictionary<int, string>();
        var x = Xml($"string/{lang}/{file}");
        if (x == null) return d;
        foreach (var k in x.Root.Elements("key"))
            if (int.TryParse((string)k.Attribute("id"), out int id)) d.TryAdd(id, (string)k.Attribute("name"));
        return d;
    }

    static List<int> Ints(string s) => (s ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(t => int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0).Where(v => v != 0).ToList();

    void LoadJobs()
    {
        var x = Xml("table/charactercreateselect.xml");
        if (x == null) return;
        var names = Strings("jobgroupname.xml");
        var descs = Strings("stringcharactercreateselect.xml");
        var rows = new Dictionary<int, (string Movie, int DescKey)>();
        XElement display = null, displayAny = null;
        foreach (var g in x.Root.Elements("group"))
        {
            var kind = (string)g.Attribute("name");
            if (string.Equals(kind, "data", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var l in g.Elements("list"))
                    if (int.TryParse((string)l.Attribute("jobCode"), out int jc) && jc != 0)
                        rows[jc] = ((string)l.Attribute("movie"), int.TryParse((string)l.Attribute("descKey"), out int dk) ? dk : 0);
            }
            else if (string.Equals(kind, "display", StringComparison.OrdinalIgnoreCase))
            {
                // the player's locale group wins over the group without a locale; this client is the KR one
                var locale = (string)g.Attribute("locale");
                if (string.IsNullOrEmpty(locale)) displayAny = g;
                else if (locale == "KR") display = g;
            }
        }
        // every feature is on: the last listed row is the highest feature level
        var sel = (display ?? displayAny)?.Elements("list").LastOrDefault();
        if (sel == null) return;
        var disabled = Ints((string)sel.Attribute("disableJobCode"));
        var events = Ints((string)sel.Attribute("eventJobCode"));
        RandomJobs.AddRange(Ints((string)sel.Attribute("randomselectJobCode")));
        int.TryParse((string)sel.Attribute("selectJobCode"), out int select);
        foreach (var jc in Ints((string)sel.Attribute("order")))
        {
            if (!rows.TryGetValue(jc, out var r)) continue;
            Jobs.Add(new Job(jc, names.GetValueOrDefault(jc, jc.ToString()), r.DescKey != 0 ? descs.GetValueOrDefault(r.DescKey, "") : "",
                r.Movie, !disabled.Contains(jc), events.Contains(jc)));
            if (jc == select) SelectJob = jc;
        }
    }

    /// <summary>FUN_140f30150: a random job of randomselectJobCode, or selectJobCode.</summary>
    public int StartJob(Random rng) => RandomJobs.Count > 0 ? RandomJobs[rng.Next(RandomJobs.Count)] : SelectJob;

    void LoadLobbyField()
    {
        var x = Xml("table/lobbyfield.xml");
        if (x == null) return;
        foreach (var k in x.Root.Elements("key"))
        {
            if (!int.TryParse((string)k.Attribute("jobCode"), out int jc) || jc == 0) continue;
            int.TryParse((string)k.Attribute("selectMap"), out int sm);
            int.TryParse((string)k.Attribute("createMap"), out int cm);
            selectMap[jc] = sm; createMap[jc] = cm;
            if (cm == 0) selectMaps.Add(sm);
        }
    }

    /// <summary>LobbyFieldTable::GetCreateMap: the job's create map, else a select map.</summary>
    public int CreateMap(int jobCode) => createMap.TryGetValue(jobCode, out int m) && m != 0 ? m : selectMaps.FirstOrDefault(SelectMap(1));

    /// <summary>LobbyFieldTable::GetSelectMap (job 0 counts as 1).</summary>
    public int SelectMap(int jobCode) => selectMap.TryGetValue(jobCode == 0 ? 1 : jobCode, out int m) ? m : selectMap.GetValueOrDefault(1);

    /// <summary>The xblock name of a map id (table/fielddata.xml, the environment without a locale).</summary>
    public string XBlockOf(int mapId)
    {
        var x = Xml("table/fielddata.xml");
        var fd = x?.Root.Elements("fieldData").FirstOrDefault(e => (int?)e.Attribute("id") == mapId);
        var env = fd?.Elements("environment").FirstOrDefault(e => e.Attribute("locale") == null);
        return (string)env?.Element("xblock")?.Attribute("name");
    }
}
