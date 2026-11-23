using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>RevivalCount (0x36 GMS2, 0x35 KMS2): one i32, the used revive count.</summary>
public class RevivalCountTests {
    // gms2 recording: packet 2308, 4 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2 = new byte[] {
        0x00, 0x00, 0x00, 0x00,
    };
    // kms2 recording: packet 590, 4 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Kms = new byte[] {
        0x00, 0x00, 0x00, 0x00,
    };

    [Test] public void Gms2RevivalCountReadsTheUsedCount() {
        var r = new ByteReader(Gms2);
        Assert.That(RevivalCount.Read(r).Used, Is.EqualTo(0));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsRevivalCountReadsTheUsedCount() {
        var r = new ByteReader(Kms);
        Assert.That(RevivalCount.Read(r).Used, Is.EqualTo(0));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
