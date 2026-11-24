using Maple2.PacketLib.Tools;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The skill window's tables and read code: JobTable, SkillDataTable, JobSystem, CSkillPointSimulator, and the 0x44 / 0x10C
/// packets. The table tests need MS2_DATA; the rest run on hand-made rows.</summary>
public class SkillTableTests
{
    static GameTables tables;
    static PackSet packs;

    static GameTables Tables()
    {
        if (tables != null) return tables;
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        return tables = new GameTables(packs = new PackSet(d), "en");
    }

    /// <summary>True on KR data; GMS2 keeps table/job.xml in code shape, which has no require* attributes.</summary>
    static bool IsKr() { Tables(); return UiDataRows.IsKr(packs); }

    /// <summary>The code-shape &lt;job code&gt; rows from table/job.xml (&lt;skill main&gt; by skill id, first row kept, plus the
    /// &lt;learn level="1"&gt; skill ids).</summary>
    static (Dictionary<uint, System.Xml.Linq.XElement> Rows, HashSet<uint> LearnOne) CodeShapeJob(int code)
    {
        var root = Ms2.Formats.World.GameStrings.Parse(packs.Read("Xml", "table/job.xml")).Root;
        Assert.That(root.Elements("job").Any(e => e.Attribute("code") != null), Is.True, "non-KR data uses the code-shape table/job.xml");
        var job = FeatureElements.Collect(packs, root, "job", "code").First(e => UiDataRows.Int(e, "code") == code);
        var rows = new Dictionary<uint, System.Xml.Linq.XElement>();
        foreach (var e in job.Element("skills").Elements("skill")) rows.TryAdd((uint)UiDataRows.Int(e, "main"), e);
        var learnOne = job.Elements("learn").Where(l => UiDataRows.Int(l, "level") == 1).SelectMany(l => l.Elements("skill"))
            .Select(e => (uint)UiDataRows.Int(e, "id")).ToHashSet();
        return (rows, learnOne);
    }

    const uint ArrowStream = 10500001, ArrowBarrage = 10500021;

    [Test]
    public void TheArcherJobRowsAndSkillData()
    {
        var t = Tables().Skills;
        var job = t.Jobs.GetJobData(50);
        Assert.That(job, Is.Not.Null);
        Assert.That((job.SubJob1, job.SubJob2, job.GetSubJob(1), job.GetSubJob(2)), Is.EqualTo((500, 501, 500, 501)));
        var row = job.FindSkill(ArrowStream);
        Assert.That((row.UiX, row.UiY, row.JobLevel, row.IsLearn, row.QuickSlotPriority), Is.EqualTo((0, 0, 1, true, 1)));
        if (IsKr())
        {
            Assert.That((row.GetRequireLevel(1), row.GetRequireLevel(2), row.GetRequireLevel(12), row.GetRequireLevel(20)), Is.EqualTo(((ushort)0, (ushort)10, (ushort)30, (ushort)30)));
            var barrage = job.FindSkill(ArrowBarrage);
            Assert.That(barrage.RequireSkillIds, Is.EqualTo(new uint[] { 10500001, 10500011 }));
            Assert.That(barrage.RequireSkillLevels, Is.EqualTo(new ushort[] { 3, 1 }));
            // a sub skill finds the row of its skill
            Assert.That(job.GetSkillData(10500152)?.SkillId, Is.EqualTo(10500151u));
            Assert.That(job.FindSkill(10500261).RequireQuestId, Is.EqualTo(40002795u));
        }
        else
        {
            // GMS2 table/job.xml (code shape) lists uiPosition, quickSlotPriority, sub and <learn level="1"> with no require* attributes
            var (rows, learnOne) = CodeShapeJob(50);
            Assert.That(job.Skills.Select(r => r.SkillId), Is.EquivalentTo(rows.Keys));
            foreach (var (id, e) in rows)
            {
                var r = job.FindSkill(id);
                var pos = ((string)e.Attribute("uiPosition") ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
                Assert.That((r.UiX, r.UiY), Is.EqualTo((pos.ElementAtOrDefault(0), pos.ElementAtOrDefault(1))), $"skill {id}");
                Assert.That(r.QuickSlotPriority, Is.EqualTo(UiDataRows.Int(e, "quickSlotPriority", 0x7fffffff)), $"skill {id}");
                Assert.That(r.SubSkillIds, Is.EqualTo(((string)e.Attribute("sub") ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(uint.Parse)), $"skill {id}");
                foreach (var sub in r.SubSkillIds) Assert.That(job.GetSkillData(sub)?.SkillId, Is.EqualTo(id), "a sub skill finds the row of its skill");
                Assert.That(r.IsLearn, Is.EqualTo(learnOne.Contains(id)), $"skill {id}");
                Assert.That(e.Attributes().Any(a => a.Name.LocalName.StartsWith("require", StringComparison.OrdinalIgnoreCase)), Is.False);
                Assert.That((r.HasRequirement, r.GetRequireLevel(1), r.GetRequireLevel(20)), Is.EqualTo((false, (ushort)0, (ushort)0)), $"skill {id}");
            }
        }

        var s = t.GetSkillData(ArrowStream);
        Assert.That(s, Is.Not.Null);
        Assert.That((s.MainType, s.AttackType, s.RangeType, s.Icon), Is.EqualTo((2, 1, 2, "./Data/Resource/Image/skill/icon/10500001.png")));
        Assert.That(s.Weapons, Does.Contain(new SkillWeapon(0, 51)));
        Assert.That(s.MaxLevelRaw, Is.GreaterThanOrEqualTo((ushort)10));
        // the job's maxLevel caps the window's level ("1/10" in the capture)
        Assert.That(t.MaxLevel(s), Is.EqualTo((ushort)10));
        Assert.That(JobTable.GetJobRank(500), Is.EqualTo(1));
        Assert.That(JobTable.GetJobRank(501), Is.EqualTo(2));
        Assert.That((Tables().SkillPointLimitLevel1, Tables().SkillPointLimitLevel2), Is.EqualTo((80, 80)));      // constants.xml: 80, and 80 under JobChange_02
    }

    [Test]
    public void ALevelOneArcherHasNoPointsAndOnlyTheFreeSkills()
    {
        var t = Tables().Skills;
        var js = new JobSystem(t);
        // the skills of a new archer's book (as the server sends it): learnt-with-rank skills learnt, the others not
        var book = t.Jobs.GetJobData(50).Skills.Select(r => new BookSkill((int)r.SkillId, 1, false, r.IsLearn, false)).ToList();
        js.Read(1, 50, book);
        Assert.That(js.SubJob, Is.EqualTo(500));
        Assert.That(js.FindSkill(ArrowStream).Learned, Is.True);
        Assert.That(js.GetUsedPoint(1), Is.EqualTo(0));

        var sim = new SkillPointSimulator(t, js, _ => false);
        sim.Reset(1, js.SubJob, js.GetSkills(false, true), new Dictionary<int, int>(), 0);
        Assert.That((sim.JobRank, sim.GetTotalPoints(1), sim.GetRemainPoints(1), sim.GetTotalPoints(2)), Is.EqualTo((1, 0, 0, 0)));
        Assert.That(sim.CanLevelUp(ArrowStream), Is.False);         // no points
        Assert.That(sim.CanLevelDown(ArrowStream), Is.False);       // learnt with the rank at level 1
        Assert.That(sim.CheckRequirements(10500041, 1), Is.EqualTo(!IsKr()));  // KR: needs level 10; GMS2 code-shape job.xml has no require attributes
        Assert.That(sim.CheckRequirements(ArrowStream, 1), Is.True);
        Assert.That(sim.IsChanged(), Is.False);

        // level 20: 11 rank 1 points; Arrow Stream to 2 needs level 10
        sim.Reset(20, js.SubJob, js.GetSkills(false, true), new Dictionary<int, int> { [1] = 2 }, 1);
        Assert.That((sim.GetTotalPoints(1), sim.GetRemainPoints(1), sim.GetTotalPoints(2)), Is.EqualTo((11 + 2 + 1, 14, 0 + 0 + 1)));
        Assert.That(sim.LevelUp(ArrowStream), Is.True);
        Assert.That((sim.GetEditSkill(ArrowStream).Level, sim.GetRemainPoints(1), sim.IsChanged()), Is.EqualTo(((ushort)2, 13, true)));
        Assert.That(sim.GetChanged(0).Select(s => s.Id), Is.EqualTo(new[] { ArrowStream }));
        Assert.That(sim.GetUsedPoints(0), Is.EqualTo(new[] { (ArrowStream, 1) }));
        Assert.That(sim.LevelDown(ArrowStream, out _), Is.True);
        Assert.That(sim.IsChanged(), Is.False);
        if (!IsKr())
        {
            // GMS2 code-shape job.xml has no require attributes: Arrow Barrage can take a point right away
            Assert.That(sim.CanLevelUp(ArrowBarrage), Is.True);
            Assert.That(sim.LevelUp(ArrowBarrage), Is.True);
            Assert.That(sim.GetEditSkill(ArrowBarrage).Learned, Is.True);
            sim.Commit(0);
            Assert.That(sim.IsChanged(), Is.False);
            sim.ResetRank(1);
            Assert.That((sim.GetEditSkill(ArrowStream).Level, sim.GetEditSkill(ArrowStream).Learned, sim.GetEditSkill(ArrowBarrage).Learned), Is.EqualTo(((ushort)1, true, false)));
            return;
        }
        // Arrow Barrage needs Arrow Stream 3 (and level 16)
        Assert.That(sim.CanLevelUp(ArrowBarrage), Is.False);
        sim.LevelUp(ArrowStream); sim.LevelUp(ArrowStream);
        Assert.That(sim.CanLevelUp(ArrowBarrage), Is.True);
        Assert.That(sim.LevelUp(ArrowBarrage), Is.True);
        Assert.That(sim.GetEditSkill(ArrowBarrage).Learned, Is.True);
        // taking Arrow Stream back below 3 breaks Barrage's requirement and is undone
        Assert.That(sim.LevelDown(ArrowStream, out var bad), Is.False);
        Assert.That((bad, sim.GetEditSkill(ArrowStream).Level), Is.EqualTo((ArrowBarrage, (ushort)3)));
        sim.Commit(0);
        Assert.That(sim.IsChanged(), Is.False);
        sim.ResetRank(1);
        Assert.That((sim.GetEditSkill(ArrowStream).Level, sim.GetEditSkill(ArrowStream).Learned, sim.GetEditSkill(ArrowBarrage).Learned), Is.EqualTo(((ushort)1, true, false)));
    }

    [Test]
    public void TheSkillTooltipOfALevelOneArrowStream()
    {
        var tables = Tables();
        var t = tables.Skills;
        var js = new JobSystem(t);
        js.Read(1, 50, t.Jobs.GetJobData(50).Skills.Select(r => new BookSkill((int)r.SkillId, 1, false, r.IsLearn, false)));
        var player = new SkillTooltipPlayer { Jobs = js, Level = 1 };
        var o = SkillTooltip.Build(tables, player, ArrowStream, 1, 0x10, true);
        Assert.That(o, Is.Not.Null);
        Assert.That((o["tooltip"], o["skillName"], o["iconPath"], o["tooltipAlign"]),
            Is.EqualTo(("export_common_tooltip_kindsSkill", "Arrow Stream", "./Data/Resource/Image/skill/icon/10500001.png", 5)));
        var stat = (Dictionary<string, object>)o["statDescription"];
        Assert.That((stat["type"], stat["level"]), Is.EqualTo((1, "Level 1")));
        // learnt, on no quick slot: the right-click hint
        Assert.That(o.ContainsKey("enablePuton") && !o.ContainsKey("itemEquip"), Is.True);
        // the capture: "[Requirements]" alone (the KR job row has a level list, level 1 needs none); GMS2 code-shape rows have no requirements
        if (IsKr())
        {
            var req = ((List<object>)o["requireDescription"]).Cast<Dictionary<string, object>>().ToList();
            Assert.That(req.Select(r => r["text"]), Is.EqualTo(new[] { "[Requirements]" }));
        }
        else Assert.That(o.ContainsKey("requireDescription"), Is.False);
        // Active; Long Range / Physical; Weapon Required: Two-handed Bow, gray with no weapon: the entry is rh 51 lh 0 and
        // FUN_1409e6d00 matches an empty left hand to lh 0
        var extra = ((List<object>)o["extraDescription"]).Cast<Dictionary<string, object>>().ToList();
        Assert.That(extra.Select(r => ((string)r["text"], (string)r["color"])), Is.EqualTo(new[]
            { ("Active", "aaaaaa"), ("Long Range / Physical", "aaaaaa"), ("Weapon Required: Two-handed Bow", "aaaaaa") }));
        Assert.That(SkillTooltip.Weapons(tables, 51, 0, t.GetSkillData(ArrowStream).Weapons).Allowed, Is.True);
        // other weapons in both hands: red
        player.EquipSubtype = slot => slot == 5 ? 52 : slot == 4 ? 53 : 0;
        var held = ((List<object>)SkillTooltip.Build(tables, player, ArrowStream, 1, 0x10, true)["extraDescription"]).Cast<Dictionary<string, object>>().Last();
        Assert.That(held["color"], Is.EqualTo("ff4040"));
        player.EquipSubtype = _ => 0;
        // the description's {0} fields are filled from the level's stringParam
        var desc = (string)o["skillDescription"];
        Assert.That(desc, Is.Not.Empty);
        Assert.That(desc, Does.Not.Contain("{0}"));
        // on a quick slot: "Equipped" instead of the hint
        player.QuickSlotOf = _ => 3;
        var q = SkillTooltip.Build(tables, player, ArrowStream, 1, 0x10, true);
        Assert.That((q["itemEquip"], q.ContainsKey("enablePuton")), Is.EqualTo(("Equipped", false)));
        if (!IsKr())
        {
            Assert.That(SkillTooltip.Build(tables, player, ArrowBarrage, 1, 0x10, false).ContainsKey("requireDescription"), Is.False);
            return;
        }
        // 10500021 at level 1: level 16 and Arrow Stream 3 unmet (red), 10500011 at 1 learnt with the rank (yellow)
        var b = ((List<object>)SkillTooltip.Build(tables, player, ArrowBarrage, 1, 0x10, false)["requireDescription"]).Cast<Dictionary<string, object>>().ToList();
        TestContext.WriteLine(string.Join(" | ", b.Select(r => $"{r["text"]} {r["color"]}")));
        Assert.That(b.Count, Is.EqualTo(4));
        Assert.That(b.Skip(1).Select(r => (string)r["color"]), Is.EqualTo(new[] { "ff4040", "ff4040", "ffd400" }));
        Assert.That((string)b[1]["text"], Is.EqualTo("Level 16+"));
        Assert.That((string)b[2]["text"], Is.EqualTo("Arrow Stream [Level 3+]"));
    }

    [Test]
    public void LevelSkillPointsFollowTheLimits()
    {
        Assert.That(JobSystem.GetLevelSkillPoint(9, 1, 80, 100), Is.EqualTo(0));
        Assert.That(JobSystem.GetLevelSkillPoint(10, 1, 80, 100), Is.EqualTo(1));
        Assert.That(JobSystem.GetLevelSkillPoint(90, 1, 80, 100), Is.EqualTo(71));
        Assert.That(JobSystem.GetLevelSkillPoint(59, 2, 80, 100), Is.EqualTo(0));
        Assert.That(JobSystem.GetLevelSkillPoint(60, 2, 80, 100), Is.EqualTo(1));
        Assert.That(JobSystem.GetLevelSkillPoint(120, 2, 80, 100), Is.EqualTo(41));
        Assert.That(JobSystem.GetLevelSkillPoint(50, 3, 80, 100), Is.EqualTo(0));
    }

    [Test]
    public void SkillPointAndSkillBookTreePacketsRoundTrip()
    {
        var b = SkillPointPacket.Write(7, new (int, IEnumerable<(int, int)>)[] { (1, new[] { (1, 3), (2, 1) }), (2, new[] { (1, 2) }) }, 4);
        var p = SkillPointPacket.Read(new ByteReader(b));
        var pts = new SkillPoints();
        pts.Apply(p);
        Assert.That((p.First, pts.RankTotal(1), pts.RankTotal(2), pts.Source(2, 1), pts.Source(1, 2), pts.Extra), Is.EqualTo((7, 5, 1, 2, 1, 4)));

        var t = SkillBookTreePacket.Read(new ByteReader(SkillBookTreePacket.WriteTabs(3, 42, new[] { new SkillTreeTab(42, "abcdefghijkl", new[] { (10500001, 2) }) })));
        Assert.That((t.Mode, t.MaxTabs, t.TabId, t.Tabs.Count, t.Tabs[0].Name, t.Tabs[0].Slots[0]), Is.EqualTo((0, 3, 42L, 1, "abcdefghij", (10500001, 2))));
    }
}
