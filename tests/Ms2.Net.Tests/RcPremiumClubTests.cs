using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>PremiumClub (KMS 0xEB, GMS2 0xED): CSceneStateGame::OnPremiumClub (scenestategame.cpp:8426). Recorded GMS2 modes: 0, 1, 2.</summary>
public class RcPremiumClubTests {
    static ByteReader Hex(string s) => new(RcPkt.Hex(s.Replace(" ", "")));
    static PremiumClub Gms2(string s) => PremiumClub.Read(Hex(s), ProtocolProfile.Gms2);

    // recorded: HeavyGunner_Rank1 (mode 0 with the player id)
    [Test] public void TimeWithPlayerId() {
        var m = Gms2("00 F7A47000 E3FF835E 00000000");
        Assert.That(m.Kind, Is.EqualTo(PremiumClubKind.Time));
        Assert.That(m.HasPlayerId, Is.True);
        Assert.That(m.PlayerId, Is.EqualTo(0x0070A4F7));
        Assert.That(m.EndTime, Is.EqualTo(0x5E83FFE3L));
    }

    // recorded: the 9-byte form, sent at login (1-Char creation)
    [Test] public void TimeAloneOnGms2() {
        var m = Gms2("00 1A55205D 00000000");
        Assert.That(m.Kind, Is.EqualTo(PremiumClubKind.Time));
        Assert.That(m.HasPlayerId, Is.False);
        Assert.That(m.EndTime, Is.EqualTo(0x5D20551AL));
    }

    [Test] public void KmsReadsTheIdAlways() {
        var m = PremiumClub.Read(Hex("00 F7A47000 E3FF835E 00000000"), ProtocolProfile.Kms2525);
        Assert.That(m.HasPlayerId, Is.True);
        Assert.That(m.PlayerId, Is.EqualTo(0x0070A4F7));
        // a KMS packet of the 8-byte shape is short for the client's reads
        Assert.Throws<IndexOutOfRangeException>(() => PremiumClub.Read(Hex("00 1A55205D 00000000"), ProtocolProfile.Kms2525));
    }

    [Test] public void ClaimedListEmptyAndFilled() {
        var e = Gms2("01 00000000");
        Assert.That(e.Kind, Is.EqualTo(PremiumClubKind.ClaimedList));
        Assert.That(e.BenefitIds, Is.Empty);
        var f = Gms2("01 01000000 2D010000");
        Assert.That(f.BenefitIds, Is.EqualTo(new[] { 0x12D }));
    }

    // recorded: premium_club (mode 2)
    [Test] public void Claimed() {
        var m = Gms2("02 2D010000");
        Assert.That(m.Kind, Is.EqualTo(PremiumClubKind.Claimed));
        Assert.That(m.BenefitIds, Is.EqualTo(new[] { 0x12D }));
    }

    [Test] public void UnknownModeReadsNothingMore() {
        var m = Gms2("03 00000000");
        Assert.That(m.Kind, Is.EqualTo(PremiumClubKind.Unknown));
    }

    [Test] public void TrailingBytesAndImpossibleCountsAreRefused() {
        Assert.Throws<PacketLayoutException>(() => Gms2("02 2D010000 00"));
        Assert.Throws<PacketLayoutException>(() => Gms2("00 F7A47000 E3FF835E 00000000 00"));
        Assert.Throws<InvalidDataException>(() => Gms2("01 FFFF0000 2D010000"));
    }

    [Test] public void EveryRecordedGms2PacketReadsToTheEnd() {
        var all = Gms2Sniffs.Inbound("gms2", ProtocolProfile.Gms2.Number(Inbound.PremiumClub));
        Assert.That(all, Is.Not.Empty);
        var seen = new HashSet<(byte, bool)>();
        foreach (var (file, data) in all) {
            var r = new ByteReader(data);
            var m = PremiumClub.Read(r, ProtocolProfile.Gms2);
            Assert.That(m.Kind, Is.Not.EqualTo(PremiumClubKind.Unknown), file);
            Assert.That(r.Available, Is.EqualTo(0), file);
            seen.Add((m.Mode, m.HasPlayerId));
        }
        Assert.That(seen, Is.EquivalentTo(new[] { ((byte) 0, true), ((byte) 0, false), ((byte) 1, false), ((byte) 2, false) }));
    }

    [Test] public void NumberOnBothProfiles() {
        Assert.That(ProtocolProfile.Kms2525.Number(Inbound.PremiumClub), Is.EqualTo((ushort) 0xEB));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.PremiumClub), Is.EqualTo((ushort) 0xED));
    }
}
