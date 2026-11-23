using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The cast motions' table (Ms2.Formats SkillTable) filled from SkillDataTable's read of the same files, so one read serves both.
/// </summary>
public class SkillTableFeedTests
{
    [Test]
    public void EachSkillElementIsGivenOnceAsTheFilesAreRead()
    {
        var doc = XDocument.Parse("<ms2><skill id=\"7\"><basic/><level value=\"1\"><motion sequenceName=\"Attack_01_A\" motionEffect=\"e.xml\"/></level></skill>"
            + "<skill id=\"8\"><basic><combo chargingSkill=\"1\"/></basic></skill></ms2>");
        var rows = new SkillTable();
        int calls = 0;
        var t = new SkillDataTable(() => new[] { doc }, (_, _, _) => new List<XElement>(),
            e => { calls++; rows.Add(int.Parse((string)e.Attribute("id")), e); });
        t.Load();
        t.Load();
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(rows.Motion(7, 1, 0).MotionEffect, Is.EqualTo("e.xml"));
        Assert.That((rows.Count, rows.ChargingSkill(8), rows.ChargingSkill(7)), Is.EqualTo((2, true, false)));
    }

    [Test]
    public void TheFirstSourceOfASetStaysAndAnotherSetHasItsOwn()
    {
        // a second GameTables of the same set (another language) does not replace the source the prefetch filled; another
        // Data folder (KMS2 against GMS2) is another PackSet and never gets this one's table
        var dirs = new[] { Directory.CreateTempSubdirectory("skillfeed"), Directory.CreateTempSubdirectory("skillfeed") };
        try
        {
            foreach (var dir in dirs) dir.CreateSubdirectory("Resource");
            var (a, b) = (new PackSet(dirs[0].FullName), new PackSet(dirs[1].FullName));
            var (first, second, other) = (new SkillTable(), new SkillTable(), new SkillTable());
            SkillTable.SetSource(a, () => first);
            SkillTable.SetSource(a, () => second);
            SkillTable.SetSource(b, () => other);
            Assert.That(SkillTable.For(a), Is.SameAs(first));
            Assert.That(SkillTable.For(b), Is.SameAs(other));
        }
        finally
        {
            foreach (var dir in dirs) dir.Delete(true);
        }
    }

    [Test]
    public void AFailedReadIsNotReadAgainForTheCast()
    {
        // the prefetch's read throws; SkillTable.For then gets no rows at once (and reads the files itself), the skill files
        // are not read a second time on the cast's frame first
        int reads = 0;
        var t = new SkillDataTable(() => { reads++; throw new InvalidDataException("bad skill file"); }, (_, _, _) => new List<XElement>());
        var source = GameTables.FedRows(t, new SkillTable());
        Assert.Throws<InvalidDataException>(t.Load);
        Assert.That((t.Failed, source(), reads), Is.EqualTo((true, (SkillTable)null, 1)));
    }

    [Test]
    public void AReadThatSucceedsAfterAFailedOneClearsIt()
    {
        // a later lookup's read that succeeds clears the failure, so the source gives the rows again and For does not read
        // the files a second time
        int reads = 0;
        var doc = XDocument.Parse("<ms2><skill id=\"7\"><basic/></skill></ms2>");
        var rows = new SkillTable();
        var t = new SkillDataTable(() => ++reads == 1 ? throw new InvalidDataException("bad skill file") : new[] { doc },
            (_, _, _) => new List<XElement>(), e => rows.Add(int.Parse((string)e.Attribute("id")), e));
        Assert.Throws<InvalidDataException>(t.Load);
        Assert.That(t.Failed, Is.True);
        t.Load();
        Assert.That((t.Failed, GameTables.FedRows(t, rows)(), reads), Is.EqualTo((false, rows, 2)));
    }

    [Test]
    public void TheSourceGivesTheRowsOnceTheReadIsDone()
    {
        var doc = XDocument.Parse("<ms2><skill id=\"7\"><basic/><level value=\"1\"><motion sequenceName=\"Attack_01_A\"/></level></skill></ms2>");
        var rows = new SkillTable();
        var t = new SkillDataTable(() => new[] { doc }, (_, _, _) => new List<XElement>(), e => rows.Add(int.Parse((string)e.Attribute("id")), e));
        Assert.That(GameTables.FedRows(t, rows)(), Is.SameAs(rows));
        Assert.That((t.Failed, rows.Count), Is.EqualTo((false, 1)));
    }

    [Test]
    public void OnTheDataTheFedTableEqualsItsOwnRead()
    {
        // SkillTable.For takes the rows the field build's prefetch (GameTables.Skills) read; they are the rows its own read of
        // the files gives, skill by skill
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = new PackSet(d);
        var tables = new GameTables(packs, "en");
        tables.PrefetchSkillDamage().Wait();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var fed = SkillTable.For(packs);
        TestContext.Progress.WriteLine($"SkillTable.For after the prefetch: {clock.Elapsed.TotalMilliseconds:F2} ms, {(GC.GetAllocatedBytesForCurrentThread() - before) / 1024} KiB, {fed.Count} skills");
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(64 * 1024), "the rows came from the prefetch, not a second read");
        var own = new SkillTable(packs);
        Assert.That(fed.Ids, Is.EquivalentTo(own.Ids));
        foreach (var id in own.Ids)
        {
            Assert.That((fed.ChargingSkill(id), fed.StateMove(id)), Is.EqualTo((own.ChargingSkill(id), own.StateMove(id))), $"skill {id}");
            Assert.That(Text(fed.Levels(id)), Is.EqualTo(Text(own.Levels(id))), $"skill {id}");
        }
    }

    static string Text(IReadOnlyList<SkillLevelRow> levels) => string.Join(";", levels.Select(l => l.Level + ":" + string.Join(",", l.Motions.Select(m =>
        string.Join("|", m.SequenceName, m.MotionEffect, string.Join("/", m.HitEffects), m.LoopCount, string.Join("/", m.AttackPoints), m.MoveType,
            m.KeyMove, m.MoveDistance, m.MoveAngle, m.IgnoreCollisionGroups, m.LoopEffect, m.SplashInvokeEffect, m.SplashEndEffect,
            m.SplashDurationCastEffect, m.IgnoreOptimalHideCastEffect, m.SplashCastEffectStartDelay, m.PushCylinder)))));
}
