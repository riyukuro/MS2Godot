using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>WorldShareInfo (KMS 0xB0, GMS2 0xB2): u8 mode 0 + SpecialNPCInfo (OnWorldShareInfo, scenestategame.cpp:9756).</summary>
public class RcWorldShareInfoTests {
    // gms2 recording: 20 bytes
    static readonly byte[] Announce = RcPkt.Hex("00D3F35E01D5841E000200C40AA55E0000000001");

    [Test] public void ReadsTheAnnouncedNpc() {
        var r = new ByteReader(Announce);
        var m = WorldShareInfo.Read(r);
        Assert.That(m.Mode, Is.EqualTo(0));
        Assert.That(m.Npc.NpcId, Is.EqualTo(0x015EF3D3));
        Assert.That(m.Npc.FieldId, Is.EqualTo(0x001E84D5));
        Assert.That(m.Npc.Unk08, Is.EqualTo(2));
        Assert.That(m.Npc.Unk10, Is.EqualTo(0x5EA50AC4));
        Assert.That(m.Npc.Unk18, Is.EqualTo(1));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void OtherModeReadsNothingMore() {
        var r = new ByteReader(new byte[] { 1, 9, 9 });
        var m = WorldShareInfo.Read(r);
        Assert.That(m.Mode, Is.EqualTo(1));
        Assert.That(r.Available, Is.EqualTo(2));
    }

    [Test] public void TrailingByteIsRefused() {
        var data = new byte[Announce.Length + 1];
        Announce.CopyTo(data, 0);
        Assert.Throws<PacketLayoutException>(() => WorldShareInfo.Read(new ByteReader(data)));
    }

    [Test] public void ShortBodyIsRefused() =>
        Assert.Catch(() => WorldShareInfo.Read(new ByteReader(Announce[..10])));

    [Test] public void EveryRecordedGms2PacketReadsToTheEnd() {
        var all = Gms2Sniffs.Inbound("gms2", ProtocolProfile.Gms2.Number(Inbound.WorldShareInfo));
        Assert.That(all, Is.Not.Empty);
        foreach (var (file, data) in all) {
            var r = new ByteReader(data);
            var m = WorldShareInfo.Read(r);
            Assert.That(m.Mode, Is.EqualTo(0), file);
            Assert.That(m.Npc.NpcId, Is.GreaterThan(0), file);
            Assert.That(r.Available, Is.EqualTo(0), file);
        }
    }

    [Test] public void NumberOnBothProfiles() {
        Assert.That(ProtocolProfile.Kms2525.Number(Inbound.WorldShareInfo), Is.EqualTo((ushort) 0xB0));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.WorldShareInfo), Is.EqualTo((ushort) 0xB2));
    }
}
