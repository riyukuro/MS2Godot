using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Cinematic (0x68 GMS2, 0x66 KMS2) types 2 (close, one byte) and 3 (start: an i32 id and two unicode strings, the
/// second dropped). The recorded starts carry empty strings after the name masking. Types 4-12 are unread.
/// </summary>
public class CinematicRecordedTests {
    // gms2 recording: packet 2360, 1 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2Close = new byte[] {
        0x02,
    };
    // gms2 recording: packet 2334, 9 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2Start = new byte[] {
        0x03, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    };
    // kms2 recording: packet 614, 1 bytes (names masked with '*' by the masking pass).
    static readonly byte[] KmsClose = new byte[] {
        0x02,
    };
    // kms2 recording: packet 1165, 9 bytes (names masked with '*' by the masking pass).
    static readonly byte[] KmsStart = new byte[] {
        0x03, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    };

    [Test] public void Gms2CloseReadsOneByte() {
        var r = new ByteReader(Gms2Close);
        Assert.That(CinematicPacket.Read(r), Is.TypeOf<CinematicPacket.Close>());
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void Gms2StartReadsTheIdAndText() {
        var r = new ByteReader(Gms2Start);
        var c = CinematicPacket.Read(r) as CinematicPacket.Start;
        Assert.That(c, Is.Not.Null);
        Assert.That((c.Id, c.Text), Is.EqualTo((3, "")));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsCloseReadsOneByte() {
        var r = new ByteReader(KmsClose);
        Assert.That(CinematicPacket.Read(r), Is.TypeOf<CinematicPacket.Close>());
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsStartReadsTheIdAndText() {
        var r = new ByteReader(KmsStart);
        var c = CinematicPacket.Read(r) as CinematicPacket.Start;
        Assert.That(c, Is.Not.Null);
        Assert.That((c.Id, c.Text), Is.EqualTo((4, "")));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
