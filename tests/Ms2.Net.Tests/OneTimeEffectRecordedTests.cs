using System;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode tests for OneTimeEffect (KMS 0xF1, GMS2 0xF3;
/// CSceneStateGame::OnOneTimeEffect @0x14173a420, scenestategame.cpp:8453 -> OneTimeEffect.Read).
/// Reader-only: LiveSession.Effects.cs logs OneTimeEffect and drives RegionSkills.OneTime in the Godot layer.
/// </summary>
public class OneTimeEffectRecordedTests
{
    // kms2: 0xF1, 109 bytes (id 2, add true, game asset path)
    static readonly byte[] KmsAddEffect = Convert.FromHexString(
        "02000000013300420047002F0043006F006D006D006F006E002F00530063007200650065006E004D" +
        "00610073006B002F004500660066005F00430061006D006500720061004D00610073006B0069006E" +
        "0067005F0053006C006F00770046006100640065002E0078006D006C00");

    // kms2: 0xF1, 5 bytes (id 2, add false)
    static readonly byte[] KmsRemoveEffect = Convert.FromHexString("0200000000");

    // gms2: 0xF3, 87 bytes (id 1, add true, game asset path)
    static readonly byte[] Gms2AddEffect = Convert.FromHexString(
        "01000000012800420047002F0043006F006D006D006F006E002F00530063007200650065006E004D" +
        "00610073006B002F004500660066005F00660061006400650069006E005F0031007300650063002E" +
        "0078006D006C00");

    // gms2: 0xF3, 5 bytes (id 1, add false)
    static readonly byte[] Gms2RemoveEffect = Convert.FromHexString("0100000000");

    [Test]
    public void KmsOneTimeEffectAddAndRemoveReadToEnd()
    {
        var rAdd = new ByteReader(KmsAddEffect);
        var add = OneTimeEffect.Read(rAdd);
        Assert.That(rAdd.Available, Is.EqualTo(0));
        Assert.That(add.Id, Is.EqualTo(2));
        Assert.That(add.Add, Is.True);
        Assert.That(add.Name, Is.EqualTo("BG/Common/ScreenMask/Eff_CameraMasking_SlowFade.xml"));

        var rRem = new ByteReader(KmsRemoveEffect);
        var rem = OneTimeEffect.Read(rRem);
        Assert.That(rRem.Available, Is.EqualTo(0));
        Assert.That(rem.Id, Is.EqualTo(2));
        Assert.That(rem.Add, Is.False);
        Assert.That(rem.Name, Is.EqualTo(""));
    }

    [Test]
    public void Gms2OneTimeEffectAddAndRemoveReadToEnd()
    {
        var rAdd = new ByteReader(Gms2AddEffect);
        var add = OneTimeEffect.Read(rAdd);
        Assert.That(rAdd.Available, Is.EqualTo(0));
        Assert.That(add.Id, Is.EqualTo(1));
        Assert.That(add.Add, Is.True);
        Assert.That(add.Name, Is.EqualTo("BG/Common/ScreenMask/Eff_fadein_1sec.xml"));

        var rRem = new ByteReader(Gms2RemoveEffect);
        var rem = OneTimeEffect.Read(rRem);
        Assert.That(rRem.Available, Is.EqualTo(0));
        Assert.That(rem.Id, Is.EqualTo(1));
        Assert.That(rem.Add, Is.False);
        Assert.That(rem.Name, Is.EqualTo(""));
    }
}
