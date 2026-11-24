using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The GMS2 table/job.xml shape (&lt;job code&gt;, &lt;skill main sub uiPosition maxLevel quickSlotPriority subJobCode&gt;) read by
/// JobTable, with the sub jobs from table/jobgroup.xml. The data test needs MS2_GMS2_DATA.</summary>
public class Gms2JobTableTests
{
    static List<XElement> All(XElement p, string c, string k) => p.Elements(c).ToList();

    const string Job = @"<ms2><job code=""010"" startField=""52000065"">
  <skills>
    <skill main=""20000001"" />
    <skill main=""10100061"" sub=""10100062"" uiPosition=""2, 0"" quickSlotPriority=""4"" />
    <skill main=""10100001"" uiPosition=""0, 1"" maxLevel=""10"" quickSlotPriority=""1"" />
    <skill main=""10100171"" subJobCode=""101"" uiPosition=""0, 3"" maxLevel=""4"" />
  </skills>
  <learn level=""1""><skill id=""20000001"" /><skill id=""10100001"" /></learn><learn level=""2""><skill id=""10100061"" /></learn>
</job></ms2>";
    const string Group = @"<ms2><JobGroup jobCode=""10"" subJobLevel=""0"" subJobCode=""100"" /><JobGroup jobCode=""10"" subJobLevel=""1"" subJobCode=""101"" /></ms2>";

    [Test]
    public void TheCodeShapeGivesTheRowsAndTheSubJobsOfTheGroup()
    {
        var t = new JobTable(XDocument.Parse(Job), All, XDocument.Parse(Group));
        var j = t.GetJobData(10);
        Assert.That(j, Is.Not.Null);
        Assert.That((j.SubJob1, j.SubJob2, j.StartField), Is.EqualTo((100, 101, 52000065u)));
        var a = j.FindSkill(10100001);
        Assert.That((a.UiX, a.UiY, a.JobLevel, a.IsLearn, a.QuickSlotPriority), Is.EqualTo((0, 1, 1, true, 1)));
        Assert.That(t.GetSkillMaxLevel(10100001), Is.EqualTo((ushort)10));
        Assert.That(j.GetSkillData(10100062)?.SkillId, Is.EqualTo(10100061u));
        var b = j.FindSkill(10100171);
        Assert.That((b.JobLevel, b.IsLearn, t.GetSkillMaxLevel(10100171)), Is.EqualTo((2, false, (ushort)4)));
        Assert.That((j.FindSkill(10100061).IsLearn, j.FindSkill(10100061).LearnLevel, a.LearnLevel), Is.EqualTo((false, 2, 1)));
    }

    /// <summary>A row without a uiPosition (Swift Swim here) stays a job skill but is out of the skill window's tree; a
    /// positioned row and every KMS row are in it.</summary>
    [Test]
    public void ARowWithoutAPositionIsOutOfTheTree()
    {
        var j = new JobTable(XDocument.Parse(Job), All, XDocument.Parse(Group)).GetJobData(10);
        Assert.That(j.FindSkill(20000001), Is.Not.Null, "still a job skill");
        Assert.That(j.FindSkill(20000001).OutOfTree, Is.True);
        Assert.That(new[] { 10100061u, 10100001u, 10100171u }.Select(id => j.FindSkill(id).OutOfTree), Is.All.False);
        var kms = JobSkillData.Load(XElement.Parse(@"<v skillID=""10000041"" jobLevel=""1"" isLearn=""1"" />"));
        Assert.That(kms.OutOfTree, Is.False, "the KMS rows are read as the client reads them");
    }

    [Test]
    public void TheKmsShapeIsReadAsBefore()
    {
        var kms = XDocument.Parse(@"<ms2><job jobGroupID=""10"" subJob1=""100"" subJob2=""101""><skills>
            <v skillID=""10100001"" jobLevel=""1"" isLearn=""1"" uiPosition=""0,0"" maxLevel=""10"" quickSlotPriority=""1"" /></skills></job></ms2>");
        var j = new JobTable(kms, All, XDocument.Parse(Group)).GetJobData(10);
        Assert.That((j.SubJob1, j.SubJob2, j.FindSkill(10100001).IsLearn), Is.EqualTo((100, 101, true)));
    }

    [Test]
    public void TheGms2DataKnightHasATreeAndHotbarSkills()
    {
        var d = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_GMS2_DATA is not set");
        var g = new GameTables(new PackSet(d), "en");
        var j = g.Skills.Jobs.GetJobData(10);
        Assert.That(j, Is.Not.Null);
        Assert.That((j.SubJob1, j.SubJob2), Is.EqualTo((100, 101)));
        Assert.That(j.Skills.Count(s => s.JobLevel == 1 && s.QuickSlotPriority != 0x7fffffff), Is.EqualTo(15));
        Assert.That(j.FindSkill(10100001).IsLearn, Is.True);
        Assert.That(j.Skills.Any(s => s.QuickSlotPriority != 0x7fffffff), Is.True);
        var book = UiFixture.NewCharacterSkillBook(g.Skills.Jobs, 10);
        Assert.That(book.Skills.Count, Is.EqualTo(j.Skills.Count()));
    }
}
