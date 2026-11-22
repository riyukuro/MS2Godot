using System.Numerics;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The hit npc's shake: CNpcVibrate (Start, Update) and its arming in CNpc::vfunc_33.</summary>
public class NpcVibrateTests
{
    static SkillAttackDamage Attack(float ratio) => new() { DamagedVibrateRatio = ratio };

    [Test]
    public void AnOwnHitWithDamageArmsTheRowAmpTimesTheAttackRatio()
    {
        var v = new NpcVibrate();
        Assert.That(v.OnHit(10f, -120, true, 0.3f, 4f, Attack(0.5f)), Is.True);
        Assert.That((v.Active, v.Start, v.End, v.Amp), Is.EqualTo((true, 10f, 10.3f, 2f)));
        // no attack on the record: the row's amp alone
        var w = new NpcVibrate();
        Assert.That(w.OnHit(1f, -1, true, 0.2f, 4f, null), Is.True);
        Assert.That(w.Amp, Is.EqualTo(4f));
    }

    [TestCase(-120L, false, 0.3f, 4f, 1f, TestName = "another owner's hit")]
    [TestCase(0L, true, 0.3f, 4f, 1f, TestName = "a total of 0 (a miss)")]
    [TestCase(50L, true, 0.3f, 4f, 1f, TestName = "a heal total")]
    [TestCase(-120L, true, 0f, 4f, 1f, TestName = "no duration")]
    [TestCase(-120L, true, 0.3f, 0f, 1f, TestName = "no amp")]
    [TestCase(-120L, true, 0.3f, 4f, 0f, TestName = "an attack ratio of 0")]
    public void NothingElseArms(long total, bool own, float duration, float amp, float ratio)
    {
        var v = new NpcVibrate();
        Assert.That(v.OnHit(10f, total, own, duration, amp, Attack(ratio)), Is.False);
        Assert.That(v.Active, Is.False);
    }

    [Test]
    public void TheTotalIsTheSumOfTheHits()
    {
        Assert.That(NpcVibrate.TotalDamage(new[] { new Hit(0, -100), new Hit(2, 0), new Hit(1, -25) }), Is.EqualTo(-125L));
        Assert.That(NpcVibrate.TotalDamage(System.Array.Empty<Hit>()), Is.EqualTo(0L));
    }

    [Test]
    public void EachFrameMovesTheAmpInXYUntilTheEnd()
    {
        var v = new NpcVibrate();
        v.Arm(0f, 1f, 3f);
        var rnd = new System.Random(5);
        int moved = 0;
        var signs = new HashSet<(int, int)>();
        for (int f = 0; f < 200; f++)
        {
            var p = new Vector3(100f, 200f, 50f);
            Assert.That(v.Update(f * 0.004f, ref p, rnd), Is.True);
            var off = p - new Vector3(100f, 200f, 50f);
            Assert.That(off.Z, Is.EqualTo(0f));
            if (off == Vector3.Zero) continue;
            Assert.That(off.Length(), Is.EqualTo(3f).Within(1e-4f), "a unit direction times the amp");
            signs.Add((Math.Sign(off.X), Math.Sign(off.Y)));
            moved++;
        }
        Assert.That(moved, Is.GreaterThan(190));
        Assert.That(signs, Has.Count.EqualTo(4), "each axis takes either sign");
        // at the end time it turns off and leaves the point
        var q = new Vector3(1f, 2f, 3f);
        Assert.That(v.Update(1f, ref q, rnd), Is.False);
        Assert.That((v.Active, q), Is.EqualTo((false, new Vector3(1f, 2f, 3f))));
        Assert.That(v.Update(0.5f, ref q, rnd), Is.False, "off stays off");
    }

    [Test]
    public void AFrameAllocatesNothing()
    {
        var v = new NpcVibrate();
        var rnd = new System.Random(1);
        var p = Vector3.Zero;
        v.Arm(0f, 100f, 2f);
        v.Update(0f, ref p, rnd);
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 1000; f++) { v.Update(f * 0.01f, ref p, rnd); v.OnHit(f, -1, true, 1f, 1f, null); }
        Assert.That(System.GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0L));
    }
}
