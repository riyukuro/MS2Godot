using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>SkillUse (0x3D GMS2, 0x3C KMS2): the cast and serial uids, the actor, skill and level, the motion, position,
/// direction and rotation, then the optional extra block. Both recorded 57-byte bodies carry no extra block.</summary>
public class SkillUseTests {
    // gms2 recording: packet 2413, 57 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2 = new byte[] {
        0x8F, 0xF0, 0x4C, 0x51, 0x73, 0x35, 0x00, 0x00, 0x55, 0xC2, 0xE0, 0x06, 0x7F, 0xC0, 0x06, 0x00,
        0x53, 0x52, 0xA6, 0x00, 0x01, 0x00, 0x00, 0x96, 0x00, 0xA2, 0xFB, 0x1B, 0x04, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x34, 0x43, 0x00, 0x00, 0x00, 0x00,
    };
    // kms2 recording: packet 8760, 57 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Kms = new byte[] {
        0x6A, 0xC7, 0x87, 0x9E, 0xFF, 0x39, 0x00, 0x00, 0x20, 0x1C, 0xB4, 0x1E, 0x70, 0x80, 0xE5, 0x00,
        0x07, 0x4F, 0x5D, 0x05, 0x01, 0x00, 0x00, 0x20, 0xF4, 0x62, 0xFE, 0x59, 0x02, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0xB2, 0x42, 0x00, 0x00, 0x00, 0x00,
    };

    [Test] public void Gms2SkillUseReadsTheCast() {
        var r = new ByteReader(Gms2);
        var s = SkillUse.Read(r);
        Assert.That((s.CastUid, s.Serial, s.ActorId), Is.EqualTo((58769401507983L, 115393109, 442495)));
        Assert.That((s.SkillId, s.Level, s.Motion), Is.EqualTo((10900051, (short) 1, (byte) 0)));
        Assert.That((s.Position.X, s.Position.Y, s.Position.Z), Is.EqualTo(((short) 150, (short) -1118, (short) 1051)));
        Assert.That((s.Direction.X, s.Direction.Y, s.Direction.Z), Is.EqualTo((0f, 1f, 0f)));
        Assert.That((s.Rotation.X, s.Rotation.Y, s.Rotation.Z), Is.EqualTo((0f, 0f, 180f)));
        Assert.That((s.Speed, s.PetEffect, s.HasExtra), Is.EqualTo((0f, false, false)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsSkillUseReadsTheCast() {
        var r = new ByteReader(Kms);
        var s = SkillUse.Read(r);
        Assert.That((s.CastUid, s.Serial, s.ActorId), Is.EqualTo((63770039142250L, 515120160, 15040624)));
        Assert.That((s.SkillId, s.Level), Is.EqualTo((90001159, (short) 1)));
        Assert.That((s.Rotation.X, s.Rotation.Y, s.Rotation.Z), Is.EqualTo((0f, 0f, 89f)));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
