using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// G24 — split skill/additional strings (D9): GMS2 has skillname.xml (854 keys) plus skillname_1.xml.. and
/// korskilldescription_1.xml, and no stringskilldescription.xml; GameTables reads skillname.xml and
/// stringskilldescription.xml only. GMS2 skillname_1.xml has 10000001 "Hammer Blow" and koradditionaldescription_1.xml
/// 10000031 level 1 "Headbutt". KMS skillname.xml drops no names.
/// </summary>
public class RcG24StringSplitTests
{
    [Test]
    public void TheKmsSkillNameIsUnchanged()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        Assert.That(new GameTables(packs, "en").SkillName(10000001), Is.Not.Empty, "kms string/en/skillname.xml");
    }

    [Test]
    [Ignore("G24: GameTables reads skillname.xml and stringskilldescription.xml only; the _<prefix> files and koradditionaldescription* are unread")]
    public void TheGms2SplitNamesAndDescriptionsLoad()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var g = new GameTables(packs, "en");
        Assert.That(g.SkillName(10000001), Is.EqualTo("Hammer Blow"), "gms string/en/skillname_1.xml");
        Assert.That(g.SkillDescriptionText(10000001), Is.Not.Empty, "gms string/en/korskilldescription_1.xml");
        Assert.That(g.AdditionalDescription(10000031, 1, "name"), Is.EqualTo("Headbutt"), "gms string/en/koradditionaldescription_1.xml");
    }
}
