using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class PlayerStatTests {
    [Test]
    public void UpdateListReadsHealthAndSpirit() {
        var w = new ByteWriter();
        w.WriteInt(77); w.WriteByte(0); w.WriteByte(2);
        w.WriteByte(4); w.WriteLong(1000); w.WriteLong(900); w.WriteLong(650);
        w.WriteByte(7); w.WriteInt(100); w.WriteInt(100); w.WriteInt(40);
        var s = PlayerStat.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()));
        Assert.That(s.ObjectId, Is.EqualTo(77));
        Assert.That(s.TryGet(PlayerStat.Health, out long max, out long cur), Is.True);
        Assert.That((max, cur), Is.EqualTo((1000L, 650L)));
        Assert.That(s.TryGet(PlayerStat.Spirit, out max, out cur), Is.True);
        Assert.That((max, cur), Is.EqualTo((100L, 40L)));
    }

    [Test]
    public void InitReadsAllAttributesByIndex() {
        var w = new ByteWriter();
        w.WriteInt(5); w.WriteByte(0); w.WriteByte(35);
        for (int a = 0; a < 35; a++) {
            if (a == 4) { w.WriteLong(500); w.WriteLong(500); w.WriteLong(480); }
            else { w.WriteInt(a); w.WriteInt(a); w.WriteInt(a + 1); }
        }
        var s = PlayerStat.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()));
        Assert.That(s.Values.Count, Is.EqualTo(35));
        s.TryGet(PlayerStat.Health, out _, out long cur);
        Assert.That(cur, Is.EqualTo(480));
        s.TryGet(10, out long t, out cur);
        Assert.That((t, cur), Is.EqualTo((10L, 11L)));
    }

    [Test]
    public void AGms2NpcsThreeRowStatPacketReadsWhole() {
        // recorded GMS2 0x2F, 42 bytes: npc 0x334f, command 0, count 0x23, three rows of (i64 health, i32 100)
        const string hex = "4f330000 0023 4d1e000000000000 64000000 4d1e000000000000 64000000 4d1e000000000000 64000000";
        var b = Convert.FromHexString(hex.Replace(" ", ""));
        Assert.That(b.Length, Is.EqualTo(42));
        var s = NpcStat.Read(new ByteReader(b));
        Assert.That((s.ObjectId, s.IsHealth, s.Max, s.Current), Is.EqualTo((0x334f, true, 0x1e4dL, 0x1e4dL)));
    }
}
