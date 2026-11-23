using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>SkillDamage mode 5 (region skill) without target records: the cast uid, the owner and the region id, the attack point
/// and a zero target count. The recorded 19-byte bodies are header only, so the whole buffer must be consumed.</summary>
public class SkillDamageMode5Tests {
    // gms2 recording: packet 3255, 19 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2 = new byte[] {
        0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xA9, 0x33, 0x00, 0x00, 0xA9, 0x33, 0x00,
        0x00, 0x00, 0x00,
    };
    // kms2 recording: packet 15882, 19 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Kms = new byte[] {
        0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xBD, 0xAB, 0x64, 0x00, 0xBD, 0xAB, 0x64,
        0x00, 0x00, 0x00,
    };

    [Test] public void Gms2Mode5ReadsTheRegion() {
        var r = new ByteReader(Gms2);
        var d = SkillDamage.Read(r, ProtocolProfile.Gms2);
        Assert.That(d.Mode, Is.EqualTo(5));
        Assert.That((d.OwnerId, d.RegionId, d.AttackPoint), Is.EqualTo((13225, 13225, (byte) 0)));
        Assert.That(d.Targets, Is.Empty);
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsMode5ReadsTheRegion() {
        var r = new ByteReader(Kms);
        var d = SkillDamage.Read(r);
        Assert.That(d.Mode, Is.EqualTo(5));
        Assert.That((d.OwnerId, d.RegionId, d.AttackPoint), Is.EqualTo((6597565, 6597565, (byte) 0)));
        Assert.That(d.Targets, Is.Empty);
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
