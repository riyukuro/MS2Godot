using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>LevelUp (0x39 GMS2, 0x38 KMS2): object id and the new level. The client reads the level as an i16; these recorded
/// bodies end there, but the reader also accepts a KMS server's i32 level (two zero bytes) when only those are left.</summary>
public class LevelUpTests {
    // gms2 recording: packet 2672, 8 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2 = new byte[] {
        0xD7, 0x09, 0x07, 0x00, 0x1B, 0x00, 0x00, 0x00,
    };
    // kms2 recording: packet 2898, 8 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Kms = new byte[] {
        0x1E, 0x55, 0x58, 0x00, 0x04, 0x00, 0x00, 0x00,
    };

    [Test] public void Gms2LevelUpReadsTheLevel() {
        var r = new ByteReader(Gms2);
        Assert.That(LevelUp.Read(r), Is.EqualTo(new LevelUp(461271, 27)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsLevelUpReadsTheLevel() {
        var r = new ByteReader(Kms);
        Assert.That(LevelUp.Read(r), Is.EqualTo(new LevelUp(5788958, 4)));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
