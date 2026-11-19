using System.Xml.Linq;

namespace Ms2.Ui;

/// <summary>
/// The fishing spot rows (CFishingTable::LoadSpots, fishsimultedata.cpp:331; CFishingSpotData::Load :230) and the fishing
/// mastery grades (CMasteryTable::LoadMastery, masterytable.cpp:173, type 1), for CFishingManager::GetSpotDescription.
/// </summary>
public sealed class FishingSpots
{
    sealed record Spot(uint MinMastery, List<int> Liquids);

    readonly Dictionary<int, Spot> spots = new();
    /// <summary>m_mastery[1]: grade to the value it starts at, in grade order (CRBMap).</summary>
    readonly SortedDictionary<int, uint> grades = new();

    /// <param name="available">IsElementAvailable on each &lt;spot&gt; / &lt;v&gt; row.</param>
    public FishingSpots(XDocument spotXml, XDocument masteryXml, Func<XElement, bool> available)
    {
        foreach (var e in spotXml?.Root?.Elements("spot") ?? Enumerable.Empty<XElement>())
        {
            if (!available(e)) continue;
            int id = (int)UInt(e, "id");
            var liquids = new List<int>();
            foreach (var l in ((string)e.Attribute("liquidType") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (HabitatId(l.Trim()) is var h && h != 8) liquids.Add(h);
            if (id != 0) spots[id] = new Spot(UInt(e, "minMastery"), liquids);   // m_mapSpot.SetAt: a later row replaces
        }
        foreach (var t in masteryXml?.Root?.Elements("mastery") ?? Enumerable.Empty<XElement>())
        {
            if ((int?)t.Attribute("type") != 1) continue;
            foreach (var v in t.Elements("v"))
                if (available(v)) grades[(int?)v.Attribute("grade") ?? 0] = UInt(v, "value");
        }
    }

    static uint UInt(XElement e, string name) => uint.TryParse((string)e.Attribute(name), out var v) ? v : 0;

    /// <summary>CFishingTable::GetHabitatId @0x1403b1f70 (fishsimultedata.cpp:279); 8 = unknown.</summary>
    public static int HabitatId(string s) => s switch
    {
        "water" => 0, "seawater" => 1, "poison" => 3, "lava" => 2, "oil" => 4, "devilwater" => 5, "emeraldwater" => 6,
        "all" => 7, _ => 8,
    };

    /// <summary>CFishingTable::GetHabitatName @0x1403b20b0 (:301): common strings 0xbd0..0xbd6.</summary>
    static string HabitatKey(int h) => h switch
    {
        0 => "s_fishing_habitat_water", 1 => "s_fishing_habitat_seawater", 3 => "s_fishing_habitat_poison",
        2 => "s_fishing_habitat_lava", 4 => "s_fishing_habitat_oil", 5 => "s_fishing_habitat_devilwater",
        6 => "s_fishing_habitat_emeraldwater", _ => null,
    };

    /// <summary>CMasteryTable::MaxGrade(1) @0x14066b080 (masterytable.cpp:380): the last grade, 0 without one.</summary>
    public int MaxGrade => grades.Count == 0 ? 0 : grades.Keys.Last();

    /// <summary>CMasteryTable::Value(1, grade) @0x14066b0d0 (:427): the value the grade starts at; uint.MaxValue (the client's
    /// (unsigned)-1) below grade 1, above the last grade or for a missing grade.</summary>
    public uint Value(int grade) => grade >= 1 && grade <= MaxGrade && grades.TryGetValue(grade, out var v) ? v : uint.MaxValue;

    /// <summary>CMasteryTable::Grade(1, value) @0x14066af60 (masterytable.cpp:364): one below the first grade whose value
    /// is above <paramref name="value"/>, else the last grade.</summary>
    public int Grade(uint value)
    {
        foreach (var (grade, start) in grades)
            if (start > value) return grade - 1;
        return grades.Count == 0 ? 0 : grades.Keys.Last();
    }

    /// <summary>
    /// CFishingManager::GetSpotDescription @0x140cdc000 (fishingmanager.cpp:1102): the spot's habitat names joined by ", ",
    /// its level name (FUN_1403b2240: common string 0xbba + Grade(1, minMastery)), formatted into common string 0x718
    /// s_worldmap_tooltip_fishingspot_low, or 0x719 s_worldmap_tooltip_fishingspot_high when the player's fishing mastery is
    /// below the spot's. "" for a field without a spot.
    /// </summary>
    /// <param name="common">GetStringCommonByKey.</param>
    public string Description(int fieldId, uint mastery, Func<string, string> common)
    {
        if (!spots.TryGetValue(fieldId, out var spot)) return "";
        var habitat = string.Join(", ", spot.Liquids.Select(h => HabitatKey(h) is { } k ? common(k) ?? "" : ""));
        habitat = habitat.TrimEnd(',', ' ');   // the client appends ", " to each and TrimRight(", ")
        var level = common(LevelKey(Grade(spot.MinMastery))) ?? "";
        var text = common(mastery < spot.MinMastery ? "s_worldmap_tooltip_fishingspot_high" : "s_worldmap_tooltip_fishingspot_low");
        return string.IsNullOrEmpty(text) ? "" : Ms2.Quest.ClientFormat.Format(text, habitat, level);   // FUN_14028dee0
    }

    /// <summary>Common string 0xbba + n: s_fishing_grade_Lv{n} for n &gt; 0 (commonstringtable.cpp, ids 0xbbb on).</summary>
    public static string LevelKey(int n) => n > 0 ? "s_fishing_grade_Lv" + n : "s_err_ugcmap_nothing_to_export";
}
