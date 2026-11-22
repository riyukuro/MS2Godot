using System.Globalization;
using System.Xml.Linq;

namespace Ms2.Ui.Skills;

/// <summary>One &lt;v&gt; of a job's &lt;skills&gt; (JobSkillData, commonfiles/jobtable.cpp:74).</summary>
public sealed class JobSkillData
{
    public uint SkillId;
    /// <summary>m_uiPosition: the tree cell (column, row) the skill window places the skill at.</summary>
    public int UiX, UiY;
    public bool UiHighlight;
    public int QuickSlotPriority = 0x7fffffff;
    /// <summary>m_jobLevel: the rank whose tree has the skill (1 or 2).</summary>
    public int JobLevel;
    public readonly List<uint> SubSkillIds = new();
    /// <summary>m_isLearn: learnt with the rank, without points.</summary>
    public bool IsLearn;
    public readonly List<ushort> RequireLevel = new();
    public readonly List<uint> RequireSkillIds = new();
    public readonly List<ushort> RequireSkillLevels = new();
    public uint RequireQuestId;

    /// <summary>JobSkillData::Load @0x140643720 (jobtable.cpp:97).</summary>
    public static JobSkillData Load(XElement e)
    {
        var d = new JobSkillData
        {
            SkillId = Xml.UInt(e, "skillID"),
            UiHighlight = Xml.Bool(e, "uiHighlight"),
            JobLevel = Xml.Int(e, "jobLevel"),
            IsLearn = Xml.Bool(e, "isLearn"),
            RequireQuestId = Xml.UInt(e, "requireQuestID"),
        };
        // XmlValue(CPoint) @0x140645190 (:82): two "," tokens through FUN_140718170
        if (e.Attribute("uiPosition") is { } p)
        {
            var t = Xml.Tokens((string)p);
            d.UiX = t.Count > 0 ? Xml.Atoi(t[0]) : 0;
            d.UiY = t.Count > 1 ? Xml.Atoi(t[1]) : 0;
        }
        if (e.Attribute("quickSlotPriority") != null) d.QuickSlotPriority = Xml.Int(e, "quickSlotPriority");
        foreach (var t in Xml.Tokens((string)e.Attribute("subSkillIDs"))) d.SubSkillIds.Add((uint)Xml.Atoi(t));
        foreach (var t in Xml.Tokens((string)e.Attribute("requireLevel"))) d.RequireLevel.Add((ushort)Xml.Atoi(t));
        foreach (var t in Xml.Tokens((string)e.Attribute("requireSkillIDs"))) d.RequireSkillIds.Add((uint)Xml.Atoi(t));
        foreach (var t in Xml.Tokens((string)e.Attribute("requireSkillLevels"))) d.RequireSkillLevels.Add((ushort)Xml.Atoi(t));
        return d;
    }

    /// <summary>HasRequirement @0x140643880 (:114).</summary>
    public bool HasRequirement => RequireLevel.Count > 0 || RequireSkillIds.Count > 0 || RequireQuestId != 0;

    /// <summary>GetRequireLevel @0x1406438e0 (:127): the player level the skill level needs; past the list, its last entry.</summary>
    public ushort GetRequireLevel(int skillLevel)
    {
        if (RequireLevel.Count == 0 || skillLevel <= 0) return 0;
        if (RequireLevel.Count < skillLevel) return RequireLevel[^1];
        return RequireLevel[skillLevel - 1];
    }

    /// <summary>The require skill level of entry <paramref name="i"/> (CAtlArray index; 0 past the list, as an empty slot).</summary>
    public ushort RequireSkillLevel(int i) => i < RequireSkillLevels.Count ? RequireSkillLevels[i] : (ushort)0;
}

/// <summary>One &lt;job&gt; of table/job.xml (JobData, jobtable.cpp:139).</summary>
public sealed class JobData
{
    public int JobGroupId;
    public int SubJob1, SubJob2;
    public uint StartField = 2000001;
    /// <summary>m_skills / m_subSkills: std::map by skill id (insert keeps the first).</summary>
    readonly SortedDictionary<uint, JobSkillData> skills = new(), subSkills = new();

    /// <summary>JobData::Load @0x140643a60 (:145). Each &lt;v&gt; also sets the table's max level of its skill (0 when absent).</summary>
    internal static JobData Load(XElement e, Dictionary<uint, ushort> maxLevels)
    {
        var j = new JobData
        {
            JobGroupId = Xml.Int(e, "jobGroupID"),
            SubJob1 = Xml.Int(e, "subJob1"),
            SubJob2 = Xml.Int(e, "subJob2"),
        };
        if (e.Attribute("startField") != null) j.StartField = Xml.UInt(e, "startField");
        if (e.Element("skills") is { } s)
            foreach (var v in s.Elements("v"))
            {
                var d = JobSkillData.Load(v);
                j.skills.TryAdd(d.SkillId, d);
                foreach (var sub in d.SubSkillIds) j.subSkills.TryAdd(sub, d);
                maxLevels[d.SkillId] = (ushort)Xml.Int(v, "maxLevel");      // s_skillMaxLevel.SetAt
            }
        return j;
    }

    /// <summary>GetSubJob @0x140643e60 (:181): 1 or 2, else none (0).</summary>
    public int GetSubJob(int index) => index == 1 ? SubJob1 : index == 2 ? SubJob2 : 0;

    /// <summary>FindSkill @0x140644150 (:253).</summary>
    public JobSkillData FindSkill(uint id) => skills.TryGetValue(id, out var d) ? d : null;
    /// <summary>FindSubSkill @0x140644210 (:263).</summary>
    public JobSkillData FindSubSkill(uint id) => subSkills.TryGetValue(id, out var d) ? d : null;
    /// <summary>GetSkillData @0x1406442d0 (:273): the skill's row, else the row of the skill it is a sub skill of.</summary>
    public JobSkillData GetSkillData(uint id) => FindSkill(id) ?? FindSubSkill(id);
    public bool HasSkill(uint id) => GetSkillData(id) != null;
    /// <summary>GetSkills @0x140644380 (:283): the skill rows in id order.</summary>
    public IEnumerable<JobSkillData> Skills => skills.Values;
}

/// <summary>
/// JobTable (commonfiles/jobtable.cpp): table/job.xml, one <see cref="JobData"/> per job group, and JobData's static max
/// level of each skill (s_skillMaxLevel, set by every &lt;v&gt; as it loads).
/// </summary>
public sealed class JobTable
{
    readonly SortedDictionary<int, JobData> jobs = new();
    readonly Dictionary<uint, ushort> maxLevels = new();

    public static readonly JobTable Empty = new(null, null);

    /// <param name="collect">FUN_14028faa0(root, "job", "jobGroupID"): the available &lt;job&gt; elements, one per group.</param>
    public JobTable(XDocument doc, Func<XElement, string, string, List<XElement>> collect)
    {
        var root = doc?.Root;
        if (root == null || root.Name != "ms2") return;
        foreach (var e in collect(root, "job", "jobGroupID"))       // JobTable::Load @0x140644540 (:309)
        {
            var j = JobData.Load(e, maxLevels);
            jobs.TryAdd(j.JobGroupId, j);
        }
    }

    /// <summary>GetJobData @0x140644880 (:339).</summary>
    public JobData GetJobData(int jobGroup) => jobs.TryGetValue(jobGroup, out var j) ? j : null;

    /// <summary>JobData::GetSkillMaxLevel @0x140644430 (:291): the job table's maxLevel of the skill, 0 when none.</summary>
    public ushort GetSkillMaxLevel(uint skillId) => maxLevels.TryGetValue(skillId, out var v) ? v : (ushort)0;

    /// <summary>GetJobRank @0x140644b80 (:383): job code % 10 + 1.</summary>
    public static int GetJobRank(int jobCode) => jobCode % 10 + 1;

    /// <summary>GetSubJob1 @0x140644bb0 (:391).</summary>
    public int GetSubJob1(int jobGroup) => GetJobData(jobGroup)?.SubJob1 ?? 0;
}

/// <summary>The attribute reads the skill tables use (XmlGetAttr / XmlTokenList: a missing attribute leaves the default).</summary>
static class Xml
{
    public static int Int(XElement e, string name) => Atoi((string)e.Attribute(name));
    public static uint UInt(XElement e, string name) => (uint)Atoll((string)e.Attribute(name));
    public static bool Bool(XElement e, string name)
    {
        var s = (string)e.Attribute(name);
        return s != null && (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || Atoi(s) != 0);
    }

    /// <summary>
    /// XmlGetBool (tinyxmlhelper.cpp:218) over TiXmlElement::QueryBoolAttribute (tinyxml.cpp:620): a value starting with true,
    /// yes or 1 is true, with false, no or 0 false, any case (TiXmlBase::StringEqual matches the tag as a prefix); a missing,
    /// empty or other value keeps <paramref name="keep"/>.
    /// </summary>
    public static bool GetBool(XElement e, string name, bool keep)
    {
        var s = (string)e.Attribute(name);
        if (string.IsNullOrEmpty(s)) return keep;
        if (s.StartsWith("true", StringComparison.OrdinalIgnoreCase) || s.StartsWith("yes", StringComparison.OrdinalIgnoreCase) || s[0] == '1') return true;
        if (s.StartsWith("false", StringComparison.OrdinalIgnoreCase) || s.StartsWith("no", StringComparison.OrdinalIgnoreCase) || s[0] == '0') return false;
        return keep;
    }

    /// <summary>atoi: leading spaces, a sign, the digits; 0 when none.</summary>
    public static int Atoi(string s) => (int)Atoll(s);

    static long Atoll(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) neg = s[i++] == '-';
        long v = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') v = v * 10 + (s[i++] - '0');
        return neg ? -v : v;
    }

    /// <summary>CString::Tokenize(","): the non-empty pieces.</summary>
    public static List<string> Tokens(string s) =>
        string.IsNullOrEmpty(s) ? new List<string>() : s.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

    public static float Float(XElement e, string name) =>
        float.TryParse((string)e.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;
}
