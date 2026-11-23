using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>SkillDamage mode 3 (damage over time) from recordings: the damage is an i32 on GMS2 (18 B) and an i64 on KMS2 (22 B).
/// The reader picks the width from the bytes left after the flag byte (Combat.cs:115 @ c5a525e9).</summary>
public class SkillDamageMode3Tests {
    // gms2 recording: packet 6711, 18 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2 = new byte[] {
        0x03, 0x36, 0xDC, 0x06, 0x00, 0x48, 0xDD, 0x06, 0x00, 0x7D, 0x88, 0x0C, 0x00, 0x00, 0x5C, 0xFF,
        0xFF, 0xFF,
    };
    // kms2 recording: packet 12354, 22 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Kms = new byte[] {
        0x03, 0x86, 0x6F, 0x36, 0x00, 0x47, 0x79, 0x36, 0x00, 0x2C, 0xAD, 0x24, 0x00, 0x00, 0x6B, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
    };

    [Test] public void Gms2Mode3ReadsAnI32Damage() {
        var r = new ByteReader(Gms2);
        var d = SkillDamage.Read(r, ProtocolProfile.Gms2);
        Assert.That(d.Mode, Is.EqualTo(3));
        Assert.That((d.OwnerId, d.TargetId, d.BuffId), Is.EqualTo((449590, 449864, 821373)));
        Assert.That((d.DotHit.Flag, d.DotHit.Damage), Is.EqualTo(((byte) 0, -164L)));
        Assert.That(d.Targets.Single().TargetId, Is.EqualTo(449864));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsMode3ReadsAnI64Damage() {
        var r = new ByteReader(Kms);
        var d = SkillDamage.Read(r);
        Assert.That(d.Mode, Is.EqualTo(3));
        Assert.That((d.OwnerId, d.TargetId, d.BuffId), Is.EqualTo((3567494, 3569991, 2403628)));
        Assert.That((d.DotHit.Flag, d.DotHit.Damage), Is.EqualTo(((byte) 0, -149L)));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
