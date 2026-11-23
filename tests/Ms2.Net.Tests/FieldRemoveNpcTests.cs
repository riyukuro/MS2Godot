using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>FieldRemoveNpc (0x57 GMS2, 0x56 KMS2): the object id alone.</summary>
public class FieldRemoveNpcTests {
    // gms2 recording: packet 2550, 4 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2 = new byte[] {
        0xF2, 0x75, 0x00, 0x00,
    };
    // kms2 recording: packet 2042, 4 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Kms = new byte[] {
        0x87, 0x8E, 0xCA, 0x00,
    };

    [Test] public void Gms2RemoveReadsTheObjectId() {
        var r = new ByteReader(Gms2);
        Assert.That(NpcObjectId.Read(r).ObjectId, Is.EqualTo(30194));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsRemoveReadsTheObjectId() {
        var r = new ByteReader(Kms);
        Assert.That(NpcObjectId.Read(r).ObjectId, Is.EqualTo(13274759));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
