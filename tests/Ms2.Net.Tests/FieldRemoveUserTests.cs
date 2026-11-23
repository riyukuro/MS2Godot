using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>FieldRemoveUser (0x18): the object id alone.</summary>
public class FieldRemoveUserTests {
    // gms2 recording: packet 37430, 4 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2 = new byte[] {
        0x03, 0xA9, 0x37, 0x00,
    };
    // kms2 recording: packet 32755, 4 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Kms = new byte[] {
        0xC1, 0x7F, 0xE5, 0x00,
    };

    [Test] public void Gms2RemoveReadsTheObjectId() {
        var r = new ByteReader(Gms2);
        Assert.That(FieldRemoveUser.Read(r).ObjectId, Is.EqualTo(3647747));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsRemoveReadsTheObjectId() {
        var r = new ByteReader(Kms);
        Assert.That(FieldRemoveUser.Read(r).ObjectId, Is.EqualTo(15040449));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
