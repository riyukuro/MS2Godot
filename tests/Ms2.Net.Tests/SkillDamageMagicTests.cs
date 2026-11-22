using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>SkillDamage mode 0 (FUN_14171c1d0, scenestategame.cpp:5511): the header and the tag tree of its records.</summary>
public class SkillDamageMagicTests {
    static SkillDamage Read(bool server, params (long parent, long serial, int target, byte hit, byte bounce)[] recs) {
        var w = new ByteWriter();
        w.WriteByte(0); w.WriteLong(77); w.WriteInt(9); w.WriteInt(10500001); w.WriteShort(4); w.WriteByte(2); w.WriteByte(1);
        w.WriteShort(100); w.WriteShort(-200); w.WriteShort(30); w.WriteFloat(0.6f); w.WriteFloat(-0.8f); w.WriteFloat(0);
        w.WriteByte((byte) (server ? 1 : 0)); w.WriteByte((byte) recs.Length);
        foreach (var t in recs) { w.WriteLong(t.parent); w.WriteLong(t.serial); w.WriteInt(t.target); w.WriteByte(t.hit); w.WriteByte(t.bounce); }
        return SkillDamage.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()));
    }

    [Test]
    public void TheHeaderKeepsTheRecordsMotionAttackDirectionAndServerFlag() {
        var d = Read(true, (0, 5, 30, 0, 0));
        Assert.That((d.CastUid, d.OwnerId, d.SkillId, d.Level, d.Motion, d.AttackPoint), Is.EqualTo((77L, 9, 10500001, (short) 4, (byte) 2, (byte) 1)));
        Assert.That(d.Position, Is.EqualTo(new Vector3S(100, -200, 30)));
        Assert.That(d.MagicDirection, Is.EqualTo(new Vector3(0.6f, -0.8f, 0)));
        Assert.That(d.FromServer, Is.True);
        Assert.That(Read(false, (0, 5, 30, 0, 0)).FromServer, Is.False);
    }

    [Test]
    public void LinksFollowTheirParentAndALaterLinkOfTheSameParentReplacesIt() {
        var d = Read(false,
            (0, 5, 30, 0, 0), (0, 6, 31, 1, 0),
            (5, 7, 32, 0, 1),                // 5 -> 7
            (7, 8, 33, 0, 2),                // 7 -> 8
            (7, 9, 34, 0, 3),                // 7 -> 9 replaces 8
            (99, 10, 35, 0, 1),              // no parent 99: skipped
            (8, 11, 36, 0, 3));              // 8 is still in the map, but no longer on 5's chain
        Assert.That(d.ProjectileCount, Is.EqualTo(7));
        Assert.That(d.Projectiles, Has.Count.EqualTo(2));
        var a = d.Projectiles[0];
        Assert.That((a.Serial, a.TargetId, a.Hit), Is.EqualTo((5L, 30, (byte) 0)));
        Assert.That(a.Links, Is.EqualTo(new[] { new SkillDamageLink(7, 32, 1, 0), new SkillDamageLink(9, 34, 3, 0) }));
        var b = d.Projectiles[1];
        Assert.That((b.Serial, b.TargetId, b.Hit, b.Links.Count), Is.EqualTo((6L, 31, (byte) 1, 0)));
    }
}
