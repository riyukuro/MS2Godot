using System;
using System.Collections.Generic;
using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode and handler-effect tests for ResponseRide (KMS 0x79, GMS2 0x7B;
/// CSceneStateGame::OnResponseRide @0x14172a8f0, scenestategame.cpp:12026 -> OwnRide.For, RidePassengers.Apply).
/// </summary>
public class ResponseRideRecordedTests
{
    const int KmsOwnerId = 0x0001_0100;
    const int KmsMountObjId = 0x0001_0101;
    const int KmsRiderId = 0x0001_0102;

    const int Gms2OwnerId = 0x0002_0200;
    const int Gms2MountObjId = 0x0002_0201;
    const int Gms2RiderId = 0x0002_0202;
    const long Gms2RideItemUid = 0x4444_0000_0000_0001L;

    // kms2: 0x79 mode 0, 15 bytes (type 3 Object ride, rideId 99920179, KMS flag 0; object IDs masked)
    static readonly byte[] KmsMode0RideOn = Convert.FromHexString(
        "00000101000333A9F4050101010000");

    // kms2: 0x79 mode 1, 7 bytes (type 13 Dead ride-off, arg 0; object ID masked)
    static readonly byte[] KmsMode1RideOff = Convert.FromHexString("01000101000D00");

    // kms2: 0x79 mode 3, 10 bytes (passenger boards seat 0; owner and rider IDs masked)
    static readonly byte[] KmsMode3Board = Convert.FromHexString("03000101000201010000");

    // kms2: 0x79 mode 4, 9 bytes (passenger leaves; owner and rider IDs masked)
    static readonly byte[] KmsMode4Leave = Convert.FromHexString("040001010002010100");

    // gms2: 0x7B mode 0, 72 bytes (type 1 UseItem ride, rideId 50600145, no KMS flag byte, empty UgcLook; object IDs and item UID masked)
    static readonly byte[] Gms2Mode0RideOn = Convert.FromHexString(
        "000002020001D118040301020200D11804030100000000004444" +
        "000000000000000000000000000000000000000000000000" +
        "00000000000000000000000000000000000000000000");

    // gms2: 0x7B mode 1, 16 bytes (type 1 UseSkill ride-off, skill 10900155, flag true, key 0; object ID masked)
    static readonly byte[] Gms2Mode1RideOff = Convert.FromHexString(
        "01000202000100BB52A6000100000000");

    // gms2: 0x7B mode 3, 10 bytes (passenger boards seat 0; owner and rider IDs masked)
    static readonly byte[] Gms2Mode3Board = Convert.FromHexString("03000202000202020000");

    // gms2: 0x7B mode 5, 5 bytes (clear all passengers; owner ID masked)
    static readonly byte[] Gms2Mode5Clear = Convert.FromHexString("0500020200");

    static ResponseRide DecodeAll(byte[] payload, int selfObjectId, ProtocolProfile profile)
    {
        var r = new ByteReader(payload);
        var msg = ResponseRide.Read(r, selfObjectId, profile);
        Assert.That(r.Available, Is.EqualTo(0));
        return msg;
    }

    [Test]
    public void KmsResponseRideDrivesOwnRideAndPassengerMap()
    {
        var on = DecodeAll(KmsMode0RideOn, KmsOwnerId, ProtocolProfile.Kms2525);
        Assert.That(on.Mode, Is.EqualTo(0));
        Assert.That(on.ObjectId, Is.EqualTo(KmsOwnerId));
        Assert.That(on.RideOn.Type, Is.EqualTo(3));
        Assert.That(on.RideOn.RideId, Is.EqualTo(99_920_179));
        Assert.That(on.RideOn.ObjectId, Is.EqualTo(KmsMountObjId));
        Assert.That(on.RideOn.Flag, Is.False);
        Assert.That(OwnRide.For(on, KmsOwnerId, mounted: false), Is.EqualTo(OwnRide.Step.RideOn));
        Assert.That(OwnRide.For(on, KmsOwnerId, mounted: true), Is.EqualTo(OwnRide.Step.None));

        var off = DecodeAll(KmsMode1RideOff, KmsOwnerId, ProtocolProfile.Kms2525);
        Assert.That(off.Mode, Is.EqualTo(1));
        Assert.That(off.RideOff, Is.EqualTo(new RideOffAction(13, 0)));
        Assert.That(OwnRide.For(off, KmsOwnerId, mounted: true), Is.EqualTo(OwnRide.Step.RideOff));

        var passengers = new Dictionary<int, int>();
        var board = DecodeAll(KmsMode3Board, KmsRiderId, ProtocolProfile.Kms2525);
        Assert.That(RidePassengers.Apply(board, passengers), Is.True);
        Assert.That(passengers[KmsRiderId], Is.EqualTo(0));
        Assert.That(RidePassengers.BoardsSelf(board, KmsRiderId), Is.True);

        var leave = DecodeAll(KmsMode4Leave, KmsRiderId, ProtocolProfile.Kms2525);
        Assert.That(RidePassengers.Apply(leave, passengers), Is.True);
        Assert.That(passengers, Is.Empty);
    }

    [Test]
    public void Gms2ResponseRideDrivesOwnRideAndPassengerClear()
    {
        var on = DecodeAll(Gms2Mode0RideOn, Gms2OwnerId, ProtocolProfile.Gms2);
        Assert.That(on.Mode, Is.EqualTo(0));
        Assert.That(on.ObjectId, Is.EqualTo(Gms2OwnerId));
        Assert.That(on.RideOn.Type, Is.EqualTo(1));
        Assert.That(on.RideOn.RideId, Is.EqualTo(50_600_145));
        Assert.That(on.RideOn.ObjectId, Is.EqualTo(Gms2MountObjId));
        Assert.That(on.RideOn.ItemId, Is.EqualTo(50_600_145));
        Assert.That(on.RideOn.ItemUid, Is.EqualTo(Gms2RideItemUid));
        Assert.That(OwnRide.For(on, Gms2OwnerId, mounted: false), Is.EqualTo(OwnRide.Step.RideOn));

        var off = DecodeAll(Gms2Mode1RideOff, Gms2OwnerId, ProtocolProfile.Gms2);
        Assert.That(off.Mode, Is.EqualTo(1));
        Assert.That(off.RideOff, Is.EqualTo(new RideOffUseSkill(0, 10_900_155, true, 0)));
        Assert.That(OwnRide.For(off, Gms2OwnerId, mounted: true), Is.EqualTo(OwnRide.Step.RideOff));

        var passengers = new Dictionary<int, int>();
        var board = DecodeAll(Gms2Mode3Board, Gms2RiderId, ProtocolProfile.Gms2);
        Assert.That(RidePassengers.Apply(board, passengers), Is.True);
        Assert.That(passengers[Gms2RiderId], Is.EqualTo(0));
        Assert.That(RidePassengers.BoardsSelf(board, Gms2RiderId), Is.True);

        var clear = DecodeAll(Gms2Mode5Clear, Gms2RiderId, ProtocolProfile.Gms2);
        Assert.That(RidePassengers.Apply(clear, passengers), Is.True);
        Assert.That(passengers, Is.Empty);
    }
}
