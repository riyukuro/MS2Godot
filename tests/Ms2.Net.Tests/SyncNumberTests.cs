using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>SyncNumber (0x14): one u8, the same on both regions.</summary>
public class SyncNumberTests {
    // gms2 recording: packet 30, 1 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2 = new byte[] {
        0x00,
    };
    // kms2 recording: packet 37, 1 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Kms = new byte[] {
        0x00,
    };

    [Test] public void Gms2SyncNumberReadsTheByte() {
        var r = new ByteReader(Gms2);
        Assert.That(SyncNumber.Read(r).Number, Is.EqualTo(0));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsSyncNumberReadsTheByte() {
        var r = new ByteReader(Kms);
        Assert.That(SyncNumber.Read(r).Number, Is.EqualTo(0));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
