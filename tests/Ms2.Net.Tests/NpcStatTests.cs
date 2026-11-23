using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>NpcStat (0x2F GMS2, 0x2E KMS2): the single-stat form (u8 flag, u8 type 4 = health, three i64) and the three-row
/// form (type 0x23, three times i64 health + i32 spirit). Every recorded body is consumed exactly.</summary>
public class NpcStatTests {
    // gms2 recording: packet 2978, 31 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2Single = new byte[] {
        0x4F, 0x33, 0x00, 0x00, 0x00, 0x01, 0x04, 0x4D, 0x1E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x4D,
        0x1E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x4D, 0x1E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    };
    // gms2 recording: packet 2625, 42 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2ThreeRows = new byte[] {
        0x4F, 0x33, 0x00, 0x00, 0x00, 0x23, 0x4D, 0x1E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x64, 0x00,
        0x00, 0x00, 0x4D, 0x1E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x64, 0x00, 0x00, 0x00, 0x4D, 0x1E,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x64, 0x00, 0x00, 0x00,
    };
    // kms2 recording: packet 819, 31 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Kms = new byte[] {
        0xB8, 0x81, 0xE5, 0x00, 0x00, 0x01, 0x04, 0x7B, 0x2D, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xE7,
        0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x7B, 0x2D, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    };

    [Test] public void Gms2SingleStatForm() {
        var r = new ByteReader(Gms2Single);
        var s = NpcStat.Read(r);
        Assert.That((s.ObjectId, s.Flag, s.Stat, s.IsHealth), Is.EqualTo((13135, (byte) 0, (byte) 4, true)));
        Assert.That(s.Values, Is.EqualTo(new long[] { 7757, 7757, 7757 }));
        Assert.That((s.Max, s.Current), Is.EqualTo((7757L, 7757L)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void Gms2ThreeRowForm() {
        var r = new ByteReader(Gms2ThreeRows);
        var s = NpcStat.Read(r);
        Assert.That((s.ObjectId, s.Flag, s.Stat), Is.EqualTo((13135, (byte) 0, (byte) 4)));
        Assert.That(s.Values, Is.EqualTo(new long[] { 7757, 7757, 7757 }));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void KmsThreeRowForm() {
        var r = new ByteReader(Kms);
        var s = NpcStat.Read(r);
        Assert.That(s.ObjectId, Is.EqualTo(15040952));
        Assert.That(s.Values, Is.EqualTo(new long[] { 11643, 3047, 11643 }));
        Assert.That((s.Max, s.Current), Is.EqualTo((11643L, 11643L)));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
