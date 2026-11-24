using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The dungeon enter answer (0x19) is read by FieldEntrance: u8 result; 0 = i32 count and count x (i32 dungeon id, u8 state);
/// 1 = no body; 2 = u8 type, i32 room id (CSceneStateGame::OnFieldEntrance, scenestategame.cpp:2366). Layouts: the GMS2 packet layout notes C10.</summary>
public class RcFieldEntranceTests {
    // gms2 recording: 13-Tria, 10 bytes (one pair)
    static readonly byte[] OnePair = RcPkt.Hex("00010000001154310100");
    // gms2 recording: Port 54200, 6 bytes
    static readonly byte[] Limit = RcPkt.Hex("020F61BD4001");

    [Test] public void StatePairsAreReadInOrder() {
        var r = new ByteReader(OnePair);
        var m = FieldEntrance.Read(r);
        Assert.That(m.Result, Is.EqualTo((byte) 0));
        Assert.That(m.Entries, Is.EqualTo(new[] { new DungeonEnterEntry(0x01315411, 0) }));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void LimitMessageCarriesReasonAndRoom() {
        var r = new ByteReader(Limit);
        var m = FieldEntrance.Read(r);
        Assert.That((m.Result, m.Type, m.Id), Is.EqualTo(((byte) 2, (byte) 15, 0x0140BD61)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void NoticeResultHasNoBody() {
        var r = new ByteReader(new byte[] { 1 });
        Assert.That(FieldEntrance.Read(r).Result, Is.EqualTo((byte) 1));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void ACountPastTheBodyIsRefused() {
        var w = new ByteWriter();
        w.WriteByte(0); w.WriteInt(1000); w.WriteInt(5); w.WriteByte(0);
        Assert.Throws<InvalidDataException>(() => FieldEntrance.Read(new ByteReader(w.ToArray())));
    }

    [Test] public void EveryRecordedGms2PacketReadsToTheEnd() {
        var all = Gms2Sniffs.Inbound("gms2", ProtocolProfile.Gms2.Number(Inbound.FieldEntrance));
        Assert.That(all, Is.Not.Empty);
        foreach (var (file, data) in all) {
            var r = new ByteReader(data);
            var m = FieldEntrance.Read(r);
            Assert.That(r.Available, Is.EqualTo(0), file);
            if (m.Result == 0) Assert.That(m.Entries, Is.Not.Empty, file);
        }
    }

    [Test] public void NumberOnBothProfiles() {
        Assert.That(ProtocolProfile.Kms2525.Number(Inbound.FieldEntrance), Is.EqualTo((ushort) 0x19));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.FieldEntrance), Is.EqualTo((ushort) 0x19));
    }
}
