using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class GlobalFactorTests {
    static byte[] Packet(float seed, params int[] kinds) {
        var w = new ByteWriter();
        for (int t = 0; t < 6; t++) { w.WriteByte((byte) kinds[t]); for (int lv = 1; lv <= 99; lv++) w.WriteFloat(seed + t * 100 + lv); }
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    [Test] public void RatesAreOneUntilReceived() {
        var g = new GlobalFactor();
        Assert.That(g.ExpRate(50, 5), Is.EqualTo(1f)); Assert.That(g.MesoRate(50, 2), Is.EqualTo(1f));
    }

    [Test] public void ReadsThreeExpThenThreeMesoTables() {
        var g = new GlobalFactor(); var r = new ByteReader(Packet(0, 5, 6, 7, 0, 1, 2));
        Assert.That(g.Apply(r), Is.True); Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(g.ExpRate(10, 5), Is.EqualTo(10f)); Assert.That(g.ExpRate(99, 7), Is.EqualTo(299f));
        Assert.That(g.MesoRate(1, 0), Is.EqualTo(301f)); Assert.That(g.MesoRate(99, 2), Is.EqualTo(599f));
        Assert.That(g.ExpRate(10, 0), Is.EqualTo(1f));      // a kind the packet did not carry
        Assert.That(g.ExpRate(0, 5), Is.EqualTo(1f)); Assert.That(g.ExpRate(100, 5), Is.EqualTo(1f));
    }

    [Test] public void OnlyTheFirstPacketIsApplied() {
        var g = new GlobalFactor(); g.Apply(new ByteReader(Packet(0, 5, 6, 7, 0, 1, 2)));
        Assert.That(g.Apply(new ByteReader(Packet(1000, 5, 6, 7, 0, 1, 2))), Is.False);
        Assert.That(g.ExpRate(10, 5), Is.EqualTo(10f));
    }

    [Test] public void QuestRatesMapTypesToKinds() {
        var g = new GlobalFactor(); g.Apply(new ByteReader(Packet(0, 5, 6, 7, 0, 1, 2)));
        Assert.That(g.QuestRates(0, 10), Is.EqualTo((10f, 310f)));
        Assert.That(g.QuestRates(1, 10), Is.EqualTo((110f, 410f)));
        Assert.That(g.QuestRates(3, 10), Is.EqualTo((210f, 510f)));
        Assert.That(g.QuestRates(2, 10), Is.EqualTo((1f, 1f)));
    }

    [Test] public void KindOutsideTheTableIsReadAndDropped() {
        var g = new GlobalFactor(); var r = new ByteReader(Packet(0, 200, 6, 7, 0, 1, 200));
        Assert.That(g.Apply(r), Is.True); Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(g.ExpRate(10, 6), Is.EqualTo(110f));
    }
}
