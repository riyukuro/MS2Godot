using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The recorded GMS2 client's data keeps one file per skill (skill/&lt;nn&gt;/&lt;id&gt;.xml, root &lt;ms2&gt; holding basic and level, the id in the file name), not skilldata/*.xml
/// with &lt;skill id&gt; rows. Without the per-skill read the skill table is empty: no cast effect, no hit effect, no damage rows.
/// Needs MS2_DATA at a GMS2 Data folder; skipped otherwise.</summary>
public class Gms2SkillDataTests {
    static PackSet Packs() {
        string data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("no MS2_DATA");
        var p = new PackSet(data);
        if (!p.Exists("Xml", "skill/10/10800001.xml")) { p.Dispose(); Assert.Ignore("MS2_DATA has no per-skill GMS2 files"); }
        return p;
    }

    [Test]
    public void PerSkillFileNamesGiveTheId() {
        Assert.That(Ms2.Ui.Skills.PerSkillFile.Id("skill/10/10800001.xml"), Is.EqualTo("10800001"));
        Assert.That(Ms2.Ui.Skills.PerSkillFile.Id("skilldata/skill1.xml"), Is.Null);
        Assert.That(Ms2.Ui.Skills.PerSkillFile.Id("skill/10/sub/10800001.xml"), Is.Null);
        string w = Ms2.Ui.Skills.PerSkillFile.Wrap("skill/00/00900021.xml", "<?xml version=\"1.0\"?>\n<!-- c -->\n<ms2 feature=\"\">\n<basic/><level value=\"1\"/>\n</ms2>");
        Assert.That(w, Does.StartWith("<ms2><skill id=\"900021\" feature=\"\">").And.EndsWith("</skill></ms2>").And.Contain("<level value=\"1\"/>"));
        // a comment before the root that names <ms2, a root id of its own, and a root that is not ms2
        string c = Ms2.Ui.Skills.PerSkillFile.Wrap("skill/00/00900021.xml", "<!-- <ms2 x> -->\n<ms2 id=\"5\" feature=\"\"><level/></ms2>");
        Assert.That(c, Does.StartWith("<ms2><skill id=\"900021\" feature=\"\">").And.Not.Contain("id=\"5\""));
        int before = Ms2.Ui.Skills.PerSkillFile.UnwrappedCount;
        Assert.That(Ms2.Ui.Skills.PerSkillFile.Wrap("skill/00/00900021.xml", "<ms2x><level/></ms2x>"), Does.StartWith("<ms2x>"));
        Assert.That(Ms2.Ui.Skills.PerSkillFile.UnwrappedCount, Is.EqualTo(before + 1), "a file it cannot wrap is counted");
    }

    [Test]
    public void TheTutorialSkillsHaveRowsAndDamage() {
        using var packs = Packs();
        int unwrapped = Ms2.Ui.Skills.PerSkillFile.UnwrappedCount;
        var t = Ms2.Ui.GameTables.Shared(packs, "en");
        foreach (uint id in new uint[] { 10800001, 900021, 900022, 10400011 })
            Assert.That(t.Skills.Skills.GetSkillLevelData(id, 1), Is.Not.Null, "skill " + id);
        Assert.That(t.Skills.Skills.GetSkillLevelData(10800001, 1).Motions, Is.Not.Empty);
        Assert.That(t.SkillDamage.Get(10800001, 1, 0, 0), Is.Not.Null, "the first attack of the assassin's basic skill has a damage row");
        Assert.That(Ms2.Ui.Skills.PerSkillFile.UnwrappedCount, Is.EqualTo(unwrapped), "every per-skill file wraps");
    }
}
