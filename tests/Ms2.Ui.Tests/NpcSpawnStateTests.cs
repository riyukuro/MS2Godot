using Ms2.Ui.Skills;
using NUnit.Framework;
using Step = Ms2.Ui.Skills.NpcSpawnState.Step;

namespace Ms2.Ui.Tests;

/// <summary>NpcSpawnState against StateSpawn (statespawn.cpp:65-130) on made-up frames.</summary>
public class NpcSpawnStateTests
{
    [Test]
    public void TheDelayIsTheObjectIdsFirstDrawModThreeHundredPlusOneHundred()
    {
        // Unk_Random(id) (questmasktable.cpp:400-406, SetState :424-429) and FUN_1407684c0 (:451-461), worked by hand
        var want = new (int id, int ms)[] { (0, 373), (1, 242), (2, 207), (1000, 397), (0x01000123, 177), (-5, 270) };
        foreach (var (id, ms) in want) Assert.That(NpcSpawnState.DelayMs(id), Is.EqualTo(ms), $"object {id}");
        for (int id = 0; id < 5000; id++) Assert.That(NpcSpawnState.DelayMs(id), Is.InRange(100, 399));
    }

    [Test]
    public void ThePointsSequencePlaysAfterTheDelayThenTheStateGoesToIdleAtItsLength()
    {
        var s = new NpcSpawnState();
        s.Enter(1, "Regen_A", 10_000);
        Assert.That(s.Active && s.Delay == 242, Is.True);
        int asked = 0;
        float Length(string name) { asked++; Assert.That(name, Is.EqualTo("Regen_A")); return 1.5f; }
        Assert.That(s.Update(0.1f, 10_100, Length), Is.EqualTo(Step.Wait));
        Assert.That(s.Update(0.1f, 10_241, Length), Is.EqualTo(Step.Wait));
        Assert.That(s.Update(0.5f, 10_242, Length), Is.EqualTo(Step.Show));
        Assert.That(asked, Is.EqualTo(1));
        Assert.That(s.Update(0.5f, 10_742, Length), Is.EqualTo(Step.None));
        Assert.That(s.Update(0.25f, 10_992, Length), Is.EqualTo(Step.None));
        Assert.That(s.Update(0.25f, 11_242, Length), Is.EqualTo(Step.Idle));
        Assert.That(asked, Is.EqualTo(1));
        Assert.That(s.Active, Is.False);
        Assert.That(s.Update(0.1f, 12_000, Length), Is.EqualTo(Step.None));
    }

    [Test]
    public void WithoutASequenceOrWithOneOfNoLengthTheNpcShowsAndIdlesInOneFrame()
    {
        var s = new NpcSpawnState();
        s.Enter(2, null, 0);
        Assert.That(s.Update(0.016f, 206, _ => throw new AssertionException("no sequence to measure")), Is.EqualTo(Step.Wait));
        Assert.That(s.Update(0.016f, 207, _ => throw new AssertionException("no sequence to measure")), Is.EqualTo(Step.ShowAndIdle));
        s.Enter(2, "Missing_A", 0);
        Assert.That(s.Update(0.016f, 500, _ => 0f), Is.EqualTo(Step.ShowAndIdle));
    }

    [Test]
    public void LeavingTheStateStopsIt()
    {
        var s = new NpcSpawnState();
        s.Enter(0, "Regen_A", 0);
        s.Leave();
        Assert.That(s.Active, Is.False);
        Assert.That(s.Update(1f, 10_000, _ => 1f), Is.EqualTo(Step.None));
    }
}
