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
    /// <summary>GMS2 only: the level of the &lt;learn&gt; block that lists the skill (0 = none; the KMS table has no such value).</summary>
    public int LearnLevel;
    /// <summary>
    /// GMS2 only: a row without a uiPosition, a job skill the skill window's tree leaves out. INFERRED (the GMS2 exe is not
    /// decompiled; every KMS row has a uiPosition): in the GMS2 table/job.xml the rows without one are 20000001 and 20000011
    /// (Swift Swim, Wall Climbing) in every job, 11000082 in job 100, 11100001 in job 110, and 01000001, 01000011 (job 090),
    /// 01200001, 01200011 (job 110), which have no skill file; each of these with a skill file has ui@exceptUI 1 there, and none
    /// of the 345 positioned skills has. Placed at the default (0, 0) they covered the first tree skill.
    /// </summary>
    public bool OutOfTree;
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

    /// <summary>The GMS2 &lt;skill main sub subJobCode uiPosition quickSlotPriority&gt; row (no require lists in that table).</summary>
    internal static JobSkillData LoadGms2(XElement e)
    {
        var d = new JobSkillData
        {
            SkillId = Xml.UInt(e, "main"),
            UiHighlight = Xml.Bool(e, "uiHighlight"),
            JobLevel = e.Attribute("subJobCode") != null ? 2 : 1,
            OutOfTree = e.Attribute("uiPosition") == null,
        };
        if (e.Attribute("uiPosition") is { } p)
        {
            var t = Xml.Tokens((string)p);
            d.UiX = t.Count > 0 ? Xml.Atoi(t[0]) : 0;
            d.UiY = t.Count > 1 ? Xml.Atoi(t[1]) : 0;
        }
        if (e.Attribute("quickSlotPriority") != null) d.QuickSlotPriority = Xml.Int(e, "quickSlotPriority");
        foreach (var t in Xml.Tokens((string)e.Attribute("sub"))) d.SubSkillIds.Add((uint)Xml.Atoi(t));
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

    /// <summary>
    /// The GMS2 &lt;job code&gt; (table/job.xml): &lt;skills&gt;&lt;skill main sub uiPosition maxLevel quickSlotPriority [subJobCode]&gt;, the
    /// learnt skills in &lt;learn&gt;&lt;skill id&gt;. A skill with a subJobCode is put in the second rank's tree (JobLevel 2): inferred from the data (every subJobCode in the file is a SubJob2 code of jobgroup.xml), not from a client source. The sub jobs of the group
    /// are in table/jobgroup.xml (subJobLevel 0 and 1 are SubJob1 and SubJob2), passed in <paramref name="subJobs"/>.
    /// </summary>
    internal static JobData LoadGms2(XElement e, Dictionary<uint, ushort> maxLevels, Dictionary<int, int[]> subJobs)
    {
        var j = new JobData { JobGroupId = Xml.Int(e, "code") };
        if (subJobs != null && subJobs.TryGetValue(j.JobGroupId, out var sj)) { j.SubJob1 = sj[0]; j.SubJob2 = sj[1]; }
        if (e.Attribute("startField") != null) j.StartField = Xml.UInt(e, "startField");
        // <learn level="N"><skill id>: the KMS table has no learn level (isLearn is learnt with the rank), so what N counts is
        // inferred: only level 1 is read as "learnt with the rank" (IsLearn); a skill in a later block (job 001's level 2:
        // 10000001, 10000011, 10000041) keeps its level in LearnLevel and is not learnt at the start.
        var learnLevel = new Dictionary<uint, int>();
        foreach (var l in e.Elements("learn"))
            foreach (var s in l.Elements("skill")) learnLevel.TryAdd(Xml.UInt(s, "id"), Xml.Int(l, "level"));
        if (e.Element("skills") is { } skills)
            foreach (var v in skills.Elements("skill"))
            {
                var d = JobSkillData.LoadGms2(v);
                if (learnLevel.TryGetValue(d.SkillId, out int ll)) { d.LearnLevel = ll; d.IsLearn = ll == 1; }
                j.skills.TryAdd(d.SkillId, d);
                foreach (var sub in d.SubSkillIds) j.subSkills.TryAdd(sub, d);
                maxLevels[d.SkillId] = (ushort)Xml.Int(v, "maxLevel");
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
    /// <param name="jobGroup">GMS2 table/jobgroup.xml (the sub jobs of each group); unused for the KMS shape.</param>
    public JobTable(XDocument doc, Func<XElement, string, string, List<XElement>> collect, XDocument jobGroup = null)
    {
        var root = doc?.Root;
        if (root == null || root.Name != "ms2") return;
        // The GMS2 file has <job code> and no jobGroupID; the KMS shape keeps its own reader.
        if (!root.Elements("job").Any(e => e.Attribute("jobGroupID") != null) && root.Elements("job").Any(e => e.Attribute("code") != null))
        {
            var subJobs = new Dictionary<int, int[]>();
            foreach (var g in jobGroup?.Root?.Elements("JobGroup") ?? Enumerable.Empty<XElement>())
            {
                int code = Xml.Int(g, "jobCode"), level = Xml.Int(g, "subJobLevel");
                // a subJobLevel other than 0 and 1 has no SubJob field and is dropped (the file has none)
                if (level is 0 or 1) { if (!subJobs.TryGetValue(code, out var a)) subJobs[code] = a = new int[2]; a[level] = Xml.Int(g, "subJobCode"); }
            }
            foreach (var e in collect(root, "job", "code"))
            {
                var j = JobData.LoadGms2(e, maxLevels, subJobs);
                jobs.TryAdd(j.JobGroupId, j);
            }
            return;
        }
        foreach (var e in collect(root, "job", "jobGroupID"))       // JobTable::Load @0x140644540 (:309)
        {
            var j = JobData.Load(e, maxLevels);
            jobs.TryAdd(j.JobGroupId, j);
        }
    }

    /// <summary>JobTable::Load (jobtable.cpp:309) of the pack's table/job.xml (and table/jobgroup.xml where the data has it).</summary>
    public static JobTable Load(Ms2.Formats.Pack.PackSet packs)
    {
        XDocument Doc(string path) => packs.Exists("Xml", path) ? Ms2.Formats.World.GameStrings.Parse(packs.Read("Xml", path)) : null;
        return new JobTable(Doc("table/job.xml"), (parent, child, key) => FeatureElements.Collect(packs, parent, child, key), Doc("table/jobgroup.xml"));
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
