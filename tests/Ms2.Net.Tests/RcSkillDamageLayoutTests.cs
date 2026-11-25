using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>SkillDamage: GMS2 mode 4 is returned as Raw and the KMS 18-byte mode 3 (build 2495's i32) is read at the width it carries; the
/// recorded mode-3 forms are covered by SkillDamageMode3Tests. Layouts: the GMS2 packet layout notes §B2 and §B3.</summary>
public class RcSkillDamageLayoutTests {
    // gms2 recording: packet 6507, 26 bytes
    static readonly byte[] Gms2Mode4a = RcPkt.Hex(
        "04E2893700E2893700CD51350058020000000000000000000001");
    // gms2 recording: packet 6993, 26 bytes
    static readonly byte[] Gms2Mode4b = RcPkt.Hex(
        "04E2893700E28937005452350058020000000000000000000001");
    // gms2 recording: packet 7213, 26 bytes
    static readonly byte[] Gms2Mode4c = RcPkt.Hex(
        "04E2893700E28937008552350014000000000000000000000001");
    // kms2 recording (build 2495): packet 1456, 18 bytes
    static readonly byte[] KmsMode3I32 = RcPkt.Hex(
        "03585322020D5622021C335509000EA0FDFF");

    [Test] public void Gms2Mode4ReadsTheHealValues() {
        foreach (var (bytes, target, owner, buff, v1, v2, v3, show) in new[] {
            (Gms2Mode4a, 3639778, 3639778, 3494349, 600U, 0U, 0U, true),
            (Gms2Mode4b, 3639778, 3639778, 3494484, 600U, 0U, 0U, true),
            (Gms2Mode4c, 3639778, 3639778, 3494533, 20U, 0U, 0U, true),
        }) {
            var r = new ByteReader(bytes);
            var d = SkillDamage.Read(r, ProtocolProfile.Gms2);
            Assert.That(d.Mode, Is.EqualTo(4));
            Assert.That((d.TargetId, d.OwnerId, d.BuffId), Is.EqualTo((target, owner, buff)));
            Assert.That((d.Value1, d.Value2, d.Value3), Is.EqualTo(((long) v1, (long) v2, (long) v3)));
            Assert.That(d.ShowValues, Is.EqualTo(show));
            Assert.That(r.Available, Is.EqualTo(0), "mode 4's 25 body bytes must be decoded, not kept as Raw");
        }
    }

    [Test] public void Gms2Modes7And8StayRawAndModeFourOnKmsReadsTheSameFields() {
        var d = SkillDamage.Read(new ByteReader(new byte[] { 7, 1, 2, 3 }), ProtocolProfile.Gms2);
        Assert.That(d.Raw, Is.EqualTo(new byte[] { 1, 2, 3 }));
        var k = SkillDamage.Read(new ByteReader(Gms2Mode4a), ProtocolProfile.Kms2525);
        Assert.That((k.TargetId, k.BuffId, k.Value1), Is.EqualTo((3639778, 3494349, 600L)));
    }

    [Test] public void KmsMode3ReadsAnI32DamageWhenOnlyFourBytesRemain() {
        var r = new ByteReader(KmsMode3I32);
        var d = SkillDamage.Read(r);
        Assert.That(d.Mode, Is.EqualTo(3));
        Assert.That((d.OwnerId, d.TargetId, d.BuffId), Is.EqualTo((35803992, 35804685, 156578588)));
        Assert.That((d.DotHit.Flag, d.DotHit.Damage), Is.EqualTo(((byte) 0, -155634L)));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
