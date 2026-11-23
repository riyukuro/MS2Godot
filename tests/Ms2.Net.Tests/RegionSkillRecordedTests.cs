using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>RegionSkill (0x4D GMS2, 0x4C KMS2): mode 0 adds an object (owner, start tick, points, skill, level, the two
/// rotations) and mode 1 removes one by id. The recorded 40-byte add carries one point.</summary>
public class RegionSkillRecordedTests {
    // gms2 recording: packet 3244, 40 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2Add = new byte[] {
        0x00, 0xA9, 0x33, 0x00, 0x00, 0xA9, 0x33, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00,
        0xE1, 0xC3, 0x00, 0x00, 0x16, 0xC3, 0x00, 0xA0, 0x0C, 0x45, 0x92, 0x1D, 0x2C, 0x04, 0x01, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    };
    // gms2 recording: packet 7860, 5 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2Remove = new byte[] {
        0x01, 0x4A, 0x7A, 0x00, 0x00,
    };
    // kms2 recording: packet 9365, 5 bytes (names masked with '*' by the masking pass).
    static readonly byte[] KmsRemove = new byte[] {
        0x01, 0xED, 0x92, 0xE5, 0x00,
    };

    [Test] public void Gms2AddCarriesTheRegion() {
        var r = new ByteReader(Gms2Add);
        var m = RegionSkillMessage.Read(r) as RegionSkillMessage.Add;
        Assert.That(m, Is.Not.Null);
        Assert.That((m.Id, m.OwnerId, m.StartTick, m.SkillId, m.Level), Is.EqualTo((13225, 13225, 0, 70000018, (short) 1)));
        Assert.That(m.Points, Has.Length.EqualTo(1));
        Assert.That((m.Points[0].X, m.Points[0].Y, m.Points[0].Z), Is.EqualTo((-450f, -150f, 2250f)));
        Assert.That((m.RotY, m.RotX), Is.EqualTo((0f, 0f)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void Gms2RemoveCarriesTheId() {
        var r = new ByteReader(Gms2Remove);
        var m = RegionSkillMessage.Read(r) as RegionSkillMessage.Remove;
        Assert.That(m, Is.Not.Null);
        Assert.That(m.Id, Is.EqualTo(31306));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsRemoveCarriesTheId() {
        var r = new ByteReader(KmsRemove);
        var m = RegionSkillMessage.Read(r) as RegionSkillMessage.Remove;
        Assert.That(m, Is.Not.Null);
        Assert.That(m.Id, Is.EqualTo(15045357));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
