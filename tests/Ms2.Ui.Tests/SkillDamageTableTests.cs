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
        new(() => docs.Select(x => XDocument.Parse(x)), (p, c, _) => p.Elements(c).ToList());

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
            return new[] { XDocument.Parse(@"<ms2><skill id=""5""><basic/><level value=""1""><motion>
                <attack><damage value=""7""/></attack></motion></level></skill></ms2>") };
        }, (p, c, _) => p.Elements(c).ToList());
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
        tables.SkillDamage.Get(1, 1, 0, 0);
        TestContext.Progress.WriteLine($"first read: SkillDataTable {skillData} ms, SkillDamageTable {clock.ElapsedMilliseconds} ms");
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
