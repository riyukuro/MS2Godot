using System;
using System.Numerics;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode and handler-effect tests for RequestFieldEnter (KMS 0x16, GMS2 0x16;
/// CSceneStateGame::OnRequestFieldEnter @0x14170f930, scenestategame.cpp:2335 -> FieldTracker.Inbound).
/// </summary>
public class RequestFieldEnterRecordedTests
{
    // kms2: 0x16, 43 bytes (error 0, map 2000062, pos (2950, 2250, 1800), rot (0, 0, 270), portal 482)
    static readonly byte[] KmsRequestFieldEnter = Convert.FromHexString(
        "00BE841E00000000000000000000000060384500A00C450000E144000000000000000000008743E2010000");

    // gms2: 0x16, 43 bytes (error 0, map 52000033, instanceType 1, instanceId 1000043, pos (150, -1500, 900), rot (0, 0, 180), portal 7070)
    static readonly byte[] Gms2RequestFieldEnter = Convert.FromHexString(
        "002175190300016B420F0000000000000016430080BBC4000061440000000000000000000034439E1B0000");

    static void VerifyFieldEnterClearsActorsAndSetsField(
        byte[] payload,
        ProtocolProfile profile,
        int expectedMapId,
        byte expectedInstanceType,
        int expectedInstanceId,
        Vector3 expectedPosition,
        Vector3 expectedRotation,
        int expectedPortal)
    {
        var tracker = new FieldTracker { Profile = profile };
        FieldTrackerSeed.AddUser(tracker, 5001, "Runner");
        Assert.That(tracker.Snapshot().Count, Is.EqualTo(1));

        tracker.Inbound(new RawPacket(0x16, payload));

        Assert.That(tracker.Snapshot(), Is.Empty);
        Assert.That(tracker.Field, Is.Not.Null);
        Assert.That(tracker.Field.Error, Is.EqualTo(0));
        Assert.That(tracker.Field.MapId, Is.EqualTo(expectedMapId));
        Assert.That(tracker.Field.FieldType, Is.EqualTo(0));
        Assert.That(tracker.Field.InstanceType, Is.EqualTo(expectedInstanceType));
        Assert.That(tracker.Field.InstanceId, Is.EqualTo(expectedInstanceId));
        Assert.That(tracker.Field.DungeonId, Is.EqualTo(0));
        Assert.That(tracker.Field.Position, Is.EqualTo(expectedPosition));
        Assert.That(tracker.Field.Rotation, Is.EqualTo(expectedRotation));
        Assert.That(tracker.Field.Portal, Is.EqualTo(expectedPortal));

        Assert.That(tracker.Events.TryDequeue(out var ev), Is.True);
        var entered = ev as FieldEnterEvent;
        Assert.That(entered, Is.Not.Null);
        Assert.That(entered.Enter.MapId, Is.EqualTo(expectedMapId));
        Assert.That(entered.Enter.Portal, Is.EqualTo(expectedPortal));
    }

    [Test]
    public void KmsRequestFieldEnterClearsSeededActorsAndEnqueuesFieldEnterEvent()
        => VerifyFieldEnterClearsActorsAndSetsField(
            KmsRequestFieldEnter,
            ProtocolProfile.Kms2525,
            expectedMapId: 2_000_062,
            expectedInstanceType: 0,
            expectedInstanceId: 0,
            expectedPosition: new Vector3(2950f, 2250f, 1800f),
            expectedRotation: new Vector3(0f, 0f, 270f),
            expectedPortal: 482);

    [Test]
    public void Gms2RequestFieldEnterClearsSeededActorsAndEnqueuesFieldEnterEvent()
        => VerifyFieldEnterClearsActorsAndSetsField(
            Gms2RequestFieldEnter,
            ProtocolProfile.Gms2,
            expectedMapId: 52_000_033,
            expectedInstanceType: 1,
            expectedInstanceId: 1_000_043,
            expectedPosition: new Vector3(150f, -1500f, 900f),
            expectedRotation: new Vector3(0f, 0f, 180f),
            expectedPortal: 7070);
}
