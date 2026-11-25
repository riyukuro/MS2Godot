using System;
using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode and FieldTracker handler tests for FieldAddPet (KMS 0x5A, GMS2 0x5B;
/// CSceneStateGame::OnFieldAddPet @0x141719a60, scenestategame.cpp:9682 -> FieldAddPet.Read, FieldTracker.Inbound).
/// </summary>
public class FieldAddPetRecordedTests
{
    const int KmsPetObjectId = 70_001;
    const int KmsOwnerObjectId = 70_002;

    const int Gms2PetObjectId = 80_001;
    const int Gms2OwnerObjectId = 80_002;
    const long Gms2PetUid = 0x2222_0000_0000_0002L;

    // kms2: 0x5A, 100 bytes (objectId, ownerId, and trailing petUid masked)
    static readonly byte[] KmsFieldAddPet = Convert.FromHexString(
        "71110100EA4FA403EA4FA403004083C4004083C4000015430000000000000000000000000000803F" +
        "7211010023C40900000000000064000000C40900000000000064000000C409000000000000640000" +
        "0001000000000011110001000500000000000000");

    // gms2: 0x5B, 142 bytes (objectId, ownerId, petUid, and 21-char player pet name masked)
    static readonly byte[] Gms2FieldAddPet = Convert.FromHexString(
        "813801006F50A4036F50A4039F0C4743B55C87C300C060440000000000000000BBDC2943CDCC0C3F" +
        "8238010023C40900000000000064000000C40900000000000064000000C409000000000000640000" +
        "0002000000000022220032002B000000000015002A002A002A002A002A002A002A002A002A002A00" +
        "2A002A002A002A002A002A002A002A002A002A002A00");

    [Test]
    public void KmsFieldAddPetReadsToEndAndRegistersOwnerInFieldTracker()
    {
        var r = new ByteReader(KmsFieldAddPet);
        var pet = FieldAddPet.Read(r, ProtocolProfile.Kms2525);
        // KMS2 reads the same whole body as GMS2 (CSceneStateGame::OnFieldAddPet, scenestategame.cpp:9682-9709)
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(pet.ObjectId, Is.EqualTo(KmsPetObjectId));
        Assert.That(pet.NpcId, Is.EqualTo(61_100_010));
        Assert.That(pet.Position, Is.EqualTo(new Vector3(-1050f, -1050f, 149f)));
        Assert.That(pet.Rotation, Is.EqualTo(Vector3.Zero));
        Assert.That(pet.Scale, Is.EqualTo(1f));
        Assert.That(pet.OwnerId, Is.EqualTo(KmsOwnerObjectId));
        Assert.That((pet.SkinId, pet.Level, pet.TamingRank, pet.Name), Is.EqualTo((61_100_010, 1, (short) 5, "")));

        var tracker = new FieldTracker();
        tracker.Inbound(new RawPacket(ProtocolProfile.Kms2525.Number(Inbound.FieldAddPet), KmsFieldAddPet));
        Assert.That(tracker.PetOwnerOf(KmsPetObjectId), Is.EqualTo(KmsOwnerObjectId));
    }

    [Test]
    public void Gms2FieldAddPetReadsToEndAndRegistersOwnerInFieldTracker()
    {
        var r = new ByteReader(Gms2FieldAddPet);
        var pet = FieldAddPet.Read(r, ProtocolProfile.Gms2);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(pet.ObjectId, Is.EqualTo(Gms2PetObjectId));
        Assert.That(pet.SkinId, Is.EqualTo(61_100_143));
        Assert.That(pet.NpcId, Is.EqualTo(61_100_143));
        Assert.That(pet.Level, Is.EqualTo(50));
        Assert.That(pet.TamingRank, Is.EqualTo((short)43));
        Assert.That(pet.OwnerId, Is.EqualTo(Gms2OwnerObjectId));
        Assert.That(pet.PetUid, Is.EqualTo(Gms2PetUid));
        Assert.That(pet.Scale, Is.EqualTo(0.55f).Within(1e-5f));
        Assert.That(pet.Name, Is.EqualTo("*********************"));

        var tracker = new FieldTracker { Profile = ProtocolProfile.Gms2 };
        tracker.Inbound(new RawPacket(ProtocolProfile.Gms2.Number(Inbound.FieldAddPet), Gms2FieldAddPet));
        Assert.That(tracker.PetOwnerOf(Gms2PetObjectId), Is.EqualTo(Gms2OwnerObjectId));
    }
}
