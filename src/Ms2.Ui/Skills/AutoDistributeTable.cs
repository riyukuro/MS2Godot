using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.Skills;

/// <summary>One preset of Table/skillAutoDistribute.xml (UISkillBookTreeDialog::AutoDistribute, uiskillbookautodistributedialog.cpp:1923):
/// its name and tooltip string keys, the sub job it is for (0 = any) and the skills with their points, in file order.</summary>
public sealed record AutoDistributePreset(string Name, string Tooltip, int SubJob, IReadOnlyList<uint> SkillIds, IReadOnlyList<uint> SkillPoints);

/// <summary>
/// LoadAutoDistribute @0x140eccf50 (uiskillbookautodistributedialog.cpp:1888): Table/skillAutoDistribute.xml, per
/// &lt;job code&gt; the &lt;type&gt; presets picked by IsElementAvailable and folded per subJobCode_name (FUN_14028fd90,
/// <see cref="FeatureElements.Collect(PackSet, XElement, string, string, string)"/>), each with its &lt;skill id point&gt; rows.
/// </summary>
public sealed class AutoDistributeTable
{
    readonly Dictionary<int, List<AutoDistributePreset>> byJob = new();

    public static readonly AutoDistributeTable Empty = new();

    // one parse per pack set: the skill window loads the table at each open, and the file is the same every time
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, AutoDistributeTable> cache = new();

    public static AutoDistributeTable Load(PackSet packs)
    {
        if (packs == null) return Empty;
        return cache.GetValue(packs, LoadFile);
    }

    static AutoDistributeTable LoadFile(PackSet packs)
    {
        string path = packs.Exists("Xml", "table/skillautodistribute.xml") ? "table/skillautodistribute.xml"
            : packs.Exists("Xml", "table/skillAutoDistribute.xml") ? "table/skillAutoDistribute.xml" : null;
        return path == null ? Empty : Load(packs, GameStrings.Parse(packs.Read("Xml", path)));
    }

    public static AutoDistributeTable Load(PackSet packs, XDocument doc)
    {
        var t = new AutoDistributeTable();
        var root = doc?.Root?.Name == "ms2" ? doc.Root : null;
        if (root == null) return t;
        foreach (var job in root.Elements("job"))
        {
            var list = new List<AutoDistributePreset>();
            foreach (var n in FeatureElements.Collect(packs, job, "type", "subJobCode", "name"))
            {
                var skills = n.Elements("skill").ToList();
                list.Add(new AutoDistributePreset((string)n.Attribute("name") ?? "", (string)n.Attribute("tooltip") ?? "", Xml.Int(n, "subJobCode"),
                    skills.Select(s => Xml.UInt(s, "id")).ToList(), skills.Select(s => Xml.UInt(s, "point")).ToList()));
            }
            t.byJob[Xml.Int(job, "code")] = list;       // CAtlMap::SetAt: a later <job> of the same code replaces it
        }
        return t;
    }

    /// <summary>The presets of a job group the player's sub job can use (the sub job 0 ones and its own, in file order);
    /// null when the job has no entry.</summary>
    public List<AutoDistributePreset> For(int jobGroup, int subJob) =>
        byJob.TryGetValue(jobGroup, out var l) ? l.Where(p => p.SubJob == 0 || p.SubJob == subJob).ToList() : null;

    /// <summary>Whether the job has an entry with any preset at all (ApplyAutoDistribute's IsEmpty test, :1333).</summary>
    public bool HasPresets(int jobGroup) => byJob.TryGetValue(jobGroup, out var l) && l.Count > 0;
}
