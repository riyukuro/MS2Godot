using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Meso (0x3A GMS2, 0x39 KMS2): the i64 balance and the i32 kind of the change.</summary>
public class MesoUpdateTests {
    // gms2 recording: packet 2855, 12 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2 = new byte[] {
        0xB8, 0xAE, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF2, 0x03, 0x00, 0x00,
    };
    // kms2 recording: packet 1153, 12 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Kms = new byte[] {
        0xB6, 0x2E, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1D, 0x04, 0x00, 0x00,
    };

    [Test] public void Gms2MesoReadsTheBalanceAndKind() {
        var r = new ByteReader(Gms2);
        Assert.That(MesoUpdate.Read(r), Is.EqualTo(new MesoUpdate(175800L, 1010)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsMesoReadsTheBalanceAndKind() {
        var r = new ByteReader(Kms);
        Assert.That(MesoUpdate.Read(r), Is.EqualTo(new MesoUpdate(77494L, 1053)));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
