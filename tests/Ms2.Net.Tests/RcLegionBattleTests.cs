using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>LegionBattle (KMS 0xF4, GMS2 0xF6): CSceneStateGame::OnLegionBattle (scenestategame.cpp:8480). Recorded GMS2: modes 0 and 1.</summary>
public class RcLegionBattleTests {
    static ByteReader Hex(string s) => new(RcPkt.Hex(s.Replace(" ", "")));
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    // recorded: an empty list, 3 bytes
    [Test] public void EmptyListIsModeAndZeroCount() {
        var m = LegionBattle.Read(Hex("00 0000"));
        Assert.That(m.Mode, Is.EqualTo(LegionBattle.Replace));
        Assert.That(m.Rows, Is.Empty);
    }

    // recorded: first rows of a 19-row list
    [Test] public void ListRowsAreFieldNpcAndTime() {
        var m = LegionBattle.Read(R(w => {
            w.WriteByte(0); w.WriteShort(2);
            w.WriteInt(1000); w.WriteInt(0x2A002A); w.WriteLong(0x5EA273L);
            w.WriteInt(1001); w.WriteInt(0x2A002A); w.WriteLong(0x5EA27AL);
        }));
        Assert.That(m.Rows, Is.EqualTo(new[] { new SpawnGuarantee(1000, 0x2A002A, 0x5EA273), new SpawnGuarantee(1001, 0x2A002A, 0x5EA27A) }));
    }

    [Test] public void SetIsOneRow() {
        var m = LegionBattle.Read(R(w => { w.WriteByte(1); w.WriteInt(5); w.WriteInt(6); w.WriteLong(7); }));
        Assert.That(m.Rows, Is.EqualTo(new[] { new SpawnGuarantee(5, 6, 7) }));
    }

    [Test] public void RemoveIsOneFieldId() {
        var m = LegionBattle.Read(Hex("02 39050000"));
        Assert.That(m.RemovedFieldId, Is.EqualTo(0x539));
    }

    [Test] public void UnknownModeReadsNothingMore() {
        var m = LegionBattle.Read(Hex("07 0102"));
        Assert.That(m.Rows, Is.Empty);
    }

    [Test] public void TrailingBytesAndImpossibleCountsAreRefused() {
        Assert.Throws<PacketLayoutException>(() => LegionBattle.Read(Hex("00 0000 00")));
        Assert.Throws<PacketLayoutException>(() => LegionBattle.Read(Hex("02 01000000 00")));
        Assert.Throws<InvalidDataException>(() => LegionBattle.Read(Hex("00 FFFF 00000000")));
        Assert.Throws<IndexOutOfRangeException>(() => LegionBattle.Read(Hex("01 01000000")));
    }

    [Test] public void EveryRecordedGms2PacketReadsToTheEnd() {
        var all = Gms2Sniffs.Inbound("gms2", ProtocolProfile.Gms2.Number(Inbound.LegionBattle));
        Assert.That(all, Is.Not.Empty);
        var kinds = new HashSet<byte>();
        int rows = 0;
        foreach (var (file, data) in all) {
            var r = new ByteReader(data);
            var m = LegionBattle.Read(r);
            kinds.Add(m.Mode);
            rows += m.Rows.Count;
            Assert.That(r.Available, Is.EqualTo(0), file);
        }
        Assert.That(kinds, Is.EquivalentTo(new byte[] { 0, 1 }));
        Assert.That(rows, Is.GreaterThan(0));
    }

    [Test] public void NumberOnBothProfiles() {
        Assert.That(ProtocolProfile.Kms2525.Number(Inbound.LegionBattle), Is.EqualTo((ushort) 0xF4));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.LegionBattle), Is.EqualTo((ushort) 0xF6));
    }
}
