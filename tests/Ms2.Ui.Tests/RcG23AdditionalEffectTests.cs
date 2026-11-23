using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// G23 — GMS2 additional effects (perf #6): the data is additionaleffect/&lt;id&gt;.xml (6,079 files, &lt;ms2&gt;&lt;level&gt;
/// with BasicProperty/MotionProperty/UIProperty) plus string/en/koradditionaldescription*.xml; GameTables lists additional/
/// and AdditionalTable.LoadFile expects &lt;additional&gt;/&lt;Basic&gt;. GMS2 100000001 level 1 has
/// BasicProperty durationTick="600000" and UIProperty icon="./Data/Resource/Image/Skill/icon/100000001.png"; the split
/// description file names it "Soul's Respite" (the data gap notes' example says "Toxic Powder", which the data does not hold).
/// KMS additional/1000.xml id 100000001 isDebuff="1" durationTick 600000.
/// </summary>
public class RcG23AdditionalEffectTests
{
    [Test]
    public void TheKmsAdditionalKeepsItsLevel()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var g = new GameTables(packs, "en");
        var lv = g.Additional.GetLevel(100000001, 1);
        Assert.That(lv, Is.Not.Null, "kms additional/1000.xml 100000001 level 1");
        Assert.That(lv.DurationTick, Is.EqualTo(600000u));
        Assert.That(lv.Parent.IsDebuff, Is.True, "isDebuff=\"1\"");
    }

    [Test]
    [Ignore("G23: GameTables lists additional/ and AdditionalTable expects <additional>/<Basic>; GMS2 additionaleffect/*Property and koradditionaldescription* are unread")]
    public void TheGms2AdditionalEffectAndItsTextLoad()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var g = new GameTables(packs, "en");
        var lv = g.Additional.GetLevel(100000001, 1);
        Assert.That(lv, Is.Not.Null, "gms/additionaleffect/100000001.xml level 1");
        Assert.That(lv.DurationTick, Is.EqualTo(600000u), "BasicProperty durationTick=\"600000\"");
        Assert.That(lv.Icon, Is.EqualTo("./Data/Resource/Image/Skill/icon/100000001.png"), "UIProperty icon");
        Assert.That(g.AdditionalDescription(100000001, 1, "name"), Is.EqualTo("Soul's Respite"),
            "gms string/en/koradditionaldescription.xml key id=100000001 level=1");
    }
}
