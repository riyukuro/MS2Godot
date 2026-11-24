using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The GMS2 skill requirements: that data's table/job.xml has no require lists; each skill's own file
/// skill/&lt;nn&gt;/&lt;id&gt;.xml has &lt;level value&gt;&lt;upgrade level skillIDs skillLevels questIDs/&gt;, which
/// <see cref="JobSkillData.LoadGms2Upgrade"/> reads into the KMS row's requireLevel, requireSkillIDs, requireSkillLevels and
/// requireQuestID.</summary>
public class Gms2SkillRequirementTests
{
    static JobTable Jobs(PackSet packs)
    {
        XDocument Xml(string path) => packs.Exists("Xml", path) ? GameStrings.Parse(packs.Read("Xml", path)) : null;
        return new JobTable(Xml("table/job.xml"), (parent, child, key) => FeatureElements.Collect(packs, parent, child, key), Xml("table/jobgroup.xml"),
            id => Xml(JobTable.Gms2SkillFile(id)));
    }

    static IEnumerable<JobData> All(JobTable t) => Enumerable.Range(0, 1000).Select(t.GetJobData).Where(j => j != null);

    [Test]
    public void TheFileNameIsTheIdsMillionsThenTheId()
    {
        Assert.That(JobTable.Gms2SkillFile(10500021), Is.EqualTo("skill/10/10500021.xml"));
        Assert.That(JobTable.Gms2SkillFile(100000001), Is.EqualTo("skill/100/100000001.xml"));
        Assert.That(JobTable.Gms2SkillFile(1000001), Is.EqualTo("skill/01/01000001.xml"));
    }

    /// <summary>The levels the collect picks, the level list by value, and the skills and quest of the lowest level with them.</summary>
    [Test]
    public void TheUpgradeOfEachPickedLevelIsTheRequirement()
    {
        var skill = XElement.Parse("""
            <ms2>
              <level value="1" feature="Off"><upgrade level="99" skillIDs="7" skillLevels="9" /></level>
              <level value="1"><upgrade /></level>
              <level value="3"><upgrade level="30" skillIDs="10500001,10500011" skillLevels="3,1" questIDs="40002795" /></level>
              <level value="2"><upgrade level="20" skillIDs="10500001,10500011" skillLevels="3,1" questIDs="40002795" /></level>
              <level value="4" />
            </ms2>
            """);
        List<XElement> collect(XElement parent, string child, string key) =>
            parent.Elements(child).Where(e => (string)e.Attribute("feature") != "Off").ToList();
        var d = new JobSkillData();
        d.LoadGms2Upgrade(skill, collect);
        Assert.That(d.RequireLevel, Is.EqualTo(new ushort[] { 0, 20, 30 }));
        Assert.That((d.GetRequireLevel(1), d.GetRequireLevel(2), d.GetRequireLevel(4)), Is.EqualTo(((ushort)0, (ushort)20, (ushort)30)));
        Assert.That(d.RequireSkillIds, Is.EqualTo(new uint[] { 10500001, 10500011 }));
        Assert.That(d.RequireSkillLevels, Is.EqualTo(new ushort[] { 3, 1 }));
        Assert.That((d.RequireQuestId, d.HasRequirement), Is.EqualTo((40002795u, true)));

        // only empty upgrades: no requirement (as a KMS row with no requireLevel)
        var none = new JobSkillData();
        none.LoadGms2Upgrade(XElement.Parse("<ms2><level value=\"1\"><upgrade /></level><level value=\"2\" /></ms2>"), collect);
        Assert.That((none.RequireLevel.Count, none.HasRequirement), Is.EqualTo((0, false)));
    }

    /// <summary>
    /// Every skill of every GMS2 job: its file is there, and where the KMS job table (MS2_DATA) has the same job and skill, the
    /// requirement read from the file is the KMS row's: the level list, the skills, their levels and the quest. The skills and
    /// quest of the picked levels are the same at every level that has them (one list per skill, as the KMS row).
    /// </summary>
    [Test]
    public void EveryGms2JobSkillHasTheKmsRequirement()
    {
        using var gPacks = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        using var kPacks = new PackSet(RcGap.Data("MS2_DATA"));
        var g = Jobs(gPacks);
        var k = Jobs(kPacks);
        int rows = 0, both = 0, same = 0, withReq = 0, gated = 0;
        var bad = new List<string>();
        var offFeature = new List<string>();
        foreach (var gj in All(g))
        {
            var kj = k.GetJobData(gj.JobGroupId);
            foreach (var gs in gj.Skills)
            {
                rows++;
                var path = JobTable.Gms2SkillFile(gs.SkillId);
                if (!gPacks.Exists("Xml", path)) { bad.Add($"{gj.JobGroupId}/{gs.SkillId}: no {path}"); continue; }
                if (gs.HasRequirement) withReq++;
                if (gs.GetRequireLevel(1) > 1 || gs.RequireSkillIds.Count > 0 || gs.RequireQuestId != 0) gated++;
                foreach (var u in Upgrades(gPacks, path))
                {
                    var q = (string)u.Attribute("questIDs");
                    if (q != null && q.Split(',', StringSplitOptions.RemoveEmptyEntries).Length > 1) bad.Add($"{gs.SkillId}: questIDs {q}");
                    if (u.Attribute("skillIDs") != null && ((string)u.Attribute("skillIDs") != string.Join(",", gs.RequireSkillIds)
                        || (string)u.Attribute("skillLevels") != string.Join(",", gs.RequireSkillLevels)))
                        bad.Add($"{gs.SkillId}: a level's skills {u.Attribute("skillIDs")} {u.Attribute("skillLevels")} differ");
                }
                if (kj?.FindSkill(gs.SkillId) is not { } ks) continue;
                both++;
                if (Same(gs, ks)) { same++; continue; }
                // a difference the data makes: the KMS value is the one of a feature the GMS2 data has off
                var root = GameStrings.Parse(gPacks.Read("Xml", path)).Root;
                var feature = root.Elements("level").Select(l => (string)l.Attribute("feature")).Where(f => f != null).Distinct()
                    .FirstOrDefault(f => Same(Picked(root, f), ks));
                if (feature != null) { offFeature.Add($"{gj.JobGroupId}/{gs.SkillId}: GMS2 {Show(gs)}, KMS {Show(ks)} (the {feature} levels, off in GMS2)"); continue; }
                bad.Add($"{gj.JobGroupId}/{gs.SkillId}: GMS2 {Show(gs)} KMS {Show(ks)}");
            }
        }
        TestContext.WriteLine($"{rows} GMS2 job skill rows, {withReq} with a requirement, {gated} gated at level 1; {both} in the KMS table too: {same} the same, {offFeature.Count} a feature's levels, {bad.Count} other");
        foreach (var o in offFeature) TestContext.WriteLine(o);
        foreach (var b in bad.Take(60)) TestContext.WriteLine(b);
        Assert.That(both, Is.GreaterThan(300));
        Assert.That(offFeature.Count, Is.LessThan(5));
        Assert.That(bad, Is.Empty);
    }

    /// <summary>The same requirement: the level every skill level needs (GetRequireLevel: a one-entry KMS list is that level
    /// for all), the skills, their levels and the quest.</summary>
    static bool Same(JobSkillData a, JobSkillData b) =>
        Enumerable.Range(1, Math.Max(a.RequireLevel.Count, b.RequireLevel.Count)).All(l => a.GetRequireLevel(l) == b.GetRequireLevel(l))
        && a.RequireSkillIds.SequenceEqual(b.RequireSkillIds) && a.RequireSkillLevels.SequenceEqual(b.RequireSkillLevels) && a.RequireQuestId == b.RequireQuestId;

    static string Show(JobSkillData d) => $"[{string.Join(",", d.RequireLevel)}] [{string.Join(",", d.RequireSkillIds)}] [{string.Join(",", d.RequireSkillLevels)}] {d.RequireQuestId}";

    /// <summary>The requirement read from one feature's levels (a value with none of that feature: its plain level).</summary>
    static JobSkillData Picked(XElement root, string feature)
    {
        var d = new JobSkillData();
        d.LoadGms2Upgrade(root, (parent, child, _) => parent.Elements(child).GroupBy(l => (string)l.Attribute("value"))
            .Select(g => g.FirstOrDefault(l => (string)l.Attribute("feature") == feature) ?? g.FirstOrDefault(l => l.Attribute("feature") == null))
            .Where(l => l != null).ToList());
        return d;
    }

    /// <summary>The &lt;upgrade&gt; of every level the feature collect picks.</summary>
    static IEnumerable<XElement> Upgrades(PackSet packs, string path)
    {
        var root = GameStrings.Parse(packs.Read("Xml", path)).Root;
        return FeatureElements.Collect(packs, root, "level", "value").Select(l => l.Element("upgrade")).Where(u => u != null);
    }

    /// <summary>A level-1 GMS2 archer: Arrow Barrage (upgrade level 16, Arrow Stream 3 and 10500011 1) and Eagle's Majesty
    /// (10500261: level 76 and quest 40002795) cannot take a point; with the old reader (no requirement) both could.</summary>
    [Test]
    public void ALevelOneGms2ArcherCannotLevelAGatedSkill()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var tables = new GameTables(packs, "en");
        var t = tables.Skills;
        var js = new JobSystem(t);
        js.Read(1, 50, t.Jobs.GetJobData(50).Skills.Select(r => new BookSkill((int)r.SkillId, 1, false, r.IsLearn, false)));
        var sim = new SkillPointSimulator(t, js, _ => false);
        sim.Reset(1, js.SubJob, js.GetSkills(false, true), new Dictionary<int, int> { [1] = 5 }, 0);
        Assert.That(sim.GetRemainPoints(1), Is.GreaterThan(0), "points to spend");
        Assert.That(sim.CheckRequirements(10500021, 1), Is.False);
        Assert.That(sim.CanLevelUp(10500021), Is.False);
        Assert.That(sim.CheckRequirements(10500001, 1), Is.True);
        var eagle = t.Jobs.GetJobData(50).FindSkill(10500261);
        Assert.That((eagle.GetRequireLevel(1), eagle.RequireQuestId), Is.EqualTo(((ushort)76, 40002795u)));
    }
}
