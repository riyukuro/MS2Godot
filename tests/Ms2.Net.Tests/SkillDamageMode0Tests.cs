using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>SkillDamage mode 0 (a tag's projectiles): the cast uid, owner, skill and level, the motion and attack point,
/// the start and direction, the from-server bool, then the projectiles. GMS2 adds an i32 server tick after the bool
/// (Combat.cs:96 @ c5a525e9); the recorded KMS2 body has no tick field and reads 0.</summary>
public class SkillDamageMode0Tests {
    // gms2 recording: packet 2654, 67 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2 = new byte[] {
        0x00, 0x02, 0x00, 0x00, 0x00, 0x4F, 0x33, 0x00, 0x00, 0x4F, 0x33, 0x00, 0x00, 0xB1, 0x90, 0x62,
        0x02, 0x01, 0x00, 0x00, 0x00, 0xFE, 0x10, 0xE6, 0xFB, 0xA5, 0x0E, 0xEC, 0x2B, 0x7F, 0xBF, 0x79,
        0xA0, 0xA4, 0x3D, 0x00, 0x00, 0x00, 0x00, 0x01, 0xCD, 0x86, 0x8C, 0x10, 0x01, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x4F, 0x33, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00,
    };
    // kms2 recording: packet 13488, 63 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Kms = new byte[] {
        0x00, 0x02, 0x00, 0x00, 0x00, 0x72, 0x5E, 0x31, 0x00, 0x72, 0x5E, 0x31, 0x00, 0x43, 0x98, 0x64,
        0x02, 0x01, 0x00, 0x00, 0x00, 0x86, 0xFB, 0xC6, 0xFF, 0x71, 0x06, 0xAB, 0xE4, 0x55, 0x3F, 0x5E,
        0xA9, 0x0C, 0xBF, 0x00, 0x00, 0x00, 0x00, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x03, 0x00, 0x00, 0x00, 0x72, 0x5E, 0x31, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    };

    [Test] public void Gms2Mode0ReadsTheServerTickAndTheProjectile() {
        var r = new ByteReader(Gms2);
        var d = SkillDamage.Read(r, ProtocolProfile.Gms2);
        Assert.That(d.Mode, Is.EqualTo(0));
        Assert.That((d.CastUid, d.OwnerId, d.SkillId, d.Level, d.Motion, d.AttackPoint), Is.EqualTo((56414395432962L, 13135, 40014001, (short) 1, (byte) 0, (byte) 0)));
        Assert.That((d.FromServer, d.ServerTick, d.ProjectileCount), Is.EqualTo((true, 277645005, 1)));
        Assert.That(d.Projectiles, Has.Count.EqualTo(1));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsMode0ReadsWithoutATick() {
        var r = new ByteReader(Kms);
        var d = SkillDamage.Read(r);
        Assert.That(d.Mode, Is.EqualTo(0));
        Assert.That((d.CastUid, d.OwnerId, d.SkillId, d.Level), Is.EqualTo((13896117578104834L, 3235442, 40147011, (short) 1)));
        Assert.That((d.FromServer, d.ServerTick, d.ProjectileCount), Is.EqualTo((true, 0, 1)));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
