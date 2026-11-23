using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;

namespace Ms2Godot.Ui.Create;

/// <summary>
/// The tables character creation reads from the player's Xml.m2d, as the client does:
/// table/lobbyfield.xml (LobbyFieldTable: the create map per job),
/// table/fielddata.xml (map id to xblock) and table/defaultitems.xml (CItemDefaultData: the items each slot offers).
/// The job tabs (table/charactercreateselect.xml) are Ms2.Ui.CharacterCreateSelectController's.
/// </summary>
public sealed class CreateTables
{
    readonly Dictionary<int, int> createMap = new(), selectMap = new();
    readonly List<int> selectMaps = new();
    readonly PackSet packs;
    readonly Ms2.Ui.Skills.JobTable jobTable;

    public CreateTables(PackSet packs)
    {
        this.packs = packs;
        jobTable = Ms2.Ui.Skills.JobTable.Load(packs);
        LoadLobbyField();
    }

    XDocument Xml(string entry) => packs.Exists("Xml", entry)
        ? PackXml.Parse(packs.Read("Xml", entry))
        : null;

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

    /// <summary>JobTable::GetSubJob1: the sub job 1 of the job group (0 when the table has none), from either layout of table/job.xml.</summary>
    public int SubJob1(int jobGroup) => jobTable.GetSubJob1(jobGroup);

    /// <summary>The xblock name of a map id (table/fielddata.xml, the environment without a locale).</summary>
    public string XBlockOf(int mapId)
    {
        var x = packs.Exists("Xml", "table/fielddata.xml") ? Xml("table/fielddata.xml") : Ms2.Formats.World.FieldData.Document(packs);
        var fd = x?.Root.Elements("fieldData").FirstOrDefault(e => (int?)e.Attribute("id") == mapId);
        var env = fd?.Elements("environment").FirstOrDefault(e => e.Attribute("locale") == null);
        return (string)env?.Element("xblock")?.Attribute("name");
    }
}
