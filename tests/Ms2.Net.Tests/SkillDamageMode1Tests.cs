using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>SkillDamage mode 1 (a cast's hits): the cast uids, the caster, the skill and its level, the motion and attack point,
/// the position and direction, then one target with its hit list. One recorded sample per region, both 56 bytes.</summary>
public class SkillDamageMode1Tests {
    // gms2 recording: packet 2980, 56 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2 = new byte[] {
        0x01, 0x06, 0x00, 0x00, 0x00, 0x4F, 0x33, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x4F, 0x33, 0x00,
        0x00, 0x4F, 0x33, 0x00, 0x00, 0xB1, 0x90, 0x62, 0x02, 0x01, 0x00, 0x01, 0x00, 0x56, 0x0F, 0xEA,
        0xFC, 0x47, 0x0D, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0xF2, 0x4B, 0x26, 0x00, 0x01, 0x01,
        0xF6, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
    };
    // kms2 recording: packet 10379, 56 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Kms = new byte[] {
        0x01, 0x06, 0x98, 0xAC, 0x95, 0x2A, 0x3F, 0x00, 0x00, 0x36, 0x0B, 0x00, 0x00, 0x30, 0x6E, 0x68,
        0x00, 0x44, 0x54, 0x68, 0x00, 0x85, 0x81, 0xF4, 0x05, 0x01, 0x00, 0x00, 0x00, 0x24, 0xF4, 0xA6,
        0xFF, 0x90, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x12, 0x6F, 0x68, 0x00, 0x01, 0x00,
        0x57, 0xDC, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
    };

    [Test] public void Gms2Mode1ReadsTheCastAndItsTarget() {
        var r = new ByteReader(Gms2);
        var d = SkillDamage.Read(r, ProtocolProfile.Gms2);
        Assert.That(d.Mode, Is.EqualTo(1));
        Assert.That((d.CasterUid, d.CastUid), Is.EqualTo((56414395432966L, 56414395432968L)));
        Assert.That((d.OwnerId, d.SkillId, d.Level, d.Motion, d.AttackPoint), Is.EqualTo((13135, 40014001, (short) 1, (byte) 1, (byte) 0)));
        Assert.That((d.Position.X, d.Position.Y, d.Position.Z), Is.EqualTo(((short) 3926, (short) -790, (short) 3399)));
        Assert.That(d.Targets, Has.Count.EqualTo(1));
        Assert.That(d.Targets[0].Hits, Is.Not.Empty);
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsMode1ReadsTheCastAndItsTarget() {
        var r = new ByteReader(Kms);
        var d = SkillDamage.Read(r);
        Assert.That(d.Mode, Is.EqualTo(1));
        Assert.That((d.CasterUid, d.CastUid), Is.EqualTo((69452132292614L, 29394550015396662L)));
        Assert.That((d.OwnerId, d.SkillId, d.Level, d.Motion), Is.EqualTo((6837316, 99910021, (short) 1, (byte) 0)));
        Assert.That(d.Targets, Has.Count.EqualTo(1));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
