using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Survival (KMS 0x119, GMS2 0x11B): no client handler is named; layouts from the GMS2 recordings (modes 0x16, 0x17, 0x1E).</summary>
public class RcSurvivalTests {
    static ByteReader Hex(string s) => new(RcPkt.Hex(s.Replace(" ", "")));
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    // recorded: 695 packets, 42 bytes
    [Test] public void StatsAreAccountPassExpAndLevels() {
        var m = SurvivalPacket.Read(Hex("17 846BB6305D93172C 00000000 00 0000000000000000 01000000 00000000 00000000 0000000000000000"));
        Assert.That(m.AccountId, Is.EqualTo(0x2C17935D30B66B84L));
        Assert.That((m.GoldPass, m.Exp, m.Level, m.SilverClaimed, m.GoldClaimed, m.ExpGained), Is.EqualTo((false, 0L, 1, 0, 0, 0L)));
    }

    [Test] public void StatsFieldsKeepTheirOrder() {
        var m = SurvivalPacket.Read(R(w => { w.WriteByte(0x17); w.WriteLong(5); w.WriteInt(9); w.WriteBool(true); w.WriteLong(1234); w.WriteInt(7); w.WriteInt(3); w.WriteInt(2); w.WriteLong(88); }));
        Assert.That((m.AccountId, m.Unknown, m.GoldPass, m.Exp, m.Level, m.SilverClaimed, m.GoldClaimed, m.ExpGained), Is.EqualTo((5L, 9, true, 1234L, 7, 3, 2, 88L)));
    }

    // recorded: 675 packets, 26 bytes, three empty types
    [Test] public void EmptyMedalList() {
        var m = SurvivalPacket.Read(Hex("1E 03 00000000 00000000 00000000 00000000 00000000 00000000"));
        Assert.That(m.Medals.Count, Is.EqualTo(3));
        Assert.That(m.Medals.All(t => t.Medals.Count == 0), Is.True);
    }

    // recorded: 2 packets, 62 bytes
    [Test] public void MedalListHoldsEquippedIdAndMedals() {
        var m = SurvivalPacket.Read(R(w => {
            w.WriteByte(0x1E); w.WriteByte(2);
            w.WriteInt(100); w.WriteInt(2); w.WriteInt(100); w.WriteLong(long.MaxValue); w.WriteInt(101); w.WriteLong(5);
            w.WriteInt(0); w.WriteInt(0);
        }));
        Assert.That(m.Medals[0].EquippedId, Is.EqualTo(100));
        Assert.That(m.Medals[0].Medals, Is.EqualTo(new[] { new SurvivalMedal(100, long.MaxValue), new SurvivalMedal(101, 5) }));
        Assert.That(m.Medals[1].Medals, Is.Empty);
    }

    // recorded: 1362 packets, 47 bytes, body kept raw
    [Test] public void Mode22KeepsTheRawBody() {
        var m = SurvivalPacket.Read(Hex("16 00000000 04" + new string('0', 82)));
        Assert.That(m.RawBody.Length, Is.EqualTo(46));
        Assert.That(m.RawBody[4], Is.EqualTo(4));
    }

    [Test] public void UnknownModeReadsNothingMore() =>
        Assert.That(SurvivalPacket.Read(Hex("63 0102")).Mode, Is.EqualTo(0x63));

    [Test] public void TrailingBytesAndImpossibleCountsAreRefused() {
        Assert.Throws<PacketLayoutException>(() => SurvivalPacket.Read(R(w => { w.WriteByte(0x17); w.WriteLong(5); w.WriteInt(9); w.WriteBool(true); w.WriteLong(1); w.WriteInt(1); w.WriteInt(1); w.WriteInt(1); w.WriteLong(1); w.WriteByte(0); })));
        Assert.Throws<InvalidDataException>(() => SurvivalPacket.Read(R(w => { w.WriteByte(0x1E); w.WriteByte(1); w.WriteInt(1); w.WriteInt(1000); })));
        Assert.Throws<PacketLayoutException>(() => SurvivalPacket.Read(Hex("1E 00 00")));
    }

    [Test] public void EveryRecordedGms2PacketReadsToTheEnd() {
        var all = Gms2Sniffs.Inbound("gms2", ProtocolProfile.Gms2.Number(Inbound.Survival));
        Assert.That(all, Is.Not.Empty);
        var kinds = new HashSet<byte>();
        int medals = 0;
        foreach (var (file, data) in all) {
            var r = new ByteReader(data);
            var m = SurvivalPacket.Read(r);
            kinds.Add(m.Mode);
            medals += m.Medals.Sum(t => t.Medals.Count);
            Assert.That(r.Available, Is.EqualTo(0), file);
        }
        Assert.That(kinds, Is.EquivalentTo(new byte[] { 0x16, 0x17, 0x1E }));
        Assert.That(medals, Is.EqualTo(4 * 8 + 2 * 3));
    }

    [Test] public void NumberOnBothProfiles() {
        Assert.That(ProtocolProfile.Kms2525.Number(Inbound.Survival), Is.EqualTo((ushort) 0x119));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.Survival), Is.EqualTo((ushort) 0x11B));
    }
}
