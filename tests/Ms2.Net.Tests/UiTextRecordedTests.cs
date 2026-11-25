using System;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode and grade-mapping tests for UiText (KMS 0x94, GMS2 0x96;
/// CSceneStateGame::FUN_141731190, scenestategame.cpp:7878 -> UiText.Read, UiText.Grade).
/// Reader-only: LiveSession dispatches UiText to DamageNumbered / remote.ShowNumber in the Godot layer.
/// </summary>
public class UiTextRecordedTests
{
    // kms2: 0x94, 12 bytes (objectId masked to 65553, type 4 -> grade 5, value 8)
    static readonly byte[] KmsUiText = Convert.FromHexString("110001000400000008000000");

    // gms2: 0x96, 12 bytes (objectId masked to 131106, type 4 -> grade 5, value 10)
    static readonly byte[] Gms2UiText = Convert.FromHexString("22000200040000000A000000");

    [Test]
    public void KmsUiTextDecodesAndMapsTypeToGrade()
    {
        var r = new ByteReader(KmsUiText);
        var msg = UiText.Read(r);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(msg.ObjectId, Is.EqualTo(65_553));
        Assert.That(msg.Type, Is.EqualTo(4));
        Assert.That(msg.Value, Is.EqualTo(8));
        Assert.That(msg.Grade, Is.EqualTo(5));
    }

    [Test]
    public void Gms2UiTextDecodesAndMapsTypeToGrade()
    {
        var r = new ByteReader(Gms2UiText);
        var msg = UiText.Read(r);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(msg.ObjectId, Is.EqualTo(131_106));
        Assert.That(msg.Type, Is.EqualTo(4));
        Assert.That(msg.Value, Is.EqualTo(10));
        Assert.That(msg.Grade, Is.EqualTo(5));
    }
}
