using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The attacks' &lt;damage&gt; blocks (SkillDamage::Load @0x1407e01c0, skillattackdata.cpp:251) and the walk to them.
/// </summary>
public class SkillDamageTableTests
{
    static SkillDamageTable Table(params string[] docs) =>
        new(() => docs.Select(x => (TextReader)new StringReader(x)), rows => rows);

    [Test]
    public void AReaderDuringTheWorkerLoadWaitsAndTheFilesAreReadOnce()
    {
        // the field entry loads the table on a worker thread; a first hit that comes while it runs takes the same read
        int reads = 0;
        using var entered = new System.Threading.ManualResetEventSlim();
        using var release = new System.Threading.ManualResetEventSlim();
        var t = new SkillDamageTable(() =>
        {
            if (System.Threading.Interlocked.Increment(ref reads) == 1) { entered.Set(); release.Wait(5000); }
            return new TextReader[] { new StringReader(@"<ms2><skill id=""5""><basic/><level value=""1""><motion>
                <attack><damage value=""7""/></attack></motion></level></skill></ms2>") };
        }, rows => rows);
        var worker = System.Threading.Tasks.Task.Run(t.Load);
        Assert.That(entered.Wait(5000), Is.True);
        var reader = System.Threading.Tasks.Task.Run(() => t.Get(5, 1, 0, 0).Value);
        Assert.That(reader.Wait(200), Is.False, "the reader waits for the worker's read");
        release.Set();
        Assert.That(System.Threading.Tasks.Task.WaitAll(new System.Threading.Tasks.Task[] { worker, reader }, 5000), Is.True);
        Assert.That((reader.Result, reads), Is.EqualTo((7L, 1)));
    }

    [Test]
    public void AMissingAttributeKeepsTheCtorValue()
    {
        // the ctor (:175): count 1, pushprob 1, hitSpeedRate 1, damagedVibrateRatio 1; an attack without <damage> keeps them all
        var t = Table(@"<ms2><skill id=""5""><basic/><level value=""1""><motion>
            <attack><damage value=""12345678901"" rate=""0.25"" push=""2"" pushdistance=""150"" pushEaseType=""7"" pushApplyField=""1""
              constDamageByTargetHP=""-3"" effectHit=""hit.xml"" pushAnimation=""Hit_A"" stateHitOnly=""2""/></attack>
            <attack/>
            <attack><damage count=""4"" pushprob=""0"" pushEaseType=""2""/></attack>
          </motion></level></skill></ms2>");
        var a = t.Get(5, 1, 0, 0);
        Assert.That((a.Value, a.Rate, a.Push, a.PushDistance, a.PushApplyField, a.ConstDamageByTargetHP, a.StateHitOnly),
            Is.EqualTo((12345678901L, 0.25f, 2, 150f, 1, -3L, 2)));
        Assert.That((a.EffectHit, a.PushAnimation), Is.EqualTo(("hit.xml", "Hit_A")));
        Assert.That((a.Count, a.PushProb, a.HitSpeedRate, a.DamagedVibrateRatio, a.PushEaseType), Is.EqualTo((1, 1f, 1f, 1f, 0)),
            "missing keeps the ctor's; an ease type past 2 becomes 0");
        Assert.That(a.HasDamage, Is.True);
        var none = t.Get(5, 1, 0, 1);
        Assert.That((none.Count, none.PushProb, none.HasDamage), Is.EqualTo((1, 1f, false)));
        var c = t.Get(5, 1, 0, 2);
        Assert.That((c.Count, c.PushProb, c.PushEaseType, c.HasDamage), Is.EqualTo((4, 0f, 2, false)));
    }

    [Test]
    public void HasDamageIsValueConstantRateOrMaxHpRateAboveZero()
    {
        // HasDamage @0x1407e0f40 (:384); constDamageByTargetSP is not among them
        var t = Table(@"<ms2><skill id=""5""><basic/><level value=""1""><motion>
            <attack><damage value=""1""/></attack><attack><damage constDamageByTargetHP=""1""/></attack>
            <attack><damage rate=""0.01""/></attack><attack><damage damageByTargetMaxHP=""0.5""/></attack>
            <attack><damage constDamageByTargetSP=""9"" value=""-5"" rate=""-1""/></attack>
          </motion></level></skill></ms2>");
        Assert.That(Enumerable.Range(0, 5).Select(i => t.Get(5, 1, 0, i).HasDamage), Is.EqualTo(new[] { true, true, true, true, false }));
    }

    [Test]
    public void TheWalkIsSkillDataTables()
    {
        // no <basic>: no levels; a level below 1 ends the skill's rows; a later skill of the same id replaces the earlier
        var t = Table(
            @"<ms2><skill id=""1""><basic/><level value=""1""><motion><attack><damage value=""1""/></attack></motion></level></skill>
                   <skill id=""2""><level value=""1""><motion><attack><damage value=""1""/></attack></motion></level></skill>
                   <skill id=""3""><basic/><level value=""1""><motion/></level><level value=""0""/><level value=""2""><motion><attack/></motion></level></skill></ms2>",
            @"<ms2><skill id=""1""><basic/><level value=""1""><motion><attack><damage rate=""2""/></attack></motion></level></skill></ms2>",
            @"<other><skill id=""1""/></other>");
        Assert.That((t.Get(1, 1, 0, 0).Value, t.Get(1, 1, 0, 0).Rate), Is.EqualTo((0L, 2f)), "the later file's skill");
        Assert.That((t.HasLevel(2, 1), t.Get(2, 1, 0, 0)), Is.EqualTo((false, (SkillAttackDamage)null)));
        Assert.That((t.HasLevel(3, 1), t.HasLevel(3, 2), t.Get(3, 1, 0, 0)), Is.EqualTo((true, false, (SkillAttackDamage)null)));
        Assert.That((t.Get(1, 1, 1, 0), t.Get(1, 1, 0, 1), t.Get(9, 1, 0, 0), t.Get(1, -1, 0, 0)), Is.EqualTo(((SkillAttackDamage)null, (SkillAttackDamage)null, (SkillAttackDamage)null, (SkillAttackDamage)null)));
        Assert.That(SkillDamageTable.Empty.Get(1, 1, 0, 0), Is.Null);
    }

    [Test]
    public void OnlyDirectChildrenCountAndTheFirstDamageIsRead()
    {
        // XmlGetChild / the child walks: an <attack> or <level> under another element is not a row; the first <damage> is read
        var t = Table(@"<ms2><skill id=""5""><x><level value=""2""><motion><attack/></motion></level></x><basic/>
            <level value=""1""><motion><attack><y><damage value=""9""/></y><damage value=""1""><damage value=""8""/></damage>
              <damage value=""2""/></attack><z><attack/></z><attack/></motion><w><motion/></w></level></skill></ms2>");
        Assert.That((t.HasLevel(5, 1), t.HasLevel(5, 2)), Is.EqualTo((true, false)));
        var a = t.Get(5, 1, 0, 0);
        Assert.That(a.Value, Is.EqualTo(1L));
        Assert.That((t.Get(5, 1, 0, 1), t.Get(5, 1, 0, 2), t.Get(5, 1, 1, 0)),
            Is.EqualTo((SkillAttackDamage.None, (SkillAttackDamage)null, (SkillAttackDamage)null)));
    }

    [Test]
    public void TheReadKeepsNoDocument()
    {
        // the field entry reads every skilldata file: the elements and attributes outside <damage> cost no objects
        var sb = new System.Text.StringBuilder("<ms2>");
        for (int i = 1; i <= 2000; i++)
            sb.Append($@"<skill id=""{i}""><basic><kinds type=""1"" subType=""2"" rangeType=""0""/><stateAttr useDefaultSkill=""0""/></basic>
                <level value=""1""><consume><stat sp=""10"" ep=""5""/></consume><motion><motionProperty sequenceName=""Attack_01_A"" motionEffect=""a.xml""/>
                <attack point=""0"" targetCount=""3"" pause=""0""><range type=""box"" distance=""150"" height=""100"" width=""50""/>
                <damage count=""1"" rate=""1.5""/></attack></motion></level></skill>");
        string text = sb.Append("</ms2>").ToString();
        var t = Table(text);
        long before = GC.GetAllocatedBytesForCurrentThread();
        t.Load();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(t.Get(2000, 1, 0, 0).Rate, Is.EqualTo(1.5f));
        before = GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(XDocument.Parse(text));
        long document = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.Progress.WriteLine($"read {bytes} B, a document of the text {document} B ({text.Length} chars)");
        Assert.That(bytes, Is.LessThan(document / 3), $"{bytes} B, a document {document} B");
    }

    static string Skills(int count, Func<int, string> damage)
    {
        var sb = new System.Text.StringBuilder("<ms2>");
        for (int i = 1; i <= count; i++)
            sb.Append($@"<skill id=""{i}""><basic/><level value=""1""><motion><attack><damage {damage(i)}/></attack></motion></level></skill>");
        return sb.Append("</ms2>").ToString();
    }

    static long LoadBytes(string text)
    {
        var t = Table(text);
        long before = GC.GetAllocatedBytesForCurrentThread();
        t.Load();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Test]
    public void ASkillLevelCostsItsRowsNotStringsOrAMapEach()
    {
        // per skill level: no value strings for the numbers, no level map of its own; the field entry reads 21738 levels
        string Damage(int i) => $@"count=""1"" rate=""{i}.5"" value=""{i}"" pushdistance=""{i}"" pushprob=""0.5"" hitSpeedRate=""2""";
        LoadBytes(Skills(100, Damage));
        long small = LoadBytes(Skills(2000, Damage)), large = LoadBytes(Skills(6000, Damage));
        long perSkill = (large - small) / 4000;
        TestContext.Progress.WriteLine($"{perSkill} B per skill level");
        Assert.That(perSkill, Is.LessThan(560), $"{perSkill} B per skill level");
        var t = Table(Skills(3, Damage));
        var a = t.Get(3, 1, 0, 0);
        Assert.That((a.Rate, a.Value, a.PushDistance, a.PushProb, a.HitSpeedRate, a.Count), Is.EqualTo((3.5f, 3L, 3f, 0.5f, 2f, 1)));
    }

    [Test]
    public void EqualAttacksAreOneObject()
    {
        // the levels of a skill often repeat an attack: one object for equal values, a new one for any differing field
        var t = Table(Skills(4, i => i == 4 ? @"rate=""1.5"" effectHit=""b.xml""" : @"rate=""1.5"" effectHit=""a.xml"""),
            @"<ms2><skill id=""9""><basic/><level value=""1""><motion><attack><damage rate=""1.5"" effectHit=""a.xml"" pushprob=""0.5""/></attack>
                <attack><damage rate=""1.5"" effectHit=""a.xml"" count=""2""/></attack><attack/></motion></level></skill></ms2>");
        Assert.That(t.Get(2, 1, 0, 0), Is.SameAs(t.Get(1, 1, 0, 0)));
        Assert.That(t.Get(3, 1, 0, 0), Is.SameAs(t.Get(1, 1, 0, 0)));
        Assert.That(t.Get(4, 1, 0, 0), Is.Not.SameAs(t.Get(1, 1, 0, 0)));
        Assert.That((t.Get(4, 1, 0, 0).EffectHit, t.Get(1, 1, 0, 0).EffectHit), Is.EqualTo(("b.xml", "a.xml")));
        Assert.That(t.Get(9, 1, 0, 0), Is.Not.SameAs(t.Get(1, 1, 0, 0)));
        Assert.That(t.Get(9, 1, 0, 0).PushProb, Is.EqualTo(0.5f));
        Assert.That((t.Get(9, 1, 0, 1).Count, t.Get(1, 1, 0, 0).Count, t.Get(9, 1, 0, 2)), Is.EqualTo((2, 1, SkillAttackDamage.None)));
    }

    [Test]
    public void OnTheDataEveryAttackOfSkillDataTableHasItsDamage()
    {
        // the same files and the same walk: every level, motion and attack SkillDataTable keeps has a damage block here,
        // and no more; the first read is timed (it parses every SkillData file)
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var tables = new GameTables(new PackSet(d), "en");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        tables.Skills.GetSkillData(1);
        long skillData = clock.ElapsedMilliseconds;
        clock.Restart();
        long before = GC.GetAllocatedBytesForCurrentThread();
        tables.SkillDamage.Get(1, 1, 0, 0);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.Progress.WriteLine($"first read: SkillDataTable {skillData} ms, SkillDamageTable {clock.ElapsedMilliseconds} ms, {bytes / 1024} KiB");
        int skills = 0, attacks = 0, damaging = 0;
        foreach (var id in tables.SkillDamage.SkillIds)
        {
            var s = tables.Skills.GetSkillData(id);
            Assert.That(s, Is.Not.Null, $"skill {id}");
            skills++;
            for (int level = 0; level <= s.MaxLevelRaw + 1; level++)
            {
                var lv = s.GetLevelData(level);
                Assert.That(tables.SkillDamage.HasLevel((int)id, level), Is.EqualTo(lv != null), $"skill {id} level {level}");
                if (lv == null) continue;
                for (int m = 0; m <= lv.Motions.Count; m++)
                    for (int a = 0; a <= (m < lv.Motions.Count ? lv.Motions[m].Attacks.Count : 0); a++)
                    {
                        bool exists = m < lv.Motions.Count && a < lv.Motions[m].Attacks.Count;
                        var dmg = tables.SkillDamage.Get((int)id, level, m, a);
                        Assert.That(dmg != null, Is.EqualTo(exists), $"skill {id} level {level} motion {m} attack {a}");
                        if (dmg == null) continue;
                        attacks++;
                        if (dmg.HasDamage) damaging++;
                    }
            }
        }
        Assert.That((skills, attacks, damaging), Is.Not.EqualTo((0, 0, 0)));
        Assert.That(damaging, Is.GreaterThan(0).And.LessThan(attacks));
        TestContext.Progress.WriteLine($"{skills} skills, {attacks} attacks, {damaging} with damage");
    }
}
