using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>GMS2 per-skill files carry kinds type/subType and no basic@mainType, so every skill read main type 0, a book skill kind,
/// and the basic attack (900021) was refused as not learnt although it is never in the book. Needs MS2_GMS2_DATA for the data test.</summary>
public class Gms2MainTypeTests
{
    static SkillData Load(string basic) => SkillData.Load(XElement.Parse("<skill id=\"900021\">" + basic + "<level value=\"1\"/></skill>"),
        (_, _, _) => new System.Collections.Generic.List<XElement>());

    [Test]
    public void KindsTypeGivesTheMainTypeWhenThereIsNone()
    {
        Assert.That(Load("<basic><kinds type=\"0\" subType=\"1\"/></basic>").MainType, Is.EqualTo(1), "a basic attack");
        Assert.That(Load("<basic><kinds type=\"1\" subType=\"2\"/></basic>").MainType, Is.EqualTo(6), "a passive");
        Assert.That(Load("<basic><kinds type=\"3\" subType=\"3\"/></basic>").MainType, Is.EqualTo(3));
        Assert.That(Load("<basic><kinds type=\"0\" subType=\"0\"/></basic>").MainType, Is.EqualTo(0));
        Assert.That(Load("<basic mainType=\"2\"><kinds type=\"0\" subType=\"1\"/></basic>").MainType, Is.EqualTo(2), "a KMS row keeps its own mainType");
        Assert.That(Load("<basic mainType=\"1\"><kinds rangeType=\"2\"/></basic>").MainType, Is.EqualTo(1));
        Assert.That(Load("<basic><kinds rangeType=\"2\"/></basic>").MainType, Is.EqualTo(0));
    }

    [Test]
    public void TheStarBasicAttackIsNotABookSkillOnGms2Data()
    {
        var d = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_GMS2_DATA is not set");
        using var packs = new PackSet(d);
        var t = GameTables.Shared(packs, "en").Skills.Skills;
        foreach (uint id in new uint[] { 900001, 900021 })
        {
            var s = t.GetSkillData(id);
            Assert.That(s.MainType, Is.EqualTo(1), "skill " + id);
            Assert.That(JobSystem.IsBookSkillKind(s), Is.False, "skill " + id + " is learnt without the book");
        }
    }
}
