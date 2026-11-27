using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Ui;

namespace Ms2.Formats.World;

/// <summary>
/// DungeonMissionDataTable (commonfiles/dungeonmissiondatatable.cpp, ctor @0x140318c80): Table/DungeonMission.xml. ReadFromXml (@0x140318d40)
/// takes the &lt;ms2&gt; root, then each available DungeonMission row through DungeonMissionData::Create (@0x140314d60): a row without a type is
/// dropped, the others are loaded and kept whatever their Load returned (Create does not look at it), and a row whose id is 0 is dropped.
/// The rows go in with std::map::insert, so the first row of an id stays. A missing file or root gives no rows.
/// STILL-OPEN: the per-type attributes (value1 and value2 of PlayTime, KillNpc, LastHitNpc, DamageBySkill and the others) are not read; the
/// dungeon room panels read only the base fields kept here.
/// </summary>
public sealed class DungeonMissionTable
{
    readonly SortedDictionary<uint, DungeonMissionData> missions = new();

    /// <summary>The table of a data set (<c>Table/DungeonMission.xml</c> through <see cref="TablePath"/>); empty without the file.</summary>
    public DungeonMissionTable(PackSet packs)
        : this(Root(packs), e => FeatureElements.IsAvailable(packs, e)) { }

    /// <summary>The table from the xml itself (tests): <paramref name="root"/> is the file's root (null for none), <paramref name="available"/> IsElementAvailable (every row when null).</summary>
    public DungeonMissionTable(XElement root, Func<XElement, bool> available)
    {
        if (root is not { Name.LocalName: "ms2" }) return;
        available ??= _ => true;
        foreach (var e in root.Elements("DungeonMission"))
        {
            if (!available(e)) continue;
            var d = DungeonMissionData.Create(e);
            if (d == null || d.Id == 0) continue;
            missions.TryAdd(d.Id, d);
        }
    }

    static XElement Root(PackSet packs)
    {
        string entry = packs == null ? null : TablePath.Resolve(packs, "DungeonMission.xml");
        return entry == null ? null : Ms2.Formats.Text.PackXml.Parse(packs.Read("Xml", entry))?.Root;
    }

    /// <summary>GetMission (@0x140318ce0): the mission of an id, null when none.</summary>
    public DungeonMissionData GetMission(uint id) => missions.TryGetValue(id, out var m) ? m : null;

    public int Count => missions.Count;

    /// <summary>Every mission, by id.</summary>
    public IEnumerable<DungeonMissionData> All => missions.Values;
}

/// <summary>DungeonMissionData (commonfiles/dungeonmissiondata.cpp, the base fields every mission type loads): a score of <see cref="MaxScore"/> split into <see cref="ApplyCount"/> steps.</summary>
public sealed class DungeonMissionData
{
    public uint Id;
    /// <summary>The type attribute; the client builds the class "DungeonMissionData" + type, the classes of types it does not know being the base class.</summary>
    public string Type = "";
    public short MaxScore;
    public short ApplyCount = 1;
    public bool IsPenaltyType;

    /// <summary>Create (@0x140314d60) and Load (@0x140314960): the row's id, type, maxScore, applyCount and isPenaltyType, each left alone when missing or unreadable (<see cref="XmlAttr"/>).
    /// Null for a row without a type. The base Load's checks (id 0, applyCount 0 or less, a score the steps do not divide) only log, and the callers ignore its result.</summary>
    public static DungeonMissionData Create(XElement e)
    {
        string type = XmlAttr.Str(e, "type");
        if (string.IsNullOrEmpty(type)) return null;
        var d = new DungeonMissionData();
        XmlAttr.UInt(e, "id", ref d.Id);
        d.Type = type;
        XmlAttr.Short(e, "maxScore", ref d.MaxScore);
        XmlAttr.Short(e, "applyCount", ref d.ApplyCount);
        XmlAttr.Bool(e, "isPenaltyType", ref d.IsPenaltyType);
        return d;
    }

    static short Clamp(short v, short lo, short hi) => v < lo ? lo : v > hi ? hi : v;

    /// <summary>GetScore (@0x140314c20): the score of <paramref name="count"/> applied steps; a penalty mission starts from the full score and loses a step's share each time.</summary>
    public short GetScore(short count)
    {
        short unit = MaxScore;
        if (ApplyCount > 1) unit = (short)(MaxScore / ApplyCount);
        short applied = Clamp(count, 0, ApplyCount);
        if (!IsPenaltyType) return Clamp((short)(unit * applied), 0, MaxScore);
        return Clamp((short)(MaxScore - unit * applied), 0, MaxScore);
    }

    /// <summary>The mission names the client's DungeonMissionCli::vfunc_4 and the room panels show: string table 0x3c (dungeonMissionName.xml) by <see cref="Id"/>.</summary>
    public const int NameTable = 0x3c;
}
