using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Lapenshard 0x11B read and the 0xAD sends (OnLapenshard, scenestategame.cpp:8743; itemlapensharddata.cpp:455).</summary>
public class LapenshardTests {
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }
    static byte[] Bytes(Ms2.Net.Wire.IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    [Test] public void OpcodesAreTheClientsOnKmsAndTheMappedOnesOnGms() {
        Assert.That((int) Outbound.Lapenshard, Is.EqualTo(0xAD));
        Assert.That((int) Inbound.Lapenshard, Is.EqualTo(0x11B));
        Assert.That(ProtocolProfile.Gms2.Number(Outbound.Lapenshard), Is.EqualTo((ushort) 0xAF));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.Lapenshard), Is.EqualTo((ushort) 0x11D));
    }

    [Test] public void WornReadsTheSlotsAndItems() {
        var r = R(w => { w.WriteByte(0); w.WriteInt(2); w.WriteInt(1); w.WriteInt(9001); w.WriteInt(4); w.WriteInt(9002); });
        var m = (LapenshardMessage.Worn) LapenshardMessage.Read(r);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(m.Entries, Is.EqualTo(new[] { (1, 9001), (4, 9002) }));
    }

    [Test] public void EquippedAndUnequippedRead() {
        var r = R(w => { w.WriteByte(1); w.WriteInt(3); w.WriteInt(77); });
        Assert.That(LapenshardMessage.Read(r), Is.EqualTo(new LapenshardMessage.Equipped(3, 77)));
        Assert.That(r.Available, Is.EqualTo(0));
        r = R(w => { w.WriteByte(2); w.WriteInt(3); });
        Assert.That(LapenshardMessage.Read(r), Is.EqualTo(new LapenshardMessage.Unequipped(3)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void UpgradeTabKindsRead() {
        var r = R(w => { w.WriteByte(4); w.WriteInt(250); });
        Assert.That(LapenshardMessage.Read(r), Is.EqualTo(new LapenshardMessage.Preview(250)));
        r = R(w => { w.WriteByte(5); w.WriteLong(12); w.WriteInt(55); w.WriteInt(2); w.WriteByte(1); });
        Assert.That(LapenshardMessage.Read(r), Is.EqualTo(new LapenshardMessage.UpgradeResult(12, 55, 2, true)));
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(LapenshardMessage.Read(R(w => w.WriteByte(6))), Is.TypeOf<LapenshardMessage.UpgradeDone>());
        Assert.That(LapenshardMessage.Read(R(w => w.WriteByte(3))), Is.Null);
    }

    [Test] public void SendsWriteTheClientsBytes() {
        Assert.That(Bytes(new LapenshardEquip(2, 0x0102030405060708)), Is.EqualTo(new byte[] { 1, 2,0,0,0, 8,7,6,5,4,3,2,1 }));
        Assert.That(Bytes(new LapenshardUnequip(5)), Is.EqualTo(new byte[] { 2, 5,0,0,0 }));
        Assert.That(Bytes(new LapenshardPreviewRequest(1, 2, 3)), Is.EqualTo(new byte[] { 3, 1,0,0,0,0,0,0,0, 2,0,0,0, 3,0,0,0 }));
        Assert.That(Bytes(new LapenshardAddFodder(1, 2, 3, 4)), Is.EqualTo(new byte[] { 4, 1,0,0,0,0,0,0,0, 2,0,0,0, 3,0,0,0, 4,0,0,0 }));
        Assert.That(Bytes(new LapenshardUpgrade(1, 2, 3, new[] { (9L, 6) }, 7)),
            Is.EqualTo(new byte[] { 5, 1,0,0,0,0,0,0,0, 2,0,0,0, 3,0,0,0, 1,0,0,0, 9,0,0,0,0,0,0,0, 6,0,0,0, 7,0,0,0 }));
    }
}
