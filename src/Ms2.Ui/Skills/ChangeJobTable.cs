using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.Skills;

/// <summary>One ChangeJobData (changejobtable.cpp:85-118): a &lt;subjob&gt; row of Table/changeJob.xml with its strings resolved
/// at load, the job name from string table 6 (jobName) of the changed sub job and the three texts from table 0x4f
/// (stringChangeJobDesc).</summary>
public sealed record ChangeJobData(int SubJobCode, int ChangeSubJobCode, int StartQuestId, int EndQuestId, string JobName, string Movie,
    string Desc, string SkillDesc1, string SkillDesc2, IReadOnlyList<uint> Skills1, IReadOnlyList<uint> Skills2);

/// <summary>
/// CChangeJobTable (commonfiles/changejobtable.cpp): Load @0x1401d6550 reads Table/changeJob.xml only with feature 0xca
/// (JobChange_01) on, and keeps a &lt;subjob&gt; row only when its feature name is on (IsFeatureEnabledByName), keyed by
/// subJobCode (CAtlMap::SetAt: a later row of the same code replaces it).
/// </summary>
public sealed class ChangeJobTable
{
    readonly Dictionary<int, ChangeJobData> map = new();

    public static readonly ChangeJobTable Empty = new();

    /// <summary>The rows, by sub job code.</summary>
    public IReadOnlyDictionary<int, ChangeJobData> Rows => map;

    /// <summary>Load from the data: feature 0xca and the row features from the pack set's feature table.</summary>
    public static ChangeJobTable Load(PackSet packs, Func<int, bool> isFeatureEnabled, GameTables tables)
    {
        if (packs == null || !isFeatureEnabled(0xca)) return Empty;
        string path = packs.Exists("Xml", "table/changejob.xml") ? "table/changejob.xml"
            : packs.Exists("Xml", "table/changeJob.xml") ? "table/changeJob.xml" : null;
        if (path == null) return Empty;
        return Load(GameStrings.Parse(packs.Read("Xml", path)), f => LobbyFeatures.IsEnabledByName(packs, f), tables.JobNameOrEmpty, tables.ChangeJobDesc);
    }

    /// <summary>Load @0x1401d6550 from a parsed file (feature 0xca already tested).</summary>
    public static ChangeJobTable Load(XDocument doc, Func<string, bool> isFeatureEnabledByName, Func<int, string> jobName, Func<int, string> changeJobDesc)
    {
        var t = new ChangeJobTable();
        var root = doc?.Root?.Name == "ms2" ? doc.Root : null;
        if (root == null) return t;
        foreach (var n in root.Elements("subjob"))
        {
            if (!isFeatureEnabledByName((string)n.Attribute("feature") ?? "")) continue;
            // one nValue for the four codes and one nStringId for the three texts: a missing attribute keeps the last read
            int v = 0, sid = 0;
            int sub = Get(n, "subJobCode", ref v), change = Get(n, "changeSubJobCode", ref v);
            int start = Get(n, "startquestid", ref v), end = Get(n, "endquestid", ref v);
            string desc = changeJobDesc(Get(n, "desc", ref sid)) ?? "", d1 = changeJobDesc(Get(n, "skilldesc1", ref sid)) ?? "";
            string d2 = changeJobDesc(Get(n, "skilldesc2", ref sid)) ?? "";
            var d = new ChangeJobData(sub, change, start, end, jobName(change) ?? "", (string)n.Attribute("movie") ?? "", desc, d1, d2,
                Skills(n, "skillID1"), Skills(n, "skillID2"));
            t.map[d.SubJobCode] = d;
        }
        return t;
    }

    /// <summary>XmlGetAttr(int&amp;): the attribute's atoi, or the variable unchanged when it is missing.</summary>
    static int Get(XElement n, string attr, ref int v) => n.Attribute(attr) is { } a ? v = Xml.Atoi(a.Value) : v;

    /// <summary>The comma list split (Unk_SplitA) and each token's atoi.</summary>
    static List<uint> Skills(XElement n, string attr)
    {
        var list = new List<uint>();
        foreach (var tok in ((string)n.Attribute(attr) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            list.Add((uint)Xml.Atoi(tok));
        return list;
    }

    /// <summary>Get @0x1401d6c30: the row of a sub job, null when none.</summary>
    public ChangeJobData Get(int subJob) => map.TryGetValue(subJob, out var d) ? d : null;

    /// <summary>CanChange @0x1401d6ce0: the level is at least Client::Constants ChangeJobLevel, the sub job is the job group's
    /// first (JobTable::GetSubJob1) and the table has a row for it.</summary>
    public bool CanChange(int jobGroup, int subJob, int level, int changeJobLevel, JobTable jobs)
        => level >= changeJobLevel && jobs.GetSubJob1(jobGroup) == subJob && map.ContainsKey(subJob);

    /// <summary>GetStartQuestId @0x1401d6d70 (0 when no row).</summary>
    public int GetStartQuestId(int subJob) => Get(subJob)?.StartQuestId ?? 0;

    /// <summary>IsChangeTarget @0x1401d6ef0: a row changes into this job code.</summary>
    public bool IsChangeTarget(int jobCode)
    {
        foreach (var d in map.Values) if (d.ChangeSubJobCode == jobCode) return true;
        return false;
    }

    public override string ToString() => map.Count.ToString(CultureInfo.InvariantCulture) + " rows";
}
