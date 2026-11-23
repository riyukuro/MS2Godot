using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>SkillCooldown (0x43 GMS2, 0x42 KMS2): a u8 count, then per entry an i64 key and the i32 end tick and uses. Both
/// recorded bodies carry one entry.</summary>
public class SkillCooldownTests {
    // gms2 recording: packet 2273, 17 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2 = new byte[] {
        0x01, 0x2B, 0x52, 0xA6, 0x00, 0x21, 0x52, 0xA6, 0x00, 0x0F, 0x6B, 0x37, 0x0B, 0x00, 0x00, 0x00,
        0x00,
    };
    // kms2 recording: packet 587, 17 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Kms = new byte[] {
        0x01, 0x51, 0xB1, 0x9E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0E, 0xD6, 0x18, 0x2B, 0x00, 0x00, 0x00,
        0x00,
    };

    [Test] public void Gms2CooldownReadsOneEntry() {
        var r = new ByteReader(Gms2);
        var c = SkillCooldown.Read(r);
        Assert.That(c.Entries, Has.Length.EqualTo(1));
        Assert.That((c.Entries[0].Key, c.Entries[0].EndTick, c.Entries[0].Uses), Is.EqualTo((46815147832267307L, 188181263, 0)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsCooldownReadsOneEntry() {
        var r = new ByteReader(Kms);
        var c = SkillCooldown.Read(r);
        Assert.That(c.Entries, Has.Length.EqualTo(1));
        Assert.That((c.Entries[0].Key, c.Entries[0].EndTick, c.Entries[0].Uses), Is.EqualTo((10400081L, 723047950, 0)));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
