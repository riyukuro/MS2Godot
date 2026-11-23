using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>ExpUp (0x38 GMS2, 0x37 KMS2): mode 0 is gained exp, message type, total, rest, npc id and a bool; mode 1 is the
/// rest exp alone.</summary>
public class ExpUpTests {
    // gms2 recording: packet 2856, 32 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2Mode0 = new byte[] {
        0x00, 0x2C, 0x20, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF2, 0x03, 0xFC, 0x91, 0x01, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    };
    // gms2 recording: packet 3984, 9 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2Mode1 = new byte[] {
        0x01, 0x8E, 0x62, 0xAA, 0x0A, 0x00, 0x00, 0x00, 0x00,
    };
    // kms2 recording: packet 499, 32 bytes (names masked with '*' by the masking pass).
    static readonly byte[] KmsMode0 = new byte[] {
        0x00, 0x23, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xB9, 0x0B, 0x25, 0x02, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    };

    [Test] public void Gms2Mode0ReadsTheGain() {
        var r = new ByteReader(Gms2Mode0);
        var e = ExpUp.Read(r);
        Assert.That((e.Mode, e.Gained, e.Type, e.Total, e.Rest, e.NpcId, e.Flag), Is.EqualTo(((byte) 0, 73772L, 1010, 102908L, 0L, 0, false)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void Gms2Mode1ReadsTheRest() {
        var r = new ByteReader(Gms2Mode1);
        var e = ExpUp.Read(r);
        Assert.That((e.Mode, e.Rest), Is.EqualTo(((byte) 1, 178938510L)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsMode0ReadsTheGain() {
        var r = new ByteReader(KmsMode0);
        var e = ExpUp.Read(r);
        Assert.That((e.Mode, e.Gained, e.Type, e.Total, e.Rest), Is.EqualTo(((byte) 0, 35L, 3001, 549L, 0L)));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
