using System.Xml.Linq;

namespace Ms2.Ui.Skills;

/// <summary>CCompactControlSkill (ctor @0x140dcb4a0): a skill of the compact control table and its repeat count (0 unset).</summary>
public sealed record CompactControlSkill(uint SkillId, int RepeatCount);

/// <summary>
/// CSkillCompactControlTable (commonfiles/skillcompactcontroltable.cpp), Table/SkillCompactControl.xml: per job group the
/// skills the compact skill control window lists, in file order, and the job's base SP. Load @0x140dca800 reads nothing when
/// feature 0x10 (CompactControl) is off; the caller passes that as <c>enabled</c>.
/// </summary>
public sealed class SkillCompactControlTable
{
    public static readonly SkillCompactControlTable Empty = new(null, _ => true, true);

    readonly Dictionary<uint, CompactControlSkill> skills = new();                 // s_skills
    readonly Dictionary<int, List<CompactControlSkill>> jobSkills = new();         // s_jobSkills
    readonly Dictionary<int, long> baseSP = new();                                 // s_baseSP

    /// <summary>Load @0x140dca800: per &lt;job code= baseSP=(50)&gt; its &lt;skill id= repeatCount=&gt; rows that pass
    /// IsElementAvailable; a later job of the same code replaces the earlier one (SetAt).</summary>
    public SkillCompactControlTable(XDocument xml, Func<XElement, bool> available, bool enabled)
    {
        if (!enabled) return;
        foreach (var job in xml?.Root?.Elements("job") ?? Enumerable.Empty<XElement>())
        {
            int.TryParse((string)job.Attribute("code"), out int code);
            long sp = long.TryParse((string)job.Attribute("baseSP"), out var b) ? b : 50;
            var list = new List<CompactControlSkill>();
            foreach (var e in job.Elements("skill"))
            {
                if (!available(e)) continue;
                uint.TryParse((string)e.Attribute("id"), out uint id);
                int.TryParse((string)e.Attribute("repeatCount"), out int repeat);
                var s = new CompactControlSkill(id, repeat);
                list.Add(s);
                skills[id] = s;
            }
            jobSkills[code] = list;
            baseSP[code] = sp;
        }
    }

    /// <summary>GetSkill @0x140dcacd0: null when the skill is not listed.</summary>
    public CompactControlSkill GetSkill(uint id) => skills.TryGetValue(id, out var s) ? s : null;

    /// <summary>GetJobSkills @0x140dcada0: the job group's skills in file order, null without the job.</summary>
    public IReadOnlyList<CompactControlSkill> GetJobSkills(int job) => jobSkills.TryGetValue(job, out var l) ? l : null;

    /// <summary>GetBaseSP @0x140dcae70: 50 without the job.</summary>
    public long GetBaseSP(int job) => baseSP.TryGetValue(job, out var v) ? v : 50;
}
