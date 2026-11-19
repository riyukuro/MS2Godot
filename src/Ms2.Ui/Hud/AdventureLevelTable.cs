using System.Xml.Linq;
using Ms2.Ui.Skills;

namespace Ms2.Ui.Hud;

/// <summary>
/// CAdventureLevelTable (ms2/commonfiles/adventureleveltablehelper.cpp): Table/AdventureLevelReward.xml,
/// AdventureLevelAbility.xml and, with feature 0xfb, AdventureLevelMission.xml (LoadAll :291), and the
/// CAdventureLevelTableHelper's current ability (FindCurrent @0x1401954e0).
/// </summary>
public sealed class AdventureLevelTable
{
    /// <summary>CAdventureLevelReward: m_nType (1 item, 2 stat point, 0 other), m_nId, m_nRank, m_nValue.</summary>
    public sealed record Reward(int Type, uint Id, int Rank, int Value);

    /// <summary>CAdventureLevelAbility: additionalEffectId, id, requireLevel, interval, maxCount, startValue, addValue.</summary>
    public sealed record Ability(uint EffectId, int Id, uint RequireLevel, int Interval, int MaxCount, float StartValue, float AddValue)
    {
        /// <summary>GetCount (@0x140195910): the ability's step at the adventure level.</summary>
        public ushort GetCount(uint level)
        {
            if (Id == 0) return 0;
            if (Interval == 0) return (ushort)(level >= RequireLevel ? 1 : 0);
            if (level < RequireLevel) return 0;
            return Math.Min((ushort)((level - RequireLevel + (uint)Interval) / (uint)Interval), (ushort)MaxCount);
        }

        /// <summary>GetLevelOfCount (@0x1401959d0): the level the current step started at.</summary>
        public int GetLevelOfCount(uint level) => Id == 0 ? 0 : (GetCount(level) - 1) * Interval + (int)RequireLevel;

        /// <summary>GetPercent (@0x140195a20).</summary>
        public float GetPercent(uint level) => Id == 0 ? 0f : (StartValue + (float)(GetCount(level) - 1) * AddValue) * 100.0f;
    }

    /// <summary>CAdventureLevelMission: missionCount, m_nOne (1 for every loaded row), itemId, itemRank, itemCount.</summary>
    public sealed record Mission(uint Count, int One, uint ItemId, int ItemRank, int ItemCount);

    /// <summary>The empty rows (DAT_142ceae98, DAT_142ceaea8, DAT_142ceaec8) GetReward / GetAbility / GetMission return
    /// for a missing key; IsEmptyReward etc. compare against them.</summary>
    public static readonly Reward EmptyReward = new(0, 0, 0, 0);
    public static readonly Ability EmptyAbility = new(0, 0, 0, 0, 0, 0, 0);
    public static readonly Mission EmptyMission = new(0, 0, 0, 0, 0);

    public const int MissionFeature = 0xfb;

    readonly Dictionary<uint, Reward> rewards = new();       // m_mapReward
    readonly Dictionary<uint, Ability> abilities = new();    // m_mapAbility
    readonly Dictionary<uint, Mission> missions = new();     // m_mapMission

    /// <summary>m_vecLevels: every row's level, sorted by its low 16 bits (std::less&lt;unsigned short&gt;; with 32 rows
    /// or fewer std::sort is an insertion sort, so equal keys keep the file order).</summary>
    public readonly List<uint> Levels = new();
    /// <summary>m_vecAbilityIds: every row's effect id, sorted by required level (AbilityLess @0x140196b70, whose tie
    /// test compares a row with itself; for 32 rows or fewer the file order of a tie stays).</summary>
    public readonly List<uint> AbilityIds = new();
    /// <summary>CAdventureLevelTableHelper::m_pCurrent: the first ability, in <see cref="AbilityIds"/> order, whose
    /// level 1 effect has eventBuffType 3; null when none.</summary>
    public Ability Current;

    /// <summary>
    /// LoadAll (:291) and FindCurrent (ms2.cpp:2057 runs it after the tables load): each &lt;reward&gt;, &lt;ability&gt;
    /// and &lt;mission&gt; under &lt;ms2&gt; that IsElementAvailable; map.insert keeps the first row of a key, the
    /// vectors keep every row. A missing file loads nothing.
    /// </summary>
    public static AdventureLevelTable Load(XDocument reward, XDocument ability, XDocument mission, Func<XElement, bool> available,
                                           bool missionFeature, AdditionalTable effects)
    {
        var t = new AdventureLevelTable();
        if (reward?.Root is { Name.LocalName: "ms2" } rr)
        {
            foreach (var e in rr.Elements("reward"))
            {
                if (!available(e)) continue;
                uint level = (ushort)Xml.Int(e, "level");                       // FUN_140197310: an unsigned short
                var row = new Reward(ParseRewardType(((string)e.Attribute("type") ?? "").Trim()),
                                     Xml.UInt(e, "id"), Xml.Int(e, "rank"), Xml.Int(e, "value"));
                t.rewards.TryAdd(level, row);
                t.Levels.Add(level);
            }
            var sorted = t.Levels.OrderBy(l => (ushort)l).ToList();       // stable
            t.Levels.Clear();
            t.Levels.AddRange(sorted);
        }
        if (ability?.Root is { Name.LocalName: "ms2" } ar)
        {
            foreach (var e in ar.Elements("ability"))
            {
                if (!available(e)) continue;
                var row = new Ability(Xml.UInt(e, "additionalEffectId"), Xml.Int(e, "id"), Xml.UInt(e, "requireLevel"),
                                      Xml.Int(e, "interval"), Xml.Int(e, "maxCount"), Xml.Float(e, "startValue"), Xml.Float(e, "addValue"));
                t.abilities.TryAdd(row.EffectId, row);
                t.AbilityIds.Add(row.EffectId);
            }
            // STILL-OPEN: over 32 rows std::sort's introsort would order the ties otherwise; the data has 15
            var sorted = t.AbilityIds.OrderBy(id => t.GetAbility(id).RequireLevel).ToList();
            t.AbilityIds.Clear();
            t.AbilityIds.AddRange(sorted);
        }
        if (missionFeature && mission?.Root is { Name.LocalName: "ms2" } mr)
        {
            foreach (var e in mr.Elements("mission"))
            {
                if (!available(e)) continue;
                t.missions.TryAdd(Xml.UInt(e, "missionId"),
                    new Mission(Xml.UInt(e, "missionCount"), 1, Xml.UInt(e, "itemId"), Xml.Int(e, "itemRank"), Xml.Int(e, "itemCount")));
            }
        }
        // FindCurrent: the client dereferences the level 1 effect unchecked; a missing one is skipped here
        foreach (uint id in t.AbilityIds)
        {
            var a = t.GetAbility(id);
            if (effects?.GetLevel(a.EffectId, 1) is { EventBuffType: 3 }) { t.Current = a; break; }
        }
        return t;
    }

    /// <summary>ParseRewardType (@0x140195b20).</summary>
    static int ParseRewardType(string s) =>
        string.Equals(s, "statPoint", StringComparison.OrdinalIgnoreCase) ? 2
        : string.Equals(s, "item", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

    /// <summary>GetReward (:335): the empty row when missing.</summary>
    public Reward GetReward(uint level) => rewards.TryGetValue(level, out var r) ? r : EmptyReward;
    /// <summary>GetAbility (:344).</summary>
    public Ability GetAbility(uint effectId) => abilities.TryGetValue(effectId, out var a) ? a : EmptyAbility;
    /// <summary>GetMission (:353).</summary>
    public Mission GetMission(uint missionId) => missions.TryGetValue(missionId, out var m) ? m : EmptyMission;

    /// <summary>CountUnclaimedRewards (@0x140195f70): the rewards at or below the level, not stat points, not claimed.</summary>
    public int CountUnclaimedRewards(uint level, IReadOnlySet<uint> claimed)
    {
        int n = 0;
        foreach (var (l, r) in rewards)
            if (l <= level && r.Type != 2 && !claimed.Contains(l)) n++;
        return n;
    }
}
