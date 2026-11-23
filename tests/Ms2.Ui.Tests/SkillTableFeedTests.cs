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
