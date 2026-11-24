using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// G24 — split skill/additional strings (D9): GMS2 has skillname.xml (854 keys) plus skillname_1.xml.. and
/// korskilldescription_1.xml (a filled uiDescription per skill level), and no stringskilldescription.xml. GMS2
/// skillname_1.xml has 10000001 "Hammer Blow" and koradditionaldescription_1.xml 10000031 level 1 "Headbutt". KMS
/// skillname.xml drops no names. GameTables reads the split names and additional descriptions, and the skill tooltip's
/// description takes the GMS2 level's row (GameTables.Gms2SkillDescription).
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
    public void TheGms2SplitNamesAndDescriptionsLoad()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var g = new GameTables(packs, "en");
        Assert.That(g.SkillName(10000001), Is.EqualTo("Hammer Blow"), "gms string/en/skillname_1.xml");
        Assert.That(g.SkillDescriptionText(10000001), Is.Null, "the GMS2 data has no stringskilldescription.xml");
        Assert.That(Ms2.Ui.Skills.SkillTooltip.Description(g, 10000001, 1), Is.EqualTo(Raw(packs, "korskilldescription_1.xml", 10000001, 1)).And.Not.Empty,
            "gms string/en/korskilldescription_1.xml level 1");
        // each level has its own filled text (the KMS text is one template filled from the level's stringParam)
        Assert.That(Ms2.Ui.Skills.SkillTooltip.Description(g, 10500001, 2), Is.EqualTo(Raw(packs, "korskilldescription_50.xml", 10500001, 2))
            .And.Not.EqualTo(Ms2.Ui.Skills.SkillTooltip.Description(g, 10500001, 1)));
        Assert.That(g.AdditionalDescription(10000031, 1, "name"), Is.EqualTo("Headbutt"), "gms string/en/koradditionaldescription_1.xml");
    }

    /// <summary>The raw uiDescription of a GMS2 row ("\\n" as a line break), "" without one.</summary>
    static string Raw(PackSet packs, string file, uint id, int level) =>
        ((string)Ms2.Formats.World.GameStrings.Parse(packs.Read("Xml", "string/en/" + file)).Root.Elements("key")
            .FirstOrDefault(e => (string)e.Attribute("id") == id.ToString() && (string)e.Attribute("level") == level.ToString())?.Attribute("uiDescription") ?? "")
            .Replace("\\n", "\n");
}
