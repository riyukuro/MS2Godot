using System;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode and grade-mapping tests for UiText (KMS 0x94, GMS2 0x96;
/// CSceneStateGame::FUN_141731190, scenestategame.cpp:7878 -> UiText.Read, UiText.Grade, DamageNumber).
/// </summary>
public class UiTextRecordedTests
{
    // kms2: 0x94, 12 bytes (objectId masked to 65553, type 4 -> grade 5, value 8)
    static readonly byte[] KmsUiText = Convert.FromHexString("110001000400000008000000");

    // gms2: 0x96, 12 bytes (objectId masked to 131106, type 4 -> grade 5, value 10)
    static readonly byte[] Gms2UiText = Convert.FromHexString("22000200040000000A000000");

    [Test]
    public void KmsUiTextDecodesAndMapsTypeToDamageNumberGrade()
    {
        var msg = UiText.Read(new ByteReader(KmsUiText));
        Assert.That(msg.ObjectId, Is.EqualTo(65_553));
        Assert.That(msg.Type, Is.EqualTo(4));
        Assert.That(msg.Value, Is.EqualTo(8));
        Assert.That(msg.Grade, Is.EqualTo(5));

        var num = new DamageNumber(msg.ObjectId, msg.Value, msg.Grade, msg.ObjectId);
        Assert.That(num.TargetId, Is.EqualTo(65_553));
        Assert.That(num.Value, Is.EqualTo(8L));
        Assert.That(num.Grade, Is.EqualTo(5));
        Assert.That(num.CasterId, Is.EqualTo(65_553));
    }

    [Test]
    public void Gms2UiTextDecodesAndMapsTypeToDamageNumberGrade()
    {
        var msg = UiText.Read(new ByteReader(Gms2UiText));
        Assert.That(msg.ObjectId, Is.EqualTo(131_106));
        Assert.That(msg.Type, Is.EqualTo(4));
        Assert.That(msg.Value, Is.EqualTo(10));
        Assert.That(msg.Grade, Is.EqualTo(5));

        var num = new DamageNumber(msg.ObjectId, msg.Value, msg.Grade, msg.ObjectId);
        Assert.That(num.TargetId, Is.EqualTo(131_106));
        Assert.That(num.Value, Is.EqualTo(10L));
        Assert.That(num.Grade, Is.EqualTo(5));
        Assert.That(num.CasterId, Is.EqualTo(131_106));
    }
}
