using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The Taxi packet (0x80, CSceneStateGame::OnTaxi): i32 count, the field ids, u8 bOpen.</summary>
public class TaxiPacketTests {
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    [Test]
    public void ReadsTheIdsAndTheOpenFlag() {
        Assert.That((int)Inbound.Taxi, Is.EqualTo(0x80));
        var t = TaxiFound.Read(R(w => { w.WriteInt(2); w.WriteInt(2000001); w.WriteInt(2000062); w.WriteBool(true); }));
        Assert.That(t.Fields, Is.EqualTo(new[] { 2000001, 2000062 }));
        Assert.That(t.Open, Is.True);
        var empty = TaxiFound.Read(R(w => { w.WriteInt(0); w.WriteBool(false); }));
        Assert.That(empty.Fields, Is.Empty);
        Assert.That(empty.Open, Is.False);
    }

    [Test]
    public void ACountPastTheBodyIsALayoutError() {
        Assert.Throws<PacketLayoutException>(() => TaxiFound.Read(R(w => { w.WriteInt(5); w.WriteInt(1); w.WriteBool(true); })));
    }
}
