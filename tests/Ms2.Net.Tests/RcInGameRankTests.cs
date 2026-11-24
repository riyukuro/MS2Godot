using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>InGameRank (KMS 0xEE, GMS2 0xF0): CGameRankingCli::OnPacket (gamerankingcli.cpp:350). Every recorded GMS2 packet is mode 0x1F.</summary>
public class RcInGameRankTests {
    static ByteReader Hex(string s) => new(RcPkt.Hex(s.Replace(" ", "")));

    // recorded: every packet
    [Test] public void LifetimesAreSecondsAndMinutes() {
        var m = InGameRankAnswer.Read(Hex("1F 78000000 3C000000"));
        Assert.That(m.IsLifetimes, Is.True);
        Assert.That(m.PersonalSeconds, Is.EqualTo(120));
        Assert.That(m.BoardMinutes, Is.EqualTo(60));
    }

    [Test] public void OtherModesReadNothingMore() {
        var m = InGameRankAnswer.Read(Hex("05 01020304"));
        Assert.That(m.IsLifetimes, Is.False);
        Assert.That(m.Mode, Is.EqualTo(5));
    }

    [Test] public void TrailingBytesAndShortBodiesAreRefused() {
        Assert.Throws<PacketLayoutException>(() => InGameRankAnswer.Read(Hex("1F 78000000 3C000000 00")));
        Assert.Throws<IndexOutOfRangeException>(() => InGameRankAnswer.Read(Hex("1F 78000000")));
    }

    [Test] public void EveryRecordedGms2PacketReadsToTheEnd() {
        var all = Gms2Sniffs.Inbound("gms2", ProtocolProfile.Gms2.Number(Inbound.InGameRank));
        Assert.That(all, Is.Not.Empty);
        foreach (var (file, data) in all) {
            var r = new ByteReader(data);
            var m = InGameRankAnswer.Read(r);
            Assert.That(m.IsLifetimes, Is.True, file);
            Assert.That(r.Available, Is.EqualTo(0), file);
            Assert.That((m.PersonalSeconds, m.BoardMinutes), Is.EqualTo((120, 60)), file);
        }
    }

    [Test] public void NumberOnBothProfiles() {
        Assert.That(ProtocolProfile.Kms2525.Number(Inbound.InGameRank), Is.EqualTo((ushort) 0xEE));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.InGameRank), Is.EqualTo((ushort) 0xF0));
    }
}
