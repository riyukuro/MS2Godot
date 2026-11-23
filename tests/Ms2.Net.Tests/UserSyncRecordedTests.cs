using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>UserSync (0x1C): the actor id and its state records. The layout is the same on both regions; the 32-byte bodies
/// carry one record each.</summary>
public class UserSyncRecordedTests {
    // gms2 recording: packet 37087, 32 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2 = new byte[] {
        0x03, 0xA9, 0x37, 0x00, 0x01, 0x01, 0x01, 0x00, 0x2C, 0x01, 0xEE, 0x02, 0x2D, 0x01, 0x44, 0x0C,
        0x09, 0x5A, 0x00, 0x2D, 0x00, 0xD9, 0xFF, 0x02, 0x44, 0x0C, 0x2C, 0x01, 0x02, 0x05, 0x01, 0x00,
    };
    // kms2 recording: packet 503, 32 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Kms = new byte[] {
        0x74, 0xCE, 0x75, 0x00, 0x01, 0x01, 0x01, 0x2C, 0x0B, 0xC5, 0x08, 0x09, 0x07, 0x82, 0x0A, 0x09,
        0x5A, 0x00, 0x2D, 0x00, 0xD9, 0xFF, 0x02, 0x82, 0x0A, 0x34, 0x01, 0x00, 0x13, 0x0F, 0x0B, 0x00,
    };

    [Test] public void Gms2SyncReadsOneRecord() {
        var r = new ByteReader(Gms2);
        var u = UserSyncIn.Read(r, ProtocolProfile.Gms2);
        Assert.That((u.ObjectId, u.Records.Count), Is.EqualTo((3647747, 1)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsSyncReadsOneRecord() {
        var r = new ByteReader(Kms);
        var u = UserSyncIn.Read(r);
        Assert.That((u.ObjectId, u.Records.Count), Is.EqualTo((7720564, 1)));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
