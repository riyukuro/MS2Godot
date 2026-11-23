using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>PcStat (another player's 0x2F GMS2): the 0x23 block is three rows of i64 health and the four i32 speeds PC::vfunc_90
/// reads; the recorded body carries all five stats for each of the three rows.</summary>
public class PcStatTests {
    // gms2 recording: packet 6102, 78 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2 = new byte[] {
        0x94, 0xA5, 0x37, 0x00, 0x00, 0x23, 0xE3, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x64, 0x00,
        0x00, 0x00, 0x64, 0x00, 0x00, 0x00, 0x64, 0x00, 0x00, 0x00, 0x64, 0x00, 0x00, 0x00, 0xD0, 0x06,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x64, 0x00, 0x00, 0x00, 0x64, 0x00, 0x00, 0x00, 0x64, 0x00,
        0x00, 0x00, 0x64, 0x00, 0x00, 0x00, 0xE3, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x64, 0x00,
        0x00, 0x00, 0x64, 0x00, 0x00, 0x00, 0x64, 0x00, 0x00, 0x00, 0x64, 0x00, 0x00, 0x00,
    };

    [Test] public void Gms2ReadsTheThreeStatRows() {
        var r = new ByteReader(Gms2);
        var s = PcStat.Read(r);
        Assert.That((s.ObjectId, s.Values.Count), Is.EqualTo((3646868, 5)));
        Assert.That(s.Values[PcStat.Health], Is.EqualTo(new long[] { 1763, 1744, 1763 }));
        Assert.That(s.Values[PcStat.AttackSpeed], Is.EqualTo(new long[] { 100, 100, 100 }));
        Assert.That(s.Values[PcStat.MovementSpeed], Is.EqualTo(new long[] { 100, 100, 100 }));
        Assert.That(s.Values[PcStat.MountSpeed], Is.EqualTo(new long[] { 100, 100, 100 }));
        Assert.That(s.Values[PcStat.JumpHeight], Is.EqualTo(new long[] { 100, 100, 100 }));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
