using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// RideSync (KMS 0x7A, GMS2 0x7C): CSceneStateGame::OnRideSync (scenestategame.cpp:2472-2482), handled by
/// <see cref="FieldTracker.Inbound"/> (updates the tracked actor's pose and enqueues <see cref="UserMovedEvent"/> with
/// <see cref="UserMovedEvent.Ride"/> true). Recorded KMS2 frames only: the GMS2 capture corpus has 0 RideSync (0x7C) frames.
/// </summary>
public class RideSyncRecordedTests {
    // kms2 recording: 0x7A, packet 6689, 32 bytes (extracted with extract.py; 1 sync record for object 15040952).
    static readonly byte[] KmsStep1 = Convert.FromHexString(
        "B881E500010218371F25001B043C050D5A002D00D9FF023C0543000035000600");
    // kms2 recording: 0x7A, packet 6691, 32 bytes (extracted with extract.py; 1 sync record for object 15040952).
    static readonly byte[] KmsStep2 = Convert.FromHexString(
        "B881E500010218421F30001B043C050D5A002D00D9FF023C0518000035000600");

    static void SeedActor(FieldTracker t, int objectId) {
        var w = new ByteWriter();
        w.WriteInt(objectId);
        w.WriteLong(1); w.WriteLong(2); w.WriteUnicodeString("Rider");
        w.WriteByte(0); w.WriteByte(0); w.WriteLong(0); w.WriteInt(0);
        w.WriteInt(8); w.WriteInt(0); w.WriteInt(0); w.WriteShort(21); w.WriteShort(1);
        w.WriteInt(10); w.WriteInt(100); w.WriteInt(2100); w.WriteInt(1630); w.WriteShort(0);
        w.WriteLong(0); w.WriteLong(0); w.WriteLong(0); w.WriteInt(0);
        w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0); w.WriteInt(0);
        for (int i = 0; i < 8; i++) w.WriteByte(0);
        w.WriteLong(0); w.WriteInt(0); w.WriteInt(0); w.WriteInt(0);
        w.WriteLong(0); w.WriteUnicodeString(""); w.WriteUnicodeString(""); w.WriteUnicodeString("");
        w.WriteByte(0); w.WriteByte(0);
        for (int i = 0; i < 12; i++) w.WriteInt(0);
        w.WriteUnicodeString(""); w.WriteLong(0);
        w.WriteInt(0); w.WriteByte(0); w.WriteBool(false); w.WriteLong(0); w.WriteInt(0);
        w.WriteInt(0); w.WriteLong(0); w.WriteInt(0); w.WriteLong(0);
        w.WriteInt(0); w.WriteInt(0); w.WriteShort(0); w.WriteLong(0);
        w.WriteInt(1); w.WriteInt(100); w.WriteByte(0);
        w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0);
        w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0);
        w.WriteByte(0); w.WriteByte(0x23);
        for (int i = 0; i < 3; i++) { w.WriteLong(2100); w.WriteInt(0); w.WriteInt(0); w.WriteInt(0); w.WriteInt(0); }
        w.WriteBool(false); w.WriteByte(0);
        w.WriteInt(0); w.WriteLong(0); w.WriteLong(0); w.WriteBool(false); w.WriteInt(0);
        for (int i = 0; i < 8; i++) w.WriteByte(0);
        w.WriteUnicodeString(""); w.WriteBool(false);
        w.WriteInt(0); w.WriteLong(0); w.WriteInt(0); w.WriteInt(0);
        void Block(params byte[] body) { w.WriteBool(false); w.WriteInt(body.Length); w.WriteBytes(body); }
        Block(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        Block(0);
        Block(0);
        w.WriteShort(0);
        w.WriteInt(0); w.WriteInt(0); w.WriteByte(0); w.WriteInt(0); w.WriteByte(0); w.WriteByte(0);
        w.WriteInt(0); w.WriteShort(0); w.WriteBool(false);
        w.WriteInt(0); w.WriteBool(false);
        w.WriteLong(0); w.WriteInt(0); w.WriteByte(0); w.WriteShort(0);
        t.Inbound(new RawPacket((ushort)Inbound.FieldAddUser, w.Buffer.AsSpan(0, w.Length).ToArray()));
        t.Events.Clear();
    }

    [Test] public void KmsRideSyncDecodesAndMovesTrackedActorWithRideFlag() {
        var r1 = new ByteReader(KmsStep1);
        var s1 = UserSyncIn.Read(r1, ProtocolProfile.Kms2525);
        Assert.That(s1.ObjectId, Is.EqualTo(15040952));
        Assert.That(s1.Records, Has.Count.EqualTo(1));
        Assert.That(r1.Available, Is.EqualTo(0));

        var r2 = new ByteReader(KmsStep2);
        var s2 = UserSyncIn.Read(r2, ProtocolProfile.Kms2525);
        Assert.That(s2.ObjectId, Is.EqualTo(15040952));
        Assert.That(s2.Records, Has.Count.EqualTo(1));
        Assert.That(r2.Available, Is.EqualTo(0));

        var tracker = new FieldTracker { Profile = ProtocolProfile.Kms2525, Time = 1.5 };
        SeedActor(tracker, 15040952);
        tracker.Inbound(new RawPacket((ushort)Inbound.RideSync, KmsStep1));
        tracker.Time = 2.0;
        tracker.Inbound(new RawPacket((ushort)Inbound.RideSync, KmsStep2));

        var moved = tracker.Events.OfType<UserMovedEvent>().ToList();
        Assert.That(moved, Has.Count.EqualTo(2));
        Assert.That(moved.All(e => e.ObjectId == 15040952 && e.Ride && !e.IsSelf), Is.True);
        Assert.That(moved[0].Position, Is.EqualTo(s1.Records[0].Position.ToVector3()));
        Assert.That(moved[1].Position, Is.EqualTo(s2.Records[0].Position.ToVector3()));
        Assert.That(moved[1].Position, Is.Not.EqualTo(moved[0].Position));

        var actor = tracker.Snapshot().Single(a => a.ObjectId == 15040952);
        Assert.That(actor.Moves, Is.EqualTo(2));
        Assert.That(actor.Position, Is.EqualTo(moved[1].Position));
        Assert.That(actor.YawDegrees, Is.EqualTo(s2.Records[0].Rotation2 / 10f));
    }
}
