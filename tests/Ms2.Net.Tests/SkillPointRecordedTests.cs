using System;
using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode and handler-effect tests for SkillPoint (KMS 0x44, GMS2 0x45;
/// CSceneStateGame::OnSkillPoint @0x14172f210, scenestategame.cpp:4241 -> SkillPoints.Apply).
/// </summary>
public class SkillPointRecordedTests
{
    // kms2: 0x44, 60 bytes (first=18, 3 sources, trailing extra i32 = 0)
    static readonly byte[] KmsSkillPoint = Convert.FromHexString(
        "12000000030000000100000002000000010009000000020004000000" +
        "0200000002000000010003000000020002000000030000000000000000000000");

    // gms2: 0x45, 56 bytes (first=7, 3 sources, no trailing extra i32)
    static readonly byte[] Gms2SkillPoint = Convert.FromHexString(
        "07000000030000000100000002000000000007000000010000000000" +
        "02000000020000000000000000000100000000000300000000000000");

    [Test]
    public void KmsSkillPointDecodesAndPopulatesSkillPointsBySourceAndRank()
    {
        var r = new ByteReader(KmsSkillPoint);
        var pkt = SkillPointPacket.Read(r, ProtocolProfile.Kms2525);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(pkt.First, Is.EqualTo(18));
        Assert.That(pkt.Sources.Count, Is.EqualTo(3));
        Assert.That(pkt.Extra, Is.EqualTo(0));

        var sp = new SkillPoints();
        sp.Apply(pkt);

        Assert.That(sp.Source(1, 1), Is.EqualTo(9));
        Assert.That(sp.Source(1, 2), Is.EqualTo(4));
        Assert.That(sp.Source(2, 1), Is.EqualTo(3));
        Assert.That(sp.Source(2, 2), Is.EqualTo(2));
        Assert.That(sp.RankTotal(1), Is.EqualTo(12));
        Assert.That(sp.RankTotal(2), Is.EqualTo(6));
        Assert.That(sp.Extra, Is.EqualTo(pkt.Extra));
    }

    [Test]
    public void Gms2SkillPointDecodesWithoutTrailingIntAndReplacesPriorPoints()
    {
        var r = new ByteReader(Gms2SkillPoint);
        var pkt = SkillPointPacket.Read(r, ProtocolProfile.Gms2);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(pkt.First, Is.EqualTo(7));
        Assert.That(pkt.Sources.Count, Is.EqualTo(3));
        Assert.That(pkt.Extra, Is.EqualTo(0));

        var sp = new SkillPoints();
        // Seed prior points first, then verify Apply replaces them completely.
        sp.Apply(SkillPointPacket.Read(new ByteReader(KmsSkillPoint), ProtocolProfile.Kms2525));
        Assert.That(sp.RankTotal(2), Is.EqualTo(6));

        sp.Apply(pkt);

        Assert.That(sp.Source(1, 0), Is.EqualTo(7));
        Assert.That(sp.Source(1, 1), Is.EqualTo(0));
        Assert.That(sp.RankTotal(0), Is.EqualTo(7));
        Assert.That(sp.RankTotal(1), Is.EqualTo(0));
        Assert.That(sp.RankTotal(2), Is.EqualTo(0));
        Assert.That(sp.Extra, Is.EqualTo(0));
    }
}
