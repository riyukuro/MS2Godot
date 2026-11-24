using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The tombstone's states (CBreakableObject, breakableobject.cpp) and the Tombstone packet's hits (PC::FUN_14165d300).</summary>
public class TombstoneTests
{
    const string Prefix = "co_obj_brk_tomb_ani_";

    static Tombstone Make() => new(Prefix + "Regen_A", Prefix + "Damg_A", Prefix + "Idle_A", "Dead_A", n => n - 1);

    [Test]
    public void SetupKeepsIdleWithoutTimer()
    {
        var t = Make();
        Assert.That(t.Initial, Is.EqualTo(Prefix + "Regen_A"));
        Assert.That(t.State, Is.EqualTo(Tombstone.Idle));
        Assert.That(t.Play, Is.Null);
        t.Update(1_000_000);
        Assert.That(t.State, Is.EqualTo(Tombstone.Idle));
        Assert.That(t.KeepFor(true), Is.True);
        Assert.That(t.KeepFor(false), Is.False);       // PC::vfunc_142: not breaking and the PC left StateDead
    }

    [Test]
    public void HitPlaysDamageThenIdle()
    {
        var t = Make();
        Assert.That(t.Apply(2, 5, 100), Is.EqualTo(2));
        Assert.That((t.State, t.Play, t.PlayForce), Is.EqualTo((Tombstone.Hit, Prefix + "Damg_A", true)));
        t.Played();
        t.Update(1099);
        Assert.That(t.State, Is.EqualTo(Tombstone.Hit));
        t.Update(1100);
        Assert.That((t.State, t.Play), Is.EqualTo((Tombstone.Idle, Prefix + "Idle_A")));
        Assert.That(t.Visible, Is.True);
    }

    [Test]
    public void BreakHidesAndFinishes()
    {
        var t = Make();
        Assert.That(t.Apply(0, 0, 0), Is.Null);
        Assert.That((t.State, t.Play, t.PlayForce, t.Visible), Is.EqualTo((Tombstone.Break, "Dead_A", false, false)));
        Assert.That(t.KeepFor(false), Is.True);        // a breaking tomb outlives the revival
        t.Update(999);
        Assert.That(t.Finished, Is.False);
        t.Update(1000);
        Assert.That(t.Finished, Is.True);
        Assert.That(t.KeepFor(true), Is.False);
    }

    [Test]
    public void RegenGoesToIdle()
    {
        var t = Make();
        t.SetState(Tombstone.Regen, 500, 0);
        Assert.That(t.Play, Is.EqualTo(Prefix + "Regen_A"));
        t.Update(500);
        Assert.That(t.State, Is.EqualTo(Tombstone.Idle));
    }

    [Test]
    public void EmptyListsPlayNothing()
    {
        var t = new Tombstone(null, "", "idle", null);
        Assert.That(t.Initial, Is.EqualTo("idle"));
        t.Apply(1, 3, 0);
        Assert.That(t.Play, Is.Null);
        t.SetState(Tombstone.Hidden, 0, 0);
        Assert.That(t.Visible, Is.False);
    }

    [TestCase(0, 5, 0)]
    [TestCase(1, 5, 1)]
    [TestCase(4, 5, 4)]
    [TestCase(5, 5, null)]
    [TestCase(1, 3, 2)]       // ceil(5/3 x 1) = 2
    [TestCase(2, 3, 4)]       // ceil(3.33) = 4
    [TestCase(1, 0, null)]    // max(1, 0): ceil(5) = 5
    [TestCase(1, 10, 1)]      // ceil(0.5)
    public void GaugeStage(int hit, int max, int? stage) => Assert.That(Tombstone.GaugeStage(hit, max), Is.EqualTo(stage));
    /// <summary>FUN_14165d300 (pc.cpp:3464): a hit always plays FUN_140ade030's hit sound; a break only with a hitter, and the
    /// local player's break with a hitter then plays PlayEventVoice(4). The relation is 1 for the own tomb, else 3.</summary>
    [TestCase(3, 1, true, true, 1, false)]
    [TestCase(3, 0, false, true, 3, false)]
    [TestCase(0, 1, true, true, 1, true)]
    [TestCase(0, 1, false, true, 3, false)]
    [TestCase(0, 0, true, false, 1, false)]
    [TestCase(0, 0, false, false, 3, false)]
    public void ThePacketsSoundsFollowFun14165d300(int kind, int hitter, bool local, bool hitSound, int relation, bool voice) =>
        Assert.That(Tombstone.PacketSounds(kind, hitter, local), Is.EqualTo((hitSound, relation, voice)));

    /// <summary>FUN_140ade030's volume: VolumeMyPcToBreakableObject for relation 1 or 2, else VolumePcToBreakableObject;
    /// the Define 0.1 without the tables; the tables' "0.8f" and "0.3f" read through atof.</summary>
    [Test]
    public void TheHitVolumeIsTheBreakableObjectConstantOfTheRelation()
    {
        var c = new Ms2.Formats.World.ClientConstants(new Dictionary<string, string> { ["VolumeMyPcToBreakableObject"] = "0.8f", ["VolumePcToBreakableObject"] = "0.3f" });
        Assert.That((Tombstone.HitVolume(1, c), Tombstone.HitVolume(2, c), Tombstone.HitVolume(3, c), Tombstone.HitVolume(0, c)), Is.EqualTo((0.8f, 0.8f, 0.3f, 0.3f)));
        Assert.That((Tombstone.HitVolume(1, null), Tombstone.HitVolume(3, null)), Is.EqualTo((0.1f, 0.1f)));
    }
}
