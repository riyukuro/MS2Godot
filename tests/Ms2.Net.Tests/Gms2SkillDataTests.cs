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
        var attack = t.Skills.Skills.GetSkillLevelData(10800001, 1).Motions.SelectMany(m => m.Attacks).FirstOrDefault();
        Assert.That(attack, Is.Not.Null, "the motion's attack points are read");
        Assert.That(attack.Range.Distance, Is.EqualTo(800f), "the attack's range comes from its rangeProperty child");
        Assert.That(t.SkillDamage.Get(10800001, 1, 0, 0).EffectHit, Does.Contain("LuckySeven_Hit"), "the hit effect comes from the damageProperty child");
        Assert.That(t.SkillDamage.Get(10800001, 1, 0, 0), Is.Not.Null, "the first attack of the assassin's basic skill has a damage row");
        Assert.That(new Ms2.Formats.World.SkillTable(packs).Count, Is.GreaterThan(9000), "the replay and region skill table reads the per-skill files too");
        Assert.That(Ms2.Ui.Skills.PerSkillFile.UnwrappedCount, Is.EqualTo(unwrapped), "every per-skill file wraps");
    }

    [Test]
    public void TheMotionPropertyChildMovesOntoTheMotion() {
        string r = Ms2.Formats.World.SkillTable.ToSkillRows("<level><motion>\r\n\t<motionProperty sequenceName=\"a\" motionEffect=\"E.xml\" />\r\n<attack point=\"p0\"><rangeProperty distance=\"5\" /><arrowProperty x=\"1\" /><damageProperty rate=\"2\" /></attack></motion></level>");
        Assert.That(r, Does.StartWith("<level><motion sequenceName=\"a\" motionEffect=\"E.xml\">").And.Contain("<range distance=\"5\" />").And.Contain("<arrow x=\"1\" />").And.Contain("<damage rate=\"2\" />"));
        string kms = "<level><motion motionEffect=\"E.xml\"><attack><range distance=\"5\"/></attack></motion></level>";
        Assert.That(Ms2.Formats.World.SkillTable.ToSkillRows(kms), Is.SameAs(kms), "rows without a Property child are left as they are");
    }

    [Test]
    public void PairedPropertyTagsAndMotionsWithAttributesMapToo() {
        var d = System.Xml.Linq.XDocument.Parse("<level><motion a=\"1\" sequenceName=\"own\"><motionProperty sequenceName=\"x\" b=\"2\"><k/></motionProperty><attack><rangeProperty distance=\"5\"><r/></rangeProperty><damageProperty rate=\"2\"></damageProperty></attack></motion></level>");
        Ms2.Formats.World.SkillTable.ToSkillRows(d);
        var m = d.Root.Element("motion");
        Assert.That((string)m.Attribute("a"), Is.EqualTo("1"));
        Assert.That((string)m.Attribute("b"), Is.EqualTo("2"), "the property child's attribute moves up");
        Assert.That((string)m.Attribute("sequenceName"), Is.EqualTo("own"), "the motion's own attribute wins");
        Assert.That(m.Element("k"), Is.Not.Null, "its children move up");
        Assert.That(m.Element("attack").Element("range").Element("r"), Is.Not.Null, "a paired tag keeps its children");
        Assert.That(m.Element("attack").Element("damage"), Is.Not.Null);
        Assert.That(d.Descendants().Count(e => e.Name.LocalName is "rangeProperty" or "arrowProperty" or "damageProperty" or "motionProperty"), Is.Zero);
        Assert.That(Ms2.Formats.World.SkillTable.ToSkillRows("<level><motion><motionProperty"), Is.EqualTo("<level><motion><motionProperty"), "text that does not parse is left as it is");
    }

    /// <summary>The shapes of the real files (the numbers are printed): none of the four Property elements is left after the mapping, and every motionProperty sat inside a motion.</summary>
    [Test]
    public void TheRealFilesHaveNoPropertyElementLeft() {
        using var packs = Packs();
        int files = 0, motions = 0, bare = 0, withProp = 0, attrOnly = 0, selfClosing = 0, pairedProp = 0, strayMp = 0, left = 0;
        foreach (var e in packs.List("Xml").Where(n => Ms2.Formats.World.SkillTable.PerSkillId(n) != null)) {
            string text = Ms2.Formats.Text.PackXml.Text(packs.Read("Xml", e));
            files++;
            System.Xml.Linq.XDocument d;
            try { d = System.Xml.Linq.XDocument.Parse(text); } catch { continue; }
            foreach (var m in d.Descendants("motion")) {
                motions++;
                var mp = m.Elements("motionProperty").ToList();
                if (mp.Count > 0) withProp++; else if (m.HasAttributes) attrOnly++;
                if (mp.Count > 0 && !m.HasAttributes) bare++;
            }
            foreach (var x in d.Descendants().Where(x => x.Name.LocalName is "rangeProperty" or "arrowProperty" or "damageProperty" or "motionProperty")) {
                if (x.HasElements || x.Nodes().Any()) pairedProp++; else selfClosing++;
                if (x.Name.LocalName == "motionProperty" && x.Parent?.Name.LocalName != "motion") strayMp++;
            }
            Ms2.Formats.World.SkillTable.ToSkillRows(d);
            left += d.Descendants().Count(x => x.Name.LocalName is "rangeProperty" or "arrowProperty" or "damageProperty" or "motionProperty");
        }
        TestContext.Out.WriteLine($"files {files} motions {motions} motionProperty-child {withProp} (bare motion {bare}) attributes-only {attrOnly} property elements empty {selfClosing} with content {pairedProp} motionProperty outside a motion {strayMp} left after mapping {left}");
        Assert.That(strayMp, Is.Zero);
        Assert.That(left, Is.Zero);
    }

    [Test]
    public void TheSkillTableReadsOnceWhenThreadsAskTogether() {
        int reads = 0;
        var t = new Ms2.Ui.Skills.SkillDataTable(() => { System.Threading.Interlocked.Increment(ref reads); System.Threading.Thread.Sleep(50);
            return new[] { System.Xml.Linq.XDocument.Parse("<ms2><skill id=\"5\"><basic/><level value=\"1\"/></skill></ms2>") }; },
            (_, _, _) => new List<System.Xml.Linq.XElement>());
        System.Threading.Tasks.Parallel.For(0, 8, i => { if (i == 0) t.Load(); else Assert.That(t.GetSkillData(5), Is.Not.Null); });
        Assert.That(reads, Is.EqualTo(1), "one read, the others wait for it");
    }
}
