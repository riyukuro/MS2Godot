using System;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode tests for SkillMacro (KMS 0xD1, GMS2 0xD3;
/// CSceneStateGame::OnSkillMacro @0x141737d60, UISkillCompactControlDialog::vfunc_79 @0x140f9fee0).
/// Reader-only: SkillCompactControlController.OnSkillMacro drives the Flash UI host.
/// </summary>
public class SkillMacroRecordedTests
{
    // kms2: 0xD1 mode 0, 37 bytes (1 tab, 9-char name masked with '*', keyAction 700001, 0 skills)
    static readonly byte[] KmsMode0 = Convert.FromHexString(
        "000100000009002A002A002A002A002A002A002A002A002A0061AE0A000000000000000000");

    // kms2: 0xD1 mode 2, 73 bytes (2 tabs: 11-char name masked with '*', 2 skills; 5-char name masked with '*', 0 skills)
    static readonly byte[] KmsMode2 = Convert.FromHexString(
        "02020000000B002A002A002A002A002A002A002A002A002A002A002A00000000000000000002000000211D9A002B1D9A00" +
        "05002A002A002A002A002A00000000000000000000000000");

    // gms2: 0xD3 mode 0, 41 bytes (1 tab, 9-char name masked with '*', keyAction 700001, 1 skill 10900151)
    static readonly byte[] Gms2Mode0 = Convert.FromHexString(
        "000100000009002A002A002A002A002A002A002A002A002A0061AE0A000000000001000000B752A600");

    // gms2: 0xD3 mode 2, 63 bytes (2 tabs with keyActions 700001 and 700002, names masked with '*')
    static readonly byte[] Gms2Mode2 = Convert.FromHexString(
        "020200000009002A002A002A002A002A002A002A002A002A0061AE0A000000000001000000B752A600" +
        "02002A002A0062AE0A0000000000010000005352A600");

    [Test]
    public void KmsSkillMacroMode0AndMode2DecodeTabsAndSortedSkills()
    {
        var m0 = SkillMacroPacket.Read(new ByteReader(KmsMode0));
        Assert.That(m0.Mode, Is.EqualTo(0));
        Assert.That(m0.Tabs.Count, Is.EqualTo(1));
        Assert.That(m0.Tabs[0].Name, Is.EqualTo("*********"));
        Assert.That(m0.Tabs[0].KeyAction, Is.EqualTo(700_001L));
        Assert.That(m0.Tabs[0].Skills, Is.Empty);

        var m2 = SkillMacroPacket.Read(new ByteReader(KmsMode2));
        Assert.That(m2.Mode, Is.EqualTo(2));
        Assert.That(m2.Tabs.Count, Is.EqualTo(2));
        Assert.That(m2.Tabs[0].Name, Is.EqualTo("***********"));
        Assert.That(m2.Tabs[0].KeyAction, Is.EqualTo(0L));
        Assert.That(m2.Tabs[0].Skills, Is.EqualTo(new[] { 10_100_001, 10_100_011 }));
        Assert.That(m2.Tabs[1].Name, Is.EqualTo("*****"));
        Assert.That(m2.Tabs[1].KeyAction, Is.EqualTo(0L));
        Assert.That(m2.Tabs[1].Skills, Is.Empty);
    }

    [Test]
    public void Gms2SkillMacroMode0AndMode2DecodeTabsAndSortedSkills()
    {
        var m0 = SkillMacroPacket.Read(new ByteReader(Gms2Mode0));
        Assert.That(m0.Mode, Is.EqualTo(0));
        Assert.That(m0.Tabs.Count, Is.EqualTo(1));
        Assert.That(m0.Tabs[0].Name, Is.EqualTo("*********"));
        Assert.That(m0.Tabs[0].KeyAction, Is.EqualTo(700_001L));
        Assert.That(m0.Tabs[0].Skills, Is.EqualTo(new[] { 10_900_151 }));

        var m2 = SkillMacroPacket.Read(new ByteReader(Gms2Mode2));
        Assert.That(m2.Mode, Is.EqualTo(2));
        Assert.That(m2.Tabs.Count, Is.EqualTo(2));
        Assert.That(m2.Tabs[0].Name, Is.EqualTo("*********"));
        Assert.That(m2.Tabs[0].KeyAction, Is.EqualTo(700_001L));
        Assert.That(m2.Tabs[0].Skills, Is.EqualTo(new[] { 10_900_151 }));
        Assert.That(m2.Tabs[1].Name, Is.EqualTo("**"));
        Assert.That(m2.Tabs[1].KeyAction, Is.EqualTo(700_002L));
        Assert.That(m2.Tabs[1].Skills, Is.EqualTo(new[] { 10_900_051 }));
    }
}
