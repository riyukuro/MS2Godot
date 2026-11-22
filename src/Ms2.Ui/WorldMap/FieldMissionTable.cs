using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.WorldMap;

/// <summary>
/// CFieldMissionTable (commonfiles/fieldmissiontable.cpp): the exploration rewards, one per round, from
/// Table/fieldMission.xml. A round is the number of completed field missions it needs (the &lt;round mission="n"&gt;
/// attribute); its reward is an item ("itemId,rank,count"), stat points or skill points of a sub job level.
/// </summary>
public sealed class FieldMissionTable
{
    /// <summary>CFieldMissionReward: m_nType (1 item, 2 statpoint, 3 skillpoint, FUN_14037c8c0), m_nValue, m_item, m_nSubJob.</summary>
    public sealed class Reward
    {
        public int Type, Value;
        public int ItemId, ItemRank = 1, ItemCount = 1;     // ItemEntity's defaults (m_rank 1, m_count 1)
        /// <summary>m_nSubJob: subJobLevel 1 is Unk_SubJobLevel_2, anything else Unk_SubJobLevel_1 (the default).</summary>
        public int SubJobLevel = 1;
    }

    /// <summary>m_rewards: an ordered map by round (ATL CRBMap, SetAt keeps the last row of a round).</summary>
    readonly SortedDictionary<int, Reward> rewards = new();

    public IReadOnlyDictionary<int, Reward> Rewards => rewards;

    /// <summary>
    /// Load (:96): GetXmlPath("Table", "fieldMission.xml"), the region's folder first (table/kr/), then the plain path; an
    /// empty table when there is no file or no &lt;ms2&gt; root.
    /// </summary>
    public static FieldMissionTable Load(PackSet packs)
    {
        return TablePath.Resolve(packs, "fieldmission.xml") is { } p ? Parse(GameStrings.Parse(packs.Read("Xml", p))) : new FieldMissionTable();
    }

    public static FieldMissionTable Parse(XDocument doc)
    {
        var t = new FieldMissionTable();
        if (doc.Root?.Name != "ms2") return t;
        foreach (var r in doc.Root.Elements("round"))
        {
            var reward = new Reward { Type = TypeOf((string)r.Attribute("type")) };
            int round = Int((string)r.Attribute("mission"));
            if (reward.Type == 1)
            {
                // ItemEntity::vfunc_1 (commonstruct_wa.cpp:52): "id,rank,count", each part only when present
                var parts = ((string)r.Attribute("value") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1) reward.ItemId = Int(parts[0]);
                if (parts.Length >= 2) reward.ItemRank = Int(parts[1]);
                if (parts.Length >= 3) reward.ItemCount = Int(parts[2]);
            }
            else
            {
                reward.Value = Int((string)r.Attribute("value"));
                reward.SubJobLevel = Int((string)r.Attribute("subJobLevel")) == 1 ? 2 : 1;
            }
            t.rewards[round] = reward;
        }
        return t;
    }

    /// <summary>FUN_14037c8c0: "item" 1, "statpoint" 2, "skillpoint" 3 (case-insensitive), else 0.</summary>
    static int TypeOf(string s) => (s ?? "").ToLowerInvariant() switch { "item" => 1, "statpoint" => 2, "skillpoint" => 3, _ => 0 };

    static int Int(string s) => int.TryParse(s?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    /// <summary>GetReward (:136): the round's reward, null when there is none.</summary>
    public Reward GetReward(int round) => rewards.TryGetValue(round, out var r) ? r : null;

    /// <summary>GetPrevRound (:144): the round before <paramref name="round"/>, 0 when it is the first or not a round.</summary>
    public int GetPrevRound(int round)
    {
        int prev = 0;
        foreach (int k in rewards.Keys)
        {
            if (k == round) return prev;
            prev = k;
        }
        return 0;
    }

    /// <summary>GetNextRound (:161): the first round for 0, else the round after <paramref name="round"/> (0 when none).</summary>
    public int GetNextRound(int round)
    {
        bool found = false;
        foreach (int k in rewards.Keys)
        {
            if (round == 0 || found) return k;
            if (k == round) found = true;
        }
        return 0;
    }

    /// <summary>GetMaxRound (:184): the largest round, 0 when there is none.</summary>
    public int GetMaxRound() => rewards.Count == 0 ? 0 : Math.Max(0, rewards.Keys.Max());
}
