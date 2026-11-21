using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The layer switch of an outfit tab (FUN_1411df700 / FUN_1411df910, equipment.cpp:401-459) and OnActiveCodiTab's record.</summary>
public class OutfitLayerTests {
    [TestCase(false, 0L, 0L, 100L, true)]     // feature 0x8e off: always shown
    [TestCase(true, 0L, 0L, 100L, false)]     // never started
    [TestCase(true, 5L, 0L, 100L, true)]      // no end
    [TestCase(true, 5L, 200L, 100L, true)]    // running
    [TestCase(true, 5L, 100L, 100L, false)]   // ended at the server time
    [TestCase(true, 5L, 50L, 100L, false)]    // ended
    public void OutfitShownFollowsThePeriod(bool feature, long start, long end, long now, bool shown) =>
        Assert.That(WornEquips.OutfitShown(feature, start, end, now), Is.EqualTo(shown));

    [Test] public void ARunningPeriodFlipsAtItsEnd() {
        long start = 5, end = 130, now = 100;
        Assert.That(WornEquips.NextFlip(start, end, now), Is.EqualTo(end));
        Assert.That(WornEquips.OutfitShown(true, start, end, end - 1), Is.True);
        Assert.That(WornEquips.OutfitShown(true, start, end, WornEquips.NextFlip(start, end, now)), Is.False);
    }

    [TestCase(0L, 130L, 100L)] [TestCase(5L, 0L, 100L)] [TestCase(5L, 90L, 100L)]
    public void NoFlipWhenNeverStartedNoEndOrEnded(long start, long end, long now) =>
        Assert.That(WornEquips.NextFlip(start, end, now), Is.EqualTo(long.MaxValue));

    [Test] public void LookLeavesOutLayerTwoWhenOff() {
        var gear = new[] { new EquipItem { Id = 1, Uid = 1 } };
        var outfit = new[] { new EquipItem { Id = 2, Uid = 2 } };
        Assert.That(WornEquips.Look(gear, outfit, true).Select(i => i.Uid), Is.EqualTo(new long[] { 1, 2 }));
        Assert.That(WornEquips.Look(gear, outfit, false).Select(i => i.Uid), Is.EqualTo(new long[] { 1 }));
    }

    [Test] public void CodiTabReadsActorStartEndExpireSilent() {
        var w = new ByteWriter(); w.WriteInt(7); w.WriteLong(10); w.WriteLong(20); w.WriteLong(30); w.WriteBool(true);
        var t = CodiTab.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()));
        Assert.That(t, Is.EqualTo(new CodiTab(7, 10, 20, 30, true)));
    }
}
